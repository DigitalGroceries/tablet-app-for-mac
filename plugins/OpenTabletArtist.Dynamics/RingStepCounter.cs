namespace OpenTabletArtist.Dynamics;

/// <summary>
/// Build H: turns the ring's activations into key presses at a per-mode step size. OTD calls the ring
/// binding once per <c>RingStep</c> degrees of travel (the wheel's activation threshold; 5° is the PTK-440
/// ring's finest). This sends keys once per mode step: travel accumulates per mode, resets when the mode
/// changes, and the remainder carries over, so an 8° step on a 5° ring fires on average every 8°
/// (alternately after 5° and 10°). A step of 0, or not coarser than the ring step, fires every activation.
/// No dependencies, so the app's tests compile it directly.
/// </summary>
public sealed class RingStepCounter
{
    private readonly float[] _steps;
    private double _travel;
    private int _mode = -1;

    public RingStepCounter(int modes) => _steps = new float[modes];

    /// <summary>Degrees of ring travel per OTD activation; 0 = unknown (fire every activation).</summary>
    public float RingStep { get; set; }

    public float this[int mode]
    {
        get => _steps[mode];
        set => _steps[mode] = value;
    }

    public bool Count(int mode)
    {
        if (mode < 0 || mode >= _steps.Length) return false;
        if (mode != _mode) { _mode = mode; _travel = 0; }
        double step = _steps[mode];
        if (RingStep <= 0 || step <= RingStep + 1e-6) return true;
        _travel += RingStep;
        if (_travel + 1e-6 < step) return false;
        _travel -= step;
        return true;
    }
}
