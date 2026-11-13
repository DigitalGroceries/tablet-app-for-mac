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
                // No silent failures (#107 review, 13 Nov): every outcome gets its own log line, so a driver that
                // had to be killed (and could not let go of a held mouse button) shows up in the diagnostics.
                if (PosixKill(proc.Id, SigTerm) != 0)
                {
                    AppLog.Info($"Driver stop: the polite stop request to process {proc.Id} failed (errno {Marshal.GetLastPInvokeError()}); killing it.");
                }
                else if (proc.WaitForExit((int)wait.TotalMilliseconds))
                {
                    AppLog.Info($"Driver stop: process {proc.Id} exited by itself after the polite stop request.");
                    return true;
                }
                else
                {
                    AppLog.Info($"Driver stop: process {proc.Id} did not exit within {wait.TotalSeconds:0.#} s of the polite stop request; killing it.");
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Driver stop: couldn't ask process {proc.Id} to exit politely; killing it.", ex);
            }
        }
        try
        {
            proc.Kill();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Driver stop: killing process {proc.Id} failed.", ex);
            throw;
        }
        return false;
    }
}
