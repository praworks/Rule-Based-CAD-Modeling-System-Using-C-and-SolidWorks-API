using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AICAD.Services
{
    /// <summary>
    /// Subscribes to SolidWorks events and emits JSON lines describing key API activity
    /// for developer debugging (view changes, selections, and model modifications).
    /// </summary>
    public sealed class ApiEventMonitor : IDisposable
    {
        private readonly ISldWorks _swApp;
        private readonly SldWorks _swStrong;
        private readonly object _sync = new object();

        private bool _running;
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
                Emit("startup", new JObject { ["message"] = "API monitor started" });
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
                try { _partDoc.ActiveViewChangeNotify += _partViewHandler; } catch { }
            }

            if (_assemblyDoc != null)
            {
                _assemblySelectionHandler = new DAssemblyDocEvents_NewSelectionNotifyEventHandler(OnSelection);
                _assemblyModifyHandler = new DAssemblyDocEvents_ModifyNotifyEventHandler(OnModify);
                _assemblyViewHandler = new DAssemblyDocEvents_ActiveViewChangeNotifyEventHandler(OnActiveViewChanged);
                try { _assemblyDoc.NewSelectionNotify += _assemblySelectionHandler; } catch { }
                try { _assemblyDoc.ModifyNotify += _assemblyModifyHandler; } catch { }
                try { _assemblyDoc.ActiveViewChangeNotify += _assemblyViewHandler; } catch { }
            }

            if (_drawingDoc != null)
            {
                _drawingSelectionHandler = new DDrawingDocEvents_NewSelectionNotifyEventHandler(OnSelection);
                _drawingModifyHandler = new DDrawingDocEvents_ModifyNotifyEventHandler(OnModify);
                try { _drawingDoc.NewSelectionNotify += _drawingSelectionHandler; } catch { }
                try { _drawingDoc.ModifyNotify += _drawingModifyHandler; } catch { }
            }

            AttachViewHandler(model);
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
            Emit("model_change", new JObject { ["message"] = "Active model changed" });
            return 0;
        }

        private int OnFileOpenPostNotify(string fileName)
        {
            try { AttachModelHandlers(); } catch { }
            Emit("file_open", new JObject { ["path"] = fileName ?? string.Empty });
            return 0;
        }

        private int OnFileNewNotify2(object newDoc, int docType, string templateName)
        {
            try { AttachModelHandlers(); } catch { }
            Emit("file_new", new JObject { ["docType"] = docType, ["template"] = templateName ?? string.Empty });
            return 0;
        }

        private int OnDocumentLoadNotify2(string docTitle, string docPath)
        {
            try { AttachModelHandlers(); } catch { }
            Emit("file_load", new JObject { ["title"] = docTitle ?? string.Empty, ["path"] = docPath ?? string.Empty });
            return 0;
        }

        private int OnSelection()
        {
            try
            {
                var model = _swApp?.IActiveDoc2 as ModelDoc2;
                var payload = BuildSelectionPayload(model);
                Emit("selection", payload);
            }
            catch { }
            return 0;
        }

        private int OnModify()
        {
            try
            {
                var model = _swApp?.IActiveDoc2 as ModelDoc2;
                var payload = BuildModifyPayload(model);
                Emit("modify", payload);
            }
            catch { }
            return 0;
        }

        private int OnActiveViewChanged()
        {
            try
            {
                var model = _swApp?.IActiveDoc2 as ModelDoc2;
                AttachViewHandler(model);
                var payload = BuildViewPayload(model);
                Emit("view_change", payload);
            }
            catch { }
            return 0;
        }

        private int OnViewChangeNotify(object viewInfo)
        {
            try
            {
                var model = _swApp?.IActiveDoc2 as ModelDoc2;
                var payload = BuildViewPayload(model);
                if (viewInfo != null)
                {
                    payload["info"] = viewInfo.ToString();
                }
                Emit("view_change", payload);
            }
            catch { }
            return 0;
        }

        private JObject BuildSelectionPayload(ModelDoc2 model)
        {
            var payload = new JObject();
            try
            {
                var selMgr = model?.SelectionManager as ISelectionMgr;
                var count = selMgr?.GetSelectedObjectCount2(-1) ?? 0;
                payload["count"] = count;
                var items = new JArray();
                for (int i = 1; i <= count; i++)
                {
                    var item = new JObject { ["index"] = i };
                    try
                    {
                        var typeId = selMgr.GetSelectedObjectType3(i, -1);
                        item["type"] = Enum.GetName(typeof(swSelectType_e), typeId) ?? typeId.ToString();
                    }
                    catch { }

                    try
                    {
                        var obj = selMgr.GetSelectedObject6(i, -1);
                        if (obj != null)
                        {
                            item["objectType"] = obj.GetType().Name;
                            var feat = obj as Feature;
                            if (feat != null)
                            {
                                try { item["featureType"] = feat.GetTypeName2(); } catch { }
                                try { item["name"] = feat.Name ?? string.Empty; } catch { }
                            }
                            var comp = obj as Component2;
                            if (comp != null)
                            {
                                try { item["name"] = comp.Name2 ?? comp.Name; } catch { }
                            }
                            var face = obj as Face2;
                            if (face != null)
                            {
                                TrySet(() => item["id"] = face.GetFaceId());
                                if (!item.ContainsKey("name")) TrySet(() => item["name"] = face.GetFeature()?.Name ?? string.Empty);
                            }
                            var edge = obj as Edge;
                            if (edge != null)
                            {
                                TrySet(() => item["id"] = edge.GetID());
                                if (!item.ContainsKey("name")) TrySet(() => item["name"] = edge.GetTwoAdjacentFaces2()?.OfType<Face2>().FirstOrDefault()?.GetFeature()?.Name ?? string.Empty);
                            }
                            AddShapeDescriptors(item, obj);
                            if (!item.ContainsKey("name")) item["name"] = string.Empty;
                        }
                    }
                    catch { }

                    items.Add(item);
                }
                payload["items"] = items;
            }
            catch (Exception ex)
            {
                payload["error"] = ex.Message;
            }
            return payload;
        }

        private JObject BuildModifyPayload(ModelDoc2 model)
        {
            var payload = new JObject();
            try
            {
                payload["model"] = model?.GetTitle() ?? string.Empty;
                payload["path"] = model?.GetPathName() ?? string.Empty;
                payload["stack"] = System.Environment.StackTrace;
                payload["utc"] = DateTime.UtcNow.ToString("o");
            }
            catch (Exception ex)
            {
                payload["error"] = ex.Message;
            }
            return payload;
        }

        private JObject BuildViewPayload(ModelDoc2 model)
        {
            var payload = new JObject();
            try
            {
                var view = model?.ActiveView as ModelView;
                if (view != null)
                {
                    TrySet(() => payload["scale"] = view.Scale2);
                    TrySet(() =>
                    {
                        var translation = view.Translation2 as object[];
                        if (translation != null && translation.Length >= 3)
                        {
                            payload["translation"] = new JArray(
                                SafeDouble(translation.ElementAtOrDefault(0)),
                                SafeDouble(translation.ElementAtOrDefault(1)),
                                SafeDouble(translation.ElementAtOrDefault(2))
                            );
                        }
                    });
                    TrySet(() =>
                    {
                        var orientationObj = view.Orientation2 as object[];
                        if (orientationObj != null && orientationObj.Length > 0)
                        {
                            payload["orientation"] = new JArray(orientationObj.Select(SafeDouble));
                        }
                        else
                        {
                            var xform = view.Orientation as MathTransform;
                            if (xform != null)
                            {
                                var data = xform.ArrayData as object[];
                                if (data != null) payload["orientation"] = new JArray(data.Select(SafeDouble));
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                payload["error"] = ex.Message;
            }
            return payload;
        }

        private void AddShapeDescriptors(JObject item, object obj)
        {
            try
            {
                // Sketch segment details
                var seg = obj as SketchSegment;
                if (seg != null)
                {
                    var segType = (swSketchSegments_e)seg.GetType();
                    var segObj = new JObject
                    {
                        ["type"] = segType.ToString()
                    };

                    TrySet(() => segObj["length"] = SafeDouble(seg.GetLength()));
                    TrySet(() =>
                    {
                        if (segType == swSketchSegments_e.swSketchARC)
                        {
                            var arc = seg as SketchArc;
                            if (arc != null)
                            {
                                segObj["radius"] = SafeDouble(arc.GetRadius());
                            }
                        }
                    });

                    item["segment"] = segObj;
                    return;
                }

                // Sketch summary
                var sketch = obj as Sketch;
                if (sketch != null)
                {
                    var segs = sketch.GetSketchSegments() as object[];
                    if (segs != null)
                    {
                        var counts = segs
                            .OfType<SketchSegment>()
                            .GroupBy(s => (swSketchSegments_e)s.GetType())
                            .ToDictionary(g => g.Key.ToString(), g => g.Count());
                        var summary = new JObject();
                        foreach (var kvp in counts) summary[kvp.Key] = kvp.Value;
                        summary["total"] = segs.Length;
                        item["sketch"] = summary;
                    }
                    return;
                }

                // Face geometry
                var face = obj as Face2;
                if (face != null)
                {
                    var geom = new JObject();
                    TrySet(() => geom["area"] = SafeDouble(face.GetArea()));
                    TrySet(() =>
                    {
                        var box = face.GetBox();
                        var bbox = ToBoundingBox(box);
                        if (bbox != null) geom["bbox"] = bbox;
                    });
                    TrySet(() =>
                    {
                        var surf = face.GetSurface();
                        if (surf != null)
                        {
                            geom["surfaceType"] = ((swSurfaceTypes_e)surf.Identity()).ToString();
                        }
                    });
                    if (geom.Count > 0) item["geometry"] = geom;
                    return;
                }

                // Edge geometry
                var edge = obj as Edge;
                if (edge != null)
                {
                    var geom = new JObject();
                    TrySet(() =>
                    {
                        var curve = edge.GetCurve();
                        if (curve != null)
                        {
                            var bbox = ToBoundingBox(curve.GetBoundingBox());
                            if (bbox != null) geom["bbox"] = bbox;
                        }
                    });
                    if (geom.Count > 0) item["geometry"] = geom;
                    return;
                }

                // Body geometry
                var body = obj as Body2;
                if (body != null)
                {
                    var geom = new JObject();
                    TrySet(() =>
                    {
                        var bbox = ToBoundingBox(body.GetBodyBox());
                        if (bbox != null) geom["bbox"] = bbox;
                    });
                    if (geom.Count > 0) item["geometry"] = geom;
                }
            }
            catch { }
        }

        private static JArray ToBoundingBox(object box)
        {
            try
            {
                var arr = box as object[];
                if (arr == null || arr.Length < 6) return null;
                return new JArray(
                    SafeDouble(arr[0]), SafeDouble(arr[1]), SafeDouble(arr[2]),
                    SafeDouble(arr[3]), SafeDouble(arr[4]), SafeDouble(arr[5])
                );
            }
            catch { return null; }
        }

        private static double SafeDouble(object value)
        {
            try { return Convert.ToDouble(value); } catch { return 0; }
        }

        private static void TrySet(Action action)
        {
            try { action(); } catch { }
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
                // Use JsonConvert to avoid relying on extension overloads that may differ across Newtonsoft versions
                var line = JsonConvert.SerializeObject(record, Formatting.None);
                try { OnEventJson?.Invoke(line); } catch { }
                try { AddinStatusLogger.Log("API", line); } catch { }
            }
            catch { }
        }
    }
}
