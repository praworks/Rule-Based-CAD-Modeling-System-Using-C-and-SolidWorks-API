using System.Windows;
using System.Windows.Controls;

namespace AICAD.UI
{
    public partial class TextToCADTaskpaneWpf : UserControl
    {
        public TextToCADTaskpaneWpf()
        {
            InitializeComponent();
        }

        // Convenience accessors for future bridging
        public string PromptText
        {
            get => prompt.Text;
            set => prompt.Text = value;
        }

        public string VersionText
        {
            get => lblVersion.Content?.ToString() ?? string.Empty;
            set => lblVersion.Content = value;
        }

        public string RealTimeStatus
        {
            get => lblRealTimeStatus.Content?.ToString() ?? string.Empty;
            set => lblRealTimeStatus.Content = value;
        }
    }
}