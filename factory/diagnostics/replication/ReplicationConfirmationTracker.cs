using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

namespace GameFactory.Diagnostics.Replication;

/// <summary>
/// Pure acceptance diagnostics for proving that selected authoritative state revisions
/// reached the peers present at the time of that revision. It never controls gameplay.
/// </summary>
public sealed class ReplicationConfirmationTracker
{
    private readonly Dictionary<Key, PendingRevision> _revisions = [];

    public event Action<ReplicationConfirmationEvent>? Changed;

    public IReadOnlyCollection<ReplicationConfirmationSnapshot> Snapshots =>
        new ReadOnlyCollection<ReplicationConfirmationSnapshot>(_revisions.Values
            .Select(revision => revision.Snapshot())
            .OrderBy(snapshot => snapshot.ObjectId.Value)
            .ThenBy(snapshot => snapshot.Revision)
            .ToArray());

    public bool TryGetSnapshot(NetworkObjectId objectId, long revision, out ReplicationConfirmationSnapshot? snapshot)
    {
        if (_revisions.TryGetValue(new Key(objectId, revision), out PendingRevision? pending))
        {
            snapshot = pending.Snapshot();
            return true;
        }

        snapshot = null;
        return false;
    }

    public ReplicationConfirmationSnapshot Begin(NetworkObjectId objectId, long revision, IEnumerable<PeerId> expectedPeers, long elapsedMilliseconds, string reason = "mutation")
    {
        var key = new Key(objectId, revision);
        if (_revisions.ContainsKey(key))
            throw new InvalidOperationException($"Revision {revision} for network object {objectId} is already tracked.");

        var pending = new PendingRevision(objectId, revision, elapsedMilliseconds);
        foreach (PeerId peer in expectedPeers.Where(peer => !peer.IsServer).Distinct())
            pending.Expect(peer, reason);
        _revisions.Add(key, pending);
        ReplicationConfirmationSnapshot snapshot = pending.Snapshot();
        Changed?.Invoke(new ReplicationConfirmationEvent(ReplicationConfirmationEventKind.Began, snapshot));
        if (snapshot.IsComplete)
            Changed?.Invoke(new ReplicationConfirmationEvent(ReplicationConfirmationEventKind.Completed, snapshot));
        return snapshot;
    }

    public ReplicationConfirmationSnapshot Expect(NetworkObjectId objectId, long revision, PeerId peerId, long elapsedMilliseconds, string reason)
    {
        PendingRevision pending = Require(objectId, revision);
        if (peerId.IsServer) return pending.Snapshot();
        if (!pending.Expect(peerId, reason)) return pending.Snapshot();
        ReplicationConfirmationSnapshot snapshot = pending.Snapshot();
        Changed?.Invoke(new ReplicationConfirmationEvent(ReplicationConfirmationEventKind.Expected, snapshot, peerId, Reason: reason));
        return snapshot;
    }

    public ReplicationConfirmationEvent Confirm(NetworkObjectId objectId, long revision, PeerId peerId, long elapsedMilliseconds)
    {
        if (!_revisions.TryGetValue(new Key(objectId, revision), out PendingRevision? pending))
            return new ReplicationConfirmationEvent(ReplicationConfirmationEventKind.Ignored, null, peerId);
        if (!pending.IsExpected(peerId))
            return new ReplicationConfirmationEvent(ReplicationConfirmationEventKind.Ignored, pending.Snapshot(), peerId);
        if (pending.IsConfirmed(peerId))
            return new ReplicationConfirmationEvent(ReplicationConfirmationEventKind.Duplicate, pending.Snapshot(), peerId);

        long latency = elapsedMilliseconds - pending.StartedElapsedMilliseconds;
        bool late = pending.IsTimedOut(peerId);
        pending.Confirm(peerId, latency);
        ReplicationConfirmationSnapshot snapshot = pending.Snapshot();
        var result = new ReplicationConfirmationEvent(late ? ReplicationConfirmationEventKind.LateConfirmed : ReplicationConfirmationEventKind.Confirmed, snapshot, peerId, latency, pending.ReasonFor(peerId));
        Changed?.Invoke(result);
        if (snapshot.IsComplete)
            Changed?.Invoke(new ReplicationConfirmationEvent(ReplicationConfirmationEventKind.Completed, snapshot));
        return result;
    }

