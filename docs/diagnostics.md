# Diagnostics

GameFactory diagnostics is a small distributed debugging facility, not a
general-purpose observability framework. Every process writes its own JSONL file
under `user://logs/<utc-date>/`; a unique run ID and per-run sequence make local
ordering unambiguous. Entries include UTC time, elapsed milliseconds, level,
category, event name, message, fields, and the diagnostics session once known.

`GameLog` preserves live Godot output: information uses `GD.Print`, warnings use
`GD.PushWarning`, and errors use `GD.PushError`. Godot engine output remains
independent and is not intercepted.

For an authoritative multiplayer session, `NetworkLogRelay` creates a separate
diagnostics session ID and host file at:

```text
user://logs/sessions/<diagnostics-session-id>/master.jsonl
```

Clients retain their own local logs and forward bounded batches over reliable
Godot RPC channel 7. Recent pre-session entries are seeded into a newly assigned
session so the join/setup story is retained. The host derives the sending peer
from `MultiplayerApi`, records host receive time and optional platform metadata,
rejects duplicates, and acknowledges accepted sequences. A client retains at
most 512 unacknowledged entries and sends at most 32 entries per batch every 100
milliseconds. If that bound drops unacknowledged entries, the next batch reports
the sequence gap and the host records it before advancing. Diagnostics forwarding
does not log its own transport activity.

The relay is transport/platform independent. Steam peer-to-user mapping is
optional enrichment supplied by the Steam sandbox; the diagnostics layer itself
does not reference Steam. The session ID is not a Steam lobby ID. Clock
synchronization and durable upload after process restart are intentionally
deferred.
