using System;
using System.Net.Http;
using MongoDB.Driver;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using System.Text;
using System.Diagnostics;
using System.Linq;
using AICAD.Services;

namespace AICAD.UI
{
    public partial class SettingsWindow : Window
    {
        public class ProviderItem
        {
            public string Id { get; set; }
            public string DisplayName { get; set; }
        }

        private System.Collections.ObjectModel.ObservableCollection<ProviderItem> _providers;

        public SettingsWindow()
        {
            InitializeComponent();
            InitializeProviderList();
            LoadAllSettings();
        }

        private void InitializeProviderList()
        {
            _providers = new System.Collections.ObjectModel.ObservableCollection<ProviderItem>();
            
            // Load order from env or use default
            var order = Environment.GetEnvironmentVariable("AICAD_LLM_PRIORITY", EnvironmentVariableTarget.User);
            if (string.IsNullOrWhiteSpace(order)) order = "local,gemini,groq";

            var parts = order.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                AddProviderById(p.Trim().ToLower());
            }

            // Ensure all are present
            if (!_providers.Any(x => x.Id == "local")) AddProviderById("local");
            if (!_providers.Any(x => x.Id == "gemini")) AddProviderById("gemini");
            if (!_providers.Any(x => x.Id == "groq")) AddProviderById("groq");

            ProviderPriorityListBox.ItemsSource = _providers;
        }

        private void AddProviderById(string id)
        {
            if (id == "local") _providers.Add(new ProviderItem { Id = "local", DisplayName = "💻 LM Studio (Local)" });
            else if (id == "gemini") _providers.Add(new ProviderItem { Id = "gemini", DisplayName = "☁️ Google Gemini" });
            else if (id == "groq") _providers.Add(new ProviderItem { Id = "groq", DisplayName = "⚡ Groq" });
        }

        private void MoveProviderUp_Click(object sender, RoutedEventArgs e)
        {
            var selected = ProviderPriorityListBox.SelectedItem as ProviderItem;
            if (selected == null) return;
            int index = _providers.IndexOf(selected);
            if (index > 0)
            {
                _providers.Move(index, index - 1);
            }
        }

