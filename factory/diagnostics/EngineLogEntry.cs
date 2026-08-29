using System;
using System.Collections.Generic;
using System.Linq;

namespace GameFactory.Diagnostics;

/// <summary>One raw Godot engine logger callback, kept locally even when no session exists.</summary>
public sealed record EngineLogEntry(
    DateTimeOffset Utc,
    LogLevel Level,
    string Message,
    string? Function = null,
    string? File = null,
    int? Line = null,
    string? Code = null,
    string? Rationale = null,
    string? ErrorType = null,
    IReadOnlyList<string>? Backtrace = null)
{
    public IReadOnlyDictionary<string, string?> ToGameLogFields() => new Dictionary<string, string?>
    {
        ["engine_function"] = Function,
        ["engine_file"] = File,
        ["engine_line"] = Line?.ToString(),
        ["engine_type"] = ErrorType,
        ["engine_code"] = Code,
        ["engine_rationale"] = Rationale,
        ["engine_backtrace"] = Backtrace is null ? null : string.Join(" | ", Backtrace)
    };
}

public static class EngineLogFormatter
{
    public static string FormatLocal(EngineLogEntry entry)
    {
        var lines = new List<string>
        {
            $"{entry.Utc:O} [{entry.Level.ToString().ToUpperInvariant()}] {entry.Message}"
        };
        if (!string.IsNullOrWhiteSpace(entry.ErrorType)) lines.Add($"  type: {entry.ErrorType}");
        if (!string.IsNullOrWhiteSpace(entry.File)) lines.Add($"  source: {entry.File}:{entry.Line} {entry.Function}".TrimEnd());
        if (!string.IsNullOrWhiteSpace(entry.Code)) lines.Add($"  code: {entry.Code}");
        if (!string.IsNullOrWhiteSpace(entry.Rationale)) lines.Add($"  rationale: {entry.Rationale}");
        if (entry.Backtrace is not null) lines.AddRange(entry.Backtrace.Select(frame => $"  at: {frame}"));
        return string.Join(Environment.NewLine, lines);
    }
}
