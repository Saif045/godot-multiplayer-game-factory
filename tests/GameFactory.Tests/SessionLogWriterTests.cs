using GameFactory.Diagnostics;
using GameFactory.Diagnostics.Network;
using GameFactory.Networking.Peers;

namespace GameFactory.Tests;

public sealed class SessionLogWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"gamefactory-session-log-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Rewrites_the_human_view_in_normalized_chronological_order()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "session.log");
        using var writer = new SessionLogWriter(path);
        DateTimeOffset start = DateTimeOffset.Parse("2026-08-29T06:00:00Z");

        writer.Append(start.AddSeconds(5), "host", PeerId.Server, Entry("host", 1));
        writer.Append(start.AddSeconds(1), "client", new PeerId(42), Entry("client", 1));
        writer.Append(start.AddSeconds(3), "client", new PeerId(42), Entry("client", 2));

        string[] lines = File.ReadAllLines(path);
        Assert.Equal(3, lines.Length);
        Assert.Contains("06:00:01.000", lines[0]);
        Assert.Contains("06:00:03.000", lines[1]);
        Assert.Contains("06:00:05.000", lines[2]);
    }

    [Fact]
    public void Includes_relay_gaps_in_the_human_view()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "session.log");
        using var writer = new SessionLogWriter(path);

        writer.AppendGap(DateTimeOffset.Parse("2026-08-29T06:12:04.921Z"), new PeerId(42), "run", 81, 96);

        Assert.Contains("WARNING diagnostics.gap run=run missing=81-96", File.ReadAllText(path));
    }

    [Fact]
    public void Locked_destination_does_not_throw_and_later_append_recovers_the_complete_view()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "session.log");
        File.WriteAllText(path, "stale");
        using var writer = new SessionLogWriter(path);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            writer.Append(DateTimeOffset.UnixEpoch, "client", new PeerId(42), Entry("run", 1));
        }

        writer.Append(DateTimeOffset.UnixEpoch.AddSeconds(1), "client", new PeerId(42), Entry("run", 2));
        string[] lines = File.ReadAllLines(path);

        Assert.Equal(2, lines.Length);
        Assert.Contains("test.event", lines[0]);
        Assert.Contains("test.event", lines[1]);
        Assert.False(File.Exists(path + ".tmp"));
    }

    private static LogEntry Entry(string runId, long sequence) => new(
        DateTimeOffset.UnixEpoch, sequence, sequence, runId, LogLevel.Info, "test", "event", null,
        new Dictionary<string, string?>(), null);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
