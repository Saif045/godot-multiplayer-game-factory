using System;
using System.Collections.Generic;
using System.Linq;

namespace GameFactory.Diagnostics.Network;

/// <summary>Bounded pure relay state, kept separate from Godot RPC concerns.</summary>
public sealed class RelayBacklog
{
    private const int RecentLimit = 128;
    private const int BacklogLimit = 512;
    private readonly List<LogEntry> _recent = [];
    private readonly List<LogEntry> _entries = [];

    public string? SessionId { get; private set; }
    public long DroppedThroughSequence { get; private set; }
    public IReadOnlyList<LogEntry> Entries => _entries;

    public void Record(LogEntry entry)
    {
        _recent.Add(entry);
        if (_recent.Count > RecentLimit) _recent.RemoveAt(0);
        if (SessionId is null) return;
        AddToBacklog(WithSession(entry, SessionId));
    }

    public void BeginSession(DiagnosticsSessionId sessionId)
    {
        string next = sessionId.ToString();
        if (SessionId == next) return;
        SessionId = next;
        _entries.Clear();
        DroppedThroughSequence = 0;
        foreach (LogEntry entry in _recent)
            AddToBacklog(WithSession(entry, next));
    }

    public void EndSession()
    {
        SessionId = null;
        _entries.Clear();
        _recent.Clear();
        DroppedThroughSequence = 0;
    }

    public LogBatch? CreateBatch(
        string runId,
        int maximumEntries,
        long hostUtcAnchorUnixMilliseconds = 0,
        long sourceElapsedAnchorMilliseconds = 0)
    {
        if (SessionId is null || _entries.Count == 0) return null;
        return new LogBatch(
            SessionId,
            runId,
            DroppedThroughSequence,
            hostUtcAnchorUnixMilliseconds,
            sourceElapsedAnchorMilliseconds,
            _entries.Take(maximumEntries).ToArray());
    }

    public void Acknowledge(long highestSequence)
    {
        _entries.RemoveAll(entry => entry.Sequence <= highestSequence);
        if (DroppedThroughSequence <= highestSequence) DroppedThroughSequence = 0;
    }

    private void AddToBacklog(LogEntry entry)
    {
        _entries.Add(entry);
        if (_entries.Count <= BacklogLimit) return;
        LogEntry removed = _entries[0];
        _entries.RemoveAt(0);
        DroppedThroughSequence = Math.Max(DroppedThroughSequence, removed.Sequence);
    }

    private static LogEntry WithSession(LogEntry entry, string sessionId) => entry with { DiagnosticsSessionId = sessionId };
}
