using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Windows;

namespace AICAD.UI
{
    public partial class BlankWebView2Window : Window
    {
        public BlankWebView2Window()
        {
            InitializeComponent();
            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                // Use LocalAppData so WebView2 can create its user data folder without needing admin rights
                var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AICAD", "WebView2");
                Directory.CreateDirectory(userDataFolder);

                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await webView.EnsureCoreWebView2Async(env);

                // Optional: configure default settings
                try { webView.CoreWebView2.Settings.AreDevToolsEnabled = true; } catch { }

                webView.Source = new Uri("about:blank");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"WebView2 initialization failed: {ex.Message}", "WebView2 Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
