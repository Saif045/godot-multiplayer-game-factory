using GameFactory.Diagnostics;
using GameFactory.Diagnostics.Network;

namespace GameFactory.Tests;

public sealed class RelayBacklogTests
{
    [Fact]
    public void Begin_session_forwards_bounded_pre_session_entries()
    {
        var backlog = new RelayBacklog();
        backlog.Record(Entry(1));
        backlog.Record(Entry(2));
        DiagnosticsSessionId session = DiagnosticsSessionId.New();

        backlog.BeginSession(session);
        LogBatch batch = Assert.IsType<LogBatch>(backlog.CreateBatch("run", 32));

        Assert.Equal(session.ToString(), batch.DiagnosticsSessionId);
        Assert.Equal([1L, 2L], batch.Entries.Select(entry => entry.Sequence));
        Assert.All(batch.Entries, entry => Assert.Equal(session.ToString(), entry.DiagnosticsSessionId));
    }

    [Fact]
    public void Overflow_reports_gap_without_discarding_later_entries()
    {
        var backlog = new RelayBacklog();
        backlog.BeginSession(DiagnosticsSessionId.New());
        for (long sequence = 1; sequence <= 513; sequence++) backlog.Record(Entry(sequence));

        LogBatch batch = Assert.IsType<LogBatch>(backlog.CreateBatch("run", 32));

        Assert.Equal(1, batch.DroppedThroughSequence);
        Assert.Equal(2, batch.Entries[0].Sequence);
    }

    [Fact]
    public void End_session_discards_old_backlog_before_next_assignment()
    {
        var backlog = new RelayBacklog();
        backlog.BeginSession(DiagnosticsSessionId.New());
        backlog.Record(Entry(1));
        backlog.EndSession();
        backlog.Record(Entry(2));
        DiagnosticsSessionId next = DiagnosticsSessionId.New();

        backlog.BeginSession(next);
        LogBatch batch = Assert.IsType<LogBatch>(backlog.CreateBatch("run", 32));

        Assert.Equal([2L], batch.Entries.Select(entry => entry.Sequence));
        Assert.Equal(next.ToString(), batch.DiagnosticsSessionId);
    }

    [Fact]
    public void Batch_carries_timeline_anchors_with_its_entries()
    {
        var backlog = new RelayBacklog();
        backlog.BeginSession(DiagnosticsSessionId.New());
        backlog.Record(Entry(1));

        LogBatch batch = Assert.IsType<LogBatch>(backlog.CreateBatch("run", 32, 1234, 5678));

        Assert.Equal(1234, batch.HostUtcAnchorUnixMilliseconds);
        Assert.Equal(5678, batch.SourceElapsedAnchorMilliseconds);
    }

    private static LogEntry Entry(long sequence) => new(
        DateTimeOffset.UnixEpoch,
        sequence,
        sequence,
        "run",
        LogLevel.Info,
        "test",
        "entry",
        null,
        new Dictionary<string, string?>(),
        null);
}
