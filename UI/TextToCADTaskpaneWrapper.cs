using System;
using System.Windows.Forms.Integration;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace AICAD.UI
{
    /// <summary>
    /// WinForms wrapper control that hosts the WPF TextToCADTaskpaneWpf control.
    /// This allows SolidWorks COM API to work with the WPF control via WindowsFormsHost.
    /// </summary>
    public class TextToCADTaskpaneWrapper : UserControl
    {
        private ElementHost _elementHost;
        private TextToCADTaskpaneWpf _wpfControl;

        public TextToCADTaskpaneWpf WpfControl => _wpfControl;

        public TextToCADTaskpaneWrapper(ISldWorks swApp)
        {
            InitializeComponent(swApp);
        }

        private void InitializeComponent(ISldWorks swApp)
        {
            // Create ElementHost to host the WPF control
            _elementHost = new ElementHost
            {
                Dock = DockStyle.Fill,
                TabStop = true
            };

            // Create the WPF control
            _wpfControl = new TextToCADTaskpaneWpf(swApp);
            _elementHost.Child = _wpfControl;

            // Enable keyboard interop so WPF controls inside ElementHost receive keyboard input reliably
            try
            {
                // ElementHost.EnableModelessKeyboardInterop expects a Window instance; get the containing window for the control
                var wnd = System.Windows.Window.GetWindow(_wpfControl);
                if (wnd != null) System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(wnd);
            }
            catch { }

            // Ensure clicks and focus on the host forward focus into the WPF prompt
            // Ensure focus is set asynchronously to avoid host input capture issues
            _elementHost.GotFocus += (s, e) => { try { _elementHost.Focus(); this.BeginInvoke(new Action(() => _wpfControl.FocusPrompt())); } catch { } };
            _elementHost.MouseDown += (s, e) => { try { _elementHost.Focus(); this.BeginInvoke(new Action(() => _wpfControl.FocusPrompt())); } catch { } };
            // Also forward right-click keyboard focus to type description so users can click into that textbox reliably
            _elementHost.MouseClick += (s, e) => { try { if (e.Button == System.Windows.Forms.MouseButtons.Right) { _elementHost.Focus(); this.BeginInvoke(new Action(() => _wpfControl.FocusTypeDescription())); } } catch { } };

            // Install simple input logging to help diagnose key delivery issues
            try
            {
                _elementHost.KeyDown += (s, e) => { try { AppendKeyLog($"ElementHost.KeyDown: KeyCode={e.KeyCode}, KeyData={e.KeyData}, Modifiers={e.Modifiers}"); } catch { } };
                _elementHost.KeyPress += (s, e) => { try { AppendKeyLog($"ElementHost.KeyPress: KeyChar={(int)e.KeyChar} ('{e.KeyChar}')"); } catch { } };
            }
            catch { }

            // Add ElementHost to this WinForms UserControl
            this.Controls.Add(_elementHost);

        }

        private void AppendKeyLog(string line)
        {
            try
            {
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AICAD_Keys.log");
                System.IO.File.AppendAllText(path, DateTime.Now.ToString("o") + " " + line + System.Environment.NewLine);
            }
            catch { }
        }
        }
    }
}
