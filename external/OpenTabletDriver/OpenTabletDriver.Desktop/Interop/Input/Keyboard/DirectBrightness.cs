using System;
using System.Collections.Generic;
using OpenTabletDriver.Plugin;

namespace OpenTabletDriver.Desktop.Interop.Input.Keyboard
{
    /// <summary>
    /// Tablet App for Mac, Version 29 (TASKS #87): one brightness step on one screen, set directly (on the Mac through
    /// DisplayServices; injected here so the rules can be tested anywhere).
    /// Rules (Grok's review of 6336e62):
    ///  - a screen whose read or set returns an error can't be changed this way: it uses the Mac's brightness key from
    ///    then on (<see cref="Outcome.NotPossible"/>, so the caller sends the key for this press too);
    ///  - a set that says yes while the read-back hasn't moved is NOT trusted as a failure at once (some Macs fade the
    ///    change in): only 3 such presses in a row on that screen move it to the key route, any real move resets the count,
    ///    and the built-in screen is never moved by read-backs alone. No second change is sent for such a press.
    /// </summary>
    public sealed class DirectBrightness
    {
        public enum Outcome { Changed, NotSeenYet, NotPossible }

        /// <summary>Presses in a row that said yes without a visible change before a screen goes to the key route.</summary>
        public const int MissesBeforeKeyRoute = 3;

        private readonly Func<uint, (bool Ok, float Value)> _get;
        private readonly Func<uint, float, bool> _set;
        private readonly Func<uint, bool> _isBuiltIn;
        private readonly Action<string, LogLevel> _log;
        private readonly HashSet<uint> _keyRoute = new HashSet<uint>();
        private readonly Dictionary<uint, int> _misses = new Dictionary<uint, int>();

        public DirectBrightness(Func<uint, (bool Ok, float Value)> get, Func<uint, float, bool> set,
            Func<uint, bool> isBuiltIn, Action<string, LogLevel> log)
        {
            _get = get; _set = set; _isBuiltIn = isBuiltIn; _log = log;
        }

        /// <summary>True once this screen was moved to the Mac's brightness key.</summary>
        public bool UsesKeyRoute(uint display) => _keyRoute.Contains(display);

        public Outcome Step(uint display, bool up, string describe)
        {
            if (display == 0 || _keyRoute.Contains(display)) return Outcome.NotPossible;
            try
            {
                var (ok, before) = _get(display);
                if (!ok || before < 0 || float.IsNaN(before))
                {
                    _keyRoute.Add(display);
                    _log($"Brightness: display {display} ({describe}) can't be changed directly; using the Mac's brightness key for it", LogLevel.Info);
                    return Outcome.NotPossible;
                }
                var wanted = MacOSSystemKeys.NextBrightness(before, up);
                if (!_set(display, wanted))
                {
                    _keyRoute.Add(display);
                    _log($"Brightness: macOS refused to set display {display} ({describe}); using the Mac's brightness key for it (if the screen did change, this one press may count twice)", LogLevel.Warning);
                    return Outcome.NotPossible;
                }
                var (readOk, after) = _get(display);
                bool atLimit = Math.Abs(wanted - before) < 0.001f; // already at the top or bottom: nothing to see
                if (readOk && !atLimit && Math.Abs(after - before) < 0.001f)
                {
                    int misses = _misses.TryGetValue(display, out var m) ? m + 1 : 1;
                    _misses[display] = misses;
                    if (misses >= MissesBeforeKeyRoute && !_isBuiltIn(display))
                    {
                        _keyRoute.Add(display);
                        _log($"Brightness: display {display} ({describe}) stayed at {before:0.00} after {misses} direct changes in a row; the next press uses the Mac's brightness key", LogLevel.Warning);
                    }
                    else
                        _log($"Brightness: display {display} ({describe}) read {before:0.00} right after a direct change to {wanted:0.00} ({misses} in a row; may still be fading in)", LogLevel.Info);
                    return Outcome.NotSeenYet;
                }
                _misses.Remove(display);
                _log($"Brightness {(up ? "up" : "down")}: display {display} ({describe}) changed directly, {before:0.00} -> {(readOk ? after : wanted):0.00}", LogLevel.Info);
                return Outcome.Changed;
            }
            catch (Exception e)
            {
                _keyRoute.Add(display);
                _log($"Brightness: the direct change failed on display {display}: {e.Message}; using the Mac's brightness key", LogLevel.Warning);
                return Outcome.NotPossible;
            }
        }
    }
}
