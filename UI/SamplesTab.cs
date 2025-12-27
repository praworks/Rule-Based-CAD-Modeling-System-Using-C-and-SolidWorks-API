using System;
using System.Drawing;
using System.Windows.Forms;

namespace AICAD.UI
{
    public static class SamplesTab
    {
        public static Panel CreatePanel(SettingsDialog host)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15) };
            UITheme.ApplyPanelStyle(panel);

            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

            // Mode label + radio buttons
            var lblMode = new Label { Text = "Sample Mode:", Dock = DockStyle.Fill, Padding = new Padding(0,8,0,0) };
            UITheme.ApplyLabelStyle(lblMode);
            var rbPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            host._rbZeroShot = new RadioButton { Text = "Zero-Shot", AutoSize = true };
            host._rbOneShot = new RadioButton { Text = "One-Shot", AutoSize = true };
            host._rbFewShot = new RadioButton { Text = "Few-Shot", AutoSize = true };
            rbPanel.Controls.Add(host._rbZeroShot);
            rbPanel.Controls.Add(host._rbOneShot);
            rbPanel.Controls.Add(host._rbFewShot);

            table.RowCount = 4;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            table.Controls.Add(lblMode, 0, 0);
            table.SetColumnSpan(rbPanel, 2);
            table.Controls.Add(rbPanel, 1, 0);

            // Samples DB path
            var lblPath = new Label { Text = "Samples DB Path:", Dock = DockStyle.Fill, Padding = new Padding(0,8,0,0) };
            UITheme.ApplyLabelStyle(lblPath);
            host._txtSamplesDbPath = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0,5,0,5) };
            UITheme.ApplyTextBoxStyle(host._txtSamplesDbPath);
            host._btnBrowseSamples = new Button { Text = "Browse...", Width = 80, Height = 26 };
            UITheme.ApplyButtonStyle(host._btnBrowseSamples, false);
            host._btnBrowseSamples.Click += host.BtnBrowseSamples_Click;

            table.Controls.Add(lblPath, 0, 1);
            table.Controls.Add(host._txtSamplesDbPath, 1, 1);
            table.Controls.Add(host._btnBrowseSamples, 2, 1);

            // Info label
            host._lblSamplesInfo = new Label { Text = "Choose how example shots are provided to the LLM and where they are stored.", Dock = DockStyle.Fill, ForeColor = Color.Gray, AutoSize = false, Padding = new Padding(0,10,0,0) };
            UITheme.ApplyLabelStyle(host._lblSamplesInfo);
            table.SetColumnSpan(host._lblSamplesInfo, 3);
            table.Controls.Add(host._lblSamplesInfo, 0, 3);

            panel.Controls.Add(table);

            // Action bar at bottom
            var actionPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0,10,0,10) };
            host._btnSaveSamples = new Button { Text = "Save", Width = 100, Height = 30, Anchor = AnchorStyles.Right };
            UITheme.ApplyButtonStyle(host._btnSaveSamples, true);
            host._btnSaveSamples.Click += host.BtnSaveSamples_Click;
            actionPanel.Controls.Add(host._btnSaveSamples);

            panel.Controls.Add(actionPanel);

            return panel;
        }
    }
}
