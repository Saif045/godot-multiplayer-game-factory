using System;
using System.IO;
using GameFactory.Diagnostics;
using GameFactory.Networking.Peers;

namespace GameFactory.Diagnostics.Network;

/// <summary>Thread-safe concise host-side rendering of meaningful distributed events.</summary>
public sealed class SessionLogWriter : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    public SessionLogWriter(string filePath)
    {
        _writer = new StreamWriter(new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read));
    }

    public void Append(DateTimeOffset normalizedUtc, string role, PeerId peerId, LogEntry entry)
    {
        if (entry.Category is "diagnostics.relay" or "diagnostics.session") return;
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            _writer.WriteLine(SessionLogFormatter.Format(normalizedUtc, role, peerId, entry));
            _writer.Flush();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_gate) _writer.Dispose();
    }
}
