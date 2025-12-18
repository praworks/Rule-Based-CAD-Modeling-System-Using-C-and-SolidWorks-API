using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using SolidWorks.TaskpaneCalculator.Services;

namespace SolidWorks.TaskpaneCalculator
{
    [ComVisible(true)]
    [Guid("5B3BE76C-6E58-4CD2-9CAC-1F69F969BA41")]
    [ProgId("AICad.TaskpaneAddin.Test")]
    public class AICadPreviewAddin : ISwAddin
    {
        private ISldWorks _app;
        private int _addinId;
        private TaskpaneView _taskpaneView;
        private UI.TextToCADTaskpane _control;

        public bool ConnectToSW(object ThisSW, int cookie)
        {
            _app = (ISldWorks)ThisSW;
            _addinId = cookie;
            _app.SetAddinCallbackInfo2(0, this, _addinId);
            try { AddinStatusLogger.Log("AICadPreviewAddin", $"ConnectToSW called cookie={cookie}"); } catch { }
            try
            {
                _taskpaneView = _app.CreateTaskpaneView2(string.Empty, "AI-CAD Nov");
                _control = new UI.TextToCADTaskpane(_app);
                _taskpaneView.DisplayWindowFromHandlex64(_control.Handle.ToInt64());
                try { AddinStatusLogger.Log("AICadPreviewAddin", "Created AI-CAD Nov taskpane"); } catch { }
            }
            catch (Exception ex)
            {
                try { AddinStatusLogger.Error("AICadPreviewAddin", "Taskpane creation failed", ex); } catch { }
                System.Windows.Forms.MessageBox.Show("Failed to create taskpane: " + ex.Message);
            }
            try { AddinStatusLogger.Log("AICadPreviewAddin", "ConnectToSW completed"); } catch { }
            return true;
        }

        public bool DisconnectFromSW()
        {
            try
            {
                if (_taskpaneView != null)
                {
                    _taskpaneView.DeleteView();
                    Marshal.ReleaseComObject(_taskpaneView);
                    _taskpaneView = null;
                }
            }
            catch { }

            if (_app != null)
            {
                Marshal.ReleaseComObject(_app);
                _app = null;
            }
            try { AddinStatusLogger.Log("AICadPreviewAddin", "DisconnectFromSW completed"); } catch { }
            return true;
        }

        [ComRegisterFunction]
        public static void ComRegister(Type t)
        {
            try
            {
                string addinKey = $"SOFTWARE\\SolidWorks\\Addins\\{{{t.GUID.ToString().ToUpper()}}}";
                using (var rk = Registry.LocalMachine.CreateSubKey(addinKey))
                {
                    rk?.SetValue(null, 1);
                    rk?.SetValue("Title", "AI-CAD Nov");
                    rk?.SetValue("Description", "AI-powered CAD add-in for SolidWorks.");
                }
                string startupKey = $"Software\\SolidWorks\\AddInsStartup\\{{{t.GUID.ToString().ToUpper()}}}";
                using (var rkcu = Registry.CurrentUser.CreateSubKey(startupKey))
                {
                    rkcu?.SetValue(null, 1);
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
            catch { }
        }
    }
}
