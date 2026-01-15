using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using AICAD.Services.Logging;
using AICAD.UI.Models;

namespace AICAD.UI
{
    public partial class ExchangeWindow : Window
    {
        private const int MaxMessages = 400;
        private bool _subscribed;

        public ObservableCollection<ChatMessage> Messages { get; } = new ObservableCollection<ChatMessage>();

        public ExchangeWindow()
        {
            InitializeComponent();
            DataContext = this;
            // Do not cache the enabled state here — check at load time so
            // runtime changes to the environment variable take effect.

            Loaded += ExchangeWindow_Loaded;
            Closed += ExchangeWindow_Closed;

            BtnClear.Click += (_, __) => { try { Messages.Clear(); } catch { } };
            BtnCopy.Click += (_, __) => CopyAll();
        }

        private void ExchangeWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!LlmTraceLogger.Enabled)
                {
                    DisabledPanel.Visibility = Visibility.Visible;
                    ChatScroll.Visibility = Visibility.Collapsed;
                    if (LblStatus != null) LblStatus.Text = "LLM trace disabled";
                    return;
                }

                DisabledPanel.Visibility = Visibility.Collapsed;
                ChatScroll.Visibility = Visibility.Visible;

                ReplayBuffered();
                SubscribeLive();
            }
            catch { }
        }

        private void ExchangeWindow_Closed(object sender, EventArgs e)
        {
            try
            {
                if (_subscribed)
                {
                    LlmTraceLogger.OnTraceEvent -= HandleTraceEvent;
                    _subscribed = false;
                }
            }
            catch { }
        }

        private void SubscribeLive()
        {
            try
            {
                if (_subscribed) return;
                LlmTraceLogger.OnTraceEvent += HandleTraceEvent;
                _subscribed = true;
            }
            catch { }
        }

        private void ReplayBuffered()
        {
            try
            {
                Messages.Clear();
                var events = LlmTraceLogger.GetRecentEvents(MaxMessages);
                var chats = LlmWhatsAppFormatter.ToChatMessages(events);
                AppendMessages(chats);
                ScrollToBottom();
            }
            catch { }
        }

        private void HandleTraceEvent(LlmTraceEvent evt)
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(new Action<LlmTraceEvent>(HandleTraceEvent), evt);
                    return;
                }

                var chat = LlmWhatsAppFormatter.ToChatMessage(evt);
                if (chat != null) AppendMessages(new[] { chat });
            }
            catch { }
        }

        private void AppendMessages(IEnumerable<ChatMessage> items)
        {
            if (items == null) return;
            try
            {
                foreach (var m in items)
                {
                    if (m == null) continue;
                    Messages.Add(m);
                    if (Messages.Count > MaxMessages)
                    {
                        Messages.RemoveAt(0);
                    }
                }

                if (ChkAutoscroll.IsChecked == true)
                {
                    ScrollToBottom();
                }
            }
            catch { }
        }

        private void ScrollToBottom()
        {
            try
            {
                ChatList?.UpdateLayout();
                ChatScroll?.ScrollToBottom();
            }
            catch { }
        }

        private void CopyAll()
        {
            try
            {
                if (Messages.Count == 0) return;
                var sb = new StringBuilder();
                foreach (var m in Messages)
                {
                    sb.AppendLine($"{m.Sender}: {m.Meta}");
                    sb.AppendLine(m.Body ?? string.Empty);
                    sb.AppendLine();
                }
                Clipboard.SetText(sb.ToString());
            }
            catch { }
        }
    }
}
