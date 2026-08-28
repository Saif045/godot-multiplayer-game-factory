# Testing Strategy

## Current automated baseline

`tests/GameFactory.Tests/` is a separate xUnit project. Its engine-independent tests cover `PeerId`, `NetworkPeer`, `PeerRegistry`, `PlayerId`, `PlayerRegistry`, `PlayerLifecycle`, `NetworkObjectId`, `NetworkSpawnGroupKind`, and `NetworkSession` using deterministic `FakeNetworkTransport`. They cover lifecycle results, transitions, cleanup, disposal, borrowed-transport ownership, stale events, startup reentrancy, positive ID validation, registry behavior, server/client player-lifecycle policy, and serialized spawn-group enum values. They do not launch Godot, create a scene tree, open sockets, or validate ENet.

The tests do not cover `NetworkWorld`, `NetworkSpawnGroup`, `NetworkObject`, `NetworkObjectComponent`, owner-peer or shared spawn-data payloads, authority, replication, Godot resource-UID resolution, Godot scene loading, or cross-process behavior. There is no CI workflow, Godot integration-test harness, or multiprocess scenario runner.

## Evidence layers

1. **Engine-independent unit tests** cover value types and policy that do not require Godot.
2. **Godot integration tests** are planned for scene lifecycle, component placement/initialization, synchronizer configuration, authority, and ENet adapter behavior.
3. **Multiprocess scenarios** are planned for multiple roles, connection loss, spawn/despawn, replication, late joining, timeouts, and child-process cleanup.
4. **Manual probes** remain useful for exploration and diagnostics, but cannot be described as regression tests.

Diagnostics writes one JSONL file per run under `<exe-dir>/logs/runs/`, falling
back to `user://logs/` when the executable directory is not writable.
`NetworkLogRelay` is exercised next through a real host/client session: it must
preserve each local file and append host plus remote client events to the host
session's `master.jsonl`, without a Godot RPC channel fallback warning. The relay
records source, host-receive, and host-normalized timestamps for remote events.
Its batching and acknowledgement behavior is deliberately bounded in memory; it
is not durable offline upload.

The Steam re-host smoke is a narrow exception: it is a permanent manual
engine-level dependency smoke, not an automated integration test. It verifies
the vendored `SteamMultiplayerPeer` can execute `create_host(0)`, `close()`,
then `create_host(0)` again in one process. The real Steam probe additionally
has a manual `H -> L -> H -> L -> H` acceptance flow. A two-account session,
connection, and replication run remains required before Steam is accepted as
end-to-end runtime evidence.

As broader co-op capabilities are added, coverage should follow coherent playable slices rather than isolated speculative helpers. A future player-lifecycle slice, for example, should exercise join, spawn, ownership association, disconnect cleanup, late joining where relevant, and both listen-server and dedicated-server behavior.

## Replication evidence and next coverage

The replication sandbox was manually exercised for normal state change and late joining. That establishes bounded exploratory evidence only. Automated Godot tests should eventually cover:

- direct-child validation and two-phase component registration/initialization;
- capability lookup, duplicate capability diagnostics, and replaceable component scenes;
- `[Replicated]` defaults (`OnChange`, spawn enabled) and use without `[Export]`;
- `Never`, `Always`, and `OnChange` mapping;
- host/synchronizer root targeting and lifecycle cleanup;
- authority/non-authority mutation, initial/delta state, spawn/despawn, and late joining.

Manual replacement paths and stable cross-project prefab definitions are not implemented contracts and should not be tested as though they exist. Godot integration coverage should eventually validate pre-tree shared spawn-data application, receiver validation, and server-only configuration ordering.

## Test qualities

Tests should be deterministic, explicit about process role and authority, repeatable without leaked handlers or child processes, independent of editor paths and public services, and honest about missing environments or evidence. A green local run is not CI evidence.
