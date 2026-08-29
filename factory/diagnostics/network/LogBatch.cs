using System.Collections.Generic;

namespace GameFactory.Diagnostics.Network;

public sealed record LogBatch(
    string DiagnosticsSessionId,
    string RunId,
    long DroppedThroughSequence,
    long HostUtcAnchorUnixMilliseconds,
    long SourceElapsedAnchorMilliseconds,
    IReadOnlyList<LogEntry> Entries);
