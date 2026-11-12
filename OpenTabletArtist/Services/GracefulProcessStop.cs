using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenTabletArtist.Services;

/// <summary>
/// Tablet App for Mac (#73): stops the driver process politely first. A killed process (SIGKILL) can't let go
/// of the mouse button it posts down for the pen tip, and macOS then keeps that button pressed with nobody left
/// to release it, so the user's real mouse button stops clicking. SIGTERM lets the driver release held buttons
/// on its way out; if it hasn't exited within <paramref name="grace"/> it is killed as before.
/// </summary>
internal static class GracefulProcessStop
{
    /// <summary>How long the driver gets to exit after SIGTERM before it is killed.</summary>
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(3);

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int PosixKill(int pid, int signal);

    private const int SigTerm = 15;

    /// <summary>Asks <paramref name="proc"/> to exit (SIGTERM on macOS/Linux), waits up to
    /// <paramref name="grace"/>, then kills it. Throws only when even the kill fails.</summary>
    /// <returns>True when the process exited by itself after the polite request; false when it had to be killed.</returns>
    public static bool Stop(Process proc, TimeSpan? grace = null)
    {
        var wait = grace ?? DefaultGrace;
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                if (PosixKill(proc.Id, SigTerm) == 0 && proc.WaitForExit((int)wait.TotalMilliseconds))
                    return true;
            }
            catch (Exception ex)
            {
                AppLog.Debug($"Couldn't ask process {proc.Id} to exit politely; killing it.", ex);
            }
        }
        proc.Kill();
        return false;
    }
}
