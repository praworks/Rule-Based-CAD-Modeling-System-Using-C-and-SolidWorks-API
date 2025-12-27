using System;
using System.Windows.Forms.Integration;
using System.Windows.Forms;
using System.IO;
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
        private System.Windows.Forms.RichTextBox _fallbackRtb;

        // Expose WPF control and events to host code
        public TextToCADTaskpaneWpf WpfControl => _wpfControl;

        /// <summary>
        /// Raised when the WPF control requests a build
        /// </summary>
        public event EventHandler BuildRequested;

        /// <summary>
        /// Raised when the prompt text changes in WPF
        /// </summary>
        public event EventHandler<TextToCADTaskpaneWpf.PromptChangedEventArgs> PromptTextChanged;

        /// <summary>
        /// Raised when Apply Properties is requested in WPF
        /// </summary>
        public event EventHandler ApplyPropertiesRequested;

        /// <summary>
        /// Proxy to read/set the prompt text on the WPF control
        /// </summary>
        public string PromptText { get => _wpfControl?.PromptText; set { if (_wpfControl != null) _wpfControl.PromptText = value; } }

        /// <summary>
        /// Proxy to trigger WPF build programmatically
        /// </summary>
        public System.Threading.Tasks.Task RunBuildFromPromptAsync() => _wpfControl?.RunBuildFromPromptAsync() ?? System.Threading.Tasks.Task.CompletedTask;

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

            // Create the WPF control. Guard against any exceptions during WPF initialization
            // (some hosts/load orders can cause XAML/dispatcher errors). If WPF fails, provide
            // a simple WinForms fallback so the add-in doesn't crash the host.
            try
            {
                _wpfControl = new TextToCADTaskpaneWpf(swApp);
                _elementHost.Child = _wpfControl;

                // Forward WPF events to wrapper-level events so host (SwAddin) can subscribe
                try
                {
                    _wpfControl.BuildRequested += (s, e) =>
                    {
                        try { AICAD.Services.LocalLogger.Log("Wrapper: BuildRequested forwarded"); } catch { }
                        try { BuildRequested?.Invoke(this, EventArgs.Empty); } catch { }
                    };
                    _wpfControl.PromptTextChanged += (s, e) => { try { PromptTextChanged?.Invoke(this, e); } catch { } };
                    _wpfControl.ApplyPropertiesRequested += (s, e) => { try { ApplyPropertiesRequested?.Invoke(this, EventArgs.Empty); } catch { } };
                }
                catch (Exception evtEx)
                {
                    try { AICAD.Services.LocalLogger.Log("Wrapper: WPF event hookup failed: " + evtEx.Message); } catch { }
                }
            }
            catch (Exception wpfEx)
            {
                try { AICAD.Services.LocalLogger.Log("Wrapper: WPF initialization failed: " + wpfEx.Message); } catch { }

                // Hide the ElementHost (it may be unusable) and show a simple WinForms fallback control
                try
                {
                    _elementHost.Visible = false;
                    var lbl = new System.Windows.Forms.Label
                    {
                        Text = "AI-CAD UI failed to initialize (WPF)\r\nSee %TEMP%\\AICAD_Unhandled.log for details",
                        Dock = DockStyle.Fill,
                        TextAlign = System.Drawing.ContentAlignment.MiddleCenter
                    };
                    this.Controls.Add(lbl);
                }
                catch { }
            }

            // Enable keyboard interop so WPF controls inside ElementHost receive keyboard input reliably.
            // Do this after the WPF control is loaded and try multiple ways to obtain the containing Window.
            try
            {
                _wpfControl.Loaded += (s, e) =>
                {
                    try
                    {
                        var wnd = System.Windows.Window.GetWindow(_wpfControl);
                        if (wnd == null)
                        {
                            var src = System.Windows.PresentationSource.FromVisual(_wpfControl);
                            if (src is System.Windows.Interop.HwndSource hwndSrc && hwndSrc.RootVisual is System.Windows.Window rv)
                            {
                                wnd = rv;
                            }
                        }

                        if (wnd != null)
                        {
                            System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(wnd);
                        }
                    }
                    catch { }
                };

                // Optional RichTextBox fallback for hosts that swallow WM_CHAR (set env AICAD_USE_RTF_FALLBACK=1)
                try
                {
                    var useRtf = string.Equals(System.Environment.GetEnvironmentVariable("AICAD_USE_RTF_FALLBACK"), "1", System.StringComparison.Ordinal);
                    if (useRtf)
                    {
                        _fallbackRtb = new System.Windows.Forms.RichTextBox
                        {
                            Dock = System.Windows.Forms.DockStyle.Fill,
                            Multiline = true,
                            AcceptsTab = true,
                            BorderStyle = System.Windows.Forms.BorderStyle.None,
                            Visible = true
                        };

                        _fallbackRtb.TextChanged += (fs, fe) =>
                        {
                            try
                            {
                                var txt = _fallbackRtb.Text ?? string.Empty;
                                this.BeginInvoke(new Action(() => { try { if (_wpfControl != null) _wpfControl.PromptText = txt; } catch { } }));
                                try { AICAD.Services.AddinStatusLogger.Log("RTF", $"TextChanged len={txt.Length}"); } catch { }
                            }
                            catch { }
                        };

                        _fallbackRtb.KeyDown += (fs, fe) => { try { AICAD.Services.AddinStatusLogger.Log("RTF", $"KeyDown: {fe.KeyCode}"); } catch { } };
                        _fallbackRtb.KeyPress += (fs, fe) => { try { AICAD.Services.AddinStatusLogger.Log("RTF", $"KeyPress: {fe.KeyChar}"); } catch { } };

                        this.Controls.Add(_fallbackRtb);
                        _fallbackRtb.BringToFront();
                        try { AICAD.Services.AddinStatusLogger.Log("RTF", "RichTextBox fallback enabled"); } catch { }
                    }
                }
                catch { }
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
                try { AICAD.Services.AddinStatusLogger.Log("Key", line); } catch { }
            }
            catch { }
        }

        }
    }
