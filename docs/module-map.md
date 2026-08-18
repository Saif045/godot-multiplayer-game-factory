# Module Map

This inventory describes Git-tracked paths and current responsibilities. Directories use lowercase or snake_case; C# filenames remain PascalCase.

## Repository configuration

| Path | Responsibility | Status |
|---|---|---|
| `.editorconfig` | UTF-8, LF, final-newline, and basic C# indentation conventions | Implemented |
| `.gitattributes` | LF normalization for text | Implemented |
| `.gitignore` | Ignores Godot cache, Android and .NET build output, and VS Code settings | Implemented |
| `GameFactory.csproj` | Godot .NET SDK, root namespace, and target-framework configuration | Implemented |
| `GameFactory.sln` | Solution containing the Godot project and engine-independent test project | Implemented |
| `project.godot` | Godot project settings and connection-probe main scene | Implemented |
| `icon.svg` and `icon.svg.import` | Project icon and Godot import metadata | Implemented asset/configuration |

The project file currently uses Godot.NET.Sdk 4.7.1, .NET 8, and a conditional .NET 9 target for Android. These are current settings, not compatibility or platform-support promises.

## `factory/core/`

This module owns process-level runtime role and has no direct Godot dependency.

| Type | Responsibility | Dependencies | Status |
|---|---|---|---|
| `RuntimeMode` | Names offline, client, listen-server, and dedicated-server roles | None | Implemented |
| `RuntimeContext` | Stores the current role and derives server/client/offline capability | `RuntimeMode` | Implemented |

Only assembly-internal methods mutate `RuntimeContext`; `NetworkSession` is its current coordinator.

## `factory/networking/core/`

| Type | Responsibility | Dependencies | Status |
|---|---|---|---|
| `PeerId` | Validated positive transport peer identifier; server is value `1` | .NET | Implemented |

Persistent player and runtime object identities do not exist yet.

## `factory/networking/peers/`

| Type | Responsibility | Dependencies | Status |
|---|---|---|---|
| `NetworkPeer` | Immutable peer ID and locality model | `PeerId` | Implemented |
| `PeerRegistry` | Process-local peer collection with add/remove events | `PeerId`, `NetworkPeer` | Implemented |

The registry rejects conflicting locality for a known ID but is not a player roster, authentication store, or persistence layer.

## `factory/networking/transport/`

| Type | Responsibility | Dependencies | Status |
|---|---|---|---|
| `TransportResult` | Success plus optional string error | .NET | Implemented |
| `INetworkTransport` | Transport lifecycle and normalized connection-event contract | `PeerId`, `IDisposable` | Implemented boundary |
| `ENetTransport` | Godot ENet implementation of the transport boundary | Godot multiplayer API | Implemented, only adapter |

The abstraction isolates session policy from ENet. An engine-independent fake implements the same contract for deterministic session tests; it is a test double rather than another production adapter.

## `factory/networking/sessions/`

| Type | Responsibility | Dependencies | Status |
|---|---|---|---|
| `HostMode` | Selects listen or dedicated host role | None | Implemented |
| `SessionState` | Names session lifecycle states | None | Implemented |
| `SessionEndReason` | Names current intentional and failure outcomes | None | Implemented |
| `SessionResult` | Success plus optional string error | .NET | Implemented |
| `NetworkSession` | Coordinates transport, runtime role, peers, validated transitions, cleanup, and transport-event subscriptions | Runtime, peers, transport contracts | Implemented foundation |

`NetworkSession` borrows its constructor-injected `INetworkTransport`. It calls `Close()` only to terminate active session use, but never calls `Dispose()`; the composition root owns transport disposal. Disposal of an offline or failed session does not issue an extra close. Session disposal is idempotent, unsubscribes all transport events, clears peers, resets the runtime, and rejects later public lifecycle operations. Cleanup attempts its applicable steps even when an earlier step throws. Operational cleanup failure leads to `Failed` with `LastEndReason` and `LastError`; disposal cleanup failure ends offline while retaining `CleanupFailed` and `LastError` for inspection. The only extra disposal transition beyond ordinary lifecycle paths is `Starting -> Stopping -> Offline`, for disposal reentrancy during synchronous startup.

