using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using AICAD.Services;

namespace AICAD.UI
{
    public class GeminiChatTaskpane : UserControl
    {
        private readonly TextBox _input;
        private readonly Button _send;
        private readonly RichTextBox _chat;
        private readonly ComboBox _model;
        private GeminiClient _client;

        public GeminiChatTaskpane()
        {
            Dock = DockStyle.Fill;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(6)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

            var top = new Panel { Dock = DockStyle.Fill, Height = 28 };
            _model = new ComboBox { Dock = DockStyle.Left, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            _model.Items.AddRange(new object[] { "gemini-1.5-flash", "gemini-1.5-pro" });
            _model.SelectedIndex = 0;
            top.Controls.Add(_model);

            _chat = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = SystemColors.Window, HideSelection = false };

            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            _input = new TextBox { Dock = DockStyle.Fill };
            _send = new Button { Text = "Send", Dock = DockStyle.Fill };
            _send.Click += async (s, e) => await SendAsync();
            bottom.Controls.Add(_input, 0, 0);
            bottom.Controls.Add(_send, 1, 0);

            root.Controls.Add(top, 0, 0);
            root.Controls.Add(_chat, 0, 1);
            root.Controls.Add(bottom, 0, 2);
            Controls.Add(root);

            // Initialize client on first use; API key read from env var to avoid storing secrets.
            // Set an environment variable GEMINI_API_KEY to your Google API key.
        }

        private GeminiClient GetClient()
        {
            if (_client == null)
            {
                var key = global::AICAD.Services.CredentialManager.ReadGenericSecret("SolidWorksTextToCAD_GEMINI_API_KEY")
                          ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.User)
                          ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.Process)
                          ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.Machine);
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new InvalidOperationException("GEMINI_API_KEY not set. Set it in your user environment variables.");
                }
                var preferredModel = Environment.GetEnvironmentVariable("GEMINI_MODEL", EnvironmentVariableTarget.User)
                                     ?? Environment.GetEnvironmentVariable("GEMINI_MODEL", EnvironmentVariableTarget.Process)
                                     ?? Environment.GetEnvironmentVariable("GEMINI_MODEL", EnvironmentVariableTarget.Machine)
                                     ?? _model.SelectedItem?.ToString()
                                     ?? "gemini-1.0";
                _client = new GeminiClient(key, preferredModel);
            }
            else
            {
                _client.SetModel(_model.SelectedItem?.ToString());
            }
            return _client;
        }

        private async Task SendAsync()
        {
            var text = _input.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            AppendChat($"You: {text}\n");
            _input.Clear();
            try
            {
                _send.Enabled = false;
                var client = GetClient();
                var reply = await client.GenerateAsync(text);
                AppendChat($"Gemini: {reply}\n\n");
            }
            catch (Exception ex)
            {
                AppendChat($"Error: {ex.Message}\n");
            }
            finally
            {
                _send.Enabled = true;
            }
        }

        private void AppendChat(string text)
        {
            _chat.AppendText(text);
            _chat.SelectionStart = _chat.TextLength;
            _chat.ScrollToCaret();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _client?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
