# Module Map

This inventory describes tracked paths and current responsibilities. Directories, scenes, resources, and assets use lowercase or snake_case; C# namespaces, types, and filenames use PascalCase.

## Configuration

| Path | Responsibility | Status |
|---|---|---|
| `GameFactory.csproj` | Godot SDK, root namespace, and target frameworks | Implemented |
| `GameFactory.sln` | Godot project and engine-independent test project | Implemented |
| `project.godot` | Godot configuration and connection-probe main scene | Implemented |
| `.editorconfig`, `.gitattributes`, `.gitignore` | Source formatting and ignored local/generated output | Implemented |

The project currently uses Godot .NET SDK 4.7.1, .NET 8, and conditional .NET 9 for Android. These are settings, not platform-support promises.

## `factory/diagnostics/`

| Type/path | Responsibility | Status |
|---|---|---|
| `GameLog` / `LogRun` | Process-local structured JSONL events and Godot-console mirroring. | Implemented; local unit coverage |
| `LogEntry`, `LogLevel`, `DiagnosticsSessionId` | Small structured event and session identity model. | Implemented |
| `network/MasterLogWriter` | Single authoritative, reader-shareable JSONL append path for host session logs. | Implemented; concurrent append unit coverage |
| `network/NetworkLogRelay` | Reliable bounded client forwarding, host-clock-normalized timeline metadata, and authoritative host master-log collection. | Implemented; real two-client acceptance pending |

## `factory/runtime/`

| Type | Responsibility |
|---|---|
| `RuntimeMode` | Names offline, client, listen-server, and dedicated-server roles. |
| `RuntimeContext` | Stores the role and derives role capabilities. |

## `factory/networking/peers/`

| Type | Responsibility |
|---|---|
| `PeerId` | Validated positive transport peer identifier; server is `1`. |
| `NetworkPeer` | Immutable peer ID and locality model. |
| `PeerRegistry` | Process-local peer collection with add/remove events. |

`PlayerId` is session-scoped and is provided by the player layer; it is not persistent account or platform identity. Dynamic runtime-object identity is provided by `NetworkObjectId` in the world layer.

## `factory/networking/players/`

| Type | Responsibility |
|---|---|
| `PlayerId` | Positive session-scoped player identity. |
| `NetworkPlayer` | Immutable `PlayerId`, `PeerId`, and `NetworkObjectId` association. |
| `PlayerRegistry` | Engine-independent player lookup by player and peer identity. |
| `PlayerLifecycle` | Server-side peer-to-player spawn/despawn orchestration through gameplay delegates. |

This layer does not define persistent/account identity, Steam identity, player movement, input, or a concrete player scene.

## `factory/networking/transport/`

| Type | Responsibility |
|---|---|
| `TransportResult` | Success plus optional string error. |
| `INetworkTransport` | Lifecycle and normalized connection-event boundary. |
| `ENetTransport` | Current Godot ENet adapter. |

## `factory/networking/sessions/`

`HostMode`, `SessionState`, `SessionEndReason`, `SessionResult`, and `NetworkSession` define the current lifecycle foundation. `NetworkSession` borrows its injected transport, unsubscribes during idempotent disposal, and never disposes that transport; its composition root owns disposal.

## `factory/steam/`

| Type/path | Responsibility | Status |
|---|---|---|
| `ISteamAdapter` | Steam-specific lifecycle, lobby, overlay, presence, peer mapping, dedicated-server, and auth boundary. | Implemented contract |
| `SteamSession` | Explicit Steam initialization, lobby host/join/leave, and Godot `MultiplayerPeer` assignment. | Implemented; manual acceptance pending |
| `models/` | Validated Steam IDs and typed Steam data/options/errors. | Implemented |
| `adapters/godot_steam/GodotSteamAdapter` | Typed C# facade over bridge calls and signals. | Implemented; listen server only |
| `adapters/godot_steam/godot_steam_bridge.gd` | The only GameFactory GDScript that calls GodotSteam. | Implemented |
| `adapters/godot_steam/godot_steam_adapter.tscn` | Bridge composition scene. | Implemented |

Dedicated server and Steam auth methods are declared seams and intentionally report unsupported in `GodotSteamAdapter`; no dedicated-server implementation is claimed.

