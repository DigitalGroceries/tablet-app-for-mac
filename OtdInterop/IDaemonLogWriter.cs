namespace OtdInterop;

/// <summary>
/// Writes one line into the driver's own log (the daemon's <c>WriteMessage</c>), so a mark the app
/// sets, such as the start and end of a report recording, appears in the driver log too.
/// Implemented by <c>DaemonClient</c>; a separate interface so existing fakes need no change.
/// </summary>
public interface IDaemonLogWriter
{
    /// <summary>False when not connected or the daemon refused the line (the failure is logged).</summary>
    Task<bool> WriteLogAsync(string group, string message);
}
