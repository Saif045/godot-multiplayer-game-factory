using System.Text.Json;
using GameFactory.Diagnostics;

namespace GameFactory.Tests;

public sealed class LogRunTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"gamefactory-log-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Write_creates_unique_jsonl_entry_with_run_metadata()
    {
        using var run = new LogRun(_root, "testrun");
        LogEntry entry = run.Write(LogLevel.Info, "network.peer", "connected", "peer=2", new Dictionary<string, string?> { ["peer_id"] = "2" });

        using var reader = new StreamReader(new FileStream(run.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        string line = Assert.Single(reader.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        using JsonDocument json = JsonDocument.Parse(line);
        Assert.Equal("testrun", json.RootElement.GetProperty("RunId").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("Sequence").GetInt64());
        Assert.Equal("network.peer", json.RootElement.GetProperty("Category").GetString());
        Assert.Equal("2", json.RootElement.GetProperty("Fields").GetProperty("peer_id").GetString());
        Assert.Equal(entry.RunId, run.RunId);
        Assert.Equal(Path.Combine(_root, "runs"), Path.GetDirectoryName(run.RunDirectory));
        Assert.Equal(Path.Combine(run.RunDirectory, "game.jsonl"), run.FilePath);
        Assert.Equal(Path.Combine(run.RunDirectory, "engine.log"), run.EngineFilePath);
    }

    [Fact]
    public void Associate_session_is_included_in_later_entries()
    {
        using var run = new LogRun(_root, "testrun");
        DiagnosticsSessionId session = DiagnosticsSessionId.New();
        run.AssociateSession(session);

        LogEntry entry = run.Write(LogLevel.Warning, "steam.lobby", "join_failed");

        Assert.Equal(session.ToString(), entry.DiagnosticsSessionId);
        Assert.Equal(1, entry.Sequence);
    }

    [Fact]
    public void Append_engine_writes_complete_local_evidence()
    {
        using var run = new LogRun(_root, "testrun");
        run.AppendEngine(new EngineLogEntry(DateTimeOffset.UnixEpoch, LogLevel.Error, "peer missing", "Tick", "relay.cs", 42, "code", "reason", "Error", ["relay.cs:42 Tick"]));

        using var reader = new StreamReader(new FileStream(run.EngineFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        string content = reader.ReadToEnd();
        Assert.Contains("peer missing", content);
        Assert.Contains("relay.cs:42 Tick", content);
    }

    [Fact]
    public async Task Concurrent_engine_appends_remain_complete()
    {
        using var run = new LogRun(_root, "testrun");
        Task[] writes = Enumerable.Range(0, 50)
            .Select(index => Task.Run(() => run.AppendEngine(new EngineLogEntry(DateTimeOffset.UtcNow, LogLevel.Warning, $"warning-{index}"))))
            .ToArray();
        await Task.WhenAll(writes);

        using var reader = new StreamReader(new FileStream(run.EngineFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        string content = reader.ReadToEnd();
        Assert.Equal(50, Enumerable.Range(0, 50).Count(index => content.Contains($"warning-{index}")));
    }

    [Fact]
    public void Engine_warning_conversion_preserves_source_and_backtrace()
    {
        var engine = new EngineLogEntry(DateTimeOffset.UtcNow, LogLevel.Warning, "warn", "Tick", "relay.cs", 42, Backtrace: ["relay.cs:42 Tick"]);

        IReadOnlyDictionary<string, string?> fields = engine.ToGameLogFields();

        Assert.Equal("relay.cs", fields["engine_file"]);
        Assert.Equal("42", fields["engine_line"]);
        Assert.Equal("relay.cs:42 Tick", fields["engine_backtrace"]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
