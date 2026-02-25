using System;
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

        // =========================================================
        // NEW: Global lock to prevent profile switching while edit windows are open
        // (Pad Settings / Profile Manager / Pad Profile Settings)
        // Reference-counted so multiple windows can lock safely.
        // =========================================================
        private int _profileSwitchLockCount;

        private bool _isProfileSwitchLocked;
        public bool IsProfileSwitchLocked
        {
            get => _isProfileSwitchLocked;
            private set
            {
                if (_isProfileSwitchLocked == value) return;
                _isProfileSwitchLocked = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Acquire a lock that blocks profile switching globally.
        /// Dispose the returned token to release the lock.
        /// </summary>
        public IDisposable AcquireProfileSwitchLock(string reason = "")
        {
            // reason currently unused, but useful later for logging/debug.
            _profileSwitchLockCount++;
            if (_profileSwitchLockCount == 1)
                IsProfileSwitchLocked = true;

            return new ProfileSwitchLockToken(this);
        }

        private void ReleaseProfileSwitchLock()
        {
            if (_profileSwitchLockCount <= 0)
                return;

            _profileSwitchLockCount--;
            if (_profileSwitchLockCount == 0)
                IsProfileSwitchLocked = false;
        }

        private sealed class ProfileSwitchLockToken : IDisposable
        {
            private MainViewModel? _vm;
            public ProfileSwitchLockToken(MainViewModel vm) => _vm = vm;

            public void Dispose()
            {
                _vm?.ReleaseProfileSwitchLock();
                _vm = null;
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