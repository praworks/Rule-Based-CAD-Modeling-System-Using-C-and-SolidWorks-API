using System;
using System.Drawing;
using System.Windows.Forms;

namespace AICAD.UI
{
    /// <summary>
    /// Settings Dialog for managing DB connection and API key configuration
    /// </summary>
    public class SettingsDialog : Form
    {
        private TabControl _tabControl;
        
        // MongoDB Tab Controls
        private TextBox _txtMongoUri;
        private TextBox _txtMongoDb;
        private TextBox _txtMongoPassword;
        private Button _btnSaveMongo;
        private Button _btnLoadMongo;
        private Label _lblMongoStatus;
        
        // API Key Tab Controls
        private TextBox _txtApiKey;
        private ComboBox _cmbApiProvider;
        private Button _btnSaveApiKey;
        private Button _btnLoadApiKey;
        private Label _lblApiStatus;
        
        public SettingsDialog()
        {
            InitializeComponents();
        }
        
        private void InitializeComponents()
        {
            // Form properties
            Text = "Settings - AI-CAD-December";
            Size = new Size(600, 500);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            
            // Create tab control
            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Padding = new Point(10, 10)
            };
            
            // Create tabs
            var dbTab = CreateDatabaseTab();
            var apiTab = CreateApiKeyTab();
            
            _tabControl.TabPages.Add(dbTab);
            _tabControl.TabPages.Add(apiTab);
            
            Controls.Add(_tabControl);
            
