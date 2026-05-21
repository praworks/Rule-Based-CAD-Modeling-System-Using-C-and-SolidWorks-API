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
        private const string DefaultMongoHost = "localhost";
        private const int DefaultMongoPort = 27017;
        private const string DefaultMongoDatabase = "TaskPaneAddin";
        private const string PreferredSwUnitsKey = "PreferredSwUnitSystem";
        private const string PostBuildViewModeKey = AICAD.Services.PostBuildViewService.PostBuildViewModeKey;
        private const string AdminEmail = "e2240156@bit.uom.lk";

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
            var order = AICAD.Services.LlmPriorityManager.GetPriority();

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
            if (id == "local") _providers.Add(new ProviderItem { Id = "local", DisplayName = "LM Studio (Local)" });
            else if (id == "gemini") _providers.Add(new ProviderItem { Id = "gemini", DisplayName = "Google Gemini" });
            else if (id == "groq") _providers.Add(new ProviderItem { Id = "groq", DisplayName = "Groq" });
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
                if (IsAdminOnlyPanel(tag) && !IsCurrentUserAdmin())
                {
                    System.Windows.MessageBox.Show("Only the admin account can edit AI Provider and Database settings.", "Admin Only", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                contentTitle.Text = btn.Content?.ToString() ?? "";
                HighlightSelectedNav(btn);
                ShowPanel(tag);
            }
        }

        private void HighlightSelectedNav(System.Windows.Controls.Button selectedButton)
        {
            // Reset all nav buttons to default
            var navButtons = new[] { btnGeneral, btnApiKeys, btnMongo, btnSamples, btnNameEasy, btnAccount };
            foreach (var btn in navButtons)
            {
                if (btn != null)
                {
                    btn.FontWeight = FontWeights.SemiBold;
                    btn.Background = Brushes.Transparent;
                }
            }

            // Highlight selected
            if (selectedButton != null)
            {
                selectedButton.FontWeight = FontWeights.Bold;
                selectedButton.Background = (System.Windows.Media.Brush)FindResource("NavHoverBrush");
            }
        }

        private void ShowPanel(string panelName)
        {
            if (IsAdminOnlyPanel(panelName) && !IsCurrentUserAdmin())
            {
                panelName = "GeneralPanel";
                contentTitle.Text = "General";
                HighlightSelectedNav(btnGeneral);
            }

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

        private static bool IsAdminOnlyPanel(string panelName)
        {
            return string.Equals(panelName, "MongoPanel", StringComparison.OrdinalIgnoreCase)
                || string.Equals(panelName, "ApiKeysPanel", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsCurrentUserAdmin()
        {
            var email = GetCurrentAccountEmail();
            return string.Equals(email, AdminEmail, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsSignedIn()
        {
            return !string.IsNullOrWhiteSpace(GetCurrentAccountEmail());
        }

        private string GetCurrentAccountRole()
        {
            if (!IsSignedIn()) return "Guest";
            return IsCurrentUserAdmin() ? "Admin" : "User";
        }

        private string GetCurrentAccountEmail()
        {
            try
            {
                var email = EmailTextBox?.Text;
                if (!string.IsNullOrWhiteSpace(email)) return email.Trim();
                email = EmailText?.Text;
                return string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private void ApplyAdminAccess()
        {
            try
            {
                var isAdmin = IsCurrentUserAdmin();
                UpdateAccountRoleDisplay();
                if (btnMongo != null)
                {
                    btnMongo.IsEnabled = isAdmin;
                    btnMongo.ToolTip = isAdmin ? "MongoDB connection settings" : "Admin only: e2240156@bit.uom.lk";
                    btnMongo.Opacity = isAdmin ? 1.0 : 0.45;
                }

                if (btnApiKeys != null)
                {
                    btnApiKeys.IsEnabled = isAdmin;
                    btnApiKeys.ToolTip = isAdmin ? "Configure AI language model settings" : "Admin only: e2240156@bit.uom.lk";
                    btnApiKeys.Opacity = isAdmin ? 1.0 : 0.45;
                }

                if (!isAdmin && (MongoPanel.Visibility == Visibility.Visible || ApiKeysPanel.Visibility == Visibility.Visible))
                {
                    ShowPanel("GeneralPanel");
                }
            }
            catch { }
        }

        private void UpdateAccountRoleDisplay()
        {
            try
            {
                var roleText = "Role: " + GetCurrentAccountRole();
                if (AccountRoleText != null)
                {
                    AccountRoleText.Text = roleText;
                    AccountRoleText.Foreground = IsCurrentUserAdmin()
                        ? new SolidColorBrush(Colors.DarkGreen)
                        : (System.Windows.Media.Brush)FindResource("TextMutedBrush");
                }
            }
            catch { }
        }

        private void LoadAllSettings()
        {
            try
            {
                // Highlight General tab by default
                HighlightSelectedNav(btnGeneral);
                
                TryUseSecretsClientFile();
                LoadMongoButton_Click(null, null);
                LoadApiButton_Click(null, null);
                try { LoadSamplesButton_Click(null, null); } catch { }
                try { LoadNameEasySettings(); } catch { }
                try { LoadAccountInfo(); } catch { }
                try
                {
                    var units = AICAD.Services.SettingsManager.GetString(PreferredSwUnitsKey, "MMGS");
                    SwUnitsMmgsRadio.IsChecked = !string.Equals(units, "IPS", StringComparison.OrdinalIgnoreCase);
                    SwUnitsIpsRadio.IsChecked = string.Equals(units, "IPS", StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    if (SwUnitsMmgsRadio != null) SwUnitsMmgsRadio.IsChecked = true;
                }

                try
                {
                    SelectPostBuildViewMode(AICAD.Services.SettingsManager.GetString(PostBuildViewModeKey, AICAD.Services.PostBuildViewService.IsometricMode));
                }
                catch
                {
                    SelectPostBuildViewMode(AICAD.Services.PostBuildViewService.IsometricMode);
                }
            }
            catch { }
            finally
            {
                ApplyAdminAccess();
            }
        }

        private void SelectPostBuildViewMode(string mode)
        {
            var normalized = AICAD.Services.PostBuildViewService.NormalizeMode(mode);
            foreach (var item in PostBuildViewComboBox.Items)
            {
                if (item is ComboBoxItem comboItem
                    && string.Equals(comboItem.Tag?.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    PostBuildViewComboBox.SelectedItem = comboItem;
                    return;
                }
            }

            PostBuildViewComboBox.SelectedIndex = 1;
        }

        private string GetSelectedPostBuildViewMode()
        {
            if (PostBuildViewComboBox.SelectedItem is ComboBoxItem comboItem)
                return AICAD.Services.PostBuildViewService.NormalizeMode(comboItem.Tag?.ToString());

            return AICAD.Services.PostBuildViewService.IsometricMode;
        }

        // Load existing Google account info from stored tokens
        private void LoadAccountInfo()
        {
            try
            {
                var tokenJson = AICAD.Services.TokenManager.LoadStoredTokenJson();
                System.Diagnostics.Debug.WriteLine($"LoadAccountInfo: tokenJson is {(string.IsNullOrWhiteSpace(tokenJson) ? "null/empty" : $"{tokenJson.Length} chars")}");
                
                if (string.IsNullOrWhiteSpace(tokenJson))
                {
                    ShowSignedOutState();
                    return;
                }

                var j = JObject.Parse(tokenJson);
                var idToken = j.Value<string>("id_token");
                System.Diagnostics.Debug.WriteLine($"LoadAccountInfo: idToken is {(string.IsNullOrWhiteSpace(idToken) ? "null/empty" : "present")}");

                JObject payload = null;
                if (!string.IsNullOrWhiteSpace(idToken))
                {
                    payload = DecodeJwtPayload(idToken);
                    if (payload == null)
                    {
                        System.Diagnostics.Debug.WriteLine("LoadAccountInfo: payload is null");
                    }
                }

                var name = payload?.Value<string>("name")
                           ?? payload?.Value<string>("preferred_username")
                           ?? j.Value<string>("profile_name");
                var email = payload?.Value<string>("email")
                            ?? j.Value<string>("profile_email");
                System.Diagnostics.Debug.WriteLine($"LoadAccountInfo: name={name}, email={email}");

                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(email))
                {
                    ShowSignedOutState();
                    return;
                }

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
            UpdateAccountRoleDisplay();
            ApplyAdminAccess();
        }

        private void ShowSignedOutState()
        {
            try
            {
                DisplayNameTextBox.Text = string.Empty;
                EmailTextBox.Text = string.Empty;
                DisplayNameText.Text = string.Empty;
                EmailText.Text = string.Empty;
            }
            catch { }
            SignedInCard.Visibility = Visibility.Collapsed;
            SignedOutCard.Visibility = Visibility.Visible;
            UpdateAccountRoleDisplay();
            ApplyAdminAccess();
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
                    foreach (var candidateDir in new[]
                    {
                        System.IO.Path.Combine(dir.FullName, "Secrets"),
                        System.IO.Path.Combine(dir.FullName, "Login", "OAuth"),
                        System.IO.Path.Combine(dir.FullName, "Services", "Login", "OAuth")
                    })
                    {
                        if (!System.IO.Directory.Exists(candidateDir)) continue;

                        try
                        {
                            var matches = System.IO.Directory.GetFiles(candidateDir, "client_secret*.json", System.IO.SearchOption.TopDirectoryOnly);
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
                var connectionString = GetSavedMongoConnectionString();
                PopulateMongoFields(connectionString);
                MongoConnectionStringTextBox.Text = connectionString;

                ApiStatusTextBlock.Text = "";
                
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    UpdateDatabaseStatus("local", "Not Connected", null);
                    MongoLoadedInfoIcon.Visibility = Visibility.Collapsed;
                }
                else
                {
                    UpdateDatabaseStatus("local", "Ready to test", null);
                    MongoLoadedInfoIcon.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                UpdateDatabaseStatus("local", "Failed to load: " + ex.Message, false);
            }
        }

        private void SaveMongoButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsCurrentUserAdmin())
            {
                UpdateDatabaseStatus("local", "Admin only", false);
                return;
            }

            try
            {
                var connectionString = BuildMongoConnectionString(validate: true, out var validationMessage);
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    UpdateDatabaseStatus("local", validationMessage, false);
                    return;
                }

                MongoConnectionStringTextBox.Text = connectionString;

                Environment.SetEnvironmentVariable("MONGODB_URI", connectionString, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("MONGO_LOG_CONN", connectionString, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("MONGODB_DB", MongoDbNameTextBox.Text?.Trim() ?? DefaultMongoDatabase, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("MONGODB_USER", IsMongoAuthenticationEnabled() ? DbAuthUsernameTextBox.Text?.Trim() ?? string.Empty : string.Empty, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("MONGODB_PW", IsMongoAuthenticationEnabled() ? DbAuthPasswordBox.Password ?? string.Empty : string.Empty, EnvironmentVariableTarget.User);

                UpdateDatabaseStatus("local", "Saved! Restart SolidWorks.", true);
                MongoLoadedInfoIcon.Visibility = Visibility.Collapsed;

                System.Windows.MessageBox.Show("DB settings saved. Restart SolidWorks.", "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UpdateDatabaseStatus("local", "Failed to save: " + ex.Message, false);
            }
        }

        private async void TestMongoButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsCurrentUserAdmin())
            {
                UpdateDatabaseStatus("local", "Admin only", false);
                return;
            }

            // Disable button to prevent re-entry
            TestMongoButton.IsEnabled = false;
            UpdateDatabaseStatus("local", "Testing connection...", null);

            try
            {
                var conn = BuildMongoConnectionString(validate: true, out var validationMessage);

                if (string.IsNullOrWhiteSpace(conn))
                {
                    UpdateDatabaseStatus("local", validationMessage, false);
                    return;
                }

                MongoConnectionStringTextBox.Text = conn;

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
                        UpdateDatabaseStatus("local", "Connection OK", true);
                        MongoLoadedInfoIcon.Visibility = Visibility.Collapsed;
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        UpdateDatabaseStatus("local", "Test failed: " + ex.Message, false);
                    });
                }
            }
            finally
            {
                Dispatcher.Invoke(() => TestMongoButton.IsEnabled = true);
            }
        }

        private string GetSavedMongoConnectionString()
        {
            return Environment.GetEnvironmentVariable("MONGODB_URI", EnvironmentVariableTarget.User)
                ?? Environment.GetEnvironmentVariable("MONGODB_URI")
                ?? Environment.GetEnvironmentVariable("MONGO_LOG_CONN", EnvironmentVariableTarget.User)
                ?? Environment.GetEnvironmentVariable("MONGO_LOG_CONN")
                ?? string.Empty;
        }

        private void PopulateMongoFields(string connectionString)
        {
            DbHostTextBox.Text = DefaultMongoHost;
            DbPortTextBox.Text = DefaultMongoPort.ToString();
            MongoDbNameTextBox.Text = Environment.GetEnvironmentVariable("MONGODB_DB", EnvironmentVariableTarget.User)
                ?? Environment.GetEnvironmentVariable("MONGODB_DB")
                ?? DefaultMongoDatabase;
            DbAuthUsernameTextBox.Text = Environment.GetEnvironmentVariable("MONGODB_USER", EnvironmentVariableTarget.User)
                ?? Environment.GetEnvironmentVariable("MONGODB_USER")
                ?? string.Empty;
            DbAuthPasswordBox.Password = Environment.GetEnvironmentVariable("MONGODB_PW", EnvironmentVariableTarget.User)
                ?? Environment.GetEnvironmentVariable("MONGODB_PW")
                ?? string.Empty;
            SetMongoAuthenticationMode("None");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return;
            }

            try
            {
                var mongoUrl = MongoUrl.Create(connectionString);
                if (mongoUrl.Server != null)
                {
                    DbHostTextBox.Text = string.IsNullOrWhiteSpace(mongoUrl.Server.Host) ? DefaultMongoHost : mongoUrl.Server.Host;
                    DbPortTextBox.Text = mongoUrl.Server.Port.ToString();
                }

                if (!string.IsNullOrWhiteSpace(mongoUrl.DatabaseName))
                {
                    MongoDbNameTextBox.Text = mongoUrl.DatabaseName;
                }

                if (!string.IsNullOrWhiteSpace(mongoUrl.Username))
                {
                    DbAuthUsernameTextBox.Text = mongoUrl.Username;
                    DbAuthPasswordBox.Password = mongoUrl.Password ?? string.Empty;
                    SetMongoAuthenticationMode("UserPass");
                }
            }
            catch
            {
                // Leave the defaults/user overrides in place when an existing URI cannot be expanded into basic fields.
            }
        }

        private void SetMongoAuthenticationMode(string modeTag)
        {
            if (DbAuthComboBox == null)
            {
                return;
            }

            foreach (var item in DbAuthComboBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), modeTag, StringComparison.OrdinalIgnoreCase))
                {
                    DbAuthComboBox.SelectedItem = item;
                    return;
                }
            }

            DbAuthComboBox.SelectedIndex = 0;
        }

        private bool IsMongoAuthenticationEnabled()
        {
            return string.Equals(DbAuthComboBox?.SelectedValue?.ToString(), "UserPass", StringComparison.OrdinalIgnoreCase);
        }

        private string BuildMongoConnectionString(bool validate, out string validationMessage)
        {
            validationMessage = string.Empty;

            var rawConnectionString = MongoConnectionStringTextBox.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(rawConnectionString))
            {
                if (rawConnectionString.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase) ||
                    rawConnectionString.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase))
                {
                    return rawConnectionString;
                }

                validationMessage = "Enter a valid MongoDB URI starting with mongodb:// or mongodb+srv://, or clear it to use host and port.";
                return null;
            }

            var host = DbHostTextBox.Text?.Trim() ?? string.Empty;
            var portText = DbPortTextBox.Text?.Trim() ?? string.Empty;
            var databaseName = MongoDbNameTextBox.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(host))
            {
                if (validate)
                {
                    validationMessage = "Enter a MongoDB server address.";
                    return null;
                }

                host = DefaultMongoHost;
            }

            if (!int.TryParse(portText, out var port) || port <= 0 || port > 65535)
            {
                if (validate)
                {
                    validationMessage = "Enter a valid MongoDB port number.";
                    return null;
                }

                port = DefaultMongoPort;
            }

            if (string.IsNullOrWhiteSpace(databaseName))
            {
                databaseName = DefaultMongoDatabase;
            }

            var builder = new MongoUrlBuilder
            {
                Server = new MongoServerAddress(host, port),
                DatabaseName = databaseName
            };

            if (IsMongoAuthenticationEnabled())
            {
                var username = DbAuthUsernameTextBox.Text?.Trim() ?? string.Empty;
                var password = DbAuthPasswordBox.Password ?? string.Empty;

                if (string.IsNullOrWhiteSpace(username))
                {
                    validationMessage = "Enter a MongoDB username or switch authentication to None.";
                    return null;
                }

                builder.Username = username;
                builder.Password = password;
            }

            return builder.ToMongoUrl().ToString();
        }

        private void LoadApiButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LocalLlmEndpointTextBox.Text = Environment.GetEnvironmentVariable("LOCAL_LLM_ENDPOINT", EnvironmentVariableTarget.User) ?? "http://127.0.0.1:1234";
                // Gemini key is stored in a PasswordBox in the UI
                try { GeminiApiKeyPasswordBox.Password = Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.User) ?? ""; } catch { }
                try { GroqApiKeyPasswordBox.Password = Environment.GetEnvironmentVariable("GROQ_API_KEY", EnvironmentVariableTarget.User) ?? ""; } catch { }
                try { Environment.SetEnvironmentVariable("PROMPT_REFINE_PROVIDER", "disabled", EnvironmentVariableTarget.User); } catch { }

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
            if (!IsCurrentUserAdmin())
            {
                ApiStatusTextBlock.Text = "Admin only";
                ApiStatusTextBlock.Foreground = new SolidColorBrush(Colors.DarkOrange);
                return;
            }

            try
            {
                Environment.SetEnvironmentVariable("LOCAL_LLM_ENDPOINT", LocalLlmEndpointTextBox.Text ?? "", EnvironmentVariableTarget.User);
                try { Environment.SetEnvironmentVariable("GEMINI_API_KEY", GeminiApiKeyPasswordBox.Password ?? "", EnvironmentVariableTarget.User); } catch { }
                try { Environment.SetEnvironmentVariable("GROQ_API_KEY", GroqApiKeyPasswordBox.Password ?? "", EnvironmentVariableTarget.User); } catch { }
                
                // Save Priority
                var priority = string.Join(",", _providers.Select(p => p.Id));
                // Persist provider priority globally when possible; fall back to user-level if not permitted.
                AICAD.Services.LlmPriorityManager.SetPriority(priority);

                try { Environment.SetEnvironmentVariable("PROMPT_REFINE_PROVIDER", "disabled", EnvironmentVariableTarget.User); } catch { }

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
            if (!IsCurrentUserAdmin())
            {
                ApiStatusTextBlock.Text = "Admin only";
                ApiStatusTextBlock.Foreground = new SolidColorBrush(Colors.DarkOrange);
                return;
            }

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
            // Capture UI values on UI thread before going async
            string endpoint = null;
            await Dispatcher.InvokeAsync(() =>
            {
                UpdateProviderStatus("Local", "Testing...", null);
                endpoint = LocalLlmEndpointTextBox.Text?.Trim() ?? string.Empty;
            });

            if (string.IsNullOrWhiteSpace(endpoint))
            {
                await Dispatcher.InvokeAsync(() => UpdateProviderStatus("Local", "No endpoint", false));
                return;
            }

            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(5);
                    var testUrl = endpoint.TrimEnd('/') + "/v1/models";
                    var resp = await http.GetAsync(testUrl).ConfigureAwait(false);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (resp.IsSuccessStatusCode)
                            UpdateProviderStatus("Local", "Connected", true);
                        else
                            UpdateProviderStatus("Local", $"Error: {resp.StatusCode}", false);
                    });
                }
            }
            catch (Exception)
            {
                await Dispatcher.InvokeAsync(() => UpdateProviderStatus("Local", "Offline", false));
            }
        }

        private async Task TestGeminiAsync()
        {
            // Capture UI values on UI thread before going async
            string key = null;
            await Dispatcher.InvokeAsync(() =>
            {
                UpdateProviderStatus("Gemini", "Testing...", null);
                key = GeminiApiKeyPasswordBox.Password?.Trim() ?? string.Empty;
            });

            if (string.IsNullOrWhiteSpace(key))
            {
                await Dispatcher.InvokeAsync(() => UpdateProviderStatus("Gemini", "No API Key", false));
                return;
            }

            try
            {
                var client = new AICAD.Services.GeminiClient(key);
                var res = await client.TestApiKeyAsync(null).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (res != null && res.Success)
                        UpdateProviderStatus("Gemini", "Connected", true);
                    else
                        UpdateProviderStatus("Gemini", res?.Message ?? "Failed", false);
                });
            }
            catch (Exception)
            {
                await Dispatcher.InvokeAsync(() => UpdateProviderStatus("Gemini", "Error", false));
            }
        }

        private async Task TestGroqAsync()
        {
            // Capture UI values on UI thread before going async
            string key = null;
            await Dispatcher.InvokeAsync(() =>
            {
                UpdateProviderStatus("Groq", "Testing...", null);
                UpdateGroqUsageStats(); // Update rate limit stats
                key = GroqApiKeyPasswordBox.Password?.Trim() ?? string.Empty;
            });

            if (string.IsNullOrWhiteSpace(key))
            {
                await Dispatcher.InvokeAsync(() => UpdateProviderStatus("Groq", "No API Key", false));
                return;
            }

            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(10);
                    http.DefaultRequestHeaders.Add("Authorization", "Bearer " + key);
                    var resp = await http.GetAsync("https://api.groq.com/openai/v1/models").ConfigureAwait(false);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (resp.IsSuccessStatusCode)
                        {
                            UpdateProviderStatus("Groq", "Connected", true);
                            UpdateGroqUsageStats(); // Refresh after test
                        }
                        else
                            UpdateProviderStatus("Groq", $"Error: {resp.StatusCode}", false);
                    });
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() => UpdateProviderStatus("Groq", $"Error: {ex.Message}", false));
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

        private void UpdateDatabaseStatus(string dbType, string text, bool? success)
        {
            Dispatcher.Invoke(() =>
            {
                if (dbType != "local")
                {
                    return;
                }

                var circle = MongoStatusCircle;
                var txt = MongoStatusText;

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
                try
                {
                    await TestLocalAsync();
                    await TestGeminiAsync();
                    await TestGroqAsync();
                    await Dispatcher.InvokeAsync(() => UpdateGroqUsageStats()); // Update stats after all tests
                }
                catch (Exception ex)
                {
                    try { AddinStatusLogger.Error("SettingsWindow", "CheckAllLlmStatuses failed", ex); } catch { }
                }
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
                        btn.Content = "Show";
                    }
                    else
                    {
                        // Show: copy from PasswordBox to TextBox and show TextBox
                        GeminiApiKeyTextBox.Text = GeminiApiKeyPasswordBox.Password;
                        GeminiApiKeyPasswordBox.Visibility = Visibility.Collapsed;
                        GeminiApiKeyTextBox.Visibility = Visibility.Visible;
                        btn.Content = "Hide";
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
                        btn.Content = "Show";
                    }
                    else
                    {
                        // Show: copy from PasswordBox to TextBox and show TextBox
                        GroqApiKeyTextBox.Text = GroqApiKeyPasswordBox.Password;
                        GroqApiKeyPasswordBox.Visibility = Visibility.Collapsed;
                        GroqApiKeyTextBox.Visibility = Visibility.Visible;
                        btn.Content = "Hide";
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
                            reg.SetValue("AutoUpdateDescription", "1");
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
                        var desc = key.GetValue("AutoUpdateDescription")?.ToString() ?? "1";
                        ChkAutoUpdateDescription.IsChecked = string.IsNullOrWhiteSpace(desc) || desc == "1" || desc.Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        ChkAutoUpdateDescription.IsChecked = true;
                    }
                }
            }
            catch { }
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

                // Reset removed advanced options to defaults so hidden stale values do not keep affecting runtime.
                try { Environment.SetEnvironmentVariable("AICAD_SAMPLES_RANDOMIZE", "0", EnvironmentVariableTarget.User); } catch { }
                try { Environment.SetEnvironmentVariable("AICAD_FORCE_KEY_SHOTS", "0", EnvironmentVariableTarget.User); } catch { }
                try { Environment.SetEnvironmentVariable("AICAD_FORCE_STATIC_FEWSHOT", "0", EnvironmentVariableTarget.User); } catch { }
                try { Environment.SetEnvironmentVariable("AICAD_SMART_EXAMPLE_SELECTION", null, EnvironmentVariableTarget.User); } catch { }

                System.Windows.MessageBox.Show("Samples settings saved to environment variables. Restart SolidWorks for changes to take effect.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Failed to save samples settings: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                AICAD.Services.TokenManager.ClearToken();

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
                try { Environment.SetEnvironmentVariable("AICAD_USE_FEWSHOT", useFew ? "1" : "0", EnvironmentVariableTarget.User); } catch { }
            }
            catch { }
        }

        private void SaveAll_Click(object sender, RoutedEventArgs e)
        {
            if (IsCurrentUserAdmin())
            {
                SaveMongoButton_Click(sender, e);
                SaveApiKeysButton_Click(sender, e);
            }
            SaveSamplesButton_Click(sender, e);
            try
            {
                var preferredUnits = (SwUnitsIpsRadio.IsChecked == true) ? "IPS" : "MMGS";
                AICAD.Services.SettingsManager.SetString(PreferredSwUnitsKey, preferredUnits);
                AICAD.Services.SettingsManager.SetString(PostBuildViewModeKey, GetSelectedPostBuildViewMode());
            }
            catch { }
            System.Windows.MessageBox.Show("All settings applied. Restart SolidWorks.", "Applied", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
