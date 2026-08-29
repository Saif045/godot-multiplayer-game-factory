# Diagnostics

GameFactory diagnostics is a small distributed debugging facility, not a
general-purpose observability framework. Every process writes its own local
evidence beside the running executable when that location is writable:

```text
<exe-dir>/logs/
  runs/<utc-date>_<utc-time>_<run-id>/
    game.jsonl
    engine.log
  sessions/<diagnostics-session-id>/
    session.log
    master.jsonl
    manifest.json
```

If the executable directory cannot be written, `GameLog` warns and falls back to
`user://logs`. A unique run ID and per-run sequence make local ordering
unambiguous. `game.jsonl` contains GameFactory structured events: UTC time,
elapsed milliseconds, level, category, event name, message, fields, and the
diagnostics session once known. `engine.log` contains the local Godot logger
stream, including ordinary engine messages plus warning/error source and
backtrace information where Godot supplies it. Logger callbacks are local-first
and thread-safe; they do not call Godot printing APIs, avoiding logging loops.
Like all in-process logger registration, it cannot recover messages emitted
before GameFactory's launcher has initialized diagnostics.

`GameLog` preserves live Godot output: information uses `GD.Print`, warnings use
`GD.PushWarning`, and errors use `GD.PushError`. Its lines are tagged so the
engine logger keeps the raw local mirror without forwarding a duplicate into a
session timeline. Engine-originated warnings and errors are converted into
structured `engine.warning` / `engine.error` entries, while ordinary engine
messages remain local in `engine.log`.

For an authoritative multiplayer session, `NetworkLogRelay` creates a separate
diagnostics session ID and host master file in the `sessions` location above.

Clients retain their own local logs and forward bounded batches over reliable
Godot RPC transfer channel 2. Godot maps nonzero high-level transfer channels
onto underlying peer channels after its reserved system channels: transfer
channel 0 uses Godot's default/system behavior, channel 1 is the first custom
channel, and channel 2 is the second custom channel. With the current
four-channel GodotSteam peer (physical channels 0–3), diagnostics channel 2 maps
to physical channel 3; transfer channel 3 would exceed that range and fall back
to channel 0. Recent pre-session entries are seeded into a newly assigned
session so the join/setup story is retained. The host derives the sending peer
from `MultiplayerApi`, records host receive time and optional platform metadata,
rejects duplicates, and acknowledges accepted sequences. A client retains at
most 512 unacknowledged entries and sends at most 32 entries per batch every 100
milliseconds. If that bound drops unacknowledged entries, the next batch reports
the sequence gap and the host records it before advancing. Diagnostics forwarding
does not log its own transport activity.

The relay is transport/platform independent. Steam peer-to-user mapping is
optional enrichment supplied by the Steam sandbox; the diagnostics layer itself
does not reference Steam. The session ID is not a Steam lobby ID.

When the host assigns a diagnostics session, it includes its current UTC time and
the client records its current monotonic elapsed milliseconds as the matching
source anchor. Each batch carries those anchors. `master.jsonl` retains the
source `entry.Utc`, source elapsed time, and source sequence, and adds both
`host_received_utc` and `normalized_utc`. The normalized time is derived from
the host UTC anchor plus the entry's elapsed-time delta, including backwards
extrapolation for pre-session backlog. It does not depend on source wall-clock
changes after assignment. This is a debugging timeline, not NTP-grade
synchronization. Durable upload after process restart remains intentionally
deferred.

Session assignment is client-initiated: after Godot reports
`ConnectedToServer`, the client defers a reliable diagnostics-session request by
one process tick. The host derives the requesting peer from `MultiplayerApi` and
responds only while an authoritative diagnostics session exists. This avoids
sending application RPC data while a transport is still completing its own
connection handshake. Repeated requests are harmless because assigning the same
session is idempotent.

The host is the sole writer of its `master.jsonl`. Every host or remote entry,
including recorded sequence gaps, goes through one locked append path. The file
allows concurrent readers but not a second writer, and each append serializes,
writes one JSON object plus its newline, and flushes before returning.

`session.log` is the primary human view: it is atomically materialized in
normalized chronological order (then stable source and source-sequence ties)
from merged host and client INFO/WARNING/ERROR events, with short source labels
(`H` and `C:<peer-id>`). It includes important engine warnings/errors and
explicit relay-gap warnings, but not duplicate GameLog console mirrors.
`master.jsonl` is the complete append-only machine-readable source;
`manifest.json` maps the observed host/client run IDs, peer IDs, and available
Steam IDs. Debugging normally starts with `session.log`, drills into
`master.jsonl` for exact structured data, and uses a participant's `engine.log`
for native or client-specific evidence.

## Replication acceptance confirmation

The Steam gameplay acceptance probe can attach a pure
`ReplicationConfirmationTracker` to selected replicated state. The host records
the peers present for an authoritative revision; each expectation carries its
own start time, so a later joining peer is measured from its join expectation,
not from the original revision. A non-authoritative client acknowledges after it
applies that revision. The tracker exposes in-memory
read-only snapshots and change events alongside `GameLog.EntryWritten` and
`NetworkWorld.Objects`, so a future debug overlay can inspect live state without
tailing files. No overlay is implemented yet.

This is an acceptance diagnostic, not a gameplay-delivery guarantee.
`MultiplayerSynchronizer` replicates state and may legitimately converge through
rapid intermediate values to the latest state. Gameplay that must process every
intermediate action belongs on an appropriate reliable command/event/RPC path.

Before a Steam peer is removed, the sandbox asks the relay for one best-effort
client flush after recording the peer-closing event. It is deliberately not a
guaranteed-delivery protocol: the local `game.jsonl` and `engine.log` remain
authoritative if the transport is already unavailable. After peer removal the
relay becomes inactive/local-only and never attempts to log through a missing
multiplayer peer.
