using System;
using System.Drawing;
using System.Windows.Forms;

namespace AICAD.UI
{
    public static class LlmTab
    {
        public static Panel CreatePanel(SettingsDialog host)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15) };
            UITheme.ApplyPanelStyle(panel);

            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // Labels and controls
            var lblLlmMode = new Label { Text = "LLM Mode:", Dock = DockStyle.Fill, Padding = new Padding(0,8,0,0) };
            UITheme.ApplyLabelStyle(lblLlmMode);
            host._cmbLlmMode = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0,5,0,5) };
            host._cmbLlmMode.Items.AddRange(new object[] { "Cloud (Gemini/Groq)", "Local" });
            host._cmbLlmMode.SelectedIndexChanged += (s, e) =>
            {
                var isLocal = host._cmbLlmMode.SelectedIndex == 1;
                host._txtLocalEndpoint.Enabled = isLocal;
                host._txtLocalModel.Enabled = isLocal;
                host._txtLocalSystemPrompt.Enabled = isLocal;
                host._txtGeminiKey.Enabled = !isLocal;
                host._txtGroqKey.Enabled = !isLocal;
            };

            // Local endpoint
            var lblLocalEndpoint = new Label { Text = "Local LLM Endpoint:", Dock = DockStyle.Fill, Padding = new Padding(0,8,0,0) };
            UITheme.ApplyLabelStyle(lblLocalEndpoint);
            host._txtLocalEndpoint = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0,5,0,5) };
            UITheme.ApplyTextBoxStyle(host._txtLocalEndpoint);

            // Gemini key
            var lblGemini = new Label { Text = "Google Gemini API Key:", Dock = DockStyle.Fill, Padding = new Padding(0,8,0,0) };
            UITheme.ApplyLabelStyle(lblGemini);
            host._txtGeminiKey = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true, Margin = new Padding(0,5,0,5) };
            UITheme.ApplyTextBoxStyle(host._txtGeminiKey);
            var geminiPanel = new Panel { Dock = DockStyle.Fill };
            var btnToggleGemini = new Button { Width = 40, Height = 24, Dock = DockStyle.Right, FlatStyle = FlatStyle.Flat, Text = "Show", TabStop = false };
            UITheme.ApplyButtonStyle(btnToggleGemini, false);
            btnToggleGemini.Click += (s, e) => host.ToggleApiKeyVisibility(s, e, host._txtGeminiKey);
            geminiPanel.Controls.Add(btnToggleGemini);
            geminiPanel.Controls.Add(host._txtGeminiKey);

            // Groq key
            var lblGroq = new Label { Text = "Groq API Key:", Dock = DockStyle.Fill, Padding = new Padding(0,8,0,0) };
            UITheme.ApplyLabelStyle(lblGroq);
            host._txtGroqKey = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true, Margin = new Padding(0,5,0,5) };
            UITheme.ApplyTextBoxStyle(host._txtGroqKey);
            var groqPanel = new Panel { Dock = DockStyle.Fill };
            var btnToggleGroq = new Button { Width = 40, Height = 24, Dock = DockStyle.Right, FlatStyle = FlatStyle.Flat, Text = "Show", TabStop = false };
            UITheme.ApplyButtonStyle(btnToggleGroq, false);
            btnToggleGroq.Click += (s, e) => host.ToggleApiKeyVisibility(s, e, host._txtGroqKey);
            groqPanel.Controls.Add(btnToggleGroq);
            groqPanel.Controls.Add(host._txtGroqKey);

            // Local model
            var lblLocalModel = new Label { Text = "Local Model (identifier):", Dock = DockStyle.Fill, Padding = new Padding(0,8,0,0) };
            UITheme.ApplyLabelStyle(lblLocalModel);
            host._txtLocalModel = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0,5,0,5) };
            UITheme.ApplyTextBoxStyle(host._txtLocalModel);

            // Local system prompt
            var lblLocalSys = new Label { Text = "Local System Prompt:", Dock = DockStyle.Fill, Padding = new Padding(0,8,0,0) };
            UITheme.ApplyLabelStyle(lblLocalSys);
            host._txtLocalSystemPrompt = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0,5,0,5) };
            UITheme.ApplyTextBoxStyle(host._txtLocalSystemPrompt);

            // Few-shot and allow multiple
            host._chkUseFewShot = new CheckBox { Text = "Enable Few-Shot examples (use examples from DB)", Dock = DockStyle.Fill, Padding = new Padding(3,8,0,0) };
            host._chkAllowMultipleBuilds = new CheckBox { Text = "Allow multiple build requests (disable button protection)", Dock = DockStyle.Fill, Padding = new Padding(3,8,0,0) };

            // Status label
            host._lblApiStatus = new Label { Text = "", Dock = DockStyle.Fill, Padding = new Padding(0,5,0,5) };
            UITheme.ApplyLabelStyle(host._lblApiStatus);

            // Add to table
            table.RowCount = 12;
            int r = 0;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); // mode
            table.Controls.Add(lblLlmMode, 0, r); table.Controls.Add(host._cmbLlmMode, 1, r); r++;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); // endpoint
            table.Controls.Add(lblLocalEndpoint, 0, r); table.Controls.Add(host._txtLocalEndpoint, 1, r); r++;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); // gemini
            table.Controls.Add(lblGemini, 0, r); table.Controls.Add(geminiPanel, 1, r); r++;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); // groq
            table.Controls.Add(lblGroq, 0, r); table.Controls.Add(groqPanel, 1, r); r++;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); // local model
            table.Controls.Add(lblLocalModel, 0, r); table.Controls.Add(host._txtLocalModel, 1, r); r++;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); // local sys
            table.Controls.Add(lblLocalSys, 0, r); table.Controls.Add(host._txtLocalSystemPrompt, 1, r); r++;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // fewshot
            table.SetColumnSpan(host._chkUseFewShot, 2); table.Controls.Add(host._chkUseFewShot, 0, r); r++;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // allow multiple
            table.SetColumnSpan(host._chkAllowMultipleBuilds, 2); table.Controls.Add(host._chkAllowMultipleBuilds, 0, r); r++;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // status
            table.SetColumnSpan(host._lblApiStatus, 2); table.Controls.Add(host._lblApiStatus, 0, r); r++;
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // filler

            panel.Controls.Add(table);

            // Bottom action buttons
            var actionPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0,10,0,10) };
            host._btnLoadApiKey = new Button { Text = "Load from Environment", Width = 160, Height = 30, Margin = new Padding(0,0,10,0) };
            host._btnSaveApiKey = new Button { Text = "Save to Environment", Width = 160, Height = 30 };
            host._btnTestApi = new Button { Text = "Test API", Width = 120, Height = 30, Margin = new Padding(10,0,0,0) };
            UITheme.ApplyButtonStyle(host._btnLoadApiKey, false);
            UITheme.ApplyButtonStyle(host._btnSaveApiKey, true);
            UITheme.ApplyButtonStyle(host._btnTestApi, false);
            host._btnLoadApiKey.Click += host.BtnLoadApiKey_Click;
            host._btnSaveApiKey.Click += host.BtnSaveApiKey_Click;
            host._btnTestApi.Click += async (s, e) => { var _ = host.BtnTestApi_Click(s, e); };
            actionPanel.Controls.Add(host._btnLoadApiKey);
            actionPanel.Controls.Add(host._btnSaveApiKey);
            actionPanel.Controls.Add(host._btnTestApi);

            panel.Controls.Add(actionPanel);

            return panel;
        }
    }
}
