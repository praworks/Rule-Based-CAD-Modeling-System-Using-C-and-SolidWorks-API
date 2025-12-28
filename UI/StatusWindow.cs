using System;
using System.Drawing;
using System.Windows.Forms;

namespace AICAD.UI
{
    public class StatusWindowWinForms : Form
    {
        public RichTextBox StatusConsole { get; private set; }
        public TextBox ErrorTextBox { get; private set; }
        
        private Button _btnCopyAll;
        private Button _btnClear;
        private Button _btnExpand;
        private Button _btnCopyError;
        private Button _btnCopyRun;
        private bool _expanded = false;
        private Timer _restTimer;
        private Color _restColor = Color.FromArgb(179, 229, 179); // Pastel green
        private Color _activeColor = Color.FromArgb(255, 243, 205); // Light yellow when active

        public event EventHandler CopyErrorClicked;
        public event EventHandler CopyRunClicked;

        public StatusWindowWinForms()
        {
            Text = "AI-CAD-December - Status Console";
            Size = new Size(800, 300);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            MinimizeBox = true;
            MaximizeBox = true;
            BackColor = _restColor;

            _restTimer = new Timer { Interval = 3000 };
            _restTimer.Tick += (s, e) => RestoreRestState();
            
            BuildUI();
        }

        private void BuildUI()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(10),
                BackColor = _restColor
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));    // toolbar
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // console
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));    // error row

            // Toolbar
            var bar = new FlowLayoutPanel 
            { 
                FlowDirection = FlowDirection.LeftToRight, 
                Dock = DockStyle.Fill, 
                WrapContents = false,
                Padding = new Padding(0, 5, 0, 5)
            };
            
            _btnCopyAll = new Button { Text = "Copy All", Height = 28, Width = 90 };
            _btnClear = new Button { Text = "Clear", Height = 28, Width = 80 };
            _btnExpand = new Button { Text = "Expand", Height = 28, Width = 90 };
            
            _btnCopyAll.Click += (s, e) => 
            { 
                try 
                { 
                    if (!string.IsNullOrEmpty(StatusConsole?.Text)) 
                        Clipboard.SetText(StatusConsole.Text); 
                } 
                catch { } 
            };
            
            _btnClear.Click += (s, e) => 
            { 
                try { StatusConsole.Clear(); } 
                catch { } 
            };
            
            _btnExpand.Click += (s, e) => 
            {
                _expanded = !_expanded;
                if (_expanded)
                {
                    WindowState = FormWindowState.Maximized;
                    _btnExpand.Text = "Restore";
                }
                else
                {
                    WindowState = FormWindowState.Normal;
                    _btnExpand.Text = "Expand";
                }
            };
            
            bar.Controls.Add(_btnCopyAll);
            bar.Controls.Add(_btnClear);
            bar.Controls.Add(_btnExpand);

            // Console
            StatusConsole = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.Black,
                ForeColor = Color.Gainsboro,
                Font = new Font("Consolas", 9f, FontStyle.Regular),
                BorderStyle = BorderStyle.FixedSingle,
                WordWrap = false,
                HideSelection = false
            };
            
            var ctx = new ContextMenuStrip();
            ctx.Items.Add(new ToolStripMenuItem("Copy", null, (s, e) => { try { StatusConsole.Copy(); } catch { } }));
            ctx.Items.Add(new ToolStripMenuItem("Copy All", null, (s, e) => { try { Clipboard.SetText(StatusConsole.Text); } catch { } }));
            ctx.Items.Add(new ToolStripMenuItem("Select All", null, (s, e) => { try { StatusConsole.SelectAll(); } catch { } }));
            StatusConsole.ContextMenuStrip = ctx;

            // Error row
            var errRow = new FlowLayoutPanel 
            { 
                FlowDirection = FlowDirection.LeftToRight, 
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 5, 0, 0)
            };
            
            ErrorTextBox = new TextBox 
            { 
                ReadOnly = true, 
                BorderStyle = BorderStyle.FixedSingle, 
                Width = 500,
                Anchor = AnchorStyles.Left | AnchorStyles.Right
            };
            
            _btnCopyError = new Button { Text = "Copy Error", Width = 100, Height = 28 };
            _btnCopyError.Click += (s, e) => CopyErrorClicked?.Invoke(this, EventArgs.Empty);
            
            _btnCopyRun = new Button { Text = "Copy Run", Width = 100, Height = 28 };
            _btnCopyRun.Click += (s, e) => CopyRunClicked?.Invoke(this, EventArgs.Empty);
            
            errRow.Controls.Add(ErrorTextBox);
            errRow.Controls.Add(_btnCopyError);
            errRow.Controls.Add(_btnCopyRun);

            root.Controls.Add(bar, 0, 0);
            root.Controls.Add(StatusConsole, 0, 1);
            root.Controls.Add(errRow, 0, 2);

            Controls.Add(root);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                // Ensure window is visible and not minimized/hidden
                if (WindowState == FormWindowState.Minimized)
                    WindowState = FormWindowState.Normal;
                BringToFront();
                Activate();
            }
            catch { }
        }

        /// <summary>
        /// Append a status line and flash active color briefly.
        /// </summary>
        public void AppendStatus(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            try
            {
                if (StatusConsole.InvokeRequired)
                {
                    StatusConsole.Invoke(new Action(() => AppendStatus(line)));
                    return;
                }

                StatusConsole.AppendText(line + Environment.NewLine);
                SetActiveState();
            }
            catch { }
        }

        /// <summary>
        /// Replace the status content and set active color.
        /// </summary>
        public void SetStatus(string text)
        {
            try
            {
                if (StatusConsole.InvokeRequired)
                {
                    StatusConsole.Invoke(new Action(() => SetStatus(text)));
                    return;
                }

                StatusConsole.Text = text ?? string.Empty;
                SetActiveState();
            }
            catch { }
        }

        private void SetActiveState()
        {
            try
            {
                BackColor = _activeColor;
                // also set root/backing controls if present
                foreach (Control c in Controls)
                {
                    if (c is TableLayoutPanel tbl)
                        tbl.BackColor = _activeColor;
                }
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
                BackColor = _restColor;
                foreach (Control c in Controls)
                {
                    if (c is TableLayoutPanel tbl)
                        tbl.BackColor = _restColor;
                }
            }
            catch { }
        }
    }
}
