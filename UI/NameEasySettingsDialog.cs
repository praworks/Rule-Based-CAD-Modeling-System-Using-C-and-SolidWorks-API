using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AICAD.UI
{
    /// <summary>
    /// Settings dialog for configuring NameEasy database location.
    /// </summary>
    public class NameEasySettingsDialog : Form
    {
        private TextBox _dbPathTextBox;
        private Button _browseButton;
        private Button _saveButton;
        private Button _cancelButton;
        private Label _infoLabel;

        public string DatabasePath { get; private set; }

        public NameEasySettingsDialog(string currentPath)
        {
            DatabasePath = currentPath;
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            Text = "NameEasy Settings";
            Size = new Size(500, 200);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            _infoLabel = new Label
            {
                Text = "Database Location:",
                Location = new Point(10, 10),
                Size = new Size(480, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            Controls.Add(_infoLabel);

            _dbPathTextBox = new TextBox
            {
                Location = new Point(10, 35),
                Size = new Size(380, 25),
                Text = DatabasePath,
                Font = new Font("Segoe UI", 9F)
            };
            Controls.Add(_dbPathTextBox);

            _browseButton = new Button
            {
                Text = "Browse...",
                Location = new Point(395, 33),
                Size = new Size(85, 27),
                Font = new Font("Segoe UI", 9F)
            };
            _browseButton.Click += OnBrowseClick;
            Controls.Add(_browseButton);

            var helpLabel = new Label
            {
                Text = "Choose where to store the part naming database (NameEasy.db).\nDefault: Add-in installation folder",
                Location = new Point(10, 70),
                Size = new Size(480, 40),
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.Gray
            };
            Controls.Add(helpLabel);

            _saveButton = new Button
            {
                Text = "Save",
                Location = new Point(310, 120),
                Size = new Size(80, 30),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                DialogResult = DialogResult.OK
            };
            _saveButton.Click += OnSaveClick;
            Controls.Add(_saveButton);

            _cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(400, 120),
                Size = new Size(80, 30),
                Font = new Font("Segoe UI", 9F),
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(_cancelButton);

            AcceptButton = _saveButton;
            CancelButton = _cancelButton;
        }

        private void OnBrowseClick(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select folder for NameEasy.db database";
                fbd.ShowNewFolderButton = true;

                var currentPath = _dbPathTextBox.Text;
                if (!string.IsNullOrEmpty(currentPath) && File.Exists(currentPath))
                {
                    fbd.SelectedPath = Path.GetDirectoryName(currentPath);
                }

                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    _dbPathTextBox.Text = Path.Combine(fbd.SelectedPath, "NameEasy.db");
                }
            }
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            var newPath = _dbPathTextBox.Text.Trim();

            if (string.IsNullOrEmpty(newPath))
            {
                MessageBox.Show("Please specify a database path.", "Invalid Path", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var dir = Path.GetDirectoryName(newPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    var result = MessageBox.Show(
                        $"The folder does not exist:\n{dir}\n\nCreate it now?",
                        "Create Folder",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (result == DialogResult.Yes)
                    {
                        Directory.CreateDirectory(dir);
                    }
                    else
                    {
                        return;
                    }
                }

                DatabasePath = newPath;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Invalid path: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
