using System;
using System.Drawing;
using System.Windows.Forms;

namespace AICAD.UI
{
    public static class DatabaseTab
    {
        // Create a Panel containing the database controls and assign to host fields
        public static Panel CreateDatabasePanel(SettingsDialog host)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };
            UITheme.ApplyPanelStyle(panel);

            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // MongoDB URI
            var lblUri = new Label { Text = "MongoDB URI:", Dock = DockStyle.Fill, Padding = new Padding(0,10,0,0) };
            host._txtMongoUri = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0,8,0,8) };
            UITheme.ApplyTextBoxStyle(host._txtMongoUri);

            // Database Name
            var lblDb = new Label { Text = "Database Name:", Dock = DockStyle.Fill, Padding = new Padding(0,10,0,0) };
            host._txtMongoDb = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0,8,0,8) };
            UITheme.ApplyTextBoxStyle(host._txtMongoDb);

            // Username
            var lblUser = new Label { Text = "Username:", Dock = DockStyle.Fill, Padding = new Padding(0,10,0,0) };
            host._txtMongoUser = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0,8,0,8) };
            UITheme.ApplyTextBoxStyle(host._txtMongoUser);

            // Password + visibility toggle
            var lblPassword = new Label { Text = "Password:", Dock = DockStyle.Fill, Padding = new Padding(0,10,0,0) };
            host._txtMongoPassword = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true, Margin = new Padding(0,8,0,8) };
            UITheme.ApplyTextBoxStyle(host._txtMongoPassword);
            var pwPanel = new Panel { Dock = DockStyle.Fill };
            host._btnToggleMongoPwVisibility = new Button { Width = 40, Height = 24, Dock = DockStyle.Right, FlatStyle = FlatStyle.Flat, Text = "Show", TabStop = false };
            UITheme.ApplyButtonStyle(host._btnToggleMongoPwVisibility, false);
            host._btnToggleMongoPwVisibility.Click += (s, e) =>
            {
                try
                {
                    host._txtMongoPassword.UseSystemPasswordChar = !host._txtMongoPassword.UseSystemPasswordChar;
                    host._btnToggleMongoPwVisibility.Text = host._txtMongoPassword.UseSystemPasswordChar ? "Show" : "Hide";
                }
                catch { }
            };
            pwPanel.Controls.Add(host._btnToggleMongoPwVisibility);
            pwPanel.Controls.Add(host._txtMongoPassword);

            // Buttons
            var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 10, 0, 10) };
            host._btnLoadMongo = new Button { Text = "Load from Env", Width = 160, Height = 30, Margin = new Padding(0, 0, 10, 0) };
            host._btnSaveMongo = new Button { Text = "Save to Env", Width = 160, Height = 30 };
            UITheme.ApplyButtonStyle(host._btnLoadMongo, false);
            UITheme.ApplyButtonStyle(host._btnSaveMongo, true);
            host._btnLoadMongo.Click += host.BtnLoadMongo_Click;
            host._btnSaveMongo.Click += host.BtnSaveMongo_Click;
            buttonPanel.Controls.Add(host._btnLoadMongo);
            buttonPanel.Controls.Add(host._btnSaveMongo);

            // Status
            host._lblMongoStatus = new Label { Text = "", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DarkGreen, Padding = new Padding(0, 5, 0, 5) };
            UITheme.ApplyLabelStyle(host._lblMongoStatus);

            // Layout rows
            table.RowCount = 7;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            table.Controls.Add(lblUri, 0, 0); table.Controls.Add(host._txtMongoUri, 1, 0);
            table.Controls.Add(lblDb, 0, 1); table.Controls.Add(host._txtMongoDb, 1, 1);
            table.Controls.Add(lblUser, 0, 2); table.Controls.Add(host._txtMongoUser, 1, 2);
            table.Controls.Add(lblPassword, 0, 3); table.Controls.Add(pwPanel, 1, 3);
            table.SetColumnSpan(buttonPanel, 2); table.Controls.Add(buttonPanel, 0, 4);
            table.SetColumnSpan(host._lblMongoStatus, 2); table.Controls.Add(host._lblMongoStatus, 0, 5);

            panel.Controls.Add(table);
            return panel;
        }
    }
}
