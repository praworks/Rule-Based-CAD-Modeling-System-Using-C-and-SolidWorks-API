using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using AICAD.Services;

namespace AICAD
{
    [ComVisible(true)]
    [Guid("D5B8E2F9-2F3E-4D44-907F-2B983D32AF37")]
    [ProgId("AICad.TaskpaneAddin")]
    public class AICadAddin : ISwAddin
    {
        private ISldWorks _app;
        private int _addinId;
        private TaskpaneView _textToCadTaskpaneView;
        private UI.TextToCADTaskpaneWrapper _textToCadControl;
        private SeriesManager _seriesManager;
        private string _pendingPartName = null;
        private IModelDoc2 _currentDoc = null;
        private PartDoc _activePartDoc = null;
        private DPartDocEvents_RegenPostNotifyEventHandler _partRegenPostHandler;

        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            _app = (ISldWorks)ThisSW;
            _addinId = Cookie;
            _app.SetAddinCallbackInfo2(0, this, _addinId);

            try { AddinStatusLogger.Log("AICadAddin", $"ConnectToSW called cookie={Cookie}"); } catch { }
            try
            {
                // Initialize NameEasy series manager
                _seriesManager = new SeriesManager();
                try { AddinStatusLogger.Log("AICadAddin", "SeriesManager initialized"); } catch { }

                // Create AI-CAD-December Taskpane (main) using WPF
                var iconPath = TryGetTaskpaneIconPath();
                _textToCadTaskpaneView = _app.CreateTaskpaneView2(iconPath, "AI-CAD-December");
                _textToCadControl = new UI.TextToCADTaskpaneWrapper(_app);
                _textToCadTaskpaneView.DisplayWindowFromHandlex64(_textToCadControl.Handle.ToInt64());
                try { AddinStatusLogger.Log("AICadAddin", "Created AI-CAD-December taskpane with WPF design"); } catch { }

                // Hook SolidWorks application events
                AttachEventHandlers();
                try { AddinStatusLogger.Log("AICadAddin", "Event handlers attached"); } catch { }

                // Hook regen for active document
                try
                {
                    HookDocRegenForActiveDocument();
                    try { AddinStatusLogger.Log("AICadAddin", "Hooked RegenPostNotify for active document"); } catch { }
                }
                catch (Exception hookEx)
                {
                    try { AddinStatusLogger.Error("AICadAddin", "Failed to hook RegenPostNotify", hookEx); } catch { }
                }

                // Initial sync
                try
                {
                    SyncUiFromActiveDocument();
                    try { AddinStatusLogger.Log("AICadAddin", "Initial sync completed"); } catch { }
                }
                catch (Exception syncEx)
                {
                    try { AddinStatusLogger.Error("AICadAddin", "Initial sync failed", syncEx); } catch { }
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("Failed to create taskpane: " + ex.Message);
                try { AddinStatusLogger.Error("AICadAddin", "Taskpane creation failed", ex); } catch { }
            }

            try { AddinStatusLogger.Log("AICadAddin", "ConnectToSW completed"); } catch { }
            return true;
        }

        private void AttachEventHandlers()
        {
            try
            {
                (_app as SldWorks).CommandCloseNotify += OnCommandClose;
                (_app as SldWorks).FileNewNotify2 += OnFileNewNotify2;
                (_app as SldWorks).ActiveDocChangeNotify += OnActiveDocChange;
                try { AddinStatusLogger.Log("AICadAddin", "Event handlers attached"); } catch { }
            }
            catch (Exception ex)
            {
                try { AddinStatusLogger.Error("AICadAddin", "Failed to attach event handlers", ex); } catch { }
            }
        }

