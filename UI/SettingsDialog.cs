using System;
using System.Drawing;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text;

namespace AICAD.UI
{
    /// <summary>
    /// Settings Dialog for managing DB connection and API key configuration
    /// </summary>
    public class SettingsDialog : Form
    {
        private const string RecommendedMongoUri = "mongodb+srv://prashan2011th_db_user:Uobz3oeAutZMRuCl@rule-based-cad-modeling.dlrnkre.mongodb.net/";

        internal SplitContainer _splitContainer;
        internal FlowLayoutPanel _navPanel;
        internal Panel _contentHost;
        
        // MongoDB Tab Controls
        internal TextBox _txtMongoUri;
        internal TextBox _txtMongoDb;
        internal TextBox _txtMongoUser;
        internal TextBox _txtMongoPassword;
        internal CheckBox _chkUseFewShot;
        internal CheckBox _chkAllowMultipleBuilds;
        internal Button _btnToggleMongoPwVisibility;
        internal Button _btnSaveMongo;
        internal Button _btnLoadMongo;
        internal Label _lblMongoStatus;
        
        // API Key Tab Controls (Updated)
        // Note: _txtApiKey and _cmbCloudProvider removed as they are obsolete
        internal Button _btnSaveApiKey;
        internal Button _btnLoadApiKey;
        internal Label _lblApiStatus;
        internal Button _btnTestApi;
        
        // Local LLM controls
        internal ComboBox _cmbLlmMode;
        internal TextBox _txtLocalEndpoint;
        internal TextBox _txtLocalModel;
        internal TextBox _txtLocalSystemPrompt;
        
        // New controls for direct key entry
        internal TextBox _txtGeminiKey;
        internal TextBox _txtGroqKey;
        
        // Labels handled by Tabs
        internal Label _lblProvider;
        internal Label _lblLlmMode;
        internal Label _lblApiKeyLabel;
        internal Label _lblLocalEndpointLabel;
        internal Label _lblLocalModelLabel;
        internal Label _lblLocalSysLabel;
        internal Label _lblCloudProvider;
        internal Label _lblGeminiKey;
        internal Label _lblGroqKey;
        
        // NameEasy Tab Controls
        internal TextBox _txtNameEasyPath;
        internal Button _btnBrowseNameEasy;
        internal Button _btnSaveNameEasy;
        internal Label _lblNameEasyInfo;
        
        // Samples Tab Controls
        internal RadioButton _rbZeroShot;
        internal RadioButton _rbOneShot;
        internal RadioButton _rbFewShot;
        internal TextBox _txtSamplesDbPath;
        internal Button _btnBrowseSamples;
        internal Button _btnSaveSamples;
        internal Label _lblSamplesInfo;
        
        public SettingsDialog()
        {
            InitializeComponents();
            // Applies the Font and Background color globally to the form
            UITheme.ApplyFormStyle(this);
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
            }
            catch { }
        }

        internal void BtnBrowseSamples_Click(object sender, EventArgs e)
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

        internal void BtnSaveSamples_Click(object sender, EventArgs e)
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

