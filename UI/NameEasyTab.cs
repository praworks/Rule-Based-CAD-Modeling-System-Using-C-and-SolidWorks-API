using System;
using System.Drawing;
using System.Windows.Forms;

namespace AICAD.UI
{
    public static class NameEasyTab
    {
        public static Panel CreatePanel(SettingsDialog host)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };
            UITheme.ApplyPanelStyle(panel);

            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

            var lblPath = new Label { Text = "Database Path:", Dock = DockStyle.Fill, Padding = new Padding(0,12,6,0), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
            UITheme.ApplyLabelStyle(lblPath);
            host._txtNameEasyPath = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(6,8,6,8) };
            UITheme.ApplyTextBoxStyle(host._txtNameEasyPath);
            host._btnBrowseNameEasy = new Button { Text = "Browse...", Width = 120, Height = 30, Margin = new Padding(6,8,0,8) };
            UITheme.ApplyButtonStyle(host._btnBrowseNameEasy, false);
            host._btnBrowseNameEasy.Click += host.BtnBrowseNameEasy_Click;

            table.RowCount = 3;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));

            table.Controls.Add(lblPath, 0, 0);
            table.Controls.Add(host._txtNameEasyPath, 1, 0);
            table.Controls.Add(host._btnBrowseNameEasy, 2, 0);

            host._lblNameEasyInfo = new Label { Text = "Choose where to store the NameEasy.db database. Default: add-in folder.", Dock = DockStyle.Fill, ForeColor = Color.Gray, AutoSize = false, Padding = new Padding(0,16,0,0), TextAlign = ContentAlignment.TopLeft };
            UITheme.ApplyLabelStyle(host._lblNameEasyInfo);
            table.SetColumnSpan(host._lblNameEasyInfo, 3);
            table.Controls.Add(host._lblNameEasyInfo, 0, 1);

            panel.Controls.Add(table);

            var actionPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 60, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 12, 12, 12) };
            host._btnSaveNameEasy = new Button { Text = "Save", Width = 110, Height = 34, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            UITheme.ApplyButtonStyle(host._btnSaveNameEasy, true);
            host._btnSaveNameEasy.Click += host.BtnSaveNameEasy_Click;
            actionPanel.Controls.Add(host._btnSaveNameEasy);

            panel.Controls.Add(actionPanel);

            return panel;
        }
    }
}