        private void MoveProviderDown_Click(object sender, RoutedEventArgs e)
        {
            var selected = ProviderPriorityListBox.SelectedItem as ProviderItem;
            if (selected == null) return;
            int index = _providers.IndexOf(selected);
            if (index < _providers.Count - 1)
            {
                _providers.Move(index, index + 1);
            }
        }

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string tag)
            {
                contentTitle.Text = btn.Content?.ToString() ?? "";
                ShowPanel(tag);
            }
        }

        private void ShowPanel(string panelName)
        {
            GeneralPanel.Visibility = panelName == "GeneralPanel" ? Visibility.Visible : Visibility.Collapsed;
            MongoPanel.Visibility = panelName == "MongoPanel" ? Visibility.Visible : Visibility.Collapsed;
            ApiKeysPanel.Visibility = panelName == "ApiKeysPanel" ? Visibility.Visible : Visibility.Collapsed;
            AccountPanel.Visibility = panelName == "AccountPanel" ? Visibility.Visible : Visibility.Collapsed;
            NameEasyPanel.Visibility = panelName == "NameEasyPanel" ? Visibility.Visible : Visibility.Collapsed;
            SamplesPanel.Visibility = panelName == "SamplesPanel" ? Visibility.Visible : Visibility.Collapsed;

            if (panelName == "ApiKeysPanel")
            {
                CheckAllLlmStatuses();
            }
        }

        private void LoadAllSettings()
        {
            try
            {
                TryUseSecretsClientFile();
                LoadMongoButton_Click(null, null);
                LoadApiButton_Click(null, null);
                LoadDataApiSettings();
                try { LoadSamplesButton_Click(null, null); } catch { }
                try { LoadNameEasySettings(); } catch { }
                try { LoadAccountInfo(); } catch { }
            }
            catch { }
        }

        // Load existing Google account info from stored tokens
        private void LoadAccountInfo()
        {
            try
            {
                var tokenJson = AICAD.Services.CredentialManager.ReadGenericSecret("SolidWorksTextToCAD_OAuthToken");
                System.Diagnostics.Debug.WriteLine($"LoadAccountInfo: tokenJson is {(string.IsNullOrWhiteSpace(tokenJson) ? "null/empty" : $"{tokenJson.Length} chars")}");
                
                if (string.IsNullOrWhiteSpace(tokenJson))
                {
                    ShowSignedOutState();
                    return;
                }

                var j = JObject.Parse(tokenJson);
                var idToken = j.Value<string>("id_token");
                System.Diagnostics.Debug.WriteLine($"LoadAccountInfo: idToken is {(string.IsNullOrWhiteSpace(idToken) ? "null/empty" : "present")}");
                
                if (string.IsNullOrWhiteSpace(idToken))
                {
                    ShowSignedOutState();
                    return;
                }

                var payload = DecodeJwtPayload(idToken);
                if (payload == null)
                {
                    System.Diagnostics.Debug.WriteLine("LoadAccountInfo: payload is null");
                    ShowSignedOutState();
                    return;
                }

                var name = payload.Value<string>("name") ?? payload.Value<string>("preferred_username");
                var email = payload.Value<string>("email");
                System.Diagnostics.Debug.WriteLine($"LoadAccountInfo: name={name}, email={email}");
                
                if (!string.IsNullOrWhiteSpace(name))
                {
                    DisplayNameTextBox.Text = name;
                    DisplayNameText.Text = name;
                }
                if (!string.IsNullOrWhiteSpace(email))
                {
                    EmailTextBox.Text = email;
                    EmailText.Text = email;
                }
                
                ShowSignedInState();
                System.Diagnostics.Debug.WriteLine("LoadAccountInfo: ShowSignedInState() called");
            }
            catch (Exception ex)
            {
                // Debug: show what went wrong
                System.Diagnostics.Debug.WriteLine("LoadAccountInfo failed: " + ex.Message);
                ShowSignedOutState();
            }
        }

        private void ShowSignedInState()
        {
            SignedInCard.Visibility = Visibility.Visible;
            SignedOutCard.Visibility = Visibility.Collapsed;
        }

        private void ShowSignedOutState()
        {
            SignedInCard.Visibility = Visibility.Collapsed;
            SignedOutCard.Visibility = Visibility.Visible;
        }

        // If a local Secrets/client_secret*.json exists in a parent directory, register it
        // as the GOOGLE_OAUTH_CLIENT_FILE (User-level) so the OAuth helper can find it.
        private void TryUseSecretsClientFile()
        {
            try
            {
                var dir = new System.IO.DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                while (dir != null)
                {
                    var secretsDir = System.IO.Path.Combine(dir.FullName, "Secrets");
                    if (System.IO.Directory.Exists(secretsDir))
                    {
                        try
                        {
                            var matches = System.IO.Directory.GetFiles(secretsDir, "client_secret*.json", System.IO.SearchOption.TopDirectoryOnly);
                            if (matches.Length > 0)
                            {
                                var file = matches[0];
                                var existing = Environment.GetEnvironmentVariable("GOOGLE_OAUTH_CLIENT_FILE", EnvironmentVariableTarget.User)
                                               ?? Environment.GetEnvironmentVariable("GOOGLE_OAUTH_CLIENT_FILE");
                                if (string.IsNullOrWhiteSpace(existing))
                                {
                                    Environment.SetEnvironmentVariable("GOOGLE_OAUTH_CLIENT_FILE", file, EnvironmentVariableTarget.User);
                                    ApiStatusTextBlock.Text = "Found local Google client secret and registered for OAuth.";
                                }

                                // Also set client id/secret env vars if present in the JSON so Load() picks them up immediately.
                                try
                                {
                                    var text = System.IO.File.ReadAllText(file);
                                    var root = Newtonsoft.Json.Linq.JObject.Parse(text);
                                    var installed = root["installed"] as Newtonsoft.Json.Linq.JObject ?? root["web"] as Newtonsoft.Json.Linq.JObject;
                                    if (installed != null)
                                    {
                                        var fileClientId = installed.Value<string>("client_id");
                                        var fileClientSecret = installed.Value<string>("client_secret");
                                        if (!string.IsNullOrWhiteSpace(fileClientId))
                                        {
                                            try {
                                                Environment.SetEnvironmentVariable("GOOGLE_OAUTH_CLIENT_ID", fileClientId.Trim(), EnvironmentVariableTarget.User);
                                                Environment.SetEnvironmentVariable("GOOGLE_OAUTH_CLIENT_ID", fileClientId.Trim(), EnvironmentVariableTarget.Process);
                                            } catch { }
                                        }
                                        if (!string.IsNullOrWhiteSpace(fileClientSecret))
                                        {
                                            try {
                                                Environment.SetEnvironmentVariable("GOOGLE_OAUTH_CLIENT_SECRET", fileClientSecret.Trim(), EnvironmentVariableTarget.User);
                                                Environment.SetEnvironmentVariable("GOOGLE_OAUTH_CLIENT_SECRET", fileClientSecret.Trim(), EnvironmentVariableTarget.Process);
                                            } catch { }
                                        }
                                        // Refresh cached config so the running process picks up the new values immediately
                                        try { AICAD.Services.GoogleOAuthConfig.RefreshCache(); } catch { }
                                    }
                                }
                                catch { }

                                return;
                            }
                        }
                        catch { }
                    }
                    dir = dir.Parent;
                }
            }
            catch { }
        }

        private void LoadMongoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MongoConnectionStringTextBox.Text = Environment.GetEnvironmentVariable("MONGODB_URI", EnvironmentVariableTarget.User)
                    ?? Environment.GetEnvironmentVariable("MONGO_LOG_CONN", EnvironmentVariableTarget.User)
                    ?? "";

                var current = MongoConnectionStringTextBox.Text ?? string.Empty;
                if (string.IsNullOrWhiteSpace(current) ||
                    current.IndexOf("prashanth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    current.IndexOf("cluster2.9abz2oy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    current.IndexOf("prashan2011th", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    MongoConnectionStringTextBox.Text = ""; // do not populate private URIs in the new UI
                }

                MongoDbNameTextBox.Text = Environment.GetEnvironmentVariable("MONGODB_DB", EnvironmentVariableTarget.User) ?? "TaskPaneAddin";

                ApiStatusTextBlock.Text = "";
                MongoStatusTextBlock.Text = "";
                MongoLoadedInfoIcon.Visibility = Visibility.Visible;
                MongoStatusTextBlock.Foreground = new SolidColorBrush(Colors.DarkGreen);
            }
            catch (Exception ex)
            {
                MongoStatusTextBlock.Text = "Failed to load: " + ex.Message;
                MongoStatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            }
        }

        private void SaveMongoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Environment.SetEnvironmentVariable("MONGODB_URI", MongoConnectionStringTextBox.Text ?? "", EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("MONGODB_DB", MongoDbNameTextBox.Text ?? "", EnvironmentVariableTarget.User);

                MongoStatusTextBlock.Text = "Saved! Restart SolidWorks.";
                MongoLoadedInfoIcon.Visibility = Visibility.Collapsed;
                MongoStatusTextBlock.Foreground = new SolidColorBrush(Colors.DarkGreen);

                System.Windows.MessageBox.Show("DB settings saved. Restart SolidWorks.", "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MongoStatusTextBlock.Text = "Failed to save: " + ex.Message;
                MongoStatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            }
        }

        private async void TestMongoButton_Click(object sender, RoutedEventArgs e)
        {
            // Disable button to prevent re-entry
            TestMongoButton.IsEnabled = false;
            MongoStatusTextBlock.Text = "Testing connection...";
            MongoStatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);

            try
            {
                var conn = MongoConnectionStringTextBox.Text?.Trim() ?? string.Empty;
                var dbName = MongoDbNameTextBox.Text?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(conn))
                {
                    MongoStatusTextBlock.Text = "No Connection URI entered.";
                    MongoStatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                    return;
                }

                try
                {
                    var client = new MongoClient(conn);
                    // Try a lightweight operation: list database names
                    using (var cursor = await client.ListDatabaseNamesAsync().ConfigureAwait(false))
                    {
                        var any = await cursor.AnyAsync().ConfigureAwait(false);
                    }

                    Dispatcher.Invoke(() =>
                    {
                        MongoStatusTextBlock.Text = "Connection OK.";
                        MongoStatusTextBlock.Foreground = new SolidColorBrush(Colors.DarkGreen);
                        MongoLoadedInfoIcon.Visibility = Visibility.Collapsed;
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        MongoStatusTextBlock.Text = "Test failed: " + ex.Message;
                        MongoStatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                    });
                }
            }
            finally
            {
                Dispatcher.Invoke(() => TestMongoButton.IsEnabled = true);
            }
        }

        private void LoadApiButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LocalLlmEndpointTextBox.Text = Environment.GetEnvironmentVariable("LOCAL_LLM_ENDPOINT", EnvironmentVariableTarget.User) ?? "http://127.0.0.1:1234";
                // Gemini key is stored in a PasswordBox in the UI
                try { GeminiApiKeyPasswordBox.Password = Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.User) ?? ""; } catch { }
                try { GroqApiKeyPasswordBox.Password = Environment.GetEnvironmentVariable("GROQ_API_KEY", EnvironmentVariableTarget.User) ?? ""; } catch { }
                
                // Load Prompt Refinement Provider
                var refineProvider = Environment.GetEnvironmentVariable("PROMPT_REFINE_PROVIDER", EnvironmentVariableTarget.User) ?? "disabled";
                foreach (ComboBoxItem item in PromptRefineProviderComboBox.Items)
                {
                    if (item.Tag.ToString() == refineProvider)
                    {
                        PromptRefineProviderComboBox.SelectedItem = item;
                        break;
                    }
                }

                ApiStatusTextBlock.Text = "Loaded from environment variables";
                ApiStatusTextBlock.Foreground = new SolidColorBrush(Colors.DarkGreen);

                CheckAllLlmStatuses();
            }
            catch (Exception ex)
            {
                ApiStatusTextBlock.Text = "Failed to load: " + ex.Message;
                ApiStatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            }
        }

        private void SaveApiKeysButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Environment.SetEnvironmentVariable("LOCAL_LLM_ENDPOINT", LocalLlmEndpointTextBox.Text ?? "", EnvironmentVariableTarget.User);
                try { Environment.SetEnvironmentVariable("GEMINI_API_KEY", GeminiApiKeyPasswordBox.Password ?? "", EnvironmentVariableTarget.User); } catch { }
                try { Environment.SetEnvironmentVariable("GROQ_API_KEY", GroqApiKeyPasswordBox.Password ?? "", EnvironmentVariableTarget.User); } catch { }
                
                // Save Priority
                var priority = string.Join(",", _providers.Select(p => p.Id));
                Environment.SetEnvironmentVariable("AICAD_LLM_PRIORITY", priority, EnvironmentVariableTarget.User);

                // Save Prompt Refinement Provider
                var selectedRefineItem = PromptRefineProviderComboBox.SelectedItem as ComboBoxItem;
                if (selectedRefineItem != null)
                {
                    Environment.SetEnvironmentVariable("PROMPT_REFINE_PROVIDER", selectedRefineItem.Tag.ToString(), EnvironmentVariableTarget.User);
                }

                ApiStatusTextBlock.Text = "Saved! Restart SolidWorks.";
                ApiStatusTextBlock.Foreground = new SolidColorBrush(Colors.DarkGreen);

                System.Windows.MessageBox.Show("Settings saved. Restart SolidWorks.", "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ApiStatusTextBlock.Text = "Failed to save: " + ex.Message;
                ApiStatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            }
        }

        private async void TestApiButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            if (btn == null) return;

            btn.IsEnabled = false;
            try
            {
                if (btn.Name == "TestLocalButton")
                {
                    await TestLocalAsync();
                }
                else if (btn.Name == "TestGeminiButton")
                {
                    await TestGeminiAsync();
                }
                else if (btn.Name == "TestGroqButton")
                {
                    await TestGroqAsync();
                }
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }

        private async Task TestLocalAsync()
        {
            UpdateProviderStatus("Local", "Testing...", null);
            var endpoint = LocalLlmEndpointTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                UpdateProviderStatus("Local", "No endpoint", false);
                return;
            }

            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(5);
                    var testUrl = endpoint.TrimEnd('/') + "/v1/models";
                    var resp = await http.GetAsync(testUrl).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                        UpdateProviderStatus("Local", "Connected", true);
                    else
                        UpdateProviderStatus("Local", $"Error: {resp.StatusCode}", false);
                }
            }
            catch (Exception)
            {
                UpdateProviderStatus("Local", "Offline", false);
            }
        }

        private async Task TestGeminiAsync()
        {
            UpdateProviderStatus("Gemini", "Testing...", null);
            var key = GeminiApiKeyPasswordBox.Password?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                UpdateProviderStatus("Gemini", "No API Key", false);
                return;
            }

            try
            {
                var client = new AICAD.Services.GeminiClient(key);
                var res = await client.TestApiKeyAsync(null).ConfigureAwait(false);
                if (res != null && res.Success)
                    UpdateProviderStatus("Gemini", "Connected", true);
                else
                    UpdateProviderStatus("Gemini", res?.Message ?? "Failed", false);
            }
            catch (Exception)
            {
                UpdateProviderStatus("Gemini", "Error", false);
            }
        }

        private async Task TestGroqAsync()
        {
            UpdateProviderStatus("Groq", "Testing...", null);
            UpdateGroqUsageStats(); // Update rate limit stats
            
            var key = GroqApiKeyPasswordBox.Password?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                UpdateProviderStatus("Groq", "No API Key", false);
                return;
            }

            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(10);
                    http.DefaultRequestHeaders.Add("Authorization", "Bearer " + key);
                    var resp = await http.GetAsync("https://api.groq.com/openai/v1/models").ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        UpdateProviderStatus("Groq", "Connected", true);
                        UpdateGroqUsageStats(); // Refresh after test
                    }
                    else
                        UpdateProviderStatus("Groq", $"Error: {resp.StatusCode}", false);
                }
            }
            catch (Exception ex)
            {
                UpdateProviderStatus("Groq", $"Error: {ex.Message}", false);
            }
        }

        private void UpdateProviderStatus(string provider, string text, bool? success)
        {
            Dispatcher.Invoke(() =>
            {
                Ellipse circle = null;
                TextBlock txt = null;

                if (provider == "Local") { circle = LmStatusCircle; txt = LmStatusText; }
                else if (provider == "Gemini") { circle = GeminiStatusCircle; txt = GeminiStatusText; }
                else if (provider == "Groq") { circle = GroqStatusCircle; txt = GroqStatusText; }

                if (circle != null && txt != null)
                {
                    txt.Text = text;
                    if (success == true)
                    {
                        circle.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#28A745"));
                        txt.Foreground = circle.Fill;
                    }
                    else if (success == false)
                    {
                        circle.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC3545"));
                        txt.Foreground = circle.Fill;
                    }
                    else
                    {
                        circle.Fill = new SolidColorBrush(Colors.Gray);
                        txt.Foreground = new SolidColorBrush(Colors.Gray);
                    }
                }
            });
        }

        private void UpdateGroqUsageStats()
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var stats = AICAD.Services.GroqRateLimiter.GetUsageStats();
                    if (GroqUsageStatsText != null)
                    {
                        GroqUsageStatsText.Text = stats;
                    }
                }
                catch (Exception ex)
                {
                    if (GroqUsageStatsText != null)
                    {
                        GroqUsageStatsText.Text = "Stats unavailable: " + ex.Message;
                    }
                }
            });
        }

        private void ResetGroqLimits_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = System.Windows.MessageBox.Show(
                    "Reset Groq rate limit tracking? This will clear all usage history.\n\nOnly use this if you're experiencing false rate limit errors.",
                    "Reset Rate Limits",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    AICAD.Services.GroqRateLimiter.Reset();
                    UpdateGroqUsageStats();
                    System.Windows.MessageBox.Show("Rate limit tracking has been reset.", "Reset Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Failed to reset: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CheckAllLlmStatuses()
        {
            Task.Run(async () =>
            {
                await TestLocalAsync();
                await TestGeminiAsync();
                await TestGroqAsync();
                UpdateGroqUsageStats(); // Update stats after all tests
            });
        }

        private void ToggleApiKeyVisibility_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            if (btn?.Tag is string provider)
            {
                if (provider == "Gemini")
                {
                    var isPasswordVisible = GeminiApiKeyTextBox.Visibility == Visibility.Visible;
                    if (isPasswordVisible)
                    {
                        // Hide: copy from TextBox to PasswordBox and show PasswordBox
                        GeminiApiKeyPasswordBox.Password = GeminiApiKeyTextBox.Text;
                        GeminiApiKeyTextBox.Visibility = Visibility.Collapsed;
                        GeminiApiKeyPasswordBox.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        // Show: copy from PasswordBox to TextBox and show TextBox
                        GeminiApiKeyTextBox.Text = GeminiApiKeyPasswordBox.Password;
                        GeminiApiKeyPasswordBox.Visibility = Visibility.Collapsed;
                        GeminiApiKeyTextBox.Visibility = Visibility.Visible;
                    }
                }
                else if (provider == "Groq")
                {
                    var isPasswordVisible = GroqApiKeyTextBox.Visibility == Visibility.Visible;
                    if (isPasswordVisible)
                    {
                        // Hide: copy from TextBox to PasswordBox and show PasswordBox
                        GroqApiKeyPasswordBox.Password = GroqApiKeyTextBox.Text;
                        GroqApiKeyTextBox.Visibility = Visibility.Collapsed;
                        GroqApiKeyPasswordBox.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        // Show: copy from PasswordBox to TextBox and show TextBox
                        GroqApiKeyTextBox.Text = GroqApiKeyPasswordBox.Password;
                        GroqApiKeyPasswordBox.Visibility = Visibility.Collapsed;
                        GroqApiKeyTextBox.Visibility = Visibility.Visible;
                    }
                }
            }
        }

        private void UpdateApiStatus(string text, Color color)
        {
            ApiStatusTextBlock.Text = text;
            ApiStatusTextBlock.Foreground = new SolidColorBrush(color);
        }

        private void BrowseNameEasyButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "Select folder for NameEasy.db";
                dlg.ShowNewFolderButton = true;
                if (!string.IsNullOrEmpty(NameEasyFolderTextBox.Text))
                    try { dlg.SelectedPath = System.IO.Path.GetDirectoryName(NameEasyFolderTextBox.Text); } catch { }

                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    NameEasyFolderTextBox.Text = System.IO.Path.Combine(dlg.SelectedPath, "NameEasy.db");
            }
        }

        private void SaveNameEasyButton_Click(object sender, RoutedEventArgs e)
        {
            var path = NameEasyFolderTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var ok = AICAD.Services.SettingsManager.SetDatabasePath(path);

                // Persist the NameEasy boolean flags to registry under same branch
                try
                {
                    using (var reg = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\AI-CAD\NameEasy"))
                    {
                        if (reg != null)
                        {
                            reg.SetValue("AutoUpdateMaterial", (ChkAutoUpdateMaterial.IsChecked == true) ? "1" : "0");
                            reg.SetValue("AutoUpdateDescription", (ChkAutoUpdateDescription.IsChecked == true) ? "1" : "0");
                        }
                    }
                }
                catch { }

                if (ok)
                    System.Windows.MessageBox.Show("Settings saved. Restart SolidWorks.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    System.Windows.MessageBox.Show("Failed to save settings.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadNameEasySettings()
        {
            try
            {
                // Load database path using existing SettingsManager helper
                try { NameEasyFolderTextBox.Text = AICAD.Services.SettingsManager.GetDatabasePath(); } catch { }

                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\AI-CAD\NameEasy"))
                {
                    if (key != null)
                    {
                        var mat = key.GetValue("AutoUpdateMaterial")?.ToString() ?? "0";
                        var desc = key.GetValue("AutoUpdateDescription")?.ToString() ?? "0";
                        ChkAutoUpdateMaterial.IsChecked = (mat == "1" || mat.Equals("true", StringComparison.OrdinalIgnoreCase));
                        ChkAutoUpdateDescription.IsChecked = (desc == "1" || desc.Equals("true", StringComparison.OrdinalIgnoreCase));
                    }
                }
            }
            catch { }
        }

        private void BrowseSamplesButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "Select folder where sample shots are stored";
                dlg.ShowNewFolderButton = true;
                var current = SamplesFileTextBox.Text;
                if (!string.IsNullOrEmpty(current))
                {
                    try { dlg.SelectedPath = System.IO.Path.GetDirectoryName(current); } catch { }
                }

                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    SamplesFileTextBox.Text = System.IO.Path.Combine(dlg.SelectedPath, "samples.db");
                }
            }
        }

        private void SaveSamplesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Determine selected sample mode
                var mode = "few";
                if (SampleModeZeroRadio.IsChecked == true) mode = "zero";
                else if (SampleModeOneRadio.IsChecked == true) mode = "one";
                else if (SampleModeFewRadio.IsChecked == true) mode = "few";

                Environment.SetEnvironmentVariable("AICAD_SAMPLE_MODE", mode, EnvironmentVariableTarget.User);
                // Drive few-shot boolean from the Sample Mode radio buttons: zero-shot -> no few-shot; one/few -> few-shot
                var useFewFromRadio = (mode == "zero") ? "0" : "1";
                try { Environment.SetEnvironmentVariable("AICAD_USE_FEWSHOT", useFewFromRadio, EnvironmentVariableTarget.User); } catch { }
                
                // Log for debugging
                try { System.Diagnostics.Debug.WriteLine($"Sample mode saved: {mode}, AICAD_USE_FEWSHOT={useFewFromRadio}"); } catch { }
                Environment.SetEnvironmentVariable("AICAD_SAMPLES_DB_PATH", SamplesFileTextBox.Text ?? "", EnvironmentVariableTarget.User);

                // Randomize setting
                var randomize = RandomizeSamplesCheckBox.IsChecked == true ? "1" : "0";
                Environment.SetEnvironmentVariable("AICAD_SAMPLES_RANDOMIZE", randomize, EnvironmentVariableTarget.User);

                // Persist few-shot related flags from the SamplesPanel checkboxes (if present)
                // Note: `AICAD_USE_FEWSHOT` is already driven by the radio buttons above; keep key/static flags from checkboxes
                try { Environment.SetEnvironmentVariable("AICAD_FORCE_KEY_SHOTS", (ChkForceKeyShots?.IsChecked == true) ? "1" : "0", EnvironmentVariableTarget.User); } catch { }
                try { Environment.SetEnvironmentVariable("AICAD_FORCE_STATIC_FEWSHOT", (ChkForceStaticFewShot?.IsChecked == true) ? "1" : "0", EnvironmentVariableTarget.User); } catch { }

                System.Windows.MessageBox.Show("Samples settings saved to environment variables. Restart SolidWorks for changes to take effect.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Failed to save samples settings: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadDataApiSettings()
        {
            try
            {
                var endpoint = Environment.GetEnvironmentVariable("DATA_API_ENDPOINT", EnvironmentVariableTarget.User);
                var apiKey = Environment.GetEnvironmentVariable("DATA_API_KEY", EnvironmentVariableTarget.User);
                DataApiEndpointTextBox.Text = string.IsNullOrWhiteSpace(endpoint)
                    ? "https://data.mongodb-api.com/app/pedkniqj/endpoint/data/v1"
                    : endpoint;
                DataApiKeyPasswordBox.Password = string.IsNullOrWhiteSpace(apiKey)
                    ? "3b65c98d-3603-433d-bf2d-d4840aecc97c"
                    : apiKey;
                DataApiStatusTextBlock.Text = string.Empty;
            }
            catch { }
        }

        private void SaveDataApi_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Environment.SetEnvironmentVariable("DATA_API_ENDPOINT", DataApiEndpointTextBox.Text ?? string.Empty, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("DATA_API_KEY", DataApiKeyPasswordBox.Password ?? string.Empty, EnvironmentVariableTarget.User);

                DataApiStatusTextBlock.Text = "Saved! Restart SolidWorks.";
                DataApiStatusTextBlock.Foreground = new SolidColorBrush(Colors.DarkGreen);
                System.Windows.MessageBox.Show("Data API settings saved. Restart SolidWorks.", "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                DataApiStatusTextBlock.Text = "Save failed: " + ex.Message;
                DataApiStatusTextBlock.Foreground = new SolidColorBrush(Colors.Firebrick);
            }
        }

        private async void TestDataApi_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var endpoint = DataApiEndpointTextBox.Text?.Trim();
                var apiKey = DataApiKeyPasswordBox.Password;
                var service = new DataApiService(endpoint, apiKey);
                var ok = await service.TestConnectionAsync();
                if (ok)
                {
                    DataApiStatusTextBlock.Text = "Connection verified!";
                    DataApiStatusTextBlock.Foreground = new SolidColorBrush(Colors.DarkGreen);
                    DataApiErrorDetailsTextBox.Visibility = Visibility.Collapsed;
                    DataApiErrorDetailsTextBox.Text = string.Empty;
                }
                else
                {
                    DataApiStatusTextBlock.Text = "Test failed: see details below";
                    DataApiStatusTextBlock.Foreground = new SolidColorBrush(Colors.Firebrick);
                    DataApiErrorDetailsTextBox.Visibility = Visibility.Visible;
                    DataApiErrorDetailsTextBox.Text = service.LastError ?? "Unknown error";
                }
            }
            catch (Exception ex)
            {
                DataApiStatusTextBlock.Text = "Test exception: see details below";
                DataApiStatusTextBlock.Foreground = new SolidColorBrush(Colors.Firebrick);
                DataApiErrorDetailsTextBox.Visibility = Visibility.Visible;
                DataApiErrorDetailsTextBox.Text = ex.ToString();
            }
        }

        private async void GoogleSignIn_Click(object sender, RoutedEventArgs e)
        {
            GoogleSignInButton.IsEnabled = false;
            var prevContent = GoogleSignInButton.Content;
            GoogleSignInButton.Content = "Signing in...";

            try
            {
                var cfg = AICAD.Services.GoogleOAuthConfig.Load();
                if (cfg == null || string.IsNullOrWhiteSpace(cfg.ClientId))
                {
                    System.Windows.MessageBox.Show("Google OAuth client not configured. Set GOOGLE_OAUTH_CLIENT_ID or GOOGLE_OAUTH_CLIENT_FILE.", "Configuration Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string tokenJson = null;
                try
                {
                    tokenJson = await AICAD.Services.OAuthDesktopHelper.AuthorizeAsync(cfg.ClientId, cfg.Scopes?.ToArray(), cfg.ClientSecret).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => System.Windows.MessageBox.Show("OAuth failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error));
                    return;
                }

                if (!string.IsNullOrWhiteSpace(tokenJson))
                {
                    try
                    {
                        AICAD.Services.TokenManager.SaveTokenJson(tokenJson);

                        // Try parse id_token from token response to extract user info
                        var j = JObject.Parse(tokenJson);
                        var idToken = j.Value<string>("id_token");
                        if (!string.IsNullOrWhiteSpace(idToken))
                        {
                            var payload = DecodeJwtPayload(idToken);
                            if (payload != null)
                            {
                                var name = payload.Value<string>("name") ?? payload.Value<string>("preferred_username");
                                var email = payload.Value<string>("email");
                                Dispatcher.Invoke(() =>
                                {
                                    if (!string.IsNullOrWhiteSpace(name))
                                    {
                                        DisplayNameTextBox.Text = name;
                                        DisplayNameText.Text = name;
                                    }
                                    if (!string.IsNullOrWhiteSpace(email))
                                    {
                                        EmailTextBox.Text = email;
                                        EmailText.Text = email;
                                    }
                                    ShowSignedInState();
                                });

                                // Validate id_token via Google and create/update user in MongoDB
                                try
                                {
                                    var userDoc = await AICAD.Services.UserService.GetOrCreateFromIdTokenAsync(idToken).ConfigureAwait(false);
                                    if (userDoc != null)
                                    {
                                        Dispatcher.Invoke(() => ApiStatusTextBlock.Text = "Account created/updated in MongoDB.");
                                    }
                                    else
                                    {
                                        Dispatcher.Invoke(() => ApiStatusTextBlock.Text = "Account not saved: invalid token or MongoDB not configured.");
                                    }
                                }
                                catch { /* ignore failures here - non-fatal for UI */ }
                            }
                        }

                        Dispatcher.Invoke(() =>
                        {
                            System.Windows.MessageBox.Show("Signed in successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => System.Windows.MessageBox.Show("Failed to save token: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error));
                    }
                }
            }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    GoogleSignInButton.IsEnabled = true;
                    GoogleSignInButton.Content = prevContent;
                });
            }
        }

        private void GoogleSignOut_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Remove stored credential (cmdkey /delete)
                var target = "SolidWorksTextToCAD_OAuthToken";
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmdkey",
                        Arguments = $"/delete:{target}",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using (var p = Process.Start(psi)) { p.WaitForExit(3000); }
                }
                catch { }

                DisplayNameTextBox.Text = string.Empty;
                EmailTextBox.Text = string.Empty;
                DisplayNameText.Text = string.Empty;
                EmailText.Text = string.Empty;
                ShowSignedOutState();
                System.Windows.MessageBox.Show("Signed out successfully.", "Signed Out", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Sign-out failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static JObject DecodeJwtPayload(string jwt)
        {
            try
            {
                var parts = jwt.Split('.');
                if (parts.Length < 2) return null;
                var payload = parts[1];
                // base64url -> base64
                payload = payload.Replace('-', '+').Replace('_', '/');
                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "="; break;
                }
                var bytes = Convert.FromBase64String(payload);
                var json = Encoding.UTF8.GetString(bytes);
                return JObject.Parse(json);
            }
            catch
            {
                return null;
            }
        }

        private void LoadSamplesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var mode = (Environment.GetEnvironmentVariable("AICAD_SAMPLE_MODE", EnvironmentVariableTarget.User) ?? "").ToLowerInvariant();
                SampleModeZeroRadio.IsChecked = mode == "zero";
                SampleModeOneRadio.IsChecked = mode == "one";
                SampleModeFewRadio.IsChecked = string.IsNullOrWhiteSpace(mode) || mode == "few";

                SamplesFileTextBox.Text = Environment.GetEnvironmentVariable("AICAD_SAMPLES_DB_PATH", EnvironmentVariableTarget.User) ?? "";

                var rand = Environment.GetEnvironmentVariable("AICAD_SAMPLES_RANDOMIZE", EnvironmentVariableTarget.User) ?? "0";
                RandomizeSamplesCheckBox.IsChecked = rand == "1" || (rand != null && rand.Equals("true", StringComparison.OrdinalIgnoreCase));

                // Load few-shot and related flags (if controls present). If AICAD_USE_FEWSHOT isn't set, derive from radio selection.
                try
                {
                    var useFew = Environment.GetEnvironmentVariable("AICAD_USE_FEWSHOT", EnvironmentVariableTarget.User) ?? Environment.GetEnvironmentVariable("AICAD_USE_FEWSHOT");
                    if (string.IsNullOrEmpty(useFew))
                    {
                        // derive from radio: zero-shot -> false, otherwise true
                        ChkUseFewShot.IsChecked = !(SampleModeZeroRadio.IsChecked == true);
                    }
                    else
                    {
                        ChkUseFewShot.IsChecked = (useFew == "1" || (useFew != null && useFew.Equals("true", StringComparison.OrdinalIgnoreCase)));
                    }
                }
                catch { if (ChkUseFewShot != null) ChkUseFewShot.IsChecked = true; }

                try
                {
                    var fk = Environment.GetEnvironmentVariable("AICAD_FORCE_KEY_SHOTS", EnvironmentVariableTarget.User) ?? Environment.GetEnvironmentVariable("AICAD_FORCE_KEY_SHOTS");
                    ChkForceKeyShots.IsChecked = fk == "1" || (fk != null && fk.Equals("true", StringComparison.OrdinalIgnoreCase));
                }
                catch { if (ChkForceKeyShots != null) ChkForceKeyShots.IsChecked = false; }

                try
                {
                    var fs = Environment.GetEnvironmentVariable("AICAD_FORCE_STATIC_FEWSHOT", EnvironmentVariableTarget.User) ?? Environment.GetEnvironmentVariable("AICAD_FORCE_STATIC_FEWSHOT");
                    ChkForceStaticFewShot.IsChecked = fs == "1" || (fs != null && fs.Equals("true", StringComparison.OrdinalIgnoreCase));
                }
                catch { if (ChkForceStaticFewShot != null) ChkForceStaticFewShot.IsChecked = false; }
            }
            catch { }
        }

        // Update few-shot state immediately when sample mode radio buttons change
        private void SampleModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                // zero-shot -> disable few-shot; one/few -> enable few-shot
                bool useFew = !(SampleModeZeroRadio.IsChecked == true);
                if (ChkUseFewShot != null)
                {
                    ChkUseFewShot.IsChecked = useFew;
                }
                try { Environment.SetEnvironmentVariable("AICAD_USE_FEWSHOT", useFew ? "1" : "0", EnvironmentVariableTarget.User); } catch { }
            }
            catch { }
        }

        private void SaveAll_Click(object sender, RoutedEventArgs e)
        {
            SaveMongoButton_Click(sender, e);
            SaveApiKeysButton_Click(sender, e);
            SaveSamplesButton_Click(sender, e);
            System.Windows.MessageBox.Show("All settings applied. Restart SolidWorks.", "Applied", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
