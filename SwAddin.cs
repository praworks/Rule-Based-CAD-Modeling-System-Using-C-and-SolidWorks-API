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

        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            _app = (ISldWorks)ThisSW;
            _addinId = Cookie;
            _app.SetAddinCallbackInfo2(0, this, _addinId);

            try { AddinStatusLogger.Log("AICadAddin", $"ConnectToSW called cookie={Cookie}"); } catch { }
            try
            {
                // Create AI-CAD-December Taskpane (main) using WPF
                var iconPath = TryGetTaskpaneIconPath();
                _textToCadTaskpaneView = _app.CreateTaskpaneView2(iconPath, "AI-CAD-December");
                _textToCadControl = new UI.TextToCADTaskpaneWrapper(_app);
                _textToCadTaskpaneView.DisplayWindowFromHandlex64(_textToCadControl.Handle.ToInt64());
                try { AddinStatusLogger.Log("AICadAddin", "Created AI-CAD-December taskpane with WPF design"); } catch { }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("Failed to create taskpane: " + ex.Message);
                try { AddinStatusLogger.Error("AICadAddin", "Taskpane creation failed", ex); } catch { }
            }

            try { AddinStatusLogger.Log("AICadAddin", "ConnectToSW completed"); } catch { }
            return true;
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
                if (_textToCadTaskpaneView != null)
                {
                    _textToCadTaskpaneView.DeleteView();
                    Marshal.ReleaseComObject(_textToCadTaskpaneView);
                    _textToCadTaskpaneView = null;
                    try { AddinStatusLogger.Log("AICadAddin", "Deleted AI-CAD-December taskpane"); } catch { }
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
