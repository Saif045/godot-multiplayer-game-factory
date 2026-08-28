using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace GameFactory.Diagnostics;

/// <summary>Owns one process-local JSONL diagnostics file and event sequence.</summary>
public sealed class LogRun : IDisposable
{
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly StreamWriter _writer;
    private readonly object _gate = new();
    private long _nextSequence;
    private bool _disposed;

    public string RunId { get; }
    public string FilePath { get; }
    public string? DiagnosticsSessionId { get; private set; }
    public event Action<LogEntry>? EntryWritten;

    public LogRun(string logRoot, string? runId = null)
    {
        if (string.IsNullOrWhiteSpace(logRoot))
            throw new ArgumentException("A log root is required.", nameof(logRoot));

        RunId = string.IsNullOrWhiteSpace(runId)
            ? Guid.NewGuid().ToString("N")[..8]
            : runId;
        string day = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string stamp = DateTimeOffset.UtcNow.ToString("HH-mm-ss.fff", CultureInfo.InvariantCulture);
        string directory = Path.Combine(logRoot, day);
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, $"{stamp}_{RunId}.jsonl");
        _writer = new StreamWriter(new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite));
    }

    public void AssociateSession(DiagnosticsSessionId sessionId) => DiagnosticsSessionId = sessionId.ToString();
    public void ClearSession() => DiagnosticsSessionId = null;

    public LogEntry Write(
        LogLevel level,
        string category,
        string eventName,
        string? message = null,
        IReadOnlyDictionary<string, string?>? fields = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("A category is required.", nameof(category));
        if (string.IsNullOrWhiteSpace(eventName)) throw new ArgumentException("An event name is required.", nameof(eventName));

        var entry = new LogEntry(
            DateTimeOffset.UtcNow,
            _elapsed.ElapsedMilliseconds,
            Interlocked.Increment(ref _nextSequence),
            RunId,
            level,
            category,
            eventName,
            message,
            fields ?? new Dictionary<string, string?>(),
            DiagnosticsSessionId);

        lock (_gate)
        {
            _writer.WriteLine(JsonSerializer.Serialize(entry));
            _writer.Flush();
        }

        EntryWritten?.Invoke(entry);
        return entry;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writer.Dispose();
    }
}
