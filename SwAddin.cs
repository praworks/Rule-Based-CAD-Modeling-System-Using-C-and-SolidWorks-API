using System;
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
    private UI.TextToCADTaskpane _textToCadControl;
    private TaskpaneView _dbSettingsTaskpaneView;
    private UI.DBSettingsHost _dbSettingsControl;

        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            _app = (ISldWorks)ThisSW;
            _addinId = Cookie;
            _app.SetAddinCallbackInfo2(0, this, _addinId);

            try { AddinStatusLogger.Log("AICadAddin", $"ConnectToSW called cookie={Cookie}"); } catch { }
            try
            {
                // Create AI-CAD Taskpane (main)
                _textToCadTaskpaneView = _app.CreateTaskpaneView2(string.Empty, "AI-CAD");
                _textToCadControl = new UI.TextToCADTaskpane(_app);
                _textToCadTaskpaneView.DisplayWindowFromHandlex64(_textToCadControl.Handle.ToInt64());
                try { AddinStatusLogger.Log("AICadAddin", "Created AI-CAD taskpane"); } catch { }

                // Create a second taskpane for DB Settings (WPF hosted in WinForms ElementHost)
                try
                {
                    _dbSettingsTaskpaneView = _app.CreateTaskpaneView2(string.Empty, "DB Settings");
                    _dbSettingsControl = new UI.DBSettingsHost();
                    _dbSettingsTaskpaneView.DisplayWindowFromHandlex64(_dbSettingsControl.Handle.ToInt64());
                    try { AddinStatusLogger.Log("AICadAddin", "Created DB Settings taskpane"); } catch { }
                }
                catch (Exception ex2)
                {
                    System.Windows.Forms.MessageBox.Show("Failed to create DB Settings taskpane: " + ex2.Message);
                    try { AddinStatusLogger.Error("AICadAddin", "DB Settings taskpane creation failed", ex2); } catch { }
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

        public bool DisconnectFromSW()
        {
            try
            {
                if (_textToCadTaskpaneView != null)
                {
                    _textToCadTaskpaneView.DeleteView();
                    Marshal.ReleaseComObject(_textToCadTaskpaneView);
                    _textToCadTaskpaneView = null;
                    try { AddinStatusLogger.Log("AICadAddin", "Deleted AI-CAD taskpane"); } catch { }
                }
                if (_dbSettingsTaskpaneView != null)
                {
                    _dbSettingsTaskpaneView.DeleteView();
                    Marshal.ReleaseComObject(_dbSettingsTaskpaneView);
                    _dbSettingsTaskpaneView = null;
                    try { AddinStatusLogger.Log("AICadAddin", "Deleted DB Settings taskpane"); } catch { }
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
                    rk?.SetValue("Title", "AI-CAD");
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
