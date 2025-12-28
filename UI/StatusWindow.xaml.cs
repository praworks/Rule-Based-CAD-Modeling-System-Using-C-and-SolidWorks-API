using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AICAD.UI
{
    public partial class StatusWindow : Window
    {
        public RichTextBox StatusConsole => _statusConsole;
        public TextBox ErrorTextBox => _errorTextBox;

        private bool _expanded = false;
        private DispatcherTimer _restTimer;
        private Brush _restBrush;
        private Brush _activeBrush;

        public event EventHandler CopyErrorClicked;
        public event EventHandler CopyRunClicked;

        public StatusWindow()
        {
            InitializeComponent();

            // Resolve theme brushes from Theme.xaml only
            _restBrush = TryFindResource("StatusRestBrush") as Brush ?? TryFindResource("PanelBackground") as Brush;
            _activeBrush = TryFindResource("StatusActiveBrush") as Brush ?? TryFindResource("NavHoverBrush") as Brush;

            _restTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3000) };
            _restTimer.Tick += (s, e) => RestoreRestState();

            BtnCopyAll.Click += (s, e) => { try { var txt = new TextRange(_statusConsole.Document.ContentStart, _statusConsole.Document.ContentEnd).Text; if (!string.IsNullOrEmpty(txt)) Clipboard.SetText(txt.Replace('\u00A0',' ')); } catch { } };
            BtnClear.Click += (s, e) => { try { _statusConsole.Document.Blocks.Clear(); } catch { } };
            BtnExpand.Click += (s, e) => { try { _expanded = !_expanded; if (_expanded) { WindowState = WindowState.Maximized; BtnExpand.Content = "Restore"; } else { WindowState = WindowState.Normal; BtnExpand.Content = "Expand"; } } catch { } };

            BtnCopyError.Click += (s, e) => { try { CopyErrorClicked?.Invoke(this, EventArgs.Empty); } catch { } };
            BtnCopyRun.Click += (s, e) => { try { CopyRunClicked?.Invoke(this, EventArgs.Empty); } catch { } };

            var ctx = new ContextMenu();
            var miCopy = new MenuItem { Header = "Copy" };
            miCopy.Click += (s, e) => { try { _statusConsole.Copy(); } catch { } };
            var miCopyAll = new MenuItem { Header = "Copy All" };
            miCopyAll.Click += (s, e) => { try { var txt = new TextRange(_statusConsole.Document.ContentStart, _statusConsole.Document.ContentEnd).Text; Clipboard.SetText(txt.Replace('\u00A0',' ')); } catch { } };
            var miSelectAll = new MenuItem { Header = "Select All" };
            miSelectAll.Click += (s, e) => { try { _statusConsole.SelectAll(); } catch { } };
            ctx.Items.Add(miCopy);
            ctx.Items.Add(miCopyAll);
            ctx.Items.Add(miSelectAll);
            _statusConsole.ContextMenu = ctx;

            this.Background = _restBrush;
            RootPanel.Background = _restBrush;
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            try
            {
                if (WindowState == WindowState.Minimized)
                    WindowState = WindowState.Normal;
                Activate();
            }
            catch { }
        }

        public void AppendStatus(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(new Action(() => AppendStatus(line)));
                    return;
                }

                // Preserve spacing for programming-like text: use configured tab size and
                // replace regular spaces with non-breaking spaces so multiple spaces are shown.
                int tabSize = 4;
                try { var ts = TryFindResource("ConsoleTabSize"); if (ts is double d) tabSize = (int)d; } catch { }
                var spaces = new string(' ', tabSize);
                var formattedLine = line.Replace("\t", spaces).Replace(" ", "\u00A0");
                var para = new Paragraph(new Run(formattedLine)) { Margin = new Thickness(0) };
                _statusConsole.Document.Blocks.Add(para);
                _statusConsole.ScrollToEnd();
                SetActiveState();
            }
            catch { }
        }

        public void SetStatus(string text)
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(new Action(() => SetStatus(text)));
                    return;
                }

                _statusConsole.Document.Blocks.Clear();
                var t = text ?? string.Empty;
                int tabSize = 4;
                try { var ts = TryFindResource("ConsoleTabSize"); if (ts is double d) tabSize = (int)d; } catch { }
                var spaces = new string(' ', tabSize);
                var formatted = t.Replace("\t", spaces).Replace(" ", "\u00A0");
                _statusConsole.Document.Blocks.Add(new Paragraph(new Run(formatted)) { Margin = new Thickness(0) });
                _statusConsole.ScrollToEnd();
                SetActiveState();
            }
            catch { }
        }

        private void SetActiveState()
        {
            try
            {
                this.Background = _activeBrush;
                RootPanel.Background = _activeBrush;
                _restTimer.Stop();
                _restTimer.Start();
            }
            catch { }
        }

        private void RestoreRestState()
        {
            try
            {
                _restTimer.Stop();
                this.Background = _restBrush;
                RootPanel.Background = _restBrush;
            }
            catch { }
        }
    }
}
