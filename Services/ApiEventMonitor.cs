using System;
using System.Linq;
using System.Windows.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AICAD.Services
{
    public sealed class ApiEventMonitor : IDisposable
    {
        private readonly ISldWorks _swApp;
        private readonly SldWorks _swStrong;
        private readonly object _sync = new object();
        private bool _running;

        // Debounce timers to prevent flooding
        private DispatcherTimer _modifyDebounceTimer;
        private DispatcherTimer _viewDebounceTimer;
        private const int DEBOUNCE_MS = 500;
        
        // Tracking last event times
        private DateTime _lastModifyTime = DateTime.MinValue;
        private DateTime _lastViewChangeTime = DateTime.MinValue;

        private PartDoc _partDoc;
        private AssemblyDoc _assemblyDoc;
        private DrawingDoc _drawingDoc;
        private ModelView _activeView;

        private DSldWorksEvents_ActiveModelDocChangeNotifyEventHandler _docChangeHandler;
        private DSldWorksEvents_FileOpenPostNotifyEventHandler _fileOpenHandler;
        private DSldWorksEvents_FileNewNotify2EventHandler _fileNewHandler;
        private DSldWorksEvents_DocumentLoadNotify2EventHandler _docLoadHandler;

        private DPartDocEvents_NewSelectionNotifyEventHandler _partSelectionHandler;
        private DAssemblyDocEvents_NewSelectionNotifyEventHandler _assemblySelectionHandler;
        private DDrawingDocEvents_NewSelectionNotifyEventHandler _drawingSelectionHandler;

        private DPartDocEvents_ModifyNotifyEventHandler _partModifyHandler;
        private DAssemblyDocEvents_ModifyNotifyEventHandler _assemblyModifyHandler;
        private DDrawingDocEvents_ModifyNotifyEventHandler _drawingModifyHandler;

        private DPartDocEvents_ActiveViewChangeNotifyEventHandler _partViewHandler;
        private DAssemblyDocEvents_ActiveViewChangeNotifyEventHandler _assemblyViewHandler;
        private DModelViewEvents_ViewChangeNotifyEventHandler _modelViewChangeHandler;

        // Configuration flags
        public bool TrackViewChanges { get; set; } = false;  // DISABLED by default - too noisy
        public bool DebounceEvents { get; set; } = true;     // ENABLED by default - prevents flooding

        public event Action<string> OnEventJson;

        public ApiEventMonitor(ISldWorks swApp)
        {
            _swApp = swApp;
            _swStrong = swApp as SldWorks;
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_running) return;
                AttachApplicationHandlers();
                AttachModelHandlers();
                _running = true;
                Emit("startup", new JObject { ["message"] = "API monitor started (ViewChangeNotify disabled, ModifyNotify debounced @500ms)" });
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (!_running) return;
                DetachModelHandlers();
                DetachApplicationHandlers();
                _running = false;
                Emit("shutdown", new JObject { ["message"] = "API monitor stopped" });
            }
        }

        public void Dispose()
        {
            Stop();
            _modifyDebounceTimer?.Stop();
            _viewDebounceTimer?.Stop();
        }

        private void AttachApplicationHandlers()
        {
            if (_swStrong == null) return;
            _docChangeHandler = new DSldWorksEvents_ActiveModelDocChangeNotifyEventHandler(OnActiveModelDocChange);
            _fileOpenHandler = new DSldWorksEvents_FileOpenPostNotifyEventHandler(OnFileOpenPostNotify);
            _fileNewHandler = new DSldWorksEvents_FileNewNotify2EventHandler(OnFileNewNotify2);
            _docLoadHandler = new DSldWorksEvents_DocumentLoadNotify2EventHandler(OnDocumentLoadNotify2);

            try { _swStrong.ActiveModelDocChangeNotify += _docChangeHandler; } catch { }
            try { _swStrong.FileOpenPostNotify += _fileOpenHandler; } catch { }
            try { _swStrong.FileNewNotify2 += _fileNewHandler; } catch { }
            try { _swStrong.DocumentLoadNotify2 += _docLoadHandler; } catch { }
        }

        private void DetachApplicationHandlers()
        {
            if (_swStrong == null) return;
            try { if (_docChangeHandler != null) _swStrong.ActiveModelDocChangeNotify -= _docChangeHandler; } catch { }
            try { if (_fileOpenHandler != null) _swStrong.FileOpenPostNotify -= _fileOpenHandler; } catch { }
            try { if (_fileNewHandler != null) _swStrong.FileNewNotify2 -= _fileNewHandler; } catch { }
            try { if (_docLoadHandler != null) _swStrong.DocumentLoadNotify2 -= _docLoadHandler; } catch { }

            _docChangeHandler = null;
            _fileOpenHandler = null;
            _fileNewHandler = null;
            _docLoadHandler = null;
        }

        private void AttachModelHandlers()
        {
            DetachModelHandlers();
            var model = _swApp?.IActiveDoc2 as ModelDoc2;
            if (model == null) return;

            _partDoc = model as PartDoc;
            _assemblyDoc = model as AssemblyDoc;
            _drawingDoc = model as DrawingDoc;

            if (_partDoc != null)
            {
                _partSelectionHandler = new DPartDocEvents_NewSelectionNotifyEventHandler(OnSelection);
                _partModifyHandler = new DPartDocEvents_ModifyNotifyEventHandler(OnModify);
                _partViewHandler = new DPartDocEvents_ActiveViewChangeNotifyEventHandler(OnActiveViewChanged);
                try { _partDoc.NewSelectionNotify += _partSelectionHandler; } catch { }
                try { _partDoc.ModifyNotify += _partModifyHandler; } catch { }
                if (TrackViewChanges) try { _partDoc.ActiveViewChangeNotify += _partViewHandler; } catch { }
            }

            if (_assemblyDoc != null)
            {
                _assemblySelectionHandler = new DAssemblyDocEvents_NewSelectionNotifyEventHandler(OnSelection);
                _assemblyModifyHandler = new DAssemblyDocEvents_ModifyNotifyEventHandler(OnModify);
                _assemblyViewHandler = new DAssemblyDocEvents_ActiveViewChangeNotifyEventHandler(OnActiveViewChanged);
                try { _assemblyDoc.NewSelectionNotify += _assemblySelectionHandler; } catch { }
                try { _assemblyDoc.ModifyNotify += _assemblyModifyHandler; } catch { }
                if (TrackViewChanges) try { _assemblyDoc.ActiveViewChangeNotify += _assemblyViewHandler; } catch { }
            }

            if (_drawingDoc != null)
            {
                _drawingSelectionHandler = new DDrawingDocEvents_NewSelectionNotifyEventHandler(OnSelection);
                _drawingModifyHandler = new DDrawingDocEvents_ModifyNotifyEventHandler(OnModify);
                try { _drawingDoc.NewSelectionNotify += _drawingSelectionHandler; } catch { }
                try { _drawingDoc.ModifyNotify += _drawingModifyHandler; } catch { }
            }

            if (TrackViewChanges) AttachViewHandler(model);
        }

        private void AttachViewHandler(ModelDoc2 model)
        {
            try
            {
                var view = model?.ActiveView as ModelView;
                if (view == null) return;
                _activeView = view;
                _modelViewChangeHandler = new DModelViewEvents_ViewChangeNotifyEventHandler(OnViewChangeNotify);
                try { _activeView.ViewChangeNotify += _modelViewChangeHandler; } catch { }
            }
            catch { }
        }

        private void DetachModelHandlers()
        {
            if (_partDoc != null)
            {
                try { if (_partSelectionHandler != null) _partDoc.NewSelectionNotify -= _partSelectionHandler; } catch { }
                try { if (_partModifyHandler != null) _partDoc.ModifyNotify -= _partModifyHandler; } catch { }
                try { if (_partViewHandler != null) _partDoc.ActiveViewChangeNotify -= _partViewHandler; } catch { }
            }
            if (_assemblyDoc != null)
            {
                try { if (_assemblySelectionHandler != null) _assemblyDoc.NewSelectionNotify -= _assemblySelectionHandler; } catch { }
                try { if (_assemblyModifyHandler != null) _assemblyDoc.ModifyNotify -= _assemblyModifyHandler; } catch { }
                try { if (_assemblyViewHandler != null) _assemblyDoc.ActiveViewChangeNotify -= _assemblyViewHandler; } catch { }
            }
            if (_drawingDoc != null)
            {
                try { if (_drawingSelectionHandler != null) _drawingDoc.NewSelectionNotify -= _drawingSelectionHandler; } catch { }
                try { if (_drawingModifyHandler != null) _drawingDoc.ModifyNotify -= _drawingModifyHandler; } catch { }
            }
            if (_activeView != null)
            {
                try { if (_modelViewChangeHandler != null) _activeView.ViewChangeNotify -= _modelViewChangeHandler; } catch { }
            }

            _partDoc = null;
            _assemblyDoc = null;
            _drawingDoc = null;
            _activeView = null;
            _partSelectionHandler = null;
            _assemblySelectionHandler = null;
            _drawingSelectionHandler = null;
            _partModifyHandler = null;
            _assemblyModifyHandler = null;
            _drawingModifyHandler = null;
            _partViewHandler = null;
            _assemblyViewHandler = null;
            _modelViewChangeHandler = null;
        }

        private int OnActiveModelDocChange()
        {
            try { AttachModelHandlers(); } catch { }
            EmitApiCall("swApp.ActiveModelDocChangeNotify", null, null);
            return 0;
        }

        private int OnFileOpenPostNotify(string fileName)
        {
            try { AttachModelHandlers(); } catch { }
            EmitApiCall("swApp.FileOpenPostNotify", new[] { $"\"{fileName}\"" }, fileName);
            return 0;
        }

        private int OnFileNewNotify2(object newDoc, int docType, string templateName)
        {
            try { AttachModelHandlers(); } catch { }
            var docTypeName = ((swDocumentTypes_e)docType).ToString();
            EmitApiCall("swApp.FileNewNotify2", new[] { "newDoc", docTypeName, $"\"{templateName}\"" }, templateName);
            return 0;
        }

        private int OnDocumentLoadNotify2(string docTitle, string docPath)
        {
            try { AttachModelHandlers(); } catch { }
            EmitApiCall("swApp.DocumentLoadNotify2", new[] { $"\"{docTitle}\"", $"\"{docPath}\"" }, docPath);
            return 0;
        }

        private int OnSelection()
        {
            try
            {
                var model = _swApp?.IActiveDoc2 as ModelDoc2;
                if (model == null) return 0;

                var selMgr = model.SelectionManager as ISelectionMgr;
                var count = selMgr?.GetSelectedObjectCount2(-1) ?? 0;

                if (count > 0)
                {
                    var typeId = selMgr.GetSelectedObjectType3(1, -1);
                    var typeName = Enum.GetName(typeof(swSelectType_e), typeId) ?? typeId.ToString();
                    
                    string name = "";
                    try
                    {
                        var obj = selMgr.GetSelectedObject6(1, -1);
                        if (obj != null)
                        {
                            var feature = obj as Feature;
                            if (feature != null) name = feature.Name;
                            else
                            {
                                var comp = obj as Component2;
                                if (comp != null) name = comp.Name2;
                            }
                        }
                    }
                    catch { }

                    var args = new[] { $"\"{name}\"", $"\"{typeName}\"", "0", "0", "0", "false", "0", "null", "0" };
                    EmitApiCall("swModel.Extension.SelectByID2", args, $"Selected: {typeName}" + (string.IsNullOrEmpty(name) ? "" : $" ({name})"));
                }
            }
            catch { }
            return 0;
        }

        private int OnModify()
        {
            if (DebounceEvents)
            {
                _lastModifyTime = DateTime.UtcNow;
                
                if (_modifyDebounceTimer == null)
                {
                    _modifyDebounceTimer = new DispatcherTimer();
                    _modifyDebounceTimer.Interval = TimeSpan.FromMilliseconds(DEBOUNCE_MS);
                    _modifyDebounceTimer.Tick += (s, e) =>
                    {
                        if ((DateTime.UtcNow - _lastModifyTime).TotalMilliseconds >= DEBOUNCE_MS)
                        {
                            _modifyDebounceTimer.Stop();
                            EmitApiCall("swModel.ModifyNotify", null, "Model modified");
                        }
                    };
                }

                if (!_modifyDebounceTimer.IsEnabled)
                {
                    _modifyDebounceTimer.Start();
                }
            }
            else
            {
                EmitApiCall("swModel.ModifyNotify", null, "Model modified");
            }
            
            return 0;
        }

        private int OnActiveViewChanged()
        {
            if (!TrackViewChanges) return 0;
            
            try
            {
                var model = _swApp?.IActiveDoc2 as ModelDoc2;
                AttachViewHandler(model);
                EmitApiCall("swModel.ActiveViewChangeNotify", null, "View changed");
            }
            catch { }
            return 0;
        }

        private int OnViewChangeNotify(object viewInfo)
        {
            if (!TrackViewChanges) return 0;

            if (DebounceEvents)
            {
                _lastViewChangeTime = DateTime.UtcNow;
                
                if (_viewDebounceTimer == null)
                {
                    _viewDebounceTimer = new DispatcherTimer();
                    _viewDebounceTimer.Interval = TimeSpan.FromMilliseconds(DEBOUNCE_MS);
                    _viewDebounceTimer.Tick += (s, e) =>
                    {
                        if ((DateTime.UtcNow - _lastViewChangeTime).TotalMilliseconds >= DEBOUNCE_MS)
                        {
                            _viewDebounceTimer.Stop();
                            EmitApiCall("swView.ViewChangeNotify", null, "View updated");
                        }
                    };
                }

                if (!_viewDebounceTimer.IsEnabled)
                {
                    _viewDebounceTimer.Start();
                }
            }
            else
            {
                EmitApiCall("swView.ViewChangeNotify", null, "View updated");
            }

            return 0;
        }

        private void EmitApiCall(string methodSignature, string[] parameters, string description)
        {
            try
            {
                var codeSnippet = methodSignature;
                
                if (!methodSignature.Contains("(") && !methodSignature.StartsWith("//"))
                {
                    if (parameters != null && parameters.Length > 0)
                    {
                        codeSnippet += "(" + string.Join(", ", parameters) + ")";
                    }
                    else
                    {
                        codeSnippet += "()";
                    }
                }
                
                if (!codeSnippet.EndsWith(";") && !codeSnippet.StartsWith("//"))
                {
                    codeSnippet += ";";
                }

                var payload = new JObject
                {
                    ["code"] = codeSnippet,
                    ["method"] = methodSignature.Split('(')[0],
                    ["description"] = description ?? ""
                };

                Emit("api_call", payload);
            }
            catch { }
        }

        private void Emit(string type, JObject data)
        {
            try
            {
                var record = new JObject
                {
                    ["timestamp"] = DateTime.UtcNow.ToString("o"),
                    ["type"] = type,
                    ["data"] = data ?? new JObject()
                };
                var line = JsonConvert.SerializeObject(record, Formatting.None);
                try { OnEventJson?.Invoke(line); } catch { }
                try { AddinStatusLogger.Log("API", line); } catch { }
            }
            catch { }
        }
    }
}
