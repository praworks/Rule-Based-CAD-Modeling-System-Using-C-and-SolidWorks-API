using Microsoft.Web.WebView2.Wpf;
using System.Windows;

namespace AICAD.UI
{
    public partial class BlankWebView2Window : Window
    {
        public BlankWebView2Window()
        {
            InitializeComponent();
            webView.Source = new System.Uri("about:blank");
        }
    }
}
