using System.Diagnostics;
using StreamJsonRpc;

namespace OtdInterop;

/// <summary>Build R: StreamJsonRpc's default TraceSource writes through DefaultTraceListener, which on macOS
/// ends up in the system log (Console) — so a normal daemon restart or app quit shows up there as red
/// JSON-RPC "connection lost" errors. Disconnects are already handled (and reconnected) by the client;
/// route the trace to our own log at debug level, errors only, and keep it out of the system log.</summary>
public static class QuietRpcTrace
{
    public static void Apply(JsonRpc rpc, Action<string>? sink)
    {
        var ts = new TraceSource("OTA.JsonRpc", SourceLevels.Error);
        ts.Listeners.Clear();
        if (sink != null) ts.Listeners.Add(new Forward(sink));
        rpc.TraceSource = ts;
    }

    private sealed class Forward(Action<string> sink) : TraceListener
    {
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { if (!string.IsNullOrEmpty(message)) Safe("JSON-RPC: " + message); }
        private void Safe(string m) { try { sink(m); } catch (Exception) { } }
    }
}
