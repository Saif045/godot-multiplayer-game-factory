using GameFactory.Diagnostics.Replication;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

namespace GameFactory.Tests;

public sealed class ReplicationConfirmationTrackerTests
{
    private static readonly NetworkObjectId Door = new(2);
    private static readonly PeerId First = new(42);
    private static readonly PeerId Second = new(81);

    [Fact]
    public void One_expected_peer_confirms_and_completes()
    {
        var tracker = new ReplicationConfirmationTracker();
        tracker.Begin(Door, 1, [First], 100);

        ReplicationConfirmationEvent result = tracker.Confirm(Door, 1, First, 137);

        Assert.Equal(ReplicationConfirmationEventKind.Confirmed, result.Kind);
        Assert.True(result.Snapshot!.IsComplete);
        Assert.Equal(37, result.LatencyMilliseconds);
    }

    [Fact]
    public void Several_peers_complete_only_after_each_confirms()
    {
        var tracker = new ReplicationConfirmationTracker();
        tracker.Begin(Door, 1, [First, Second], 0);

        tracker.Confirm(Door, 1, First, 10);
        ReplicationConfirmationEvent second = tracker.Confirm(Door, 1, Second, 20);

        Assert.True(second.Snapshot!.IsComplete);
        Assert.Equal(2, second.Snapshot.ConfirmedLatencyMilliseconds.Count);
    }

    [Fact]
    public void Duplicate_and_unexpected_acks_are_harmless()
    {
        var tracker = new ReplicationConfirmationTracker();
        tracker.Begin(Door, 1, [First], 0);
        tracker.Confirm(Door, 1, First, 10);

        Assert.Equal(ReplicationConfirmationEventKind.Duplicate, tracker.Confirm(Door, 1, First, 20).Kind);
        Assert.Equal(ReplicationConfirmationEventKind.Ignored, tracker.Confirm(Door, 1, Second, 20).Kind);
        Assert.Single(tracker.Snapshots.Single().ConfirmedLatencyMilliseconds);
    }

    [Fact]
    public void Timeout_reports_missing_peers_and_late_ack_is_marked()
    {
        var tracker = new ReplicationConfirmationTracker();
        tracker.Begin(Door, 1, [First], 0);

        ReplicationConfirmationEvent timeout = Assert.Single(tracker.Expire(1500, 1500));
        ReplicationConfirmationEvent late = tracker.Confirm(Door, 1, First, 38125);

        Assert.Equal(ReplicationConfirmationEventKind.TimedOut, timeout.Kind);
        Assert.Equal([First], timeout.MissingPeers);
        Assert.Equal(ReplicationConfirmationEventKind.LateConfirmed, late.Kind);
        Assert.Equal(38125, late.LatencyMilliseconds);
    }

    [Fact]
    public void Later_peers_do_not_block_an_older_revision()
    {
        var tracker = new ReplicationConfirmationTracker();
        tracker.Begin(Door, 1, [First], 0);
        tracker.Begin(Door, 2, [First, Second], 10);

        tracker.Confirm(Door, 1, First, 20);
        tracker.Confirm(Door, 2, First, 30);
        tracker.Confirm(Door, 2, Second, 40);

        Assert.True(tracker.Snapshots.Single(snapshot => snapshot.Revision == 1).IsComplete);
        Assert.True(tracker.Snapshots.Single(snapshot => snapshot.Revision == 2).IsComplete);
    }

    [Fact]
    public void Late_join_expectation_for_current_revision_is_confirmable()
    {
        var tracker = new ReplicationConfirmationTracker();
        tracker.Begin(Door, 7, [], 0);

        tracker.Expect(Door, 7, First, 10, "late_join");
        ReplicationConfirmationEvent result = tracker.Confirm(Door, 7, First, 30);

        Assert.Equal(ReplicationConfirmationEventKind.Confirmed, result.Kind);
        Assert.True(result.Snapshot!.IsComplete);
    }

    [Fact]
    public void Revision_zero_can_be_started_for_a_late_joiner_without_a_mutation()
    {
        var tracker = new ReplicationConfirmationTracker();

        ReplicationConfirmationSnapshot started = tracker.Begin(Door, 0, [First], 10, "late_join");
        ReplicationConfirmationEvent result = tracker.Confirm(Door, 0, First, 40);

        Assert.False(started.IsComplete);
        Assert.Equal(ReplicationConfirmationEventKind.Confirmed, result.Kind);
        Assert.True(result.Snapshot!.IsComplete);
    }

    [Fact]
    public void Late_join_to_an_old_revision_uses_its_own_expectation_time()
    {
        var tracker = new ReplicationConfirmationTracker();
        tracker.Begin(Door, 7, [First], 1000);
        tracker.Confirm(Door, 7, First, 1040);
        tracker.Expect(Door, 7, Second, 120000, "late_join");

        Assert.Empty(tracker.Expire(120001, 1500));
        ReplicationConfirmationEvent result = tracker.Confirm(Door, 7, Second, 120035);

        Assert.Equal(ReplicationConfirmationEventKind.Confirmed, result.Kind);
        Assert.Equal(35, result.LatencyMilliseconds);
        Assert.Equal(120000, result.Snapshot!.Expectations[Second].ExpectedAtElapsedMilliseconds);
    }

    [Fact]
    public void Existing_peer_timeout_is_not_delayed_by_a_late_join()
    {
        var tracker = new ReplicationConfirmationTracker();
        tracker.Begin(Door, 7, [First], 1000);
        tracker.Expect(Door, 7, Second, 120000, "late_join");

        ReplicationConfirmationEvent timeout = Assert.Single(tracker.Expire(120000, 1500));

        Assert.Equal(First, timeout.PeerId);
        Assert.Equal(119000, timeout.ElapsedMilliseconds);
        Assert.Empty(tracker.Expire(120001, 1500));
    }

    [Fact]
    public void Peers_on_the_same_revision_can_have_different_expectation_starts()
    {
        var tracker = new ReplicationConfirmationTracker();
        tracker.Begin(Door, 7, [First], 1000);
        tracker.Expect(Door, 7, Second, 5000, "late_join");

        ReplicationConfirmationEvent first = tracker.Confirm(Door, 7, First, 1040);
        ReplicationConfirmationEvent second = tracker.Confirm(Door, 7, Second, 5060);

        Assert.Equal(40, first.LatencyMilliseconds);
        Assert.Equal(60, second.LatencyMilliseconds);
    }

    [Fact]
    public void Zero_remote_peers_is_immediately_complete()
    {
        var tracker = new ReplicationConfirmationTracker();

        ReplicationConfirmationSnapshot snapshot = tracker.Begin(Door, 1, [], 0);

        Assert.True(snapshot.IsComplete);
    }
}
