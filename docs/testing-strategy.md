# Testing Strategy

## Current automated baseline

`tests/GameFactory.Tests/` is a separate xUnit project. Its engine-independent tests cover `PeerId`, `NetworkPeer`, `PeerRegistry`, and `NetworkSession` using deterministic `FakeNetworkTransport`. They cover lifecycle results, transitions, cleanup, disposal, borrowed-transport ownership, stale events, and startup reentrancy. They do not launch Godot, create a scene tree, open sockets, or validate ENet.

The tests do not cover `NetworkObject`, `NetworkObjectComponent`, authority, replication, Godot scene loading, or cross-process behavior. There is no CI workflow, Godot integration-test harness, or multiprocess scenario runner.

## Evidence layers

1. **Engine-independent unit tests** cover value types and policy that do not require Godot.
2. **Godot integration tests** are planned for scene lifecycle, component placement/initialization, synchronizer configuration, authority, and ENet adapter behavior.
3. **Multiprocess scenarios** are planned for multiple roles, connection loss, spawn/despawn, replication, late joining, timeouts, and child-process cleanup.
4. **Manual probes** remain useful for exploration and diagnostics, but cannot be described as regression tests.

## Replication evidence and next coverage

The replication sandbox was manually exercised for normal state change and late joining. That establishes bounded exploratory evidence only. Automated Godot tests should eventually cover:

- direct-child validation and two-phase component registration/initialization;
- capability lookup, duplicate capability diagnostics, and replaceable component scenes;
- `[Replicated]` defaults (`OnChange`, spawn enabled) and use without `[Export]`;
- `Never`, `Always`, and `OnChange` mapping;
- host/synchronizer root targeting and lifecycle cleanup;
- authority/non-authority mutation, initial/delta state, spawn/despawn, and late joining.

Manual configuration, custom replacement paths, and `NetworkWorld` are not implemented contracts and should not be tested as though they exist.

## Test qualities

Tests should be deterministic, explicit about process role and authority, repeatable without leaked handlers or child processes, independent of editor paths and public services, and honest about missing environments or evidence. A green local run is not CI evidence.
