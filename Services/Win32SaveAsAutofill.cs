using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AICAD.Services
{
    /// <summary>
    /// Helper to push a filename into the native Save As dialog.
    /// </summary>
    internal static class Win32SaveAsAutofill
    {
        private const int WM_SETTEXT = 0x000C;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        public static bool TrySetSaveAsFileName(string fileName, int timeoutMs = 3000)
        {
            try
            {
                var deadline = Environment.TickCount + timeoutMs;
                IntPtr dialog = IntPtr.Zero;

                while (dialog == IntPtr.Zero && Environment.TickCount < deadline)
                {
                    dialog = FindWindow("#32770", "Save As");
                    if (dialog == IntPtr.Zero)
                    {
                        dialog = FindWindow("#32770", null);
                        if (dialog == IntPtr.Zero)
                        {
                            System.Threading.Thread.Sleep(100);
                        }
                    }
                }

                if (dialog == IntPtr.Zero) return false;

                IntPtr edit = IntPtr.Zero;
                IntPtr child = IntPtr.Zero;
                for (int i = 0; i < 100; i++)
                {
                    child = FindWindowEx(dialog, child, "Edit", null);
                    if (child == IntPtr.Zero) break;

                    var cls = new StringBuilder(256);
                    GetClassName(child, cls, 256);
                    if (edit == IntPtr.Zero)
                    {
                        edit = child;
                        break;
                    }
                }

                if (edit == IntPtr.Zero) return false;

                SendMessage(edit, WM_SETTEXT, IntPtr.Zero, fileName);
                var verify = new StringBuilder(256);
                GetWindowText(edit, verify, 256);
                AddinLogger.Log(nameof(Win32SaveAsAutofill), $"Autofilled Save As with '{verify}'");
                return true;
            }
            catch (Exception ex)
            {
                AddinLogger.Error(nameof(Win32SaveAsAutofill), "TrySetSaveAsFileName failed", ex);
                return false;
            }
        }
    }
}
