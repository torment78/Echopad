using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Echopad.Core;

namespace Echopad.App
{
    public sealed class MainViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<PadModel> Pads { get; } = new();

        private bool _isEditMode;
        public bool IsEditMode
        {
            get => _isEditMode;
            set
            {
                if (_isEditMode == value)
                    return;

                _isEditMode = value;
                OnPropertyChanged();
            }
        }

        public MainViewModel()
        {
            for (int i = 1; i <= 16; i++)
                Pads.Add(new PadModel(i));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
