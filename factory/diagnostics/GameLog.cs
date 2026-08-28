using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GameFactory.Diagnostics;

/// <summary>Small GameFactory diagnostics facade: JSONL plus Godot console output.</summary>
public static class GameLog
{
    private static readonly object Gate = new();
    private static LogRun? _run;

    public static string RunId => Run.RunId;
    public static string LocalFilePath => Run.FilePath;
    public static event Action<LogEntry>? EntryWritten;

    public static void AssociateSession(DiagnosticsSessionId sessionId) => Run.AssociateSession(sessionId);
    public static void ClearSession() => Run.ClearSession();

    public static LogEntry Info(string category, string eventName, string? message = null, IReadOnlyDictionary<string, string?>? fields = null)
        => Write(LogLevel.Info, category, eventName, message, fields);

    public static LogEntry Warning(string category, string eventName, string? message = null, IReadOnlyDictionary<string, string?>? fields = null)
        => Write(LogLevel.Warning, category, eventName, message, fields);

    public static LogEntry Error(string category, string eventName, string? message = null, IReadOnlyDictionary<string, string?>? fields = null)
        => Write(LogLevel.Error, category, eventName, message, fields);

    private static LogEntry Write(LogLevel level, string category, string eventName, string? message, IReadOnlyDictionary<string, string?>? fields)
    {
        LogEntry entry = Run.Write(level, category, eventName, message, fields);
        string fieldText = string.Join(" ", entry.Fields.Select(field => $"{field.Key}={field.Value}"));
        string line = $"{entry.Utc:HH:mm:ss.fff}Z [{entry.Category}] {entry.Event}" +
            (string.IsNullOrWhiteSpace(entry.Message) ? string.Empty : $" {entry.Message}") +
            (string.IsNullOrWhiteSpace(fieldText) ? string.Empty : $" {fieldText}");
        switch (level)
        {
            case LogLevel.Warning: GD.PushWarning(line); break;
            case LogLevel.Error: GD.PushError(line); break;
            default: GD.Print(line); break;
        }

        EntryWritten?.Invoke(entry);
        return entry;
    }

    private static LogRun Run
    {
        get
        {
            lock (Gate)
            {
                if (_run is not null) return _run;
                string root = ProjectSettings.GlobalizePath("user://logs");
                _run = new LogRun(root);
                return _run;
            }
        }
    }
}
