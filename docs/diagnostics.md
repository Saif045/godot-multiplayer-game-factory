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
Godot RPC channel 7. The host derives the sending peer from `MultiplayerApi`,
records host receive time and source metadata, rejects duplicate source
sequences, and acknowledges the highest accepted sequence. A client retains at
most 512 unacknowledged entries and sends at most 32 entries per batch every 100
milliseconds. Diagnostics forwarding does not log its own transport activity.

The relay is transport/platform independent. Steam peer-to-user mapping is
optional enrichment supplied by the Steam sandbox; the session ID is not a
Steam lobby ID. Clock synchronization and durable upload after process restart
are intentionally deferred.