        private void DetachEventHandlers()
        {
            try
            {
                if (_app != null)
                {
                    (_app as SldWorks).CommandCloseNotify -= OnCommandClose;
                    (_app as SldWorks).FileNewNotify2 -= OnFileNewNotify2;
                    (_app as SldWorks).ActiveDocChangeNotify -= OnActiveDocChange;
                }
                try { AddinStatusLogger.Log("AICadAddin", "Event handlers detached"); } catch { }
            }
            catch (Exception ex)
            {
                try { AddinStatusLogger.Error("AICadAddin", "Failed to detach event handlers", ex); } catch { }
            }
        }

        private void HookDocRegenForActiveDocument()
        {
            try
            {
                UnhookDocRegen();
                var doc = _app?.ActiveDoc as IModelDoc2;
                var part = doc as PartDoc;
                if (part != null)
                {
                    _activePartDoc = part;
                    _partRegenPostHandler = new DPartDocEvents_RegenPostNotifyEventHandler(OnPartRegenPost);
                    _activePartDoc.RegenPostNotify += _partRegenPostHandler;
                    try { AddinStatusLogger.Log("AICadAddin", "RegenPostNotify hooked"); } catch { }
                }
            }
            catch (Exception ex)
            {
                try { AddinStatusLogger.Error("AICadAddin", "Error hooking RegenPostNotify", ex); } catch { }
            }
        }

        private void UnhookDocRegen()
        {
            try
            {
                if (_activePartDoc != null && _partRegenPostHandler != null)
                {
                    _activePartDoc.RegenPostNotify -= _partRegenPostHandler;
                }
            }
            catch (Exception ex)
            {
                try { AddinStatusLogger.Error("AICadAddin", "Error unhooking RegenPostNotify", ex); } catch { }
            }
            finally
            {
                _activePartDoc = null;
                _partRegenPostHandler = null;
            }
        }

        private int OnPartRegenPost()
        {
            try
            {
                try { AddinStatusLogger.Log("AICadAddin", "RegenPostNotify fired; syncing UI"); } catch { }
                SyncUiFromActiveDocument();
            }
            catch (Exception ex)
            {
                try { AddinStatusLogger.Error("AICadAddin", "Error during RegenPostNotify sync", ex); } catch { }
            }
            return 0;
        }

        private int OnCommandClose(int command, int reason)
        {
            try { AddinStatusLogger.Log("AICadAddin", $"Command close: ID={command}, Reason={reason}"); } catch { }

            if (command == 548) // Properties dialog closed
            {
                try
                {
                    SyncUiFromActiveDocument();
                }
                catch (Exception ex)
                {
                    try { AddinStatusLogger.Error("AICadAddin", "Error syncing after properties close", ex); } catch { }
                }
            }
            return 0;
        }

        private int OnFileNewNotify2(object newDoc, int docType, string templateName)
        {
            try
            {
                if (docType == (int)swDocumentTypes_e.swDocPART)
                {
                    _currentDoc = newDoc as IModelDoc2;
                    try { AddinStatusLogger.Log("AICadAddin", "New part document created"); } catch { }
                }
            }
            catch (Exception ex)
            {
                try { AddinStatusLogger.Error("AICadAddin", "Error in FileNewNotify2", ex); } catch { }
            }
            return 0;
        }

        private int OnActiveDocChange()
        {
            try
            {
                var activeDoc = _app.ActiveDoc as IModelDoc2;
                if (activeDoc != null)
                {
                    _currentDoc = activeDoc;
                    HookDocRegenForActiveDocument();
                    SyncUiFromActiveDocument();
                }
            }
            catch (Exception ex)
            {
                try { AddinStatusLogger.Error("AICadAddin", "Error in ActiveDocChange", ex); } catch { }
            }
            return 0;
        }