            // Close button at bottom
            var btnClose = new Button
            {
                Text = "Close",
                Width = 100,
                Height = 30,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Location = new Point(Width - 120, Height - 70)
            };
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnClose);
        }
        
        private TabPage CreateDatabaseTab()
        {
            var tab = new TabPage("Database Settings");
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 6,
                Padding = new Padding(15)
            };
            
            // Configure columns
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            
            // Row 0: MongoDB URI
            var lblUri = new Label 
            { 
                Text = "MongoDB URI:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            _txtMongoUri = new TextBox 
            { 
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 0, 5)
            };
            panel.Controls.Add(lblUri, 0, 0);
            panel.Controls.Add(_txtMongoUri, 1, 0);
            
            // Row 1: Database Name
            var lblDb = new Label 
            { 
                Text = "Database Name:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            _txtMongoDb = new TextBox 
            { 
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 0, 5)
            };
            panel.Controls.Add(lblDb, 0, 1);
            panel.Controls.Add(_txtMongoDb, 1, 1);
            
            // Row 2: Password
            var lblPassword = new Label 
            { 
                Text = "Password:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            _txtMongoPassword = new TextBox 
            { 
                Dock = DockStyle.Fill,
                UseSystemPasswordChar = true,
                Margin = new Padding(0, 5, 0, 5)
            };
            panel.Controls.Add(lblPassword, 0, 2);
            panel.Controls.Add(_txtMongoPassword, 1, 2);
            
            // Row 3: Buttons
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 10, 0, 10)
            };
            
            _btnLoadMongo = new Button
            {
                Text = "Load from Environment",
                Width = 160,
                Height = 30,
                Margin = new Padding(0, 0, 10, 0)
            };
            _btnLoadMongo.Click += BtnLoadMongo_Click;
            
            _btnSaveMongo = new Button
            {
                Text = "Save to Environment",
                Width = 160,
                Height = 30,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnSaveMongo.Click += BtnSaveMongo_Click;
            
            buttonPanel.Controls.Add(_btnLoadMongo);
            buttonPanel.Controls.Add(_btnSaveMongo);
            panel.SetColumnSpan(buttonPanel, 2);
            panel.Controls.Add(buttonPanel, 0, 3);
            
            // Row 4: Status
            _lblMongoStatus = new Label
            {
                Text = "",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DarkGreen,
                Padding = new Padding(0, 5, 0, 5)
            };
            panel.SetColumnSpan(_lblMongoStatus, 2);
            panel.Controls.Add(_lblMongoStatus, 0, 4);
            
            // Row 5: Help text
            var helpText = new Label
            {
                Text = "Note: Settings are saved to user environment variables.\n" +
                       "Password is stored in plain text (MONGODB_PW).\n" +
                       "You may need to restart SolidWorks for changes to take effect.",
                Dock = DockStyle.Fill,
                ForeColor = Color.Gray,
                AutoSize = false,
                Padding = new Padding(0, 10, 0, 0)
            };
            panel.SetColumnSpan(helpText, 2);
            panel.Controls.Add(helpText, 0, 5);
            
            // Set row heights
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            
            tab.Controls.Add(panel);
            return tab;
        }
        
        private TabPage CreateApiKeyTab()
        {
            var tab = new TabPage("API Key Settings");
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                Padding = new Padding(15)
            };
            
            // Configure columns
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            
            // Row 0: API Provider
            var lblProvider = new Label 
            { 
                Text = "API Provider:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            _cmbApiProvider = new ComboBox 
            { 
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 5, 0, 5)
            };
            _cmbApiProvider.Items.AddRange(new object[] { "Google Gemini", "OpenAI", "Other" });
            _cmbApiProvider.SelectedIndex = 0;
            panel.Controls.Add(lblProvider, 0, 0);
            panel.Controls.Add(_cmbApiProvider, 1, 0);
            
            // Row 1: API Key
            var lblApiKey = new Label 
            { 
                Text = "API Key:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            _txtApiKey = new TextBox 
            { 
                Dock = DockStyle.Fill,
                UseSystemPasswordChar = true,
                Margin = new Padding(0, 5, 0, 5)
            };
            panel.Controls.Add(lblApiKey, 0, 1);
            panel.Controls.Add(_txtApiKey, 1, 1);
            
            // Row 2: Buttons
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 10, 0, 10)
            };
            
            _btnLoadApiKey = new Button
            {
                Text = "Load from Environment",
                Width = 160,
                Height = 30,
                Margin = new Padding(0, 0, 10, 0)
            };
            _btnLoadApiKey.Click += BtnLoadApiKey_Click;
            
            _btnSaveApiKey = new Button
            {
                Text = "Save to Environment",
                Width = 160,
                Height = 30,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnSaveApiKey.Click += BtnSaveApiKey_Click;
            
            buttonPanel.Controls.Add(_btnLoadApiKey);
            buttonPanel.Controls.Add(_btnSaveApiKey);
            panel.SetColumnSpan(buttonPanel, 2);
            panel.Controls.Add(buttonPanel, 0, 2);
            
            // Row 3: Status
            _lblApiStatus = new Label
            {
                Text = "",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DarkGreen,
                Padding = new Padding(0, 5, 0, 5)
            };
            panel.SetColumnSpan(_lblApiStatus, 2);
            panel.Controls.Add(_lblApiStatus, 0, 3);
            
            // Row 4: Help text
            var helpText = new Label
            {
                Text = "Note: API keys are saved to user environment variables.\n" +
                       "Variable name: GEMINI_API_KEY (for Google Gemini)\n" +
                       "You may need to restart SolidWorks for changes to take effect.",
                Dock = DockStyle.Fill,
                ForeColor = Color.Gray,
                AutoSize = false,
                Padding = new Padding(0, 10, 0, 0)
            };
            panel.SetColumnSpan(helpText, 2);
            panel.Controls.Add(helpText, 0, 4);
            
            // Set row heights
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            
            tab.Controls.Add(panel);
            return tab;
        }
        
        private void BtnLoadMongo_Click(object sender, EventArgs e)
        {
            try
            {
                _txtMongoUri.Text = Environment.GetEnvironmentVariable("MONGODB_URI", EnvironmentVariableTarget.User) 
                    ?? Environment.GetEnvironmentVariable("MONGO_LOG_CONN", EnvironmentVariableTarget.User) 
                    ?? "";
                _txtMongoDb.Text = Environment.GetEnvironmentVariable("MONGODB_DB", EnvironmentVariableTarget.User) 
                    ?? "TaskPaneAddin";
                _txtMongoPassword.Text = Environment.GetEnvironmentVariable("MONGODB_PW", EnvironmentVariableTarget.User) 
                    ?? "";
                
                _lblMongoStatus.Text = "Loaded from environment variables";
                _lblMongoStatus.ForeColor = Color.DarkGreen;
            }
            catch (Exception ex)
            {
                _lblMongoStatus.Text = "Failed to load: " + ex.Message;
                _lblMongoStatus.ForeColor = Color.Red;
            }
        }
        
        private void BtnSaveMongo_Click(object sender, EventArgs e)
        {
            try
            {
                Environment.SetEnvironmentVariable("MONGODB_URI", _txtMongoUri.Text, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("MONGODB_DB", _txtMongoDb.Text, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("MONGODB_PW", _txtMongoPassword.Text, EnvironmentVariableTarget.User);
                
                _lblMongoStatus.Text = "Settings saved successfully! Restart SolidWorks to apply changes.";
                _lblMongoStatus.ForeColor = Color.DarkGreen;
                
                MessageBox.Show(
                    "DB settings saved to user environment variables.\n\n" +
                    "Please restart SolidWorks for changes to take effect.",
                    "Settings Saved",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                _lblMongoStatus.Text = "Failed to save: " + ex.Message;
                _lblMongoStatus.ForeColor = Color.Red;
                
                MessageBox.Show(
                    "Failed to save settings: " + ex.Message,
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }
        
        private void BtnLoadApiKey_Click(object sender, EventArgs e)
        {
            try
            {
                _txtApiKey.Text = Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.User) 
                    ?? "";
                
                _lblApiStatus.Text = "Loaded from environment variables";
                _lblApiStatus.ForeColor = Color.DarkGreen;
            }
            catch (Exception ex)
            {
                _lblApiStatus.Text = "Failed to load: " + ex.Message;
                _lblApiStatus.ForeColor = Color.Red;
            }
        }
        
        private void BtnSaveApiKey_Click(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_txtApiKey.Text))
                {
                    _lblApiStatus.Text = "Please enter an API key";
                    _lblApiStatus.ForeColor = Color.Orange;
                    return;
                }
                
                // Save based on provider
                string envVarName = "GEMINI_API_KEY"; // Default to Gemini
                if (_cmbApiProvider.SelectedIndex == 1) // OpenAI
                {
                    envVarName = "OPENAI_API_KEY";
                }
                
                Environment.SetEnvironmentVariable(envVarName, _txtApiKey.Text, EnvironmentVariableTarget.User);
                
                _lblApiStatus.Text = string.Format("API key saved to {0}! Restart SolidWorks to apply changes.", envVarName);
                _lblApiStatus.ForeColor = Color.DarkGreen;
                
                MessageBox.Show(
                    string.Format("API key saved to {0}.\n\nPlease restart SolidWorks for changes to take effect.", envVarName),
                    "Settings Saved",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                _lblApiStatus.Text = "Failed to save: " + ex.Message;
                _lblApiStatus.ForeColor = Color.Red;
                
                MessageBox.Show(
                    "Failed to save API key: " + ex.Message,
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }
    }
}
