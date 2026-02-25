using System;

namespace Echopad.App.Services
{
    /// <summary>
    /// Global lock for profile switching. Reference-counted so multiple windows can lock safely.
    /// </summary>
    public sealed class ProfileSwitchLock
    {
        private int _count;

        public bool IsLocked => _count > 0;

        public event Action<bool>? LockChanged;

        public IDisposable Acquire(string reason = "")
        {
            _count++;
            if (_count == 1)
                LockChanged?.Invoke(true);

            return new Releaser(this);
        }

        private void Release()
        {
            if (_count <= 0) return;

            _count--;
            if (_count == 0)
                LockChanged?.Invoke(false);
        }

        private sealed class Releaser : IDisposable
        {
            private ProfileSwitchLock? _owner;
            public Releaser(ProfileSwitchLock owner) => _owner = owner;

            public void Dispose()
            {
                _owner?.Release();
                _owner = null;
            }
        }
    }
}