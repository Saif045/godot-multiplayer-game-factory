using System;
using System.Collections.Generic;

namespace GameFactory.Diagnostics;

public sealed record LogEntry(
    DateTimeOffset Utc,
    long ElapsedMilliseconds,
    long Sequence,
    string RunId,
    LogLevel Level,
    string Category,
    string Event,
    string? Message,
    IReadOnlyDictionary<string, string?> Fields,
    string? DiagnosticsSessionId);
