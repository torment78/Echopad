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

        public PadActionController(IList<PadModel> pads)
        {
            _pads = pads;
        }

        // =====================================================
        // EVENTS (UI / AUDIO LAYER SUBSCRIBES)
        // =====================================================
        public event Action<PadModel>? PlayRequested;
        public event Action<PadModel>? StopRequested;

        // NEW: empty pad pressed -> request “commit last buffer to this pad”
        public event Action<PadModel>? CommitFromBufferRequested;

        // NEW: fired when CTRL-copy pastes a clip to a target (so UI can persist)
        public event Action<PadModel /*source*/, PadModel /*target*/>? PadCopied;

        // =====================================================
        // NEW: short block to prevent immediate Echo re-commit
        // after delete / after closing settings (click-through / midi repeat)
        // =====================================================
        private readonly Dictionary<int, DateTime> _echoCommitBlockedUntilUtc = new();

        /// <summary>
        /// Temporarily blocks Echo commit on a pad for a short window.
        /// Call this after ClearPad() or when closing dialogs to prevent click-through re-trigger.
        /// </summary>
        public void BlockEchoCommit(int padIndex, int ms = 450)
        {
            if (padIndex <= 0) return;
            _echoCommitBlockedUntilUtc[padIndex] = DateTime.UtcNow.AddMilliseconds(ms);
        }

        private bool IsEchoCommitBlocked(PadModel pad)
        {
            if (pad == null) return true;

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

            // NEW: leaving edit mode should cancel CTRL-copy mode
            if (!_isEditMode)
                ClearCopyState();
        }

        public void SetCopyHeld(bool held)
        {
            _copyHeld = held;

            // NEW: Ctrl released = copy mode OFF
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

            // ---------------------------------------------
            // COPY MODE (CTRL held, edit mode only)
            // ---------------------------------------------
            if (_isEditMode && _copyHeld)
            {
                HandleCopy(pad);
                return;
            }

            // ---------------------------------------------
            // HARD GATE: "Echo recording / commit-from-buffer"
            // Only allowed if pad is Echo-enabled.
            // PLUS: block window to prevent immediate re-commit
            // after delete / dialog close / click-through.
            // ---------------------------------------------
            if (string.IsNullOrWhiteSpace(pad.ClipPath))
            {
                // NEW: prevent instant re-trigger
                if (IsEchoCommitBlocked(pad))
                    return;

                // NEW: empty pad click does NOTHING unless Echo mode is enabled for that pad
                if (CanStartEchoCommit(pad))
                    CommitFromBufferRequested?.Invoke(pad);

                return;
            }

            // ---------------------------------------------
            // NORMAL PLAY / STOP TOGGLE
            // ---------------------------------------------
            if (pad.State == PadState.Playing)
            {
                Stop(pad);
            }
            else
            {
                Play(pad);
            }
        }

        // =====================================================
        // PLAY / STOP
        // =====================================================
        private void Play(PadModel pad)
        {
            if (pad.State == PadState.Playing)
                return;

            pad.State = PadState.Playing;
            pad.IsBusy = true;

            PlayRequested?.Invoke(pad);
        }

        private void Stop(PadModel pad)
        {
            if (pad.State != PadState.Playing)
                return;

            StopRequested?.Invoke(pad);

            pad.IsBusy = false;

            // Return to loaded if clip exists
            pad.State = !string.IsNullOrWhiteSpace(pad.ClipPath)
                ? PadState.Loaded
                : PadState.Empty;
        }

        // =====================================================
        // CLEAR PAD (used by hold-to-clear)
        // =====================================================
        public void ClearPad(int padIndex)
        {
            var pad = GetPad(padIndex);
            if (pad == null)
                return;

            // NEW: prevent immediate Echo re-commit caused by mouse-up / midi repeat
            BlockEchoCommit(pad.Index, 650);

            StopRequested?.Invoke(pad);

            pad.ClipPath = null;
            pad.ClipDuration = TimeSpan.Zero;
            pad.StartMs = 0;
            pad.EndMs = 0;
            pad.PlayheadMs = 0;

            pad.InputSource = 1;
            pad.PreviewToMonitor = false;

            pad.ClipMod = ClipMod.None;
            pad.IsBusy = false;
            pad.IsHoldArmed = false;

            pad.State = PadState.Empty;
        }

        // =====================================================
        // CTRL-COPY (EDIT MODE) - PERSIST UNTIL CTRL RELEASE
        // =====================================================
        private PadModel? _copySource;

        private void ClearCopyState()
        {
            // NEW: clear ALL copy visuals (source + any targets)
            foreach (var p in _pads)
            {
                if (p.ClipMod == ClipMod.CopySource || p.ClipMod == ClipMod.CopiedTarget)
                    p.ClipMod = ClipMod.None;
            }

            _copySource = null;
        }

        private void HandleCopy(PadModel clicked)
        {
            // 1) If no source: first CTRL+click sets source (must have a clip)
            if (_copySource == null)
            {
                if (string.IsNullOrWhiteSpace(clicked.ClipPath))
                    return; // no-op: can't copy from empty

                _copySource = clicked;
                _copySource.ClipMod = ClipMod.CopySource;
                return; // IMPORTANT: do NOT paste on the first click
            }

            // 2) If clicking source again: no-op
            if (ReferenceEquals(_copySource, clicked))
                return;

            // 3) Paste to target (repeatable)
            CopyPad(_copySource, clicked);

            // Optional visual marker for target (non-blocking)
            clicked.ClipMod = ClipMod.CopiedTarget;

            // NEW: tell UI layer to persist target clip assignment immediately
            PadCopied?.Invoke(_copySource, clicked);

            // NEW: keep source armed until CTRL is released
            _copySource.ClipMod = ClipMod.CopySource;
        }

        private static void CopyPad(PadModel src, PadModel dst)
        {
            dst.ClipPath = src.ClipPath;
            dst.ClipDuration = src.ClipDuration;

            dst.StartMs = src.StartMs;
            dst.EndMs = src.EndMs;

            // OLD (not wanted - copies in/out routing too)
            // dst.InputSource = src.InputSource;
            // dst.PreviewToMonitor = src.PreviewToMonitor;

            dst.State = !string.IsNullOrWhiteSpace(dst.ClipPath)
                ? PadState.Loaded
                : PadState.Empty;
        }

        // =====================================================
        // ECHO COMMIT GATE (NO ACCIDENTAL RECORDS)
        // =====================================================
        private static bool CanStartEchoCommit(PadModel pad)
        {
            // Hard gate we can enforce today:
            // - pad must be Echo-enabled
            // - pad must NOT be Drop-enabled (mutual exclusion rule)
            // - pad must not be busy
            if (!pad.IsEchoMode)
                return false;

            if (pad.IsDropFolderMode)
                return false;

            if (pad.IsBusy || pad.State == PadState.Playing)
                return false;

            // empty pad already implied by caller
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
