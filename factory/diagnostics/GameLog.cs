using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace GameFactory.Diagnostics;

/// <summary>Small GameFactory diagnostics facade: JSONL plus Godot console output.</summary>
public static class GameLog
{
    private static readonly object Gate = new();
    private static LogRun? _run;
    private static GodotEngineLogger? _engineLogger;

    public static string RunId => Run.RunId;
    public static long ElapsedMilliseconds => Run.ElapsedMilliseconds;
    public static string LogRoot => Run.LogRoot;
    public static string RunDirectory => Run.RunDirectory;
    public static string LocalFilePath => Run.FilePath;
    public static string EngineFilePath => Run.EngineFilePath;
    public static event Action<LogEntry>? EntryWritten;

    public static void AssociateSession(DiagnosticsSessionId sessionId) => Run.AssociateSession(sessionId);
    public static void ClearSession() => Run.ClearSession();
    public static void EnsureInitialized() => EnsureEngineLogger();

    public static LogEntry Info(string category, string eventName, string? message = null, IReadOnlyDictionary<string, string?>? fields = null)
        => Write(LogLevel.Info, category, eventName, message, fields);

    public static LogEntry Warning(string category, string eventName, string? message = null, IReadOnlyDictionary<string, string?>? fields = null)
        => Write(LogLevel.Warning, category, eventName, message, fields);

    public static LogEntry Error(string category, string eventName, string? message = null, IReadOnlyDictionary<string, string?>? fields = null)
        => Write(LogLevel.Error, category, eventName, message, fields);

    private static LogEntry Write(LogLevel level, string category, string eventName, string? message, IReadOnlyDictionary<string, string?>? fields)
    {
        EnsureEngineLogger();
        LogEntry entry = Run.Write(level, category, eventName, message, fields);
        string fieldText = string.Join(" ", entry.Fields.Select(field => $"{field.Key}={field.Value}"));
        string line = $"[GF] {entry.Utc:HH:mm:ss.fff}Z [{entry.Category}] {entry.Event}" +
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

    private static void WriteEngine(EngineLogEntry engine)
    {
        LogEntry entry = Run.Write(engine.Level, "engine", engine.Level == LogLevel.Warning ? "warning" : "error", engine.Message, engine.ToGameLogFields());
        EntryWritten?.Invoke(entry);
    }

    private static void EnsureEngineLogger()
    {
        lock (Gate)
        {
            if (_engineLogger is not null) return;
            LogRun run = Run;
            _engineLogger = new GodotEngineLogger(run, WriteEngine);
            OS.AddLogger(_engineLogger);
        }
    }

    private static LogRun Run
    {
        get
        {
            lock (Gate)
            {
                if (_run is not null) return _run;
                _run = CreateRun(out string? fallbackWarning);
                if (fallbackWarning is not null)
                    Warning("diagnostics.log", "fallback_to_user_directory", fallbackWarning);
                return _run;
            }
        }
    }

    private static LogRun CreateRun(out string? fallbackWarning)
    {
        fallbackWarning = null;
        string fallbackRoot = ProjectSettings.GlobalizePath("user://logs");
        string executablePath = OS.GetExecutablePath();
        string? executableDirectory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(executableDirectory))
            return new LogRun(fallbackRoot);

        string preferredRoot = Path.Combine(executableDirectory, "logs");
        try
        {
            return new LogRun(preferredRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            fallbackWarning = $"Could not create a diagnostics log beside the executable; using user://logs instead. {exception.Message}";
            return new LogRun(fallbackRoot);
        }
    }
}
