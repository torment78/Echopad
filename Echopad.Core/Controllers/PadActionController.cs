using System;
using System.Collections.Generic;
using Echopad.Core;

namespace Echopad.Core.Controllers
{
    public sealed class PadActionController
    {
        private readonly IList<PadModel> _pads;

        private bool _isEditMode;
        private bool _copyHeld;

        // =====================================================
        // NEW: transition guard (prevents rapid re-entry / race)
        // =====================================================
        private readonly HashSet<int> _transitioningPads = new();

        public PadActionController(IList<PadModel> pads)
        {
            _pads = pads;
        }

        // =====================================================
        // EVENTS (UI / AUDIO LAYER SUBSCRIBES)
        // =====================================================
        public event Action<PadModel>? PlayRequested;
        public event Action<PadModel>? StopRequested;

        public event Action<PadModel>? CommitFromBufferRequested;
        public event Action<PadModel, PadModel>? PadCopied;

        // =====================================================
        // Echo commit block window
        // =====================================================
        private readonly Dictionary<int, DateTime> _echoCommitBlockedUntilUtc = new();

        public void BlockEchoCommit(int padIndex, int ms = 450)
        {
            if (padIndex <= 0) return;
            _echoCommitBlockedUntilUtc[padIndex] = DateTime.UtcNow.AddMilliseconds(ms);
        }

        private bool IsEchoCommitBlocked(PadModel pad)
        {
            if (_echoCommitBlockedUntilUtc.TryGetValue(pad.Index, out var until))
                return DateTime.UtcNow < until;

            return false;
        }

        // =====================================================
        // MODE FLAGS
        // =====================================================
        public void SetEditMode(bool isEditMode)
        {
            _isEditMode = isEditMode;
            if (!_isEditMode)
                ClearCopyState();
        }

        public void SetCopyHeld(bool held)
        {
            _copyHeld = held;
            if (!_copyHeld)
                ClearCopyState();
        }

        // =====================================================
        // PAD ACTIVATION ENTRY POINT
        // =====================================================
        public void ActivatePad(int padIndex)
        {
            var pad = GetPad(padIndex);
            if (pad == null)
                return;

            // COPY MODE
            if (_isEditMode && _copyHeld)
            {
                HandleCopy(pad);
                return;
            }

            // Empty pad (Echo commit)
            if (string.IsNullOrWhiteSpace(pad.ClipPath))
            {
                if (IsEchoCommitBlocked(pad))
                    return;

                if (CanStartEchoCommit(pad))
                    CommitFromBufferRequested?.Invoke(pad);

                return;
            }

            // NORMAL TOGGLE
            if (pad.State == PadState.Playing)
                Stop(pad);
            else
                Play(pad);
        }

        // =====================================================
        // PLAY / STOP (WITH TRANSITION GUARD)
        // =====================================================
        private void Play(PadModel pad)
        {
            // OLD:
            // if (pad.State == PadState.Playing)
            //     return;

            if (pad.State == PadState.Playing)
                return;

            // NEW: prevent re-entry while transitioning
            if (_transitioningPads.Contains(pad.Index))
                return;

            _transitioningPads.Add(pad.Index);

            pad.State = PadState.Playing;
            pad.IsBusy = true;

            PlayRequested?.Invoke(pad);

            _transitioningPads.Remove(pad.Index);
        }

        private void Stop(PadModel pad)
        {
            // OLD:
            // if (pad.State != PadState.Playing)
            //     return;

            if (pad.State != PadState.Playing)
                return;

            // NEW: prevent re-entry while transitioning
            if (_transitioningPads.Contains(pad.Index))
                return;

            _transitioningPads.Add(pad.Index);

            StopRequested?.Invoke(pad);

            pad.IsBusy = false;

            pad.State = !string.IsNullOrWhiteSpace(pad.ClipPath)
                ? PadState.Loaded
                : PadState.Empty;

            _transitioningPads.Remove(pad.Index);
        }

        // =====================================================
        // CLEAR PAD
        // =====================================================
        public void ClearPad(int padIndex)
        {
            var pad = GetPad(padIndex);
            if (pad == null)
                return;

            BlockEchoCommit(pad.Index, 650);

            StopRequested?.Invoke(pad);

            pad.ClipPath = null;
            pad.ClipDuration = TimeSpan.Zero;
            pad.StartMs = 0;
            pad.EndMs = 0;
            pad.PlayheadMs = 0;
            pad.PadName = null;
            pad.InputSource = 1;
            pad.PreviewToMonitor = false;

            pad.ClipMod = ClipMod.None;
            pad.IsBusy = false;
            pad.IsHoldArmed = false;

            pad.State = PadState.Empty;
        }

        // =====================================================
        // CTRL COPY
        // =====================================================
        private PadModel? _copySource;

        private void ClearCopyState()
        {
            foreach (var p in _pads)
            {
                if (p.ClipMod == ClipMod.CopySource || p.ClipMod == ClipMod.CopiedTarget)
                    p.ClipMod = ClipMod.None;
            }

            _copySource = null;
        }

        private void HandleCopy(PadModel clicked)
        {
            if (_copySource == null)
            {
                if (string.IsNullOrWhiteSpace(clicked.ClipPath))
                    return;

                _copySource = clicked;
                _copySource.ClipMod = ClipMod.CopySource;
                return;
            }

            if (ReferenceEquals(_copySource, clicked))
                return;

            CopyPad(_copySource, clicked);

            clicked.ClipMod = ClipMod.CopiedTarget;
            PadCopied?.Invoke(_copySource, clicked);

            _copySource.ClipMod = ClipMod.CopySource;
        }

        private static void CopyPad(PadModel src, PadModel dst)
        {
            dst.ClipPath = src.ClipPath;
            dst.ClipDuration = src.ClipDuration;
            dst.StartMs = src.StartMs;
            dst.EndMs = src.EndMs;

            dst.State = !string.IsNullOrWhiteSpace(dst.ClipPath)
                ? PadState.Loaded
                : PadState.Empty;
        }

        // =====================================================
        // ECHO COMMIT GATE
        // =====================================================
        private static bool CanStartEchoCommit(PadModel pad)
        {
            if (!pad.IsEchoMode)
                return false;

            if (pad.IsDropFolderMode)
                return false;

            if (pad.IsBusy || pad.State == PadState.Playing)
                return false;

            return true;
        }

        // =====================================================
        // UTIL
        // =====================================================
        private PadModel? GetPad(int index)
        {
            if (index <= 0 || index > _pads.Count)
                return null;

            return _pads[index - 1];
        }
    }
}
