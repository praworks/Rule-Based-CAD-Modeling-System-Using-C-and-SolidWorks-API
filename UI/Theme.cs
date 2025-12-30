using System.Drawing;
using System.Windows.Forms;

namespace AICAD.UI
{
    public static class Theme
    {
        // --- Palette (SolidWorks / Modern Windows Style) ---
        public static Color Background = Color.FromArgb(240, 240, 240);       // Main Window Background
        public static Color PanelBackground = Color.White;                    // Content Areas
        public static Color NavBackground = Color.FromArgb(225, 225, 225);    // Sidebar
        public static Color Accent = Color.FromArgb(66, 139, 202);            // SolidWorks Blue
        public static Color Text = Color.FromArgb(50, 50, 50);                // Main Text
        public static Color MutedText = Color.FromArgb(100, 100, 100);        // Labels/Hints
        public static Color BorderGray = Color.FromArgb(200, 200, 200);       // Subtle borders

        // Status / Console colors (for WinForms parts)
        public static Color StatusRest = Color.FromArgb(249, 249, 249); // #F9F9F9
        public static Color StatusActive = Color.FromArgb(255, 243, 205); // #FFF3CD

        public static Color ConsoleBackground = Color.Black;
        public static Color ConsoleForeground = Color.FromArgb(220, 220, 220); // #DCDCDC
        public static Font ConsoleFont = new Font("Consolas", 9f, FontStyle.Regular);

        // Standard Font for Modern Windows Apps
        public static Font MainFont = new Font("Segoe UI", 10f, FontStyle.Regular);
        public static Font BoldFont = new Font("Segoe UI", 10f, FontStyle.Bold);

        /// <summary>
        /// Applies standard font and color to the main window.
        /// </summary>
        public static void ApplyFormStyle(Form f)
        {
            f.BackColor = Background;
            f.ForeColor = Text;
            f.Font = MainFont;
            // Optional: Set a consistent size here if desired
            // f.Size = new Size(850, 600); 
        }

        public static void ApplyButtonStyle(Button b, bool isPrimary)
        {
            b.Font = MainFont;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.Cursor = Cursors.Hand; // Hand cursor for better UX

            if (isPrimary)
            {
                b.BackColor = Accent;
                b.ForeColor = Color.White;
                b.FlatAppearance.BorderColor = Accent; // Seamless border
            }
            else
            {
                b.BackColor = Color.FromArgb(245, 245, 245); // Slightly lighter than grey
                b.ForeColor = Text;
                b.FlatAppearance.BorderColor = BorderGray;
            }

            // Simple Hover Effect (Optional: You can remove these lines if you don't want event handlers here)
            b.MouseEnter += (s, e) => { if (!isPrimary) b.BackColor = Color.FromArgb(235, 235, 235); };
            b.MouseLeave += (s, e) => { if (!isPrimary) b.BackColor = Color.FromArgb(245, 245, 245); };
        }

        public static void ApplyNavButtonStyle(Button b, bool isActive)
        {
            b.Width = 240; // Wider for better text fit
            b.Height = 50; // Taller for better touch targets
            b.TextAlign = ContentAlignment.MiddleLeft;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.Padding = new Padding(20, 0, 0, 0); // More indent for text
            b.Cursor = Cursors.Hand;

            if (isActive)
            {
                b.BackColor = PanelBackground; // Matches content area (White)
                b.ForeColor = Accent;
                b.Font = BoldFont;
                // Add a colored "active bar" on the left visually using border
                b.FlatAppearance.BorderSize = 0; 
            }
            else
            {
                b.BackColor = Color.Transparent; // Shows NavBackground
                b.ForeColor = Text;
                b.Font = MainFont;
            }
        }

        public static void ApplyTextBoxStyle(TextBox t)
        {
            t.BackColor = Color.White;
            t.ForeColor = Text;
            t.BorderStyle = BorderStyle.FixedSingle;
            t.Font = MainFont;
            // Padding inside TextBox requires a custom control or P/Invoke in WinForms, 
            // but increasing height helps visual breathing room.
        }

        public static void ApplyLabelStyle(Label l)
        {
            l.ForeColor = Text; // Darker text is often more readable than MutedText for inputs
            l.Font = MainFont;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.BackColor = Color.Transparent;
        }

        public static void ApplyPanelStyle(Panel p)
        {
            p.BackColor = PanelBackground;
        }
    }
}
