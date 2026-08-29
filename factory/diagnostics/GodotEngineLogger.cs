using System;
using System.Collections.Generic;
using Godot;

namespace GameFactory.Diagnostics;

/// <summary>Captures Godot's logger stream without feeding it back into Godot output.</summary>
public sealed partial class GodotEngineLogger : Logger
{
    private const string GameLogMirrorPrefix = "[GF] ";
    private readonly LogRun _run;
    private readonly Action<EngineLogEntry> _importantEntry;

    public GodotEngineLogger(LogRun run, Action<EngineLogEntry> importantEntry)
    {
        _run = run;
        _importantEntry = importantEntry;
    }

    public override void _LogMessage(string message, bool error)
    {
        Write(new EngineLogEntry(DateTimeOffset.UtcNow, error ? LogLevel.Error : LogLevel.Info, message),
            isGameLogMirror: IsGameLogMirror(message));
    }

    public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        Logger.ErrorType type = (Logger.ErrorType)errorType;
        LogLevel level = type == Logger.ErrorType.Warning ? LogLevel.Warning : LogLevel.Error;
        var frames = new List<string>();
        foreach (ScriptBacktrace backtrace in scriptBacktraces)
            for (int index = 0; index < backtrace.GetFrameCount(); index++)
                frames.Add($"{backtrace.GetFrameFile(index)}:{backtrace.GetFrameLine(index)} {backtrace.GetFrameFunction(index)}");

        string message = string.IsNullOrWhiteSpace(rationale) ? code : rationale;
        Write(new EngineLogEntry(DateTimeOffset.UtcNow, level, message, function, file, line, code, rationale, type.ToString(), frames),
            isGameLogMirror: IsGameLogMirror(message) || IsGameLogMirror(code) || IsGameLogMirror(rationale));
    }

    private void Write(EngineLogEntry entry, bool isGameLogMirror)
    {
        try
        {
            _run.AppendEngine(entry);
            if (!isGameLogMirror && entry.Level is LogLevel.Warning or LogLevel.Error)
                _importantEntry(entry);
        }
        catch (ObjectDisposedException)
        {
            // Godot can emit a final logger callback while a process is shutting down.
        }
    }

    private static bool IsGameLogMirror(string? value) => value?.IndexOf(GameLogMirrorPrefix, StringComparison.Ordinal) >= 0;
}
