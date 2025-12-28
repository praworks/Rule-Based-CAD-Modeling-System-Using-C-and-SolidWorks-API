using System;
using System.Drawing;
using System.Windows.Forms;

namespace AICAD.UI
{
    public static class NameEasyTab
    {
        public static Panel CreatePanel(SettingsDialog host)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(30) }; // Increased padding for "breathing room"
            UITheme.ApplyPanelStyle(panel);

            // Use a clean layout for the inputs
            var table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, AutoScroll = true, Height = 150 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));

            // ROW 1: Label
            var lblPath = new Label 
            { 
                Text = "Database Path:", 
                Dock = DockStyle.Fill, 
                TextAlign = ContentAlignment.BottomLeft, // Align text to bottom of label for better visual flow with textbox
                Font = new Font("Segoe UI", 9F, FontStyle.Bold) 
            };
            UITheme.ApplyLabelStyle(lblPath);

            // ROW 2: Input + Browse Button
            host._txtNameEasyPath = new TextBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10F) }; // Slightly larger font
            UITheme.ApplyTextBoxStyle(host._txtNameEasyPath);
            
            host._btnBrowseNameEasy = new Button { Text = "Browse...", Height = 28, Dock = DockStyle.Top };
            UITheme.ApplyButtonStyle(host._btnBrowseNameEasy, false);
            host._btnBrowseNameEasy.Click += host.BtnBrowseNameEasy_Click;

            // ROW 3: Helper Text
            host._lblNameEasyInfo = new Label 
            { 
                Text = "Select the location for the 'NameEasy.db' file.\nDefault: Add-in installation folder.", 
                Dock = DockStyle.Fill, 
                ForeColor = Color.Gray, 
                AutoSize = true, 
                Padding = new Padding(0, 5, 0, 0) 
            };
            
            // Layout Logic
            table.Controls.Add(lblPath, 0, 0); 
            table.SetColumnSpan(lblPath, 2); // Label spans across

            table.Controls.Add(host._txtNameEasyPath, 0, 1);
            table.SetColumnSpan(host._txtNameEasyPath, 2); // Textbox takes more space
            
            table.Controls.Add(host._btnBrowseNameEasy, 2, 1); // Button on the right

            table.Controls.Add(host._lblNameEasyInfo, 0, 2);
            table.SetColumnSpan(host._lblNameEasyInfo, 3);

            panel.Controls.Add(table);

            // REMOVED: The local "Save" button and actionPanel. 
            // We rely on the global Apply button now.

            return panel;
        }
    }
}
