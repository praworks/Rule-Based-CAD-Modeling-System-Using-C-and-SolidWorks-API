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
        private CheckBox _chkAllowMultipleBuilds;
        private Button _btnToggleMongoPwVisibility;
        private Button _btnSaveMongo;
        private Button _btnLoadMongo;
        private Label _lblMongoStatus;
        
        // API Key Tab Controls
        private TextBox _txtApiKey;
        private Button _btnToggleApiKeyVisibility;
        private ComboBox _cmbCloudProvider;
        private Button _btnSaveApiKey;
        private Button _btnLoadApiKey;
        private Label _lblApiStatus;
        private Button _btnTestApi;
        // Local LLM controls
        private ComboBox _cmbLlmMode;
        private TextBox _txtLocalEndpoint;
        private TextBox _txtLocalModel;
        private TextBox _txtLocalSystemPrompt;
        // New controls for direct key entry
        private TextBox _txtGeminiKey;
        private TextBox _txtGroqKey;
        // API/LLM labels (kept as fields so visibility can be toggled)
        private Label _lblProvider;
        private Label _lblLlmMode;
        private Label _lblApiKeyLabel;
        private Label _lblLocalEndpointLabel;
        private Label _lblLocalModelLabel;
        private Label _lblLocalSysLabel;
        private Label _lblCloudProvider;
        private Label _lblGeminiKey;
        private Label _lblGroqKey;
        
        
        // NameEasy Tab Controls
        private TextBox _txtNameEasyPath;
        private Button _btnBrowseNameEasy;
        private Button _btnSaveNameEasy;
        private Label _lblNameEasyInfo;
        
        // Samples Tab Controls
        private RadioButton _rbZeroShot;
        private RadioButton _rbOneShot;
        private RadioButton _rbFewShot;
        private TextBox _txtSamplesDbPath;
        private Button _btnBrowseSamples;
        private Button _btnSaveSamples;
        private Label _lblSamplesInfo;
        
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
                // Load samples tab settings
                try { BtnLoadSamples_Click(this, EventArgs.Empty); } catch { }

                // Model selection is configured via environment (`GEMINI_MODEL`) not the UI.
            }
            catch { }
        }

        private TabPage CreateSamplesTab()
        {
            var tab = new TabPage("Samples");
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 4,
                Padding = new Padding(15)
            };

            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

            // Radio buttons for sample mode
            var lblMode = new Label
            {
                Text = "Sample Mode:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            var rbPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            _rbZeroShot = new RadioButton { Text = "Zero-Shot", AutoSize = true };
            _rbOneShot = new RadioButton { Text = "One-Shot", AutoSize = true };
            _rbFewShot = new RadioButton { Text = "Few-Shot", AutoSize = true };
            rbPanel.Controls.Add(_rbZeroShot);
            rbPanel.Controls.Add(_rbOneShot);
            rbPanel.Controls.Add(_rbFewShot);

            panel.Controls.Add(lblMode, 0, 0);
            panel.SetColumnSpan(rbPanel, 2);
            panel.Controls.Add(rbPanel, 1, 0);

            // Samples DB path
            var lblPath = new Label
            {
                Text = "Samples DB Path:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            _txtSamplesDbPath = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 5, 0, 5) };
            _btnBrowseSamples = new Button { Text = "Browse...", Width = 80, Height = 26 };
            _btnBrowseSamples.Click += BtnBrowseSamples_Click;

            panel.Controls.Add(lblPath, 0, 1);
            panel.Controls.Add(_txtSamplesDbPath, 1, 1);
            panel.Controls.Add(_btnBrowseSamples, 2, 1);

            // Save button
            _btnSaveSamples = new Button { Text = "Save", Width = 100, Height = 30, Anchor = AnchorStyles.Right };
            _btnSaveSamples.Click += BtnSaveSamples_Click;
            panel.SetColumnSpan(_btnSaveSamples, 3);
            panel.Controls.Add(_btnSaveSamples, 0, 2);

            _lblSamplesInfo = new Label
            {
                Text = "Choose how example shots are provided to the LLM and where they are stored.",
                Dock = DockStyle.Fill,
                ForeColor = Color.Gray,
                AutoSize = false,
                Padding = new Padding(0, 10, 0, 0)
            };
            panel.SetColumnSpan(_lblSamplesInfo, 3);
            panel.Controls.Add(_lblSamplesInfo, 0, 3);

            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            tab.Controls.Add(panel);
            return tab;
        }

        private void BtnBrowseSamples_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select folder where sample shots are stored";
                dlg.ShowNewFolderButton = true;
                var current = _txtSamplesDbPath.Text;
                if (!string.IsNullOrEmpty(current))
                {
                    try { dlg.SelectedPath = System.IO.Path.GetDirectoryName(current); } catch { }
                }

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _txtSamplesDbPath.Text = System.IO.Path.Combine(dlg.SelectedPath, "samples.db");
                }
            }
        }

        private void BtnSaveSamples_Click(object sender, EventArgs e)
        {
            try
            {
                var mode = _rbZeroShot.Checked ? "zero" : _rbOneShot.Checked ? "one" : "few";
                Environment.SetEnvironmentVariable("AICAD_SAMPLE_MODE", mode, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("AICAD_SAMPLES_DB_PATH", _txtSamplesDbPath.Text ?? "", EnvironmentVariableTarget.User);

                MessageBox.Show("Samples settings saved to environment variables. Restart SolidWorks for changes to take effect.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save samples settings: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnLoadSamples_Click(object sender, EventArgs e)
        {
            try
            {
                var mode = Environment.GetEnvironmentVariable("AICAD_SAMPLE_MODE", EnvironmentVariableTarget.User) ?? "";
                if (string.IsNullOrEmpty(mode)) mode = Environment.GetEnvironmentVariable("AICAD_SAMPLE_MODE") ?? "";
                switch (mode.ToLowerInvariant())
                {
                    case "zero": _rbZeroShot.Checked = true; break;
                    case "one": _rbOneShot.Checked = true; break;
                    case "few": _rbFewShot.Checked = true; break;
                    default: _rbFewShot.Checked = true; break;
                }

                _txtSamplesDbPath.Text = Environment.GetEnvironmentVariable("AICAD_SAMPLES_DB_PATH", EnvironmentVariableTarget.User) ?? Environment.GetEnvironmentVariable("AICAD_SAMPLES_DB_PATH") ?? "";
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
            var samplesTab = CreateSamplesTab();
            
            _tabControl.TabPages.Add(dbTab);
            _tabControl.TabPages.Add(apiTab);
            _tabControl.TabPages.Add(samplesTab);
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
                // Load samples DB path if present
                var samplesPath = Environment.GetEnvironmentVariable("AICAD_SAMPLES_DB_PATH", EnvironmentVariableTarget.User) ?? "";
                if (!string.IsNullOrEmpty(samplesPath) && _txtSamplesDbPath != null)
                {
                    _txtSamplesDbPath.Text = samplesPath;
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

            // Row 5: Status
            _lblMongoStatus = new Label
            {
                Text = "",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DarkGreen,
                Padding = new Padding(0, 5, 0, 5)
            };
            panel.SetColumnSpan(_lblMongoStatus, 2);
            panel.Controls.Add(_lblMongoStatus, 0, 5);
            
            // Row 6: Help text
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
            panel.Controls.Add(helpText, 0, 6);
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // status
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            
            // Set initial visibility now that all controls have been created

            tab.Controls.Add(panel);
            return tab;
        }
        
        private TabPage CreateApiKeyTab()
        {
            var tab = new TabPage("LLM Settings");
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 14,
                Padding = new Padding(15)
            };
            
            // Configure columns
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            
            // Row 0: LLM Mode (Cloud / Local)
            _lblLlmMode = new Label
            {
                Text = "LLM Mode:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            _cmbLlmMode = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 5, 0, 5)
            };
            _cmbLlmMode.Items.AddRange(new object[] { "Cloud (Gemini/Groq)", "Local" });
            _cmbLlmMode.SelectedIndexChanged += (s, e) =>
            {
                var isLocal = _cmbLlmMode.SelectedIndex == 1;
                _txtLocalEndpoint.Enabled = isLocal;
                _txtLocalModel.Enabled = isLocal;
                _txtLocalSystemPrompt.Enabled = isLocal;
                _txtGeminiKey.Enabled = !isLocal;
                _txtGroqKey.Enabled = !isLocal;
            };
            panel.Controls.Add(_lblLlmMode, 0, 0);
            panel.Controls.Add(_cmbLlmMode, 1, 0);

            // Row 1: Local LLM Endpoint
            _lblLocalEndpointLabel = new Label
            {
                Text = "Local LLM Endpoint:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            _txtLocalEndpoint = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 0, 5)
            };
            panel.Controls.Add(_lblLocalEndpointLabel, 0, 1);
            panel.Controls.Add(_txtLocalEndpoint, 1, 1);
            
            // Row 1: Google Gemini API Key
            _lblGeminiKey = new Label
            {
                Text = "Google Gemini API Key:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            _txtGeminiKey = new TextBox
            {
                Dock = DockStyle.Fill,
                UseSystemPasswordChar = true,
                Margin = new Padding(0, 5, 0, 5)
            };
            // Panel to hold Gemini key textbox + visibility toggle
            var geminiPanel = new Panel { Dock = DockStyle.Fill };
            var btnToggleGeminiVisibility = new Button
            {
                Width = 30,
                Height = 24,
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat,
                Text = "Show",
                TabStop = false
            };
            btnToggleGeminiVisibility.Click += (s, e) => ToggleApiKeyVisibility(s, e, _txtGeminiKey);
            geminiPanel.Controls.Add(btnToggleGeminiVisibility);
            geminiPanel.Controls.Add(_txtGeminiKey);
            panel.Controls.Add(_lblGeminiKey, 0, 2);
            panel.Controls.Add(geminiPanel, 1, 2);
            
            // Row 2: Groq API Key
            _lblGroqKey = new Label
            {
                Text = "Groq API Key:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            _txtGroqKey = new TextBox
            {
                Dock = DockStyle.Fill,
                UseSystemPasswordChar = true,
                Margin = new Padding(0, 5, 0, 5)
            };
            // Panel to hold Groq key textbox + visibility toggle
            var groqPanel = new Panel { Dock = DockStyle.Fill };
            var btnToggleGroqVisibility = new Button
            {
                Width = 30,
                Height = 24,
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat,
                Text = "Show",
                TabStop = false
            };
            btnToggleGroqVisibility.Click += (s, e) => ToggleApiKeyVisibility(s, e, _txtGroqKey);
            groqPanel.Controls.Add(btnToggleGroqVisibility);
            groqPanel.Controls.Add(_txtGroqKey);
            panel.Controls.Add(_lblGroqKey, 0, 3);
            panel.Controls.Add(groqPanel, 1, 3);

            // Project ID removed from UI (managed externally via environment variables if needed)

            // Row 3: Local model name
            _lblLocalModelLabel = new Label
            {
                Text = "Local Model (identifier):",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            _txtLocalModel = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 0, 5)
            };
            panel.Controls.Add(_lblLocalModelLabel, 0, 4);
            panel.Controls.Add(_txtLocalModel, 1, 4);

            // Row 4: Local system prompt
            _lblLocalSysLabel = new Label
            {
                Text = "Local System Prompt:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            _txtLocalSystemPrompt = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 0, 5)
            };
            panel.Controls.Add(_lblLocalSysLabel, 0, 5);
            panel.Controls.Add(_txtLocalSystemPrompt, 1, 5);

            // initial visibility will be set after the rest of the controls are created
            // Model selection removed from UI; GEMINI_MODEL is managed via environment variables.

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
            panel.Controls.Add(buttonPanel, 0, 5);

            // Duplicate Save button at bottom of dialog for discoverability
            var btnSaveBottom = new Button
            {
                Text = "Save to Environment",
                Width = 160,
                Height = 30,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Anchor = AnchorStyles.Bottom
            };
            btnSaveBottom.Click += BtnSaveApiKey_Click;
            btnSaveBottom.Location = new Point((this.Width / 2) - 80, this.Height - 60);
            Controls.Add(btnSaveBottom);
            
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
            panel.Controls.Add(_lblApiStatus, 0, 8);
            
            // Row 6: Help text
            var helpText = new Label
            {
                Text = "Note: Settings are saved to user environment variables.\n" +
                       "Variables: LOCAL_LLM_ENDPOINT, GEMINI_API_KEY, GROQ_API_KEY,\n" +
                       "LOCAL_LLM_MODEL, LOCAL_LLM_SYSTEM_PROMPT.\n" +
                       "You may need to restart SolidWorks for changes to take effect.",
                Dock = DockStyle.Fill,
                ForeColor = Color.Gray,
                AutoSize = false,
                Padding = new Padding(0, 10, 0, 0)
            };
            panel.SetColumnSpan(helpText, 2);
            panel.Controls.Add(helpText, 0, 9);
            
            // Row 7: Few-shot checkbox
            _chkUseFewShot = new CheckBox
            {
                Text = "Enable Few-Shot examples (use examples from DB)",
                Dock = DockStyle.Fill,
                Padding = new Padding(3, 8, 0, 0)
            };
            panel.SetColumnSpan(_chkUseFewShot, 2);
            panel.Controls.Add(_chkUseFewShot, 0, 10);
            
            // Row 8: Allow multiple builds checkbox
            _chkAllowMultipleBuilds = new CheckBox
            {
                Text = "Allow multiple build requests (disable button protection)",
                Dock = DockStyle.Fill,
                Padding = new Padding(3, 8, 0, 0)
            };
            panel.SetColumnSpan(_chkAllowMultipleBuilds, 2);
            panel.Controls.Add(_chkAllowMultipleBuilds, 0, 11);
            
            // Set row heights
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); // 0 llm mode
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); // 1 local endpoint
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); // 2 gemini key
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); // 3 groq key
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); // 4 local model
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); // 5 local sys
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); // 6 buttons
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // 7 status
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); // 8 help
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // 9 placeholder
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // 10 fewshot
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // 11 allow
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 10
            
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
                // Load all LLM settings from environment variables
                try
                {
                    _txtLocalEndpoint.Text = Environment.GetEnvironmentVariable("LOCAL_LLM_ENDPOINT", EnvironmentVariableTarget.User) ?? "";
                    _txtGeminiKey.Text = Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.User) ?? "";
                    _txtGroqKey.Text = Environment.GetEnvironmentVariable("GROQ_API_KEY", EnvironmentVariableTarget.User) ?? "";
                    _txtLocalModel.Text = Environment.GetEnvironmentVariable("LOCAL_LLM_MODEL", EnvironmentVariableTarget.User) ?? "";
                    _txtLocalSystemPrompt.Text = Environment.GetEnvironmentVariable("LOCAL_LLM_SYSTEM_PROMPT", EnvironmentVariableTarget.User) ?? "";

                    var mode = Environment.GetEnvironmentVariable("AICAD_LLM_MODE", EnvironmentVariableTarget.User) ?? "";
                    if (_cmbLlmMode != null)
                    {
                        _cmbLlmMode.SelectedIndex = (mode.Equals("local", StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
                    }
                }
                catch { }
                
                // Load few-shot flag from environment: AICAD_USE_FEWSHOT (1 = enabled)
                try
                {
                    var fs = Environment.GetEnvironmentVariable("AICAD_USE_FEWSHOT", EnvironmentVariableTarget.User) ?? Environment.GetEnvironmentVariable("AICAD_USE_FEWSHOT");
                    _chkUseFewShot.Checked = string.IsNullOrEmpty(fs) ? true : (fs == "1" || fs.Equals("true", StringComparison.OrdinalIgnoreCase));
                }
                catch { _chkUseFewShot.Checked = true; }
                
                // Load allow multiple builds flag from environment: AICAD_ALLOW_MULTIPLE_BUILDS (1 = enabled)
                try
                {
                    var amb = Environment.GetEnvironmentVariable("AICAD_ALLOW_MULTIPLE_BUILDS", EnvironmentVariableTarget.User) ?? Environment.GetEnvironmentVariable("AICAD_ALLOW_MULTIPLE_BUILDS");
                    _chkAllowMultipleBuilds.Checked = string.IsNullOrEmpty(amb) ? false : (amb == "1" || amb.Equals("true", StringComparison.OrdinalIgnoreCase));
                }
                catch { _chkAllowMultipleBuilds.Checked = false; }
                
                _lblApiStatus.Text = "Loaded from environment variables";
                _lblApiStatus.ForeColor = Color.DarkGreen;
            }
            catch (Exception ex)
            {
                _lblApiStatus.Text = "Failed to load: " + ex.Message;
                _lblApiStatus.ForeColor = Color.Red;
            }
        }

        // Model list and population removed — model selection is managed via environment variables outside the UI.
        
        private void BtnSaveApiKey_Click(object sender, EventArgs e)
        {
            try
            {
                // Save all LLM settings to environment variables
                Environment.SetEnvironmentVariable("LOCAL_LLM_ENDPOINT", _txtLocalEndpoint.Text, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("GEMINI_API_KEY", _txtGeminiKey.Text, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("GROQ_API_KEY", _txtGroqKey.Text, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("LOCAL_LLM_MODEL", _txtLocalModel.Text, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("LOCAL_LLM_SYSTEM_PROMPT", _txtLocalSystemPrompt.Text, EnvironmentVariableTarget.User);
                // Save LLM mode (local/cloud)
                try { Environment.SetEnvironmentVariable("AICAD_LLM_MODE", (_cmbLlmMode != null && _cmbLlmMode.SelectedIndex == 1) ? "local" : "cloud", EnvironmentVariableTarget.User); } catch { }
                
                // Save few-shot checkbox state
                try
                {
                    Environment.SetEnvironmentVariable("AICAD_USE_FEWSHOT", _chkUseFewShot.Checked ? "1" : "0", EnvironmentVariableTarget.User);
                }
                catch { }
                
                // Save allow multiple builds checkbox state
                try
                {
                    Environment.SetEnvironmentVariable("AICAD_ALLOW_MULTIPLE_BUILDS", _chkAllowMultipleBuilds.Checked ? "1" : "0", EnvironmentVariableTarget.User);
                }
                catch { }
                
                _lblApiStatus.Text = "Settings saved to environment variables! Restart SolidWorks to apply changes.";
                _lblApiStatus.ForeColor = Color.DarkGreen;
                
                MessageBox.Show(
                    "Settings saved to environment variables.\n\nPlease restart SolidWorks for changes to take effect.",
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
                    "Failed to save settings: " + ex.Message,
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
                    if (_cmbCloudProvider.SelectedIndex == 0)
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

                                // Model dropdown population removed; models are managed externally via env var.
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
                    else if (_cmbCloudProvider.SelectedIndex == 1)
                    {
                        // Groq: simple models list check
                        using (var http = new HttpClient())
                        {
                            http.Timeout = TimeSpan.FromSeconds(10);
                            http.DefaultRequestHeaders.Clear();
                            http.DefaultRequestHeaders.Add("Authorization", "Bearer " + key);
                            var resp = await http.GetAsync("https://api.groq.com/v1/models");
                            var body = await resp.Content.ReadAsStringAsync();
                            this.BeginInvoke((Action)(() =>
                            {
                                if (resp.IsSuccessStatusCode)
                                {
                                    _lblApiStatus.Text = $"Groq: OK — {((int)resp.StatusCode)}";
                                    _lblApiStatus.ForeColor = Color.DarkGreen;
                                }
                                else
                                {
                                    _lblApiStatus.Text = $"Groq test failed: {(int)resp.StatusCode} {resp.ReasonPhrase}";
                                    _lblApiStatus.ForeColor = Color.Red;
                                }
                            }));
                        }
                    }
                    else
                    {
                        _lblApiStatus.Text = "Test not available for New provider.";
                        _lblApiStatus.ForeColor = Color.Gray;
                    }

                        // If Local LLM mode selected, attempt a quick POST
                        if (_cmbLlmMode != null && _cmbLlmMode.SelectedIndex == 1)
                        {
                            var endpoint = _txtLocalEndpoint.Text;
                            if (string.IsNullOrWhiteSpace(endpoint))
                            {
                                this.BeginInvoke((Action)(() =>
                                {
                                    _lblApiStatus.Text = "Local endpoint is empty.";
                                    _lblApiStatus.ForeColor = Color.Orange;
                                }));
                            }
                            else
                            {
                                try
                                {
                                    using (var http = new HttpClient())
                                    {
                                        http.Timeout = TimeSpan.FromSeconds(10);
                                        var payload = new
                                        {
                                            model = string.IsNullOrWhiteSpace(_txtLocalModel.Text) ? "" : _txtLocalModel.Text,
                                            messages = new[] { new { role = "user", content = "Test: hello" } },
                                            temperature = 0.0,
                                            max_tokens = 10,
                                            stream = false
                                        };
                                        var json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
                                        var resp = http.PostAsync(endpoint, new StringContent(json, System.Text.Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
                                        var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                        this.BeginInvoke((Action)(() =>
                                        {
                                            if (resp.IsSuccessStatusCode)
                                            {
                                                _lblApiStatus.Text = "Local LLM: OK";
                                                _lblApiStatus.ForeColor = Color.DarkGreen;
                                            }
                                            else
                                            {
                                                _lblApiStatus.Text = $"Local LLM test failed: {(int)resp.StatusCode} {resp.ReasonPhrase}";
                                                _lblApiStatus.ForeColor = Color.Red;
                                            }
                                        }));
                                    }
                                }
                                catch (Exception ex)
                                {
                                    this.BeginInvoke((Action)(() =>
                                    {
                                        _lblApiStatus.Text = "Local LLM test error: " + ex.Message;
                                        _lblApiStatus.ForeColor = Color.Red;
                                    }));
                                }
                            }
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

                // GEMINI_MODEL is managed externally (env var).

                // Save project id (if set)
                try
                {
                    // Project ID field removed; no-op.
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

        private void ToggleApiKeyVisibility(object sender, EventArgs e, TextBox txtBox)
        {
            if (txtBox != null)
            {
                txtBox.UseSystemPasswordChar = !txtBox.UseSystemPasswordChar;
                var btn = sender as Button;
                if (btn != null)
                {
                    btn.Text = txtBox.UseSystemPasswordChar ? "Show" : "Hide";
                }
            }
        }
    }
}
