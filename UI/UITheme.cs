using System.Drawing;
using System.Windows.Forms;

namespace AICAD.UI
{
    [System.Obsolete("UITheme is deprecated — use Theme instead.")]
    public static class UITheme
    {
        // Forwarding properties to new Theme class to preserve compatibility.
        public static Color Background => Theme.Background;
        public static Color PanelBackground => Theme.PanelBackground;
        public static Color NavBackground => Theme.NavBackground;
        public static Color Accent => Theme.Accent;
        public static Color Text => Theme.Text;
        public static Color MutedText => Theme.MutedText;
        public static Color BorderGray => Theme.BorderGray;

        public static Font MainFont => Theme.MainFont;
        public static Font BoldFont => Theme.BoldFont;

        public static void ApplyFormStyle(Form f) => Theme.ApplyFormStyle(f);
        public static void ApplyButtonStyle(Button b, bool isPrimary) => Theme.ApplyButtonStyle(b, isPrimary);
        public static void ApplyNavButtonStyle(Button b, bool isActive) => Theme.ApplyNavButtonStyle(b, isActive);
        public static void ApplyTextBoxStyle(TextBox t) => Theme.ApplyTextBoxStyle(t);
        public static void ApplyLabelStyle(Label l) => Theme.ApplyLabelStyle(l);
        public static void ApplyPanelStyle(Panel p) => Theme.ApplyPanelStyle(p);
    }
}