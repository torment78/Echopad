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

        // =====================================================
        // MODE FLAGS
        // =====================================================
        public void SetEditMode(bool isEditMode)
        {
            _isEditMode = isEditMode;
        }

        public void SetCopyHeld(bool held)
        {
            _copyHeld = held;
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
            // NEW: EMPTY PAD => COMMIT FROM ROLLING BUFFER
            // ---------------------------------------------
            if (string.IsNullOrWhiteSpace(pad.ClipPath))
            {
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
        // COPY LOGIC (EDIT MODE)
        // =====================================================
        private PadModel? _copySource;

        private void HandleCopy(PadModel target)
        {
            // First CTRL+click = mark source (must have a clip)
            if (_copySource == null)
            {
                if (string.IsNullOrWhiteSpace(target.ClipPath))
                    return;

                _copySource = target;
                target.ClipMod = ClipMod.CopySource;
                return;
            }

            // Second CTRL+click = paste
            if (_copySource == target)
                return;

            CopyPad(_copySource, target);

            _copySource.ClipMod = ClipMod.None;
            target.ClipMod = ClipMod.CopiedTarget;

            _copySource = null;
        }

        private static void CopyPad(PadModel src, PadModel dst)
        {
            dst.ClipPath = src.ClipPath;
            dst.ClipDuration = src.ClipDuration;

            dst.StartMs = src.StartMs;
            dst.EndMs = src.EndMs;

            dst.InputSource = src.InputSource;
            dst.PreviewToMonitor = src.PreviewToMonitor;

            dst.State = !string.IsNullOrWhiteSpace(dst.ClipPath)
                ? PadState.Loaded
                : PadState.Empty;
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
