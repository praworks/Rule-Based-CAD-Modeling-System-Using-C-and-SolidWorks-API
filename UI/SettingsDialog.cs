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

        internal SplitContainer _splitContainer;
        internal FlowLayoutPanel _navPanel;
        internal Panel _contentHost;
        
        // MongoDB Tab Controls (made internal so DatabaseTab factory can assign them)
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
        
        // API Key Tab Controls
        internal TextBox _txtApiKey;
        internal Button _btnToggleApiKeyVisibility;
        internal ComboBox _cmbCloudProvider;
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
        // API/LLM labels (kept as fields so visibility can be toggled)
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

                // Model selection is configured via environment (`GEMINI_MODEL`) not the UI.
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
            Text = "Settings - AI-CAD-December";
            Size = new Size(1500, 1000);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            // Footer with Close and Apply buttons (dock first so SplitContainer fills remaining space)
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(10) };
            var footerRight = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 260, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 12, 0, 0) };
            var btnApplyAll = new Button { Text = "Apply", Width = 100, Height = 36 };
            var btnClose = new Button { Text = "Close", Width = 100, Height = 36 };
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
                SplitterDistance = 20,
                IsSplitterFixed = false
            };

            // Left: navigation
            _navPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(8),
                AutoScroll = true,
                WrapContents = false
            };
            _splitContainer.Panel1.Controls.Add(_navPanel);

            // Right: content host
            _contentHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(40) };
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

            btnDb.Click += (s, e) => { foreach (Control c in _navPanel.Controls) if (c is Button b) UITheme.ApplyNavButtonStyle(b, false); UITheme.ApplyNavButtonStyle((Button)s, true); ShowPanel((Control)((Button)s).Tag); };
            btnLlm.Click += (s, e) => { foreach (Control c in _navPanel.Controls) if (c is Button b) UITheme.ApplyNavButtonStyle(b, false); UITheme.ApplyNavButtonStyle((Button)s, true); ShowPanel((Control)((Button)s).Tag); };
            btnSamples.Click += (s, e) => { foreach (Control c in _navPanel.Controls) if (c is Button b) UITheme.ApplyNavButtonStyle(b, false); UITheme.ApplyNavButtonStyle((Button)s, true); ShowPanel((Control)((Button)s).Tag); };
            btnNameEasy.Click += (s, e) => { foreach (Control c in _navPanel.Controls) if (c is Button b) UITheme.ApplyNavButtonStyle(b, false); UITheme.ApplyNavButtonStyle((Button)s, true); ShowPanel((Control)((Button)s).Tag); };

            _navPanel.Controls.Add(btnDb);
            _navPanel.Controls.Add(btnLlm);
            _navPanel.Controls.Add(btnSamples);
            _navPanel.Controls.Add(btnNameEasy);

            // Add SplitContainer after footer so it fills the remaining area
            Controls.Add(_splitContainer);
            // Ensure the SplitContainer z-order is correct (so footer and content render properly)
            _splitContainer.BringToFront();

            // show database by default and mark nav button active
            UITheme.ApplyNavButtonStyle(btnDb, true);
            ShowPanel(dbPanel);

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
        
        
        
        





        
        
        internal void BtnLoadMongo_Click(object sender, EventArgs e)
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

        private void ShowPanel(Control p)
        {
            if (p == null) return;
            _contentHost.Controls.Clear();
            p.Dock = DockStyle.Fill;
            _contentHost.Controls.Add(p);
            // update nav button styles to reflect active panel
            try
            {
                foreach (Control c in _navPanel.Controls)
                {
                    if (c is Button b)
                    {
                        var isActive = object.ReferenceEquals(b.Tag, p);
                        UITheme.ApplyNavButtonStyle(b, isActive);
                    }
                }
            }
            catch { }
        }
        
        internal void BtnSaveMongo_Click(object sender, EventArgs e)
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
        
        internal async void BtnLoadApiKey_Click(object sender, EventArgs e)
        {
            try
            {
                // Load all LLM settings from environment variables
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
        
        internal void BtnSaveApiKey_Click(object sender, EventArgs e)
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

        internal async Task BtnTestApi_Click(object sender, EventArgs e)
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
                                        // If the user provided only a base URL (e.g. http://127.0.0.1:1234) try common OpenAI-like paths.
                                        var endpointsToTry = new System.Collections.Generic.List<string>();
                                        if (endpoint.IndexOf("/v1/", StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            endpointsToTry.Add(endpoint);
                                        }
                                        else
                                        {
                                            var baseUrl = endpoint.TrimEnd('/');
                                            endpointsToTry.Add(baseUrl + "/v1/chat/completions");
                                            endpointsToTry.Add(baseUrl + "/v1/responses");
                                            endpointsToTry.Add(baseUrl + "/v1/completions");
                                        }

                                        HttpResponseMessage lastResp = null;
                                        Exception lastEx = null;
                                        foreach (var url in endpointsToTry)
                                        {
                                            try
                                            {
                                                var resp = await http.PostAsync(url, new StringContent(json, System.Text.Encoding.UTF8, "application/json")).ConfigureAwait(false);
                                                lastResp = resp;
                                                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                                                this.BeginInvoke((Action)(() =>
                                                {
                                                    if (resp.IsSuccessStatusCode)
                                                    {
                                                        _lblApiStatus.Text = $"Local LLM: OK ({url})";
                                                        _lblApiStatus.ForeColor = Color.DarkGreen;
                                                    }
                                                    else
                                                    {
                                                        _lblApiStatus.Text = $"Local LLM test failed: {(int)resp.StatusCode} {resp.ReasonPhrase} (tried {url})";
                                                        _lblApiStatus.ForeColor = Color.Red;
                                                    }
                                                }));

                                                // stop after first successful or non-error response
                                                if (resp.IsSuccessStatusCode) break;
                                            }
                                            catch (Exception ex2)
                                            {
                                                lastEx = ex2;
                                                // try next endpoint
                                            }
                                        }

                                        if (lastResp == null && lastEx != null)
                                        {
                                            this.BeginInvoke((Action)(() =>
                                            {
                                                _lblApiStatus.Text = "Local LLM test error: " + lastEx.Message;
                                                _lblApiStatus.ForeColor = Color.Red;
                                            }));
                                        }
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

        internal void BtnBrowseNameEasy_Click(object sender, EventArgs e)
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

        internal void BtnSaveNameEasy_Click(object sender, EventArgs e)
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

        internal void ToggleApiKeyVisibility(object sender, EventArgs e, TextBox txtBox)
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
