using System;
using System.Drawing;
using System.Windows.Forms;
using SolidWorks.TaskpaneCalculator.Services;

namespace SolidWorks.TaskpaneCalculator.UI
{
    public class CredentialDialog : Form
    {
        private TextBox _txtKey;
        private Button _btnSave;
        private Button _btnCancel;

        public bool Saved { get; private set; }

        public CredentialDialog()
        {
            Text = "Set GEMINI API Key";
            Size = new Size(520, 160);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;

            var lbl = new Label { Text = "Paste your GEMINI API key below (this will be saved to Windows Credential Manager):", Dock = DockStyle.Top, Height = 30 };
            _txtKey = new TextBox { Dock = DockStyle.Top, Multiline = false, Height = 24 }; 
            _btnSave = new Button { Text = "Save", Width = 90, Height = 28, DialogResult = DialogResult.OK };
            _btnCancel = new Button { Text = "Cancel", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };

            _btnSave.Click += OnSaveClick;
            _btnCancel.Click += (s, e) => { Saved = false; Close(); };

            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft };
            btnPanel.Controls.Add(_btnSave);
            btnPanel.Controls.Add(_btnCancel);

            Controls.Add(_txtKey);
            Controls.Add(lbl);
            Controls.Add(btnPanel);
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            var key = _txtKey.Text?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                MessageBox.Show(this, "API key cannot be empty.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var ok = CredentialManager.WriteGenericSecret("SolidWorksTextToCAD_GEMINI_API_KEY", key);
            if (!ok)
            {
                MessageBox.Show(this, "Failed to save credential. You can run the cmdkey command manually.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Saved = true;
            MessageBox.Show(this, "API key saved to Windows Credential Manager.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
    }
}
