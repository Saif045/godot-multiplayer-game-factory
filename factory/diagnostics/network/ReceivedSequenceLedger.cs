using System;
using System.Collections.Generic;

namespace GameFactory.Diagnostics.Network;

/// <summary>In-memory high-water marks for idempotent remote log ingestion.</summary>
public sealed class ReceivedSequenceLedger
{
    private readonly Dictionary<string, long> _highest = [];

    public bool TryGetHighest(string runId, out long sequence) => _highest.TryGetValue(runId, out sequence);
    public long GetHighest(string runId) => _highest.GetValueOrDefault(runId);
    public void Clear() => _highest.Clear();

    /// <summary>Commits only an already-persisted contiguous sequence position.</summary>
    public void Commit(string runId, long sequence)
    {
        if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("A run ID is required.", nameof(runId));
        long current = GetHighest(runId);
        if (sequence < current) return;
        _highest[runId] = sequence;
    }
}
