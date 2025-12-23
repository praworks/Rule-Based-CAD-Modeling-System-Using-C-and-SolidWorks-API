using System;
using System.Drawing;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AICAD.UI
{
    /// <summary>
    /// Settings Dialog for managing DB connection and API key configuration
    /// </summary>
    public class SettingsDialog : Form
    {
        private const string RecommendedMongoUri = "mongodb+srv://prashan2011th_db_user:Uobz3oeAutZMRuCl@rule-based-cad-modeling.dlrnkre.mongodb.net/";

        private TabControl _tabControl;
        
        // MongoDB Tab Controls
        private TextBox _txtMongoUri;
        private TextBox _txtMongoDb;
        private TextBox _txtMongoUser;
        private TextBox _txtMongoPassword;
        private CheckBox _chkUseFewShot;
        private Button _btnToggleMongoPwVisibility;
        private Button _btnSaveMongo;
        private Button _btnLoadMongo;
        private Label _lblMongoStatus;
        
        // API Key Tab Controls
        private TextBox _txtApiKey;
        private TextBox _txtProjectId;
        private ComboBox _cmbApiModel;
        private Button _btnToggleApiKeyVisibility;
        private ComboBox _cmbApiProvider;
        private Button _btnSaveApiKey;
        private Button _btnLoadApiKey;
        private Label _lblApiStatus;
        private Button _btnTestApi;
        
        // NameEasy Tab Controls
        private TextBox _txtNameEasyPath;
        private Button _btnBrowseNameEasy;
        private Button _btnSaveNameEasy;
        private Label _lblNameEasyInfo;
        
        public SettingsDialog()
        {
            InitializeComponents();
            TryReplaceOldMongoUri();
            LoadAllSettings();
        }

        private void LoadAllSettings()
        {
            try
            {
                BtnLoadMongo_Click(this, EventArgs.Empty);
                BtnLoadApiKey_Click(this, EventArgs.Empty);

                // Model selection is configured via environment (`GEMINI_MODEL`) not the UI.
            }
            catch { }
        }

        private void TryReplaceOldMongoUri()
        {
            try
            {
                var current = Environment.GetEnvironmentVariable("MONGODB_URI", EnvironmentVariableTarget.User) ?? "";
                if (!string.IsNullOrEmpty(current) &&
                    (current.IndexOf("prashanth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     current.IndexOf("cluster2.9abz2oy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     current.IndexOf("prashan2011th", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    Environment.SetEnvironmentVariable("MONGODB_URI", RecommendedMongoUri, EnvironmentVariableTarget.User);
                }
            }
            catch { }
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
            var nameEasyTab = CreateNameEasyTab();
            
            _tabControl.TabPages.Add(dbTab);
            _tabControl.TabPages.Add(apiTab);
            _tabControl.TabPages.Add(nameEasyTab);
            
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

            // Apply all settings button
            var btnApplyAll = new Button
            {
                Text = "Apply",
                Width = 100,
                Height = 30,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Location = new Point(Width - 240, Height - 70)
            };
            btnApplyAll.Click += BtnApplyAll_Click;
            Controls.Add(btnApplyAll);

            // Load NameEasy current path into the tab if present
            try
            {
                var defaultPath = AICAD.Services.SettingsManager.GetDatabasePath();
                if (!string.IsNullOrEmpty(defaultPath) && _txtNameEasyPath != null)
                {
                    _txtNameEasyPath.Text = defaultPath;
                }
            }
            catch { }
        }
        
        private TabPage CreateDatabaseTab()
        {
            var tab = new TabPage("Database Settings");
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 7,
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

            // Row 2: Username
            var lblUser = new Label
            {
                Text = "Username:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            _txtMongoUser = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 0, 5)
            };
            panel.Controls.Add(lblUser, 0, 2);
            panel.Controls.Add(_txtMongoUser, 1, 2);
            
            // Row 3: Password
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

            // Panel to hold password textbox + visibility toggle
            var pwPanel = new Panel { Dock = DockStyle.Fill };
            _btnToggleMongoPwVisibility = new Button
            {
                Width = 30,
                Height = 24,
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat,
                Text = "Show",
                TabStop = false
            };
            _btnToggleMongoPwVisibility.Click += ToggleMongoPasswordVisibility_Click;
            pwPanel.Controls.Add(_btnToggleMongoPwVisibility);
            pwPanel.Controls.Add(_txtMongoPassword);

            panel.Controls.Add(lblPassword, 0, 3);
            panel.Controls.Add(pwPanel, 1, 3);
            
            // (Model control intentionally moved to API Key tab)

            // Row 4: Buttons
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
            panel.Controls.Add(buttonPanel, 0, 4);

            // Row 5: Few-shot checkbox
            _chkUseFewShot = new CheckBox
            {
                Text = "Enable Few-Shot examples (use examples from DB)",
                Dock = DockStyle.Fill,
                Padding = new Padding(3, 8, 0, 0)
            };
            panel.SetColumnSpan(_chkUseFewShot, 2);
            panel.Controls.Add(_chkUseFewShot, 0, 5);
            
            // Row 6: Status
            _lblMongoStatus = new Label
            {
                Text = "",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DarkGreen,
                Padding = new Padding(0, 5, 0, 5)
            };
            panel.SetColumnSpan(_lblMongoStatus, 2);
            panel.Controls.Add(_lblMongoStatus, 0, 6);
            
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
            panel.Controls.Add(helpText, 0, 7);
            
            // Set row heights
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // few-shot checkbox
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // status
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
                RowCount = 7,
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

            // Panel to hold API key textbox + visibility toggle
            var apiPanel = new Panel { Dock = DockStyle.Fill };
            _btnToggleApiKeyVisibility = new Button
            {
                Width = 30,
                Height = 24,
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat,
                Text = "Show",
                TabStop = false
            };
            _btnToggleApiKeyVisibility.Click += ToggleApiKeyVisibility_Click;
            apiPanel.Controls.Add(_btnToggleApiKeyVisibility);
            apiPanel.Controls.Add(_txtApiKey);

            panel.Controls.Add(lblApiKey, 0, 1);
            panel.Controls.Add(apiPanel, 1, 1);

            // Row 2: Project ID (for providers that require a project id, e.g., Google)
            var lblProject = new Label
            {
                Text = "Project ID:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            _txtProjectId = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 0, 5)
            };
            panel.Controls.Add(lblProject, 0, 2);
            panel.Controls.Add(_txtProjectId, 1, 2);

            // Row 3: Model selection (populated asynchronously)
            var lblModel = new Label
            {
                Text = "Model:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            _cmbApiModel = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 5, 0, 5)
            };
            _cmbApiModel.Items.Add("(not loaded)");
            panel.Controls.Add(lblModel, 0, 3);
            panel.Controls.Add(_cmbApiModel, 1, 3);

            // Row 3: Buttons
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
            // Test button
            _btnTestApi = new Button
            {
                Text = "Test API",
                Width = 120,
                Height = 30,
                Margin = new Padding(10, 0, 0, 0)
            };
            _btnTestApi.Click += (s, e) => { var _ = BtnTestApi_Click(s, e); };
            buttonPanel.Controls.Add(_btnTestApi);
            panel.SetColumnSpan(buttonPanel, 2);
            panel.Controls.Add(buttonPanel, 0, 4);
            
            // Row 5: Status
            _lblApiStatus = new Label
            {
                Text = "",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DarkGreen,
                Padding = new Padding(0, 5, 0, 5)
            };
            panel.SetColumnSpan(_lblApiStatus, 2);
            panel.Controls.Add(_lblApiStatus, 0, 5);
            
            // Row 6: Help text
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
            panel.Controls.Add(helpText, 0, 6);
            
            // Set row heights
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); // provider
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); // key
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); // project id
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); // model
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); // buttons
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // status
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); // help
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            
            tab.Controls.Add(panel);
            return tab;
        }

        private TabPage CreateNameEasyTab()
        {
            var tab = new TabPage("NameEasy");
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 3,
                Padding = new Padding(15)
            };

            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

            var lblPath = new Label
            {
                Text = "Database Path:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            _txtNameEasyPath = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0,5,0,5) };
            _btnBrowseNameEasy = new Button { Text = "Browse...", Width = 80, Height = 26 };
            _btnBrowseNameEasy.Click += BtnBrowseNameEasy_Click;

            panel.Controls.Add(lblPath, 0, 0);
            panel.Controls.Add(_txtNameEasyPath, 1, 0);
            panel.Controls.Add(_btnBrowseNameEasy, 2, 0);

            _lblNameEasyInfo = new Label
            {
                Text = "Choose where to store the NameEasy.db database. Default: add-in folder.",
                Dock = DockStyle.Fill,
                ForeColor = Color.Gray,
                AutoSize = false,
                Padding = new Padding(0,10,0,0)
            };
            panel.SetColumnSpan(_lblNameEasyInfo, 3);
            panel.Controls.Add(_lblNameEasyInfo, 0, 1);

            _btnSaveNameEasy = new Button
            {
                Text = "Save",
                Width = 100,
                Height = 30,
                Anchor = AnchorStyles.Right
            };
            _btnSaveNameEasy.Click += BtnSaveNameEasy_Click;
            panel.SetColumnSpan(_btnSaveNameEasy, 3);
            panel.Controls.Add(_btnSaveNameEasy, 0, 2);

            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

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

                // If an older or placeholder URI is present, replace it with the recommended URI
                if (string.IsNullOrWhiteSpace(_txtMongoUri.Text) ||
                    _txtMongoUri.Text.IndexOf("prashanth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    _txtMongoUri.Text.IndexOf("cluster2.9abz2oy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    _txtMongoUri.Text.IndexOf("prashan2011th", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _txtMongoUri.Text = RecommendedMongoUri;
                }
                _txtMongoDb.Text = Environment.GetEnvironmentVariable("MONGODB_DB", EnvironmentVariableTarget.User) 
                    ?? "TaskPaneAddin";
                _txtMongoUser.Text = Environment.GetEnvironmentVariable("MONGODB_USER", EnvironmentVariableTarget.User) ?? "";
                _txtMongoPassword.Text = Environment.GetEnvironmentVariable("MONGODB_PW", EnvironmentVariableTarget.User) 
                    ?? "";
                // Load few-shot flag from environment: AICAD_USE_FEWSHOT (1 = enabled)
                try
                {
                    var fs = Environment.GetEnvironmentVariable("AICAD_USE_FEWSHOT", EnvironmentVariableTarget.User) ?? Environment.GetEnvironmentVariable("AICAD_USE_FEWSHOT");
                    _chkUseFewShot.Checked = string.IsNullOrEmpty(fs) ? true : (fs == "1" || fs.Equals("true", StringComparison.OrdinalIgnoreCase));
                }
                catch { _chkUseFewShot.Checked = true; }
                
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
                Environment.SetEnvironmentVariable("MONGODB_USER", _txtMongoUser.Text, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("MONGODB_PW", _txtMongoPassword.Text, EnvironmentVariableTarget.User);
                // Save few-shot checkbox state
                try
                {
                    Environment.SetEnvironmentVariable("AICAD_USE_FEWSHOT", _chkUseFewShot.Checked ? "1" : "0", EnvironmentVariableTarget.User);
                }
                catch { }
                
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
        
        private async void BtnLoadApiKey_Click(object sender, EventArgs e)
        {
            try
            {
                _txtApiKey.Text = Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.User) 
                    ?? "";
                _txtProjectId.Text = Environment.GetEnvironmentVariable("GEMINI_PROJECT_ID", EnvironmentVariableTarget.User) ?? "";
                
                _lblApiStatus.Text = "Loaded from environment variables";
                _lblApiStatus.ForeColor = Color.DarkGreen;
                // Populate model dropdown asynchronously
                try { await PopulateModelDropdownAsync(); } catch { }
            }
            catch (Exception ex)
            {
                _lblApiStatus.Text = "Failed to load: " + ex.Message;
                _lblApiStatus.ForeColor = Color.Red;
            }
        }

        private async Task PopulateModelDropdownAsync()
        {
            try
            {
                var key = _txtApiKey.Text;
                if (string.IsNullOrWhiteSpace(key))
                {
                    key = Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.User) ?? "";
                }

                _cmbApiModel.Items.Clear();
                _cmbApiModel.Items.Add("(loading)");
                _cmbApiModel.SelectedIndex = 0;

                var client = new AICAD.Services.GeminiClient(key);
                var models = await client.ListAvailableModelsAsync(null).ConfigureAwait(false);
                this.BeginInvoke((Action)(() =>
                {
                    _cmbApiModel.Items.Clear();
                    if (models == null || models.Count == 0)
                    {
                        _cmbApiModel.Items.Add("(none)");
                        _cmbApiModel.SelectedIndex = 0;
                        return;
                    }
                    foreach (var m in models)
                    {
                        var display = m.StartsWith("models/") ? m.Substring("models/".Length) : m;
                        _cmbApiModel.Items.Add(display);
                    }

                    var configured = Environment.GetEnvironmentVariable("GEMINI_MODEL", EnvironmentVariableTarget.User) ?? "";
                    if (!string.IsNullOrEmpty(configured))
                    {
                        var simple = configured.StartsWith("models/") ? configured.Substring("models/".Length) : configured;
                        var idx = _cmbApiModel.Items.IndexOf(simple);
                        if (idx >= 0) _cmbApiModel.SelectedIndex = idx;
                    }
                    if (_cmbApiModel.SelectedIndex < 0 && _cmbApiModel.Items.Count > 0) _cmbApiModel.SelectedIndex = 0;
                }));
            }
            catch { }
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

                // Save project id for Google Gemini provider
                try
                {
                    if (_cmbApiProvider.SelectedIndex == 0)
                    {
                        Environment.SetEnvironmentVariable("GEMINI_PROJECT_ID", _txtProjectId.Text ?? "", EnvironmentVariableTarget.User);
                    }
                }
                catch { }

                // Save selected model if present
                try
                {
                    if (_cmbApiModel != null && _cmbApiModel.SelectedItem != null)
                    {
                        var sel = _cmbApiModel.SelectedItem.ToString();
                        if (!string.IsNullOrWhiteSpace(sel) && sel != "(none)" && sel != "(loading)")
                        {
                            var final = sel.StartsWith("models/") ? sel : "models/" + sel;
                            Environment.SetEnvironmentVariable("GEMINI_MODEL", final, EnvironmentVariableTarget.User);
                        }
                    }
                }
                catch { }
                
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

        private async Task BtnTestApi_Click(object sender, EventArgs e)
        {
            try
            {
                _lblApiStatus.Text = "Testing API...";
                _lblApiStatus.ForeColor = Color.Blue;

                string key = _txtApiKey.Text;
                if (string.IsNullOrWhiteSpace(key))
                {
                    _lblApiStatus.Text = "No API key provided.";
                    _lblApiStatus.ForeColor = Color.Orange;
                    return;
                }

                // Disable test button while running
                _btnTestApi.Enabled = false;
                try
                {
                    if (_cmbApiProvider.SelectedIndex == 0)
                    {
                        // Google Gemini: use GeminiClient.TestApiKeyAsync for detailed diagnostics
                        var client = new AICAD.Services.GeminiClient(key);
                        var res = await client.TestApiKeyAsync(null).ConfigureAwait(false);
                        this.BeginInvoke((Action)(() =>
                        {
                            if (res == null)
                            {
                                _lblApiStatus.Text = "API test returned no result.";
                                _lblApiStatus.ForeColor = Color.Red;
                                return;
                            }

                            if (res.Success)
                            {
                                var sample = res.ModelNames != null && res.ModelNames.Count > 0 ? res.ModelNames[0] : "(none)";
                                _lblApiStatus.Text = $"Gemini: OK — {res.ModelsFound} models (example: {sample})";
                                _lblApiStatus.ForeColor = Color.DarkGreen;

                                // Populate model dropdown with returned models
                                try
                                {
                                    _cmbApiModel.Items.Clear();
                                    foreach (var m in res.ModelNames)
                                    {
                                        var display = m.StartsWith("models/") ? m.Substring("models/".Length) : m;
                                        _cmbApiModel.Items.Add(display);
                                    }
                                    if (_cmbApiModel.Items.Count > 0) _cmbApiModel.SelectedIndex = 0;
                                }
                                catch { }
                            }
                            else
                            {
                                var code = res.StatusCode.HasValue ? res.StatusCode.Value.ToString() : "?";
                                var hint = string.IsNullOrWhiteSpace(res.Hint) ? string.Empty : " Hint: " + res.Hint;
                                _lblApiStatus.Text = $"Gemini test failed: {code}. {res.Message}{hint}";
                                _lblApiStatus.ForeColor = Color.Red;
                            }
                        }));
                    }
                    else if (_cmbApiProvider.SelectedIndex == 1)
                    {
                        // OpenAI: simple models list check (improve if needed)
                        using (var http = new HttpClient())
                        {
                            http.Timeout = TimeSpan.FromSeconds(10);
                            http.DefaultRequestHeaders.Clear();
                            http.DefaultRequestHeaders.Add("Authorization", "Bearer " + key);
                            var resp = await http.GetAsync("https://api.openai.com/v1/models");
                            var body = await resp.Content.ReadAsStringAsync();
                            this.BeginInvoke((Action)(() =>
                            {
                                if (resp.IsSuccessStatusCode)
                                {
                                    _lblApiStatus.Text = $"OpenAI: OK — {((int)resp.StatusCode)}";
                                    _lblApiStatus.ForeColor = Color.DarkGreen;
                                }
                                else
                                {
                                    _lblApiStatus.Text = $"OpenAI test failed: {(int)resp.StatusCode} {resp.ReasonPhrase}";
                                    _lblApiStatus.ForeColor = Color.Red;
                                }
                            }));
                        }
                    }
                    else
                    {
                        _lblApiStatus.Text = "No test available for selected provider.";
                        _lblApiStatus.ForeColor = Color.Gray;
                    }
                }
                finally
                {
                    _btnTestApi.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                _lblApiStatus.Text = "API test error: " + ex.Message;
                _lblApiStatus.ForeColor = Color.Red;
            }
        }

        private void BtnApplyAll_Click(object sender, EventArgs e)
        {
            try
            {
                // Save Mongo
                BtnSaveMongo_Click(sender, e);

                // Save API key
                BtnSaveApiKey_Click(sender, e);

                // GEMINI_MODEL is managed externally (env var) or via Settings dialog input fields.

                // Save project id (if set)
                try
                {
                    if (_txtProjectId != null)
                    {
                        Environment.SetEnvironmentVariable("GEMINI_PROJECT_ID", _txtProjectId.Text ?? "", EnvironmentVariableTarget.User);
                    }
                }
                catch { }

                MessageBox.Show("All settings applied. Restart SolidWorks for changes to take effect.", "Applied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to apply settings: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ToggleMongoPasswordVisibility_Click(object sender, EventArgs e)
        {
            try
            {
                _txtMongoPassword.UseSystemPasswordChar = !_txtMongoPassword.UseSystemPasswordChar;
                _btnToggleMongoPwVisibility.Text = _txtMongoPassword.UseSystemPasswordChar ? "Show" : "Hide";
            }
            catch { }
        }

        private void ToggleApiKeyVisibility_Click(object sender, EventArgs e)
        {
            try
            {
                _txtApiKey.UseSystemPasswordChar = !_txtApiKey.UseSystemPasswordChar;
                _btnToggleApiKeyVisibility.Text = _txtApiKey.UseSystemPasswordChar ? "Show" : "Hide";
            }
            catch { }
        }

        private void BtnBrowseNameEasy_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select folder for NameEasy.db database";
                dlg.ShowNewFolderButton = true;
                var current = _txtNameEasyPath.Text;
                if (!string.IsNullOrEmpty(current))
                {
                    try { dlg.SelectedPath = System.IO.Path.GetDirectoryName(current); } catch { }
                }

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _txtNameEasyPath.Text = System.IO.Path.Combine(dlg.SelectedPath, "NameEasy.db");
                }
            }
        }

        private void BtnSaveNameEasy_Click(object sender, EventArgs e)
        {
            var path = _txtNameEasyPath.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show("Please specify a database path.", "Invalid Path", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var ok = AICAD.Services.SettingsManager.SetDatabasePath(path);
                if (ok)
                {
                    MessageBox.Show("NameEasy database path saved. Restart SolidWorks for changes to take effect.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("Failed to save database path.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error saving path: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