## `factory/networking/objects/`

| Path/type | Responsibility |
|---|---|
| `NetworkObject` | Open component host for one gameplay parent node, including runtime ID and owner-peer metadata after binding. |
| `INetworkSpawnInitializable` | Gameplay-root contract for shared `Variant` data applied before tree entry. |
| `NetworkObjectComponent` | Direct-child registration and two-phase initialization base. |
| `NetworkObjectId` | Positive runtime identity for a dynamic object in a world. |
| `network_object.tscn` | Replaceable default composition of authority and replication component scenes. |
| `components/authority/INetworkAuthority` | Authority capability contract. |
| `components/authority/AuthorityComponent` | Default Godot-authority capability implementation. |
| `components/authority/authority_component.tscn` | Default authority component scene. |
| `components/replication/INetworkReplication` | Replication notification capability contract. |
| `components/replication/ReplicationComponent` | Default `MultiplayerSynchronizer` owner/configurer. |
| `components/replication/ReplicatedAttribute` | Metadata selecting replication mode and spawn behavior. |
| `components/replication/replication_component.tscn` | Default replication component scene. |

The default component scenes are implementations, not mandatory or exclusive component lists. `ReplicationComponent` recognizes `[Replicated]` properties without requiring `[Export]`; defaults are `OnChange` and spawn enabled. It replaces its synchronizer configuration as part of the current automatic path. Manual replication configuration does not exist.

## `factory/networking/world/`

| Type/path | Responsibility |
|---|---|
| `NetworkSpawnGroupKind` | Single serialized definition of generated spawn groups; `WorldObjects` is the default. |
| `NetworkWorld` | Server-owned runtime ID allocation, dynamic object registry, lookup, spawn/despawn, and automatic group creation/routing. |
| `NetworkSpawnGroup` | Runtime-generated direct world child that owns one `MultiplayerSpawner`, spawn root, and scene-instantiation boundary. |
| `network_world.tscn` | Empty authored world; groups and spawners are generated at runtime. |

`NetworkObject.SpawnGroup` selects a generated runtime group, so gameplay calls `NetworkWorld.Spawn(scene)` without passing a group. Ownership-aware overloads accept `PeerId`; existing overloads default it to `PeerId.Server`. The typed spawn-data overload accepts shared `Variant` data and an optional server-only configure callback. `NetworkWorld` binds a world, `NetworkObjectId`, and `OwnerPeerId` before the host enters the tree. Spawn payloads carry the prefab's Godot resource UID, owner peer, and shared spawn data rather than a resource path. Non-nil data requires the gameplay root to implement `INetworkSpawnInitializable`; it is applied before the host enters the tree. The owner peer identifies the represented peer and does not change Godot multiplayer authority. Authored/static objects, persistence, and stable cross-project prefab definitions do not exist.

## Sandboxes and tests

| Path | Responsibility | Evidence |
|---|---|---|
| `sandbox/connection/` | Manual session/ENet lifecycle probe | Exploratory only |
| `sandbox/replication/` | Manual raw RPC, spawning, property-replication, and minimal player-lifecycle probe | Manual state-change, late-join, dynamic spawn/despawn, multi-group, and player-ownership exercise; not automated |
| `sandbox/steam/` | Manual low-level Steam/lobby smoke plus full gameplay acceptance probe over the existing player, world, and replication stack | Re-host and two-account transport/diagnostics verified; gameplay acceptance requires real two-account exercise |
| `sandbox/launcher/` | Registered-scene launcher for development/exported sandbox arguments | `--run=connection`, `--run=replication`, or `--run=steam`; not application shell |
| `tests/GameFactory.Tests/` | xUnit tests for peer/session/player policy, value types, registry, and spawn-group enum contracts, with `FakeNetworkTransport` | Automated engine-independent baseline |

The repository has no Godot integration test harness, multiprocess runner, persistence, game-shell, or general tooling module under `factory/`. Steam listen-server integration is present but is not yet accepted through real Steam runtime evidence. Speculative empty source folders are intentionally absent.

## Documentation

`README.md` introduces the repository. `docs/` contains the charter, canonical architecture, terminology, testing strategy, coding standards, this map, networking note, and historical ADRs.
