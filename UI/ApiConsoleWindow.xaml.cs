using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AICAD.UI
{
    public partial class ApiConsoleWindow : Window
    {
        private const int MaxLines = 500;
        public ObservableCollection<string> Lines { get; } = new ObservableCollection<string>();

        public ApiConsoleWindow()
        {
            InitializeComponent();
            DataContext = this;

            BtnClear.Click += (_, __) => { try { Lines.Clear(); } catch { } };
            BtnCopy.Click += (_, __) =>
            {
                try
                {
                    if (Lines.Count == 0) return;
                    var joined = string.Join(Environment.NewLine, Lines);
                    Clipboard.SetText(joined);
                }
                catch { }
            };
        }

        public void AddLine(string line)
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(new Action<string>(AddLine), line);
                    return;
                }

                if (string.IsNullOrWhiteSpace(line)) return;
                if (Lines.Count > MaxLines) Lines.RemoveAt(0);
                Lines.Add(line);

                if (ChkAutoscroll.IsChecked == true && Lines.Count > 0)
                {
                    var last = Lines.Last();
                    try { LogList.ScrollIntoView(last); } catch { }
                }
            }
            catch { }
        }
    }
}
