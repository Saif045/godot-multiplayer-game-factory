using System.Collections.Generic;

namespace GameFactory.Diagnostics.Network;

public sealed record LogBatch(
    string DiagnosticsSessionId,
    string RunId,
    long DroppedThroughSequence,
    long HostClockOffsetMilliseconds,
    IReadOnlyList<LogEntry> Entries);