        internal void BtnLoadSamples_Click(object sender, EventArgs e)
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
            Text = "Settings";
            // FIX: Reduced size to fit standard screens
            Size = new Size(1000, 750);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            // Footer with Close and Apply buttons (dock first so SplitContainer fills remaining space)
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 80, Padding = new Padding(15) };
            var footerRight = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 350, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 15, 0, 0) };
            var btnApplyAll = new Button { Text = "Apply", Width = 120, Height = 40 };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 40 };
            UITheme.ApplyButtonStyle(btnApplyAll, true);
            UITheme.ApplyButtonStyle(btnClose, false);
            btnApplyAll.Click += BtnApplyAll_Click;
            btnClose.Click += (s, e) => Close();
            footerRight.Controls.Add(btnClose);
            footerRight.Controls.Add(btnApplyAll);
            footer.Controls.Add(footerRight);
            Controls.Add(footer);

            // Create SplitContainer with a left navigation panel and right content host
            _splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                // FIX: Increased splitter distance for wider sidebar
                SplitterDistance = 280,
                // Prevent the left navigation panel from collapsing below this size
                Panel1MinSize = 280,
                IsSplitterFixed = false
            };

            // Left: navigation
            _navPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(12),
                AutoScroll = true,
                WrapContents = false
            };
            _splitContainer.Panel1.Controls.Add(_navPanel);

            // Right: content host
            // Increased padding for better spacing
            _contentHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(30) };
            _splitContainer.Panel2.Controls.Add(_contentHost);

            // Build content panels using extracted tab classes
            var dbPanel = DatabaseTab.CreateDatabasePanel(this);
            var llmPanel = LlmTab.CreatePanel(this);
            var samplesPanel = SamplesTab.CreatePanel(this);
            var nameEasyPanel = NameEasyTab.CreatePanel(this);

            // Add nav buttons
            var btnDb = new Button { Text = "Database", Tag = dbPanel };
            var btnLlm = new Button { Text = "LLM Settings", Tag = llmPanel };
            var btnSamples = new Button { Text = "Samples", Tag = samplesPanel };
            var btnNameEasy = new Button { Text = "NameEasy", Tag = nameEasyPanel };
            UITheme.ApplyNavButtonStyle(btnDb, false);
            UITheme.ApplyNavButtonStyle(btnLlm, false);
            UITheme.ApplyNavButtonStyle(btnSamples, false);
            UITheme.ApplyNavButtonStyle(btnNameEasy, false);

            btnDb.Click += (s, e) => { ActivateTab((Button)s, _navPanel); };
            btnLlm.Click += (s, e) => { ActivateTab((Button)s, _navPanel); };
            btnSamples.Click += (s, e) => { ActivateTab((Button)s, _navPanel); };
            btnNameEasy.Click += (s, e) => { ActivateTab((Button)s, _navPanel); };

            _navPanel.Controls.Add(btnDb);
            _navPanel.Controls.Add(btnLlm);
            _navPanel.Controls.Add(btnSamples);
            _navPanel.Controls.Add(btnNameEasy);

            // Add SplitContainer after footer so it fills the remaining area
            Controls.Add(_splitContainer);
            _splitContainer.BringToFront();

            // show database by default
            ActivateTab(btnDb, _navPanel);
            
            // Load defaults
            try
            {
                var defaultPath = AICAD.Services.SettingsManager.GetDatabasePath();
                if (!string.IsNullOrEmpty(defaultPath) && _txtNameEasyPath != null)
                {
                    _txtNameEasyPath.Text = defaultPath;
                }
                var samplesPath = Environment.GetEnvironmentVariable("AICAD_SAMPLES_DB_PATH", EnvironmentVariableTarget.User) ?? "";
                if (!string.IsNullOrEmpty(samplesPath) && _txtSamplesDbPath != null)
                {
                    _txtSamplesDbPath.Text = samplesPath;
                }
            }
            catch { }
        }

        private void ActivateTab(Button activeBtn, Control navPanel)
        {
            foreach (Control c in navPanel.Controls) 
                if (c is Button b) UITheme.ApplyNavButtonStyle(b, false);
            
            UITheme.ApplyNavButtonStyle(activeBtn, true);
            ShowPanel((Control)activeBtn.Tag);
        }

        internal void BtnLoadMongo_Click(object sender, EventArgs e)
        {
            try
            {
                _txtMongoUri.Text = Environment.GetEnvironmentVariable("MONGODB_URI", EnvironmentVariableTarget.User) 
                    ?? Environment.GetEnvironmentVariable("MONGO_LOG_CONN", EnvironmentVariableTarget.User) 
                    ?? "";

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

        private void ShowPanel(Control p)
        {
            if (p == null) return;
            _contentHost.Controls.Clear();
            p.Dock = DockStyle.Fill;
            _contentHost.Controls.Add(p);
        }
        
        internal void BtnSaveMongo_Click(object sender, EventArgs e)
        {
            try
            {
                Environment.SetEnvironmentVariable("MONGODB_URI", _txtMongoUri.Text, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("MONGODB_DB", _txtMongoDb.Text, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("MONGODB_USER", _txtMongoUser.Text, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("MONGODB_PW", _txtMongoPassword.Text, EnvironmentVariableTarget.User);
                
                _lblMongoStatus.Text = "Saved! Restart SolidWorks.";
                _lblMongoStatus.ForeColor = Color.DarkGreen;
                
                MessageBox.Show("DB settings saved. Restart SolidWorks.", "Settings Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _lblMongoStatus.Text = "Failed to save: " + ex.Message;
                _lblMongoStatus.ForeColor = Color.Red;
            }
        }
        
        internal async void BtnLoadApiKey_Click(object sender, EventArgs e)
        {
            try
            {
                _txtLocalEndpoint.Text = Environment.GetEnvironmentVariable("LOCAL_LLM_ENDPOINT", EnvironmentVariableTarget.User) ?? "http://127.0.0.1:1234";
                _txtGeminiKey.Text = Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.User) ?? "";
                _txtGroqKey.Text = Environment.GetEnvironmentVariable("GROQ_API_KEY", EnvironmentVariableTarget.User) ?? "";
                _txtLocalModel.Text = Environment.GetEnvironmentVariable("LOCAL_LLM_MODEL", EnvironmentVariableTarget.User) ?? "";
                _txtLocalSystemPrompt.Text = Environment.GetEnvironmentVariable("LOCAL_LLM_SYSTEM_PROMPT", EnvironmentVariableTarget.User) ?? "";

                var mode = Environment.GetEnvironmentVariable("AICAD_LLM_MODE", EnvironmentVariableTarget.User) ?? "";
                if (_cmbLlmMode != null)
                {
                    _cmbLlmMode.SelectedIndex = (mode.Equals("local", StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
                }
                
                var fs = Environment.GetEnvironmentVariable("AICAD_USE_FEWSHOT", EnvironmentVariableTarget.User) ?? Environment.GetEnvironmentVariable("AICAD_USE_FEWSHOT");
                _chkUseFewShot.Checked = string.IsNullOrEmpty(fs) ? true : (fs == "1" || fs.Equals("true", StringComparison.OrdinalIgnoreCase));
                
                var amb = Environment.GetEnvironmentVariable("AICAD_ALLOW_MULTIPLE_BUILDS", EnvironmentVariableTarget.User) ?? Environment.GetEnvironmentVariable("AICAD_ALLOW_MULTIPLE_BUILDS");
                _chkAllowMultipleBuilds.Checked = string.IsNullOrEmpty(amb) ? false : (amb == "1" || amb.Equals("true", StringComparison.OrdinalIgnoreCase));
                
                _lblApiStatus.Text = "Loaded from environment variables";
                _lblApiStatus.ForeColor = Color.DarkGreen;
            }
            catch (Exception ex)
            {
                _lblApiStatus.Text = "Failed to load: " + ex.Message;
                _lblApiStatus.ForeColor = Color.Red;
            }
        }
        
        internal void BtnSaveApiKey_Click(object sender, EventArgs e)
        {
            try
            {
                Environment.SetEnvironmentVariable("LOCAL_LLM_ENDPOINT", _txtLocalEndpoint.Text, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("GEMINI_API_KEY", _txtGeminiKey.Text, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("GROQ_API_KEY", _txtGroqKey.Text, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("LOCAL_LLM_MODEL", _txtLocalModel.Text, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("LOCAL_LLM_SYSTEM_PROMPT", _txtLocalSystemPrompt.Text, EnvironmentVariableTarget.User);
                
                var mode = (_cmbLlmMode != null && _cmbLlmMode.SelectedIndex == 1) ? "local" : "cloud";
                Environment.SetEnvironmentVariable("AICAD_LLM_MODE", mode, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("AICAD_USE_FEWSHOT", _chkUseFewShot.Checked ? "1" : "0", EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("AICAD_ALLOW_MULTIPLE_BUILDS", _chkAllowMultipleBuilds.Checked ? "1" : "0", EnvironmentVariableTarget.User);
                
                _lblApiStatus.Text = "Saved! Restart SolidWorks.";
                _lblApiStatus.ForeColor = Color.DarkGreen;
                
                MessageBox.Show("Settings saved. Restart SolidWorks.", "Settings Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _lblApiStatus.Text = "Failed to save: " + ex.Message;
                _lblApiStatus.ForeColor = Color.Red;
            }
        }

        // FIX: Rewritten to use correct key fields and logic
        internal async Task BtnTestApi_Click(object sender, EventArgs e)
        {
            _lblApiStatus.Text = "Testing API...";
            _lblApiStatus.ForeColor = Color.Blue;
            _btnTestApi.Enabled = false;

            try
            {
                bool isLocal = _cmbLlmMode.SelectedIndex == 1;

                if (!isLocal)
                {
                    // Test Gemini if key exists
                    if (!string.IsNullOrWhiteSpace(_txtGeminiKey.Text))
                    {
                        var client = new AICAD.Services.GeminiClient(_txtGeminiKey.Text);
                        var res = await client.TestApiKeyAsync(null).ConfigureAwait(false);
                        this.BeginInvoke((Action)(() =>
                        {
                            if (res != null && res.Success)
                            {
                                _lblApiStatus.Text = $"Gemini OK: Found {res.ModelsFound} models.";
                                _lblApiStatus.ForeColor = Color.DarkGreen;
                            }
                            else
                            {
                                _lblApiStatus.Text = $"Gemini Fail: {res?.Message ?? "Unknown error"}";
                                _lblApiStatus.ForeColor = Color.Red;
                            }
                        }));
                    }
                    
                    // Test Groq if key exists
                    if (!string.IsNullOrWhiteSpace(_txtGroqKey.Text))
                    {
                        using (var http = new HttpClient())
                        {
                            http.Timeout = TimeSpan.FromSeconds(10);
                            http.DefaultRequestHeaders.Add("Authorization", "Bearer " + _txtGroqKey.Text);
                            var resp = await http.GetAsync("https://api.groq.com/v1/models");
                            this.BeginInvoke((Action)(() =>
                            {
                                if (resp.IsSuccessStatusCode)
                                {
                                    // If Gemini was also tested, append status
                                    string current = _lblApiStatus.ForeColor == Color.DarkGreen ? _lblApiStatus.Text + " | " : "";
                                    _lblApiStatus.Text = current + "Groq OK.";
                                    _lblApiStatus.ForeColor = Color.DarkGreen;
                                }
                                else
                                {
                                    _lblApiStatus.Text = $"Groq Fail: {resp.StatusCode}";
                                    _lblApiStatus.ForeColor = Color.Red;
                                }
                            }));
                        }
                    }

                    if (string.IsNullOrWhiteSpace(_txtGeminiKey.Text) && string.IsNullOrWhiteSpace(_txtGroqKey.Text))
                    {
                        _lblApiStatus.Text = "No Cloud API Keys entered.";
                        _lblApiStatus.ForeColor = Color.Orange;
                    }
                }
                else
                {
                    // Test Local
                    var endpoint = _txtLocalEndpoint.Text;
                    if (string.IsNullOrWhiteSpace(endpoint))
                    {
                        _lblApiStatus.Text = "Local endpoint is empty.";
                        _lblApiStatus.ForeColor = Color.Orange;
                    }
                    else
                    {
                         using (var http = new HttpClient())
                         {
                            http.Timeout = TimeSpan.FromSeconds(5);
                            try 
                            {
                                // Simple GET to check if server is up
                                var resp = await http.GetAsync(endpoint.TrimEnd('/') + "/v1/models"); 
                                this.BeginInvoke((Action)(() =>
                                {
                                    if (resp.IsSuccessStatusCode)
                                    {
                                        _lblApiStatus.Text = "Local OK (Server reachable)";
                                        _lblApiStatus.ForeColor = Color.DarkGreen;
                                    }
                                    else
                                    {
                                        _lblApiStatus.Text = $"Local Fail: {resp.StatusCode}";
                                        _lblApiStatus.ForeColor = Color.Red;
                                    }
                                }));
                            }
                            catch(Exception ex) 
                            {
                                this.BeginInvoke((Action)(() =>
                                {
                                    _lblApiStatus.Text = "Local Error: " + ex.Message;
                                    _lblApiStatus.ForeColor = Color.Red;
                                }));
                            }
                         }
                    }
                }
            }
            catch (Exception ex)
            {
                _lblApiStatus.Text = "Test Error: " + ex.Message;
                _lblApiStatus.ForeColor = Color.Red;
            }
            finally
            {
                this.BeginInvoke((Action)(() => _btnTestApi.Enabled = true));
            }
        }

        private void BtnApplyAll_Click(object sender, EventArgs e)
        {
            try
            {
                BtnSaveMongo_Click(sender, e);
                BtnSaveApiKey_Click(sender, e);
                MessageBox.Show("All settings applied. Restart SolidWorks.", "Applied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        internal void BtnBrowseNameEasy_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select folder for NameEasy.db";
                dlg.ShowNewFolderButton = true;
                if (!string.IsNullOrEmpty(_txtNameEasyPath.Text))
                    try { dlg.SelectedPath = System.IO.Path.GetDirectoryName(_txtNameEasyPath.Text); } catch { }

                if (dlg.ShowDialog() == DialogResult.OK)
                    _txtNameEasyPath.Text = System.IO.Path.Combine(dlg.SelectedPath, "NameEasy.db");
            }
        }

        internal void BtnSaveNameEasy_Click(object sender, EventArgs e)
        {
            var path = _txtNameEasyPath.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (AICAD.Services.SettingsManager.SetDatabasePath(path))
                    MessageBox.Show("Path saved. Restart SolidWorks.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show("Failed to save path.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        internal void ToggleApiKeyVisibility(object sender, EventArgs e, TextBox txtBox)
        {
            if (txtBox != null)
            {
                txtBox.UseSystemPasswordChar = !txtBox.UseSystemPasswordChar;
                if (sender is Button btn) btn.Text = txtBox.UseSystemPasswordChar ? "Show" : "Hide";
            }
        }
    }
}
