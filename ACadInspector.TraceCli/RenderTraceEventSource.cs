using System.Diagnostics.Tracing;

namespace ACadInspector.TraceCli;

[EventSource(Name = "ACadInspector.TraceCli")]
internal sealed class RenderTraceEventSource : EventSource
{
    public static readonly RenderTraceEventSource Log = new();

    private RenderTraceEventSource()
    {
    }

    [Event(1, Level = EventLevel.Informational)]
    public void PhaseStart(string phase, string inputPath, int iteration)
    {
        WriteEvent(1, phase ?? string.Empty, inputPath ?? string.Empty, iteration);
    }

    [Event(2, Level = EventLevel.Informational)]
    public void PhaseStop(string phase, string inputPath, int iteration, double durationMs, int success)
    {
        WriteEvent(2, phase ?? string.Empty, inputPath ?? string.Empty, iteration, durationMs, success);
    }
}
