using System;
using System.Net.Http;
using System.Threading.Tasks;
using MongoDB.Driver;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace AICAD.UI
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            LoadAllSettings();
            // wire validation
            MongoDbNameTextBox.TextChanged += MongoDbNameTextBox_TextChanged;
            UpdateSaveAllState();
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
            NameEasyPanel.Visibility = panelName == "NameEasyPanel" ? Visibility.Visible : Visibility.Collapsed;
            SamplesPanel.Visibility = panelName == "SamplesPanel" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadAllSettings()
        {
            try
            {
                LoadMongoButton_Click(null, null);
                LoadApiButton_Click(null, null);
                try { LoadSamplesButton_Click(null, null); } catch { }
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
                MongoStatusTextBlock.Text = "Loaded from environment variables";
                MongoStatusTextBlock.Foreground = new SolidColorBrush(Colors.DarkGreen);
                MongoLoadedIcon.Visibility = Visibility.Visible;
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
                Environment.SetEnvironmentVariable("MONGODB_URI", GetMongoConnectionString() ?? "", EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("MONGODB_DB", MongoDbNameTextBox.Text ?? "", EnvironmentVariableTarget.User);

                MongoStatusTextBlock.Text = "Saved! Restart SolidWorks.";
                MongoStatusTextBlock.Foreground = new SolidColorBrush(Colors.DarkGreen);

                System.Windows.MessageBox.Show("DB settings saved. Restart SolidWorks.", "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MongoStatusTextBlock.Text = "Failed to save: " + ex.Message;
                MongoStatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            }
        }

        private string GetMongoConnectionString()
        {
            // If masked (passwordbox used) prefer the hidden box value
            if (MongoConnMaskToggle != null && MongoConnMaskToggle.IsChecked == true)
            {
                // When masked we stored value in Tag to avoid PasswordBox usage; simply return TextBox value
                return MongoConnectionStringTextBox.Text;
            }
            return MongoConnectionStringTextBox.Text;
        }

        private async void TestMongoButton_Click(object sender, RoutedEventArgs e)
        {
            TestMongoButton.IsEnabled = false;
            MongoStatusTextBlock.Text = "Testing...";
            MongoStatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);

            var conn = GetMongoConnectionString();
            if (string.IsNullOrWhiteSpace(conn))
            {
                MongoStatusTextBlock.Text = "Connection string is empty.";
                MongoStatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                TestMongoButton.IsEnabled = true;
                return;
            }

            int timeoutSeconds = 5;
            int.TryParse(TimeoutTextBox.Text ?? "5", out timeoutSeconds);

            try
            {
                var settings = MongoClientSettings.FromConnectionString(conn);
                settings.ConnectTimeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds));
                settings.ServerSelectionTimeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds));
                var client = new MongoClient(settings);
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds))))
                {
                    // Try an operation to force server selection
                    await client.ListDatabaseNamesAsync(cts.Token).ConfigureAwait(false);
                }

                Dispatcher.Invoke(() =>
                {
                    MongoStatusTextBlock.Text = "✔ Connection OK";
                    MongoStatusTextBlock.Foreground = new SolidColorBrush(Colors.DarkGreen);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    MongoStatusTextBlock.Text = "❌ " + ex.Message;
                    MongoStatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                });
            }
            finally
            {
                Dispatcher.Invoke(() => TestMongoButton.IsEnabled = true);
            }
        }

        private void MongoConnMaskToggle_Click(object sender, RoutedEventArgs e)
        {
            // Simple toggle: for now we do not maintain a separate PasswordBox; we just toggle visual masking by switching FontFamily
            if (MongoConnMaskToggle.IsChecked == true)
            {
                // Mask: use PasswordChar-like font by replacing characters with bullets in display but keep value in Text
                MongoConnectionStringTextBox.FontFamily = new FontFamily("Global User Interface");
                // no robust masking; leave text but change foreground to gray slightly to indicate masked
                MongoConnectionStringTextBox.Foreground = new SolidColorBrush(Colors.Gray);
            }
            else
            {
                MongoConnectionStringTextBox.FontFamily = new FontFamily("Segoe UI");
                MongoConnectionStringTextBox.Foreground = new SolidColorBrush(Colors.Black);
            }
        }

        private void TimeoutDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TimeoutTextBox.Text, out var v)) { v = Math.Max(1, v - 1); TimeoutTextBox.Text = v.ToString(); }
        }

        private void TimeoutUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TimeoutTextBox.Text, out var v)) { v = Math.Min(120, v + 1); TimeoutTextBox.Text = v.ToString(); } else TimeoutTextBox.Text = "5";
        }

        private void MongoDbNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSaveAllState();
        }

        private void UpdateSaveAllState()
        {
            var empty = string.IsNullOrWhiteSpace(MongoDbNameTextBox.Text);
            btnSaveAll.IsEnabled = !empty;
            if (empty)
            {
                MongoDbNameTextBox.BorderBrush = new SolidColorBrush(Colors.Red);
            }
            else
            {
                MongoDbNameTextBox.ClearValue(TextBox.BorderBrushProperty);
            }
        }

        private void LoadApiButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LocalLlmEndpointTextBox.Text = Environment.GetEnvironmentVariable("LOCAL_LLM_ENDPOINT", EnvironmentVariableTarget.User) ?? "http://127.0.0.1:1234";
                GeminiApiKeyTextBox.Text = Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.User) ?? "";
                GroqApiKeyTextBox.Text = Environment.GetEnvironmentVariable("GROQ_API_KEY", EnvironmentVariableTarget.User) ?? "";
                LocalLlmModelTextBox.Text = Environment.GetEnvironmentVariable("LOCAL_LLM_MODEL", EnvironmentVariableTarget.User) ?? "";
                // local system prompt
                // Note: control names differ from original; ignore if absent

                var mode = Environment.GetEnvironmentVariable("AICAD_LLM_MODE", EnvironmentVariableTarget.User) ?? "";
                ApiStatusTextBlock.Text = "Loaded from environment variables";
                ApiStatusTextBlock.Foreground = new SolidColorBrush(Colors.DarkGreen);
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
                Environment.SetEnvironmentVariable("GEMINI_API_KEY", GeminiApiKeyTextBox.Text ?? "", EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("GROQ_API_KEY", GroqApiKeyTextBox.Text ?? "", EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("LOCAL_LLM_MODEL", LocalLlmModelTextBox.Text ?? "", EnvironmentVariableTarget.User);

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
            // Disable button immediately on UI thread
            TestApiButton.IsEnabled = false;
            UpdateApiStatus("Testing API...", Colors.Blue);

            try
            {
                // Determine if user selected local mode by presence of local endpoint
                var endpoint = LocalLlmEndpointTextBox.Text?.Trim() ?? string.Empty;
                bool isLocal = !string.IsNullOrWhiteSpace(endpoint);

                if (!isLocal)
                {
                    bool anyTested = false;

                    if (!string.IsNullOrWhiteSpace(GeminiApiKeyTextBox.Text))
                    {
                        anyTested = true;
                        try
                        {
                            var client = new AICAD.Services.GeminiClient(GeminiApiKeyTextBox.Text.Trim());
                            var res = await client.TestApiKeyAsync(null).ConfigureAwait(false);
                            // Marshal update to UI thread
                            Dispatcher.Invoke(() =>
                            {
                                if (res != null && res.Success)
                                {
                                    UpdateApiStatus($"Gemini OK: Found {res.ModelsFound} models.", Colors.DarkGreen);
                                }
                                else
                                {
                                    UpdateApiStatus($"Gemini Fail: {res?.Message ?? "Unknown error"}", Colors.Red);
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() => UpdateApiStatus("Gemini Error: " + ex.Message, Colors.Red));
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(GroqApiKeyTextBox.Text))
                    {
                        anyTested = true;
                        try
                        {
                            using (var http = new HttpClient())
                            {
                                http.Timeout = TimeSpan.FromSeconds(10);
                                http.DefaultRequestHeaders.Add("Authorization", "Bearer " + GroqApiKeyTextBox.Text.Trim());
                                var resp = await http.GetAsync("https://api.groq.com/v1/models").ConfigureAwait(false);
                                Dispatcher.Invoke(() =>
                                {
                                    if (resp.IsSuccessStatusCode)
                                    {
                                        // Append if previous success
                                        if (ApiStatusTextBlock.Foreground is SolidColorBrush sc && sc.Color == Colors.DarkGreen && !string.IsNullOrWhiteSpace(ApiStatusTextBlock.Text))
                                        {
                                            ApiStatusTextBlock.Text += " | Groq OK.";
                                        }
                                        else
                                        {
                                            UpdateApiStatus("Groq OK.", Colors.DarkGreen);
                                        }
                                    }
                                    else
                                    {
                                        UpdateApiStatus($"Groq Fail: {resp.StatusCode}", Colors.Red);
                                    }
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() => UpdateApiStatus("Groq Error: " + ex.Message, Colors.Red));
                        }
                    }

                    if (!anyTested)
                    {
                        UpdateApiStatus("No Cloud API Keys entered.", Colors.Orange);
                    }
                }
                else
                {
                    // Test local endpoint: ping /v1/models
                    try
                    {
                        using (var http = new HttpClient())
                        {
                            http.Timeout = TimeSpan.FromSeconds(5);
                            var testUrl = endpoint.TrimEnd('/') + "/v1/models";
                            var resp = await http.GetAsync(testUrl).ConfigureAwait(false);
                            Dispatcher.Invoke(() =>
                            {
                                if (resp.IsSuccessStatusCode)
                                {
                                    UpdateApiStatus("Local OK (Server reachable)", Colors.DarkGreen);
                                }
                                else
                                {
                                    UpdateApiStatus($"Local Fail: {resp.StatusCode}", Colors.Red);
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => UpdateApiStatus("Local Error: " + ex.Message, Colors.Red));
                    }
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => UpdateApiStatus("Test Error: " + ex.Message, Colors.Red));
            }
            finally
            {
                Dispatcher.Invoke(() => TestApiButton.IsEnabled = true);
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
                if (AICAD.Services.SettingsManager.SetDatabasePath(path))
                    System.Windows.MessageBox.Show("Path saved. Restart SolidWorks.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    System.Windows.MessageBox.Show("Failed to save path.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                var mode = "few"; // default
                // No direct radio buttons wired; store few by default or existing env var
                Environment.SetEnvironmentVariable("AICAD_SAMPLE_MODE", mode, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("AICAD_SAMPLES_DB_PATH", SamplesFileTextBox.Text ?? "", EnvironmentVariableTarget.User);

                System.Windows.MessageBox.Show("Samples settings saved to environment variables. Restart SolidWorks for changes to take effect.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Failed to save samples settings: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadSamplesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var mode = Environment.GetEnvironmentVariable("AICAD_SAMPLE_MODE", EnvironmentVariableTarget.User) ?? "";
                switch (mode.ToLowerInvariant())
                {
                    case "zero": break;
                    case "one": break;
                    case "few": break;
                    default: break;
                }

                SamplesFileTextBox.Text = Environment.GetEnvironmentVariable("AICAD_SAMPLES_DB_PATH", EnvironmentVariableTarget.User) ?? "";
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