        private void SyncUiFromActiveDocument()
        {
            try
            {
                if (_app == null) return;
                var doc = _app.ActiveDoc as IModelDoc2;
                if (doc == null) return;
                var ext = doc.Extension;
                if (ext == null) return;

                var custPropMgr = ext.CustomPropertyManager[""];
                string material = GetCustomProperty(custPropMgr, "Material");
                string description = GetCustomProperty(custPropMgr, "Description");
                string mass = GetPartMass(doc);
                string partNo = GetCustomProperty(custPropMgr, "PartNo");

                // Update UI - call LoadFromProperties on the taskpane
                if (_textToCadControl != null)
                {
                    try
                    {
                        _textToCadControl.WpfControl?.LoadFromProperties(material, description, mass, partNo);
                        try { AddinStatusLogger.Log("AICadAddin", $"Synced taskpane: Mat={material}, Desc={description}, Mass={mass}, PartNo={partNo}"); } catch { }
                    }
                    catch (Exception uiEx)
                    {
                        try { AddinStatusLogger.Error("AICadAddin", "Failed to update taskpane UI", uiEx); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                try { AddinStatusLogger.Error("AICadAddin", "Error syncing UI from document", ex); } catch { }
            }
        }

        private string GetPartMass(IModelDoc2 doc)
        {
            try
            {
                if (doc == null) return "0.000";
                var ext = doc.Extension;
                if (ext == null) return "0.000";
                var custPropMgr = ext.CustomPropertyManager[""];
                if (custPropMgr == null) return "0.000";

                string val = string.Empty;
                string resolved = string.Empty;
                custPropMgr.Get4("Mass", false, out val, out resolved);

                if (!string.IsNullOrEmpty(resolved) && resolved != val && !resolved.Contains("SW-Mass"))
                {
                    if (double.TryParse(resolved, out double massVal))
                    {
                        return massVal.ToString("F3");
                    }
                }
            }
            catch { }
            return "0.000";
        }

        private string GetCustomProperty(ICustomPropertyManager mgr, string name)
        {
            try
            {
                if (mgr == null) return string.Empty;
                string val = string.Empty;
                string resolved = string.Empty;
                mgr.Get4(name, false, out val, out resolved);
                return string.IsNullOrEmpty(resolved) ? val : resolved;
            }
            catch
            {
                return string.Empty;
            }
        }

        public void SetPartProperties(IModelDoc2 doc, string material, string typeDescription, string partName)
        {
            try
            {
                var custPropMgr = doc.Extension.CustomPropertyManager[""];
                if (custPropMgr != null)
                {
                    if (!string.IsNullOrEmpty(material))
                    {
                        custPropMgr.Add3("Material", (int)swCustomInfoType_e.swCustomInfoText, material, (int)swCustomPropertyAddOption_e.swCustomPropertyDeleteAndAdd);
                        try { AddinStatusLogger.Log("AICadAddin", $"Set Material: {material}"); } catch { }

                        // Apply material to part model
                        try
                        {
                            var partDoc = doc as PartDoc;
                            if (partDoc != null)
                            {
                                string database = "solidworks materials.sldmat";
                                partDoc.SetMaterialPropertyName2("", database, material);
                                try { AddinStatusLogger.Log("AICadAddin", $"Applied material to model: {material}"); } catch { }
                            }
                        }
                        catch (Exception matEx)
                        {
                            try { AddinStatusLogger.Error("AICadAddin", "Material application to model failed", matEx); } catch { }
                        }
                    }

                    if (!string.IsNullOrEmpty(typeDescription))
                    {
                        custPropMgr.Add3("Description", (int)swCustomInfoType_e.swCustomInfoText, typeDescription, (int)swCustomPropertyAddOption_e.swCustomPropertyDeleteAndAdd);
                    }

                    string filename = System.IO.Path.GetFileNameWithoutExtension(doc.GetPathName());
                    if (!string.IsNullOrEmpty(filename))
                    {
                        custPropMgr.Add3("Mass", (int)swCustomInfoType_e.swCustomInfoText, $"\"SW-Mass@{filename}.SLDPRT\"", (int)swCustomPropertyAddOption_e.swCustomPropertyDeleteAndAdd);
                    }

                    if (!string.IsNullOrEmpty(partName))
                    {
                        custPropMgr.Add3("PartNo", (int)swCustomInfoType_e.swCustomInfoText, partName, (int)swCustomPropertyAddOption_e.swCustomPropertyDeleteAndAdd);
                    }
                }
            }
            catch (Exception ex)
            {
                try { AddinStatusLogger.Error("AICadAddin", "Error setting custom properties", ex); } catch { }
            }
        }

        private static string TryGetTaskpaneIconPath()
        {
            try
            {
                // 1) Allow overriding via environment variable
                var envIcon = System.Environment.GetEnvironmentVariable("AICAD_TASKPANE_ICON")?.Trim('"');
                if (!string.IsNullOrWhiteSpace(envIcon) && File.Exists(envIcon)) return envIcon;

                // 2) Look in app folder Resources for common names
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates = new[]
                {
                    Path.Combine(baseDir, "Resources", "taskpane_icon.bmp"),
                    Path.Combine(baseDir, "Resources", "taskpane_icon.png"),
                    Path.Combine(baseDir, "taskpane_icon.bmp"),
                    Path.Combine(baseDir, "taskpane_icon.png")
                };
                foreach (var p in candidates) if (File.Exists(p)) return p;
            }
            catch { }
            return string.Empty; // fallback to default SW icon
        }

        public bool DisconnectFromSW()
        {
            try
            {
                DetachEventHandlers();
                UnhookDocRegen();

                if (_textToCadTaskpaneView != null)
                {
                    _textToCadTaskpaneView.DeleteView();
                    Marshal.ReleaseComObject(_textToCadTaskpaneView);
                    _textToCadTaskpaneView = null;
                    try { AddinStatusLogger.Log("AICadAddin", "Deleted AI-CAD-December taskpane"); } catch { }
                }

                if (_seriesManager != null)
                {
                    _seriesManager.Dispose();
                    _seriesManager = null;
                    try { AddinStatusLogger.Log("AICadAddin", "Disposed SeriesManager"); } catch { }
                }
            }
            catch { }

            if (_app != null)
            {
                Marshal.ReleaseComObject(_app);
                _app = null;
                try { AddinStatusLogger.Log("AICadAddin", "Released SldWorks COM object"); } catch { }
            }

            try { AddinStatusLogger.Log("AICadAddin", "DisconnectFromSW completed"); } catch { }
            return true;
        }

        // COM registration to add required SolidWorks registry entries
        [ComRegisterFunction]
        public static void ComRegister(Type t)
        {
            try
            {
                string addinKey = $"SOFTWARE\\SolidWorks\\Addins\\{{{t.GUID.ToString().ToUpper()}}}";
                using (var rk = Registry.LocalMachine.CreateSubKey(addinKey))
                {
                    rk?.SetValue(null, 1); // Load/visible
                    rk?.SetValue("Title", "AI-CAD-December");
                    rk?.SetValue("Description", "AI-assisted CAD taskpane for generating simple parametric models from natural language.");
                }

                string startupKey = $"Software\\SolidWorks\\AddInsStartup\\{{{t.GUID.ToString().ToUpper()}}}";
                using (var rkcu = Registry.CurrentUser.CreateSubKey(startupKey))
                {
                    rkcu?.SetValue(null, 1); // Load at startup for current user
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("Add-in registration failed: " + ex.Message);
                throw;
            }
        }

        [ComUnregisterFunction]
        public static void ComUnregister(Type t)
        {
            try
            {
                string addinKey = $"SOFTWARE\\SolidWorks\\Addins\\{{{t.GUID.ToString().ToUpper()}}}";
                Registry.LocalMachine.DeleteSubKeyTree(addinKey, false);

                string startupKey = $"Software\\SolidWorks\\AddInsStartup\\{{{t.GUID.ToString().ToUpper()}}}";
                Registry.CurrentUser.DeleteSubKeyTree(startupKey, false);
            }
            catch
            {
                // Ignore errors during unregister
            }
        }
    }
}
