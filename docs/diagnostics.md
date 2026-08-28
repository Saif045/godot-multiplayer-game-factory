# Diagnostics

GameFactory diagnostics is a small distributed debugging facility, not a
general-purpose observability framework. Every process writes its own JSONL file
beside the running executable when that location is writable:

```text
<exe-dir>/logs/
  runs/<utc-date>_<utc-time>_<run-id>.jsonl
  sessions/<diagnostics-session-id>/master.jsonl
```

If the executable directory cannot be written, `GameLog` warns and falls back to
`user://logs`. A unique run ID and per-run sequence make local ordering
unambiguous. Entries include UTC time, elapsed milliseconds, level, category,
event name, message, fields, and the diagnostics session once known.

`GameLog` preserves live Godot output: information uses `GD.Print`, warnings use
`GD.PushWarning`, and errors use `GD.PushError`. Godot engine output remains
independent and is not intercepted.

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

When the host assigns a diagnostics session, it includes its current UTC time.
The client estimates a host-clock offset from that message and includes it with
each batch. `master.jsonl` retains the source `entry.Utc`, source elapsed time,
and source sequence, and adds both `host_received_utc` and `normalized_utc`.
This is a lightweight debugging estimate rather than NTP-grade synchronization.
Durable upload after process restart remains intentionally deferred.

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
