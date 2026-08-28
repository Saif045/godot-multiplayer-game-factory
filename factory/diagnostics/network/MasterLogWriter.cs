using System;
using System.IO;
using System.Text.Json;

namespace GameFactory.Diagnostics.Network;

/// <summary>Serializes authoritative master-log writes into valid JSONL.</summary>
public sealed class MasterLogWriter : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    public MasterLogWriter(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("A master-log file path is required.", nameof(filePath));

        _writer = new StreamWriter(new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read));
    }

    public void Append(object value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string line = JsonSerializer.Serialize(value);
        lock (_gate)
        {
            _writer.WriteLine(line);
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
