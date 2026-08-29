using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameFactory.Diagnostics;
using GameFactory.Networking.Peers;

namespace GameFactory.Diagnostics.Network;

/// <summary>Thread-safe concise host-side rendering of meaningful distributed events.</summary>
public sealed class SessionLogWriter : IDisposable
{
    private readonly object _gate = new();
    private readonly string _filePath;
    private readonly List<Record> _records = [];
    private bool _disposed;

    public SessionLogWriter(string filePath)
    {
        _filePath = filePath;
    }

    public void Append(DateTimeOffset normalizedUtc, string role, PeerId peerId, LogEntry entry)
    {
        if (entry.Category is "diagnostics.relay" or "diagnostics.session") return;
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            _records.Add(new EventRecord(normalizedUtc, role, peerId, entry));
            Rewrite();
        }
    }

    /// <summary>Records a known relay gap without pretending it is a source LogEntry.</summary>
    public void AppendGap(DateTimeOffset normalizedUtc, PeerId peerId, string runId, long firstMissing, long droppedThrough)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            _records.Add(new GapRecord(normalizedUtc, peerId, runId, firstMissing, droppedThrough));
            Rewrite();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }

    private void Rewrite()
    {
        string temporary = _filePath + ".tmp";
        try
        {
            File.WriteAllLines(temporary, _records
                .OrderBy(record => record.Utc)
                .ThenBy(record => record.Role, StringComparer.Ordinal)
                .ThenBy(record => record.PeerId.Value)
                .ThenBy(record => record.Sequence)
                .Select(record => record.Format()));
            File.Move(temporary, _filePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // session.log is a replaceable materialized view. Retain records and retry later.
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A temporary lock only delays the next best-effort render.
            }
        }
    }

    private abstract record Record(DateTimeOffset Utc, string Role, PeerId PeerId, long Sequence)
    {
        public abstract string Format();
    }

    private sealed record EventRecord(DateTimeOffset Utc, string Role, PeerId PeerId, LogEntry Entry)
        : Record(Utc, Role, PeerId, Entry.Sequence)
    {
        public override string Format() => SessionLogFormatter.Format(Utc, Role, PeerId, Entry);
    }

    private sealed record GapRecord(DateTimeOffset Utc, PeerId PeerId, string RunId, long FirstMissing, long DroppedThrough)
        : Record(Utc, "client", PeerId, FirstMissing)
    {
        public override string Format() => SessionLogFormatter.FormatGap(Utc, PeerId, RunId, FirstMissing, DroppedThrough);
    }
}
