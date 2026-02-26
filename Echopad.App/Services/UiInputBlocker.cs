using System;
using System.Threading;

namespace Echopad.App.Services
{
    public static class UiInputBlocker
    {
        private static int _blockCount;

        // OLD:
        // public static bool IsBlocked => _blockCount > 0;

        // NEW: thread-safe read
        public static bool IsBlocked => Volatile.Read(ref _blockCount) > 0;

        // NEW: backwards-compatible alias (older code calls Block)
        public static IDisposable Block(string reason) => Acquire(reason);

        public static IDisposable Acquire(string reason)
        {
            Interlocked.Increment(ref _blockCount);
            return new Releaser();
        }

        private sealed class Releaser : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                Interlocked.Decrement(ref _blockCount);
            }
        }
    }
}