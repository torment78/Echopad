using System;
using System.Windows;
using System.Windows.Input;
using Echopad.App.Services;
using Echopad.Core;

namespace Echopad.App.UI.Behaviors
{
    /// <summary>
    /// Attached behavior for pad edit overlay:
    /// Mouse wheel adjusts trim values while hovering.
    ///
    /// STEP 1 contract (current project state):
    /// - StartMs = absolute start (ms)
    /// - EndMs   = absolute end (ms)
    ///
    /// NEW (persistence):
    /// - If SettingsService + GlobalSettings are provided via attached props,
    ///   wheel changes are written to disk immediately.
    /// </summary>
    public static class PadTrimWheelBehavior
    {
        public enum TrimTarget
        {
            Start,
            End
        }

        // -----------------------------------------------------
        // Target (Start/End)
        // -----------------------------------------------------
        public static readonly DependencyProperty TargetProperty =
            DependencyProperty.RegisterAttached(
                "Target",
                typeof(TrimTarget),
                typeof(PadTrimWheelBehavior),
                new PropertyMetadata(TrimTarget.Start, OnAttachedPropertyChanged));

        public static void SetTarget(DependencyObject element, TrimTarget value)
            => element.SetValue(TargetProperty, value);

        public static TrimTarget GetTarget(DependencyObject element)
            => (TrimTarget)element.GetValue(TargetProperty);

        // -----------------------------------------------------
        // IsActive (Edit mode)
        // -----------------------------------------------------
        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.RegisterAttached(
                "IsActive",
                typeof(bool),
                typeof(PadTrimWheelBehavior),
                new PropertyMetadata(false, OnAttachedPropertyChanged));

        public static void SetIsActive(DependencyObject element, bool value)
            => element.SetValue(IsActiveProperty, value);

        public static bool GetIsActive(DependencyObject element)
            => (bool)element.GetValue(IsActiveProperty);

        // -----------------------------------------------------
        // NEW: SettingsService (optional, for persistence)
        // -----------------------------------------------------
        public static readonly DependencyProperty SettingsServiceProperty =
            DependencyProperty.RegisterAttached(
                "SettingsService",
                typeof(SettingsService),
                typeof(PadTrimWheelBehavior),
                new PropertyMetadata(null));

        public static void SetSettingsService(DependencyObject element, SettingsService value)
            => element.SetValue(SettingsServiceProperty, value);

        public static SettingsService? GetSettingsService(DependencyObject element)
            => element.GetValue(SettingsServiceProperty) as SettingsService;

        // -----------------------------------------------------
        // NEW: GlobalSettings (optional, for persistence)
        // -----------------------------------------------------
        public static readonly DependencyProperty GlobalSettingsProperty =
            DependencyProperty.RegisterAttached(
                "GlobalSettings",
                typeof(GlobalSettings),
                typeof(PadTrimWheelBehavior),
                new PropertyMetadata(null));

        public static void SetGlobalSettings(DependencyObject element, GlobalSettings value)
            => element.SetValue(GlobalSettingsProperty, value);

        public static GlobalSettings? GetGlobalSettings(DependencyObject element)
            => element.GetValue(GlobalSettingsProperty) as GlobalSettings;

        // -----------------------------------------------------
        // Hook guard (don’t double-hook)
        // -----------------------------------------------------
        private static readonly DependencyProperty IsHookedProperty =
            DependencyProperty.RegisterAttached(
                "IsHooked",
                typeof(bool),
                typeof(PadTrimWheelBehavior),
                new PropertyMetadata(false));

        private static void SetIsHooked(DependencyObject element, bool value)
            => element.SetValue(IsHookedProperty, value);

        private static bool GetIsHooked(DependencyObject element)
            => (bool)element.GetValue(IsHookedProperty);

        private static void OnAttachedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement element)
                return;

            if (!GetIsHooked(element))
            {
                element.PreviewMouseWheel += OnPreviewMouseWheel;
                SetIsHooked(element, true);
            }
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not FrameworkElement fe)
                return;

            if (!GetIsActive(fe))
                return;

            if (fe.DataContext is not PadModel pad)
                return;

            int steps = e.Delta / 120;
            if (steps == 0)
                return;

            int stepMs = (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                ? 100
                : 50;

            int deltaMs = steps * stepMs;

            ApplyTrimDelta(pad, GetTarget(fe), deltaMs);

            // NEW: persist to disk immediately (if wired)
            PersistTrimIfPossible(fe, pad);

            e.Handled = true;
        }

        private static void ApplyTrimDelta(PadModel pad, TrimTarget target, int deltaMs)
        {
            int maxMs = (int)Math.Max(0, pad.ClipDuration.TotalMilliseconds);

            if (target == TrimTarget.Start)
            {
                int next = pad.StartMs + deltaMs;
                next = Math.Max(0, next);

                if (maxMs > 0)
                    next = Math.Min(maxMs, next);

                // keep coherent if start > end
                if (pad.EndMs > 0 && next > pad.EndMs)
                    next = pad.EndMs;

                pad.StartMs = next;
            }
            else
            {
                int next = pad.EndMs + deltaMs;
                next = Math.Max(0, next);

                if (maxMs > 0)
                    next = Math.Min(maxMs, next);

                // keep coherent if end < start
                if (next < pad.StartMs)
                    next = pad.StartMs;

                pad.EndMs = next;
            }
        }

        private static void PersistTrimIfPossible(FrameworkElement fe, PadModel pad)
        {
            var settingsService = GetSettingsService(fe);
            var global = GetGlobalSettings(fe);

            if (settingsService == null || global == null)
                return;

            // Ensure dictionary exists
            if (global.Pads == null)
                global.Pads = new System.Collections.Generic.Dictionary<int, PadSettings>();

            var ps = global.GetOrCreatePad(pad.Index);
            ps.StartMs = pad.StartMs;
            ps.EndMs = pad.EndMs;

            // Save immediately (simple + deterministic)
            settingsService.Save(global);
        }
    }
}
