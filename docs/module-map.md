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

Persistent player and runtime-object identities do not exist.

## `factory/networking/transport/`

| Type | Responsibility |
|---|---|
| `TransportResult` | Success plus optional string error. |
| `INetworkTransport` | Lifecycle and normalized connection-event boundary. |
| `ENetTransport` | Current Godot ENet adapter. |

## `factory/networking/sessions/`

`HostMode`, `SessionState`, `SessionEndReason`, `SessionResult`, and `NetworkSession` define the current lifecycle foundation. `NetworkSession` borrows its injected transport, unsubscribes during idempotent disposal, and never disposes that transport; its composition root owns disposal.

## `factory/networking/objects/`

| Path/type | Responsibility |
|---|---|
| `NetworkObject` | Open component host for one gameplay parent node. |
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
| `NetworkWorld` | Server-owned runtime ID allocation, dynamic object registry, lookup, and spawn/despawn coordination. |
| `NetworkSpawnGroup` | Direct world child, one `MultiplayerSpawner`, spawn root, and scene-instantiation boundary. |
| `network_world.tscn` | Default world with one `WorldObjects` group. |
| `network_spawn_group.tscn` | Default group scene with its spawner. |

`NetworkWorld` binds a world and `NetworkObjectId` before a spawned host enters the tree. Spawn groups use the scene resource path as temporary prefab identity. Initial spawn data, authored/static objects, player spawning, persistence, and stable prefab definitions do not exist.

## Sandboxes and tests

| Path | Responsibility | Evidence |
|---|---|---|
| `sandbox/connection/` | Manual session/ENet lifecycle probe | Exploratory only |
| `sandbox/replication/` | Manual raw RPC, spawning, and property-replication probe | Manual state-change, late-join, dynamic spawn/despawn, and multi-group exercise; not automated |
| `tests/GameFactory.Tests/` | xUnit tests for peers, sessions, and `NetworkObjectId`, with `FakeNetworkTransport` | Automated engine-independent baseline |

The repository has no Godot integration test harness, multiprocess runner, persistence, platform, tooling, or testing module under `factory/`. Speculative empty source folders are intentionally absent.

## Documentation

`README.md` introduces the repository. `docs/` contains the charter, canonical architecture, terminology, testing strategy, coding standards, this map, networking note, and historical ADRs.
