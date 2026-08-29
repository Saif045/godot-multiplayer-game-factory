# Testing Strategy

## Current automated baseline

`tests/GameFactory.Tests/` is a separate xUnit project. Its engine-independent tests cover peer/player value types and registries, `PlayerLifecycle` role policy, `NetworkObjectId`, spawn-group values, diagnostics records and writers, replication-confirmation tracking, and distributed-log batching/sequence handling. They do not launch Godot, create a scene tree, open a Steam connection, or validate GodotSteam.

There is no CI workflow, Godot integration-test harness, or multiprocess scenario runner. `NetworkWorld`, scene/component lifecycle, `MultiplayerSynchronizer`, resource-UID resolution, Steam overlay behavior, and cross-process behavior therefore require manual evidence today.

The Maaack shell has a headless Godot startup smoke for addon/script/autoload/scene-load failures. Visual UX remains manual acceptance: menu focus/navigation, settings persistence, keyboard/mouse/controller remapping and reset persistence, loading transition, pause/resume, first-click leave-to-menu, and host/leave/re-host without a Steam reinitialization must be checked in a normal exported run. Maaack's physical-key display support requires a normal display server, so loading its Controls scene headlessly can emit expected display-server warnings while input labels are formatted.

## Evidence layers

1. **Engine-independent unit tests** cover deterministic values, policy, and diagnostic logic.
2. **Manual Steam dependency smoke** verifies vendored `SteamMultiplayerPeer` can host, close, and host again in one process.
3. **Manual two-account Steam gameplay acceptance** verifies the actual online vertical slice: lobby join/leave, peers, player lifecycle, dynamic spawn/despawn, late join, server-authoritative door mutation, replication acknowledgement, and distributed diagnostics.
4. **Future Godot integration and multiprocess tests** should automate scene lifecycle, authority, replication, teardown, and role behavior once a concrete repeatable harness is selected.

Diagnostics writes `game.jsonl` and `engine.log` per run. An authoritative session additionally materializes `session.log`, `master.jsonl`, and `manifest.json`; focused unit tests cover their pure/file-layout behavior. `NetworkLogRelay` is bounded best-effort telemetry, not durable offline upload.

New coverage should follow coherent playable slices rather than speculative helpers. Tests must be deterministic, explicit about authority and runtime role, and honest about external environment requirements.
