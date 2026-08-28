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
        Assert.Equal(Path.Combine(_root, "runs"), Path.GetDirectoryName(run.FilePath));
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

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
