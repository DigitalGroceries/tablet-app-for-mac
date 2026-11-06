using System;
using System.Numerics;

namespace OpenTabletDriver.Desktop.Interop.Input
{
    /// <summary>
    /// Version 25: drops left-drag posts during a tip click so Finder sees a stationary click instead
    /// of an icon drag. Separate from <see cref="DoubleClickState"/>, which only decides the click count.
    /// Double Click Fix 0 = off, 50 = 1.0 mm, 100 = 2.0 mm, converted with the live absolute mapping
    /// and clamped to 4–48 screen pixels. T is <see cref="DurationMs"/>.
    /// </summary>
    public sealed class ClickJitterGate
    {
        public const int DurationMs = 250;
        public const int MinPixels = 4;
        public const int MaxPixels = 48;
        public const int UiOff = 0;
        public const int UiDefault = 50;
        public const int UiMax = 100;

        /// <summary>Used only when the live mapping scale is not yet known (Default 1 mm → 4 px).</summary>
        public const double FallbackPixelsPerMillimetre = 4;

        public static double DisplayToMillimetres(int display)
        {
            if (display <= 0)
                return 0;
            return Math.Clamp(display, UiOff, UiMax) / 50.0;
        }

        /// <summary>Missing or negative saved values (older builds that stored pixels elsewhere) become Default.</summary>
        public static int ResolveSavedDisplay(int? saved) =>
            saved is int value && value >= 0
                ? Math.Clamp(value, UiOff, UiMax)
                : UiDefault;

        public static double PixelsPerMillimetreFromAreas(
            float inputWidthMm, float inputHeightMm, float outputWidthPx, float outputHeightPx)
        {
            var inputLen = Math.Sqrt((double)inputWidthMm * inputWidthMm + (double)inputHeightMm * inputHeightMm);
            if (inputLen <= 0)
                return inputWidthMm > 0 ? outputWidthPx / (double)inputWidthMm : 0;
            var outputLen = Math.Sqrt((double)outputWidthPx * outputWidthPx + (double)outputHeightPx * outputHeightPx);
            return outputLen / inputLen;
        }

        public static int GatePixels(int display, double pixelsPerMillimetre)
        {
            var mm = DisplayToMillimetres(display);
            if (mm <= 0)
                return 0;
            var scale = pixelsPerMillimetre > 0 ? pixelsPerMillimetre : FallbackPixelsPerMillimetre;
            return (int)Math.Clamp(Math.Round(mm * scale), MinPixels, MaxPixels);
        }

        public readonly record struct Completion(int DurationMs, float MaxWobblePx, float MaxWobbleMm, string ReleasedBy);

        public bool IsGating { get; private set; }
        public Vector2 DownPosition { get; private set; }
        public Completion? LastCompletion { get; private set; }

        private long _downTimestampMs;
        private int _sPixels;
        private double _pixelsPerMillimetre;
        private float _maxWobblePx;

        public void OnTipDown(Vector2 down, long timestampMs, int display, double pixelsPerMillimetre)
        {
            LastCompletion = null;
            DownPosition = down;
            _downTimestampMs = timestampMs;
            _pixelsPerMillimetre = pixelsPerMillimetre > 0 ? pixelsPerMillimetre : FallbackPixelsPerMillimetre;
            _sPixels = GatePixels(display, _pixelsPerMillimetre);
            _maxWobblePx = 0;
            IsGating = _sPixels > 0;
        }

        /// <summary>Null means drop this dragged position; otherwise post the returned point (the true pen on release).</summary>
        public Vector2? FilterDrag(Vector2 current, long timestampMs)
        {
            if (!IsGating)
                return current;

            Track(current);
            if (ShouldRelease(current, timestampMs) is { } reason)
            {
                Finish(reason, timestampMs);
                return current;
            }

            return null;
        }

        /// <summary>While still gated, the up is posted at the down position so Finder sees a stationary click.</summary>
        public Vector2 OnTipUp(Vector2 current, long timestampMs)
        {
            if (!IsGating)
                return current;

            Track(current);
            Finish("up", timestampMs);
            return DownPosition;
        }

        public Completion? ConsumeCompletion()
        {
            var completion = LastCompletion;
            LastCompletion = null;
            return completion;
        }

        private void Track(Vector2 current)
        {
            var dist = Vector2.Distance(DownPosition, current);
            if (dist > _maxWobblePx)
                _maxWobblePx = dist;
        }

        private string? ShouldRelease(Vector2 current, long timestampMs)
        {
            if (Vector2.Distance(DownPosition, current) > _sPixels)
                return "S";
            if (timestampMs - _downTimestampMs >= DurationMs)
                return "T";
            return null;
        }

        private void Finish(string reason, long timestampMs)
        {
            IsGating = false;
            var duration = (int)Math.Max(0, timestampMs - _downTimestampMs);
            var maxMm = _pixelsPerMillimetre > 0 ? _maxWobblePx / (float)_pixelsPerMillimetre : 0;
            LastCompletion = new Completion(duration, _maxWobblePx, maxMm, reason);
        }
    }
}
