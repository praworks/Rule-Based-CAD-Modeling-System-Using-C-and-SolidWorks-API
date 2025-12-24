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

        public string Label { get => _label; set { _label = value; OnPropertyChanged(nameof(Label)); } }
        public StepState State { get => _state; set { _state = value; OnPropertyChanged(nameof(State)); } }
        public int? Percent { get => _percent; set { _percent = value; OnPropertyChanged(nameof(Percent)); } }
        public string Message { get => _message; set { _message = value; OnPropertyChanged(nameof(Message)); } }
        public DateTime? Timestamp { get => _timestamp; set { _timestamp = value; OnPropertyChanged(nameof(Timestamp)); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
