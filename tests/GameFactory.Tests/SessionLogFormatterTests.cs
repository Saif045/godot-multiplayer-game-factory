using GameFactory.Diagnostics;
using GameFactory.Diagnostics.Network;
using GameFactory.Networking.Peers;

namespace GameFactory.Tests;

public sealed class SessionLogFormatterTests
{
    [Fact]
    public void Formats_host_and_client_sources_as_short_stable_labels()
    {
        Assert.Equal("H", SessionLogFormatter.SourceLabel("host", PeerId.Server));
        Assert.Equal("C:42", SessionLogFormatter.SourceLabel("client", new PeerId(42)));
    }

    [Fact]
    public void Formats_concise_human_line_with_message_and_fields()
    {
        var entry = new LogEntry(DateTimeOffset.UnixEpoch, 0, 1, "run", LogLevel.Warning, "engine", "warning", "peer missing\nretry", new Dictionary<string, string?> { ["peer_id"] = "42" }, null);

        string line = SessionLogFormatter.Format(DateTimeOffset.Parse("2026-08-29T06:04:44.486Z"), "client", new PeerId(42), entry);

        Assert.Contains("06:04:44.486", line);
        Assert.Contains("C:42", line);
        Assert.Contains("WARNING", line);
        Assert.Contains("engine.warning peer missing retry peer_id=42", line);
    }

    [Fact]
    public void Formats_visible_diagnostics_gap_without_a_fake_event_sequence()
    {
        string line = SessionLogFormatter.FormatGap(DateTimeOffset.Parse("2026-08-29T06:12:04.921Z"), new PeerId(42), "run", 81, 96);

        Assert.Contains("C:42", line);
        Assert.Contains("WARNING diagnostics.gap run=run missing=81-96", line);
    }
}
