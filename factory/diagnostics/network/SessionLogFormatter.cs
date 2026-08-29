using System;
using System.Collections.Generic;
using System.Linq;
using GameFactory.Diagnostics;
using GameFactory.Networking.Peers;

namespace GameFactory.Diagnostics.Network;

/// <summary>Pure presentation of the concise, human-facing session flight recorder.</summary>
public static class SessionLogFormatter
{
    public static string SourceLabel(string role, PeerId peerId) => role == "host" ? "H" : $"C:{peerId.Value}";

    public static string Format(DateTimeOffset utc, string role, PeerId peerId, LogEntry entry)
    {
        string fields = string.Join(" ", entry.Fields
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{pair.Key}={Sanitize(pair.Value!)}"));
        string message = string.IsNullOrWhiteSpace(entry.Message) ? string.Empty : $" {Sanitize(entry.Message)}";
        string suffix = string.IsNullOrWhiteSpace(fields) ? string.Empty : $" {fields}";
        return $"{utc:HH:mm:ss.fff} {SourceLabel(role, peerId),-14} {entry.Level.ToString().ToUpperInvariant(),-5} {entry.Category}.{entry.Event}{message}{suffix}";
    }

    public static string FormatGap(DateTimeOffset utc, PeerId peerId, string runId, long firstMissing, long droppedThrough) =>
        $"{utc:HH:mm:ss.fff} {SourceLabel("client", peerId),-14} WARNING diagnostics.gap run={Sanitize(runId)} missing={firstMissing}-{droppedThrough}";

    private static string Sanitize(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
