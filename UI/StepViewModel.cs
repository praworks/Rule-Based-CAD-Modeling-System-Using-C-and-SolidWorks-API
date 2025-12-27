using System;
using System.ComponentModel;

namespace AICAD.UI
{
    public enum StepState { Pending, Running, Success, Error }

    public class StepViewModel : INotifyPropertyChanged
    {
        private string _label;
        private StepState _state = StepState.Pending;
        private int? _percent;
        private string _message;
        private DateTime? _timestamp;
        private string _barString;
        private const int DefaultBarWidth = 20;

        public string Label { get => _label; set { _label = value; OnPropertyChanged(nameof(Label)); } }
        public StepState State { get => _state; set { _state = value; OnPropertyChanged(nameof(State)); } }
        public int? Percent
        {
            get => _percent;
            set
            {
                _percent = value;
                // Update computed bar string when percent changes
                _barString = _percent.HasValue ? MakeProgressBar(_percent.Value, DefaultBarWidth) : string.Empty;
                OnPropertyChanged(nameof(Percent));
                OnPropertyChanged(nameof(BarString));
            }
        }
        public string Message { get => _message; set { _message = value; OnPropertyChanged(nameof(Message)); } }
        public DateTime? Timestamp { get => _timestamp; set { _timestamp = value; OnPropertyChanged(nameof(Timestamp)); } }

        // Computed ASCII/Unicode segmented progress bar string (e.g. "■■■■□□□□...")
        public string BarString { get => _barString; }

        private string MakeProgressBar(int pct, int width)
        {
            try
            {
                if (pct < 0) pct = 0;
                if (pct > 100) pct = 100;
                int filled = (int)Math.Round(pct / 100.0 * width);
                if (filled < 0) filled = 0;
                if (filled > width) filled = width;
                int empty = width - filled;
                return new string('■', filled) + new string('□', empty);
            }
            catch
            {
                return new string('■', width);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
