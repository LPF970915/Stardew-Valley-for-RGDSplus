using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Rgdsplus.PerformanceProbe;

// Diagnostic-only capture. Never enabled in a release or timing acceptance run.
internal sealed class JitTrace : EventListener
{
    private readonly List<object> rows = new();
    private bool capturing;
    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime")
            EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x10);
    }
    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (!capturing || e.EventName?.Contains("MethodJittingStarted") != true) return;
        var values = new Dictionary<string, object>();
        for (int i = 0; i < e.PayloadNames.Count; i++) values[e.PayloadNames[i]] = e.Payload[i];
        lock (rows) rows.Add(values);
    }
    internal void Start() { lock (rows) rows.Clear(); capturing = true; }
    internal void Stop(string path)
    {
        capturing = false;
        lock (rows) File.WriteAllText(path, JsonSerializer.Serialize(rows));
    }
}