## `factory/networking/objects/`

| File/type | Responsibility | Dependencies | Status |
|---|---|---|---|
| `ReplicatedAttribute.cs` / `ReplicationMode` | Marks host properties and chooses Never, Always, or OnChange plus spawn-state behavior | Reflection metadata | Experimental implementation |
| `NetworkObject.cs` | Reflects its parent and builds a Godot replication configuration | Godot, reflection | Experimental implementation |
| `network_object.tscn` | Node3D component scene with child `MultiplayerSynchronizer` | Godot scene system | Experimental implementation |

This module does not yet provide manual configuration mode, metadata caching, structured diagnostics, dimension-independent composition, network identity, or general spawning.

## `sandbox/connection/`

| File | Responsibility | Status |
|---|---|---|
| `NetworkProbe.cs` | Constructs the runtime/session/peer/ENet graph, selects a role from user arguments, and logs lifecycle events | Manual exploratory probe |
| `network_probe.tscn` | Node3D host for the probe; configured project main scene | Manual exploratory scene |

The probe uses UDP port 7000, up to eight clients, and loopback for client connections. It recognizes `--server`, `--dedicated-server`, and `--client`.

## `sandbox/replication/`

| File | Responsibility | Status |
|---|---|---|
| `ReplicationProbe.cs` | Duplicates session composition and performs authority-only sample spawn/despawn | Manual exploratory probe |
| `replication_probe.tscn` | Hosts spawn root and configured Godot `MultiplayerSpawner` | Manual exploratory scene; not main scene |
| `spawn_root.tscn` | Empty Node3D parent for spawned objects | Probe support |
| `objects/RawDoor.cs` | Demonstrates client request RPC and authority-side property change | Probe-specific example |
| `objects/raw_door.tscn` | Composes `RawDoor` with `NetworkObject` | Probe-specific example |

The sandbox demonstrates code paths but contains no assertions or automation. Any observations from earlier manual runs are historical conversation context, not newly verified results.

## `tests/GameFactory.Tests/`

| File/module | Responsibility | Status |
|---|---|---|
| `GameFactory.Tests.csproj` | .NET 8 xUnit test project referencing `GameFactory.csproj` | Implemented |
| `TestDoubles/FakeNetworkTransport.cs` | Deterministic `INetworkTransport` results, call tracking, local identity, and event injection | Implemented test double |
| Peer and registry tests | Current identity, locality, registry, lookup, conflict, removal, clear, and event behavior | Automated engine-independent tests |
| Session tests | Current host, join, peer-event, intentional-end, failure/reset, invalid-operation, cleanup, and state-event behavior | Automated engine-independent tests |

These tests do not exercise Godot runtime integration, ENet, scenes, replication, sockets, or multiple processes.

## Documentation

| Path | Responsibility |
|---|---|
| `README.md` | Entry point, status, configuration, and documentation index |
| `docs/project-charter.md` | Mission, scope, and quality bar |
| `docs/architecture.md` | Canonical current architecture and planned direction |
| `docs/terminology.md` | Shared vocabulary and implementation status |
| `docs/module-map.md` | This source and responsibility inventory |
| `docs/coding-standards.md` | Engineering rules for subsequent work |
| `docs/testing-strategy.md` | Planned evidence layers and test priorities |
| `docs/networking-foundation.md` | Earlier focused networking principles |
| `docs/decisions/` | ADR process, template, and accepted foundational decisions |

Godot-generated `.uid` sidecars accompany all 18 tracked C# scripts. They are identity metadata, not separate implementation modules.

## Known structural debt

- Both probes duplicate composition and event-logging code.

Project naming, tracked directory casing, and basic C# formatting were normalized mechanically without changing runtime behavior.

## Planned modules and capabilities

The charter includes testing infrastructure, multiprocess scenarios, generalized world/spawn coordination, persistent player identity, application-shell concerns, platform adapters, diagnostics, build tooling, and module assembly. No corresponding implementation directories or stable APIs exist today.