    public IReadOnlyList<ReplicationConfirmationEvent> Expire(long elapsedMilliseconds, long timeoutMilliseconds)
    {
        var results = new List<ReplicationConfirmationEvent>();
        foreach (PendingRevision pending in _revisions.Values)
        {
            if (elapsedMilliseconds - pending.StartedElapsedMilliseconds < timeoutMilliseconds) continue;
            PeerId[] missing = pending.MarkTimedOut();
            if (missing.Length == 0) continue;
            var result = new ReplicationConfirmationEvent(ReplicationConfirmationEventKind.TimedOut, pending.Snapshot(), Reason: pending.ReasonsFor(missing), MissingPeers: missing, ElapsedMilliseconds: elapsedMilliseconds - pending.StartedElapsedMilliseconds);
            results.Add(result);
            Changed?.Invoke(result);
        }
        return results;
    }

    private PendingRevision Require(NetworkObjectId objectId, long revision) =>
        _revisions.TryGetValue(new Key(objectId, revision), out PendingRevision? pending)
            ? pending
            : throw new InvalidOperationException($"Revision {revision} for network object {objectId} is not tracked.");

    private readonly record struct Key(NetworkObjectId ObjectId, long Revision);

    private sealed class PendingRevision
    {
        private readonly Dictionary<PeerId, string> _expected = [];
        private readonly Dictionary<PeerId, long> _confirmed = [];
        private readonly HashSet<PeerId> _timedOut = [];

        public PendingRevision(NetworkObjectId objectId, long revision, long startedElapsedMilliseconds)
        {
            ObjectId = objectId;
            Revision = revision;
            StartedElapsedMilliseconds = startedElapsedMilliseconds;
        }

        public NetworkObjectId ObjectId { get; }
        public long Revision { get; }
        public long StartedElapsedMilliseconds { get; }
        public bool Expect(PeerId peerId, string reason) => _expected.TryAdd(peerId, reason);
        public bool IsExpected(PeerId peerId) => _expected.ContainsKey(peerId);
        public bool IsConfirmed(PeerId peerId) => _confirmed.ContainsKey(peerId);
        public bool IsTimedOut(PeerId peerId) => _timedOut.Contains(peerId);
        public void Confirm(PeerId peerId, long latency) => _confirmed[peerId] = latency;
        public string? ReasonFor(PeerId peerId) => _expected.GetValueOrDefault(peerId);
        public string ReasonsFor(IEnumerable<PeerId> peers) => string.Join(",", peers.Select(ReasonFor).Where(reason => !string.IsNullOrWhiteSpace(reason)).Distinct());
        public PeerId[] MarkTimedOut()
        {
            PeerId[] missing = _expected.Keys.Where(peer => !_confirmed.ContainsKey(peer) && _timedOut.Add(peer)).ToArray();
            return missing;
        }
        public ReplicationConfirmationSnapshot Snapshot() => new(
            ObjectId, Revision, StartedElapsedMilliseconds,
            new ReadOnlyCollection<PeerId>(_expected.Keys.OrderBy(peer => peer.Value).ToArray()),
            new ReadOnlyDictionary<PeerId, long>(new Dictionary<PeerId, long>(_confirmed)),
            new ReadOnlyCollection<PeerId>(_timedOut.OrderBy(peer => peer.Value).ToArray()),
            _expected.Count == _confirmed.Count);
    }
}

public sealed record ReplicationConfirmationSnapshot(
    NetworkObjectId ObjectId,
    long Revision,
    long StartedElapsedMilliseconds,
    IReadOnlyCollection<PeerId> ExpectedPeers,
    IReadOnlyDictionary<PeerId, long> ConfirmedLatencyMilliseconds,
    IReadOnlyCollection<PeerId> TimedOutPeers,
    bool IsComplete);

public enum ReplicationConfirmationEventKind
{
    Began,
    Expected,
    Confirmed,
    LateConfirmed,
    Completed,
    TimedOut,
    Duplicate,
    Ignored
}

public sealed record ReplicationConfirmationEvent(
    ReplicationConfirmationEventKind Kind,
    ReplicationConfirmationSnapshot? Snapshot,
    PeerId? PeerId = null,
    long? LatencyMilliseconds = null,
    string? Reason = null,
    IReadOnlyCollection<PeerId>? MissingPeers = null,
    long? ElapsedMilliseconds = null);
