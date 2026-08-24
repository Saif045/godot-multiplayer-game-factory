# Networking Foundation

## Layering

```text
Game / sandbox
-> GameFactory object components and NetworkSession
-> INetworkTransport
-> Godot Multiplayer API / MultiplayerPeer
-> transport
```

Higher session policy does not depend on ENet. `ENetTransport` is the current adapter. `NetworkSession` borrows its transport and never disposes it.

## Runtime and peers

A process is offline, client, listen server, or dedicated server. `PeerId` is a positive transport ID; `1` is the server. `PeerRegistry` records process-visible peers. Locality and server status are separate, and peer identity is not player identity.

## Session lifecycle

Transport events are facts; `NetworkSession` supplies lifecycle meaning. Remote client disconnect does not fail a server session, client server loss does fail the client session, and local cleanup does not depend on a remote disconnect. Session transitions, cleanup, disposal, and stale-event guards have engine-independent automated coverage via a fake transport.

## Object composition and replication

Gameplay nodes retain their own inheritance. A direct-child `NetworkObject` hosts an open set of `NetworkObjectComponent` children. Components register in `_EnterTree` and initialize after sibling registration in `NetworkObject._Ready`.

Default authority and replication scenes implement `INetworkAuthority` and `INetworkReplication`. Those interfaces are the capability-facing communication contracts; concrete default components can be replaced. `ReplicationComponent` owns the `MultiplayerSynchronizer` and configures host properties marked `[Replicated]`. The metadata defaults to `OnChange` and spawn enabled; `[Export]` is not required.

The replication sandbox retains raw Godot RPC and spawning access. It was manually exercised for normal state change and late join, but component/replication behavior has no automated Godot evidence yet.

## Dynamic world and spawning

`NetworkWorld` now coordinates dynamic object identity, registry, lookup, spawn, and despawn. The server allocates positive `NetworkObjectId` values globally within the world. `NetworkSpawnGroupKind` is the single list of groups; the world creates a direct-child `NetworkSpawnGroup` and its `MultiplayerSpawner` for each kind. The groups share the world's ID sequence.

`NetworkObject` exposes exported `SpawnGroup` metadata, defaulting to `WorldObjects`, so gameplay calls `NetworkWorld.Spawn(scene)` without selecting a group. Ownership-aware overloads identify the owning peer; older overloads default it to the server peer. The typed spawn-data overload accepts a shared Godot `Variant` and optional server-only configuration. The local spawner path reuses the authoritative host; remote peers resolve the prefab's Godot resource UID and instantiate their own copies. The payload carries the runtime ID, prefab UID, owner peer ID, and shared spawn data, not a resource path. The spawned host is bound to its world, ID, and owner peer before `INetworkSpawnInitializable` receives non-nil data; only then does server-only configuration run. All of this occurs before tree entry. Owner peer metadata does not change Godot multiplayer authority. Manual sandbox runs exercised off-tree configuration, automatic multi-group routing, global IDs, spawn/despawn, and late join; automated Godot and multiprocess coverage does not exist.

## Player lifecycle

`PlayerLifecycle` is an engine-independent server-side coordinator. It turns eligible peer presence into a session-scoped `PlayerId`, a gameplay-supplied spawned object, and a `NetworkPlayer` registry relationship. A listen server creates a player for its local server peer; a dedicated server skips that peer; remote peers receive players in both modes. Clients do not create players authoritatively. On disconnect, the lifecycle requests despawn and removes the association even if despawn reports a failure.

## Future work

Stable prefab definitions, persistent players/account identity, authored/static objects, Godot integration tests, and multiprocess scenarios remain future work. Do not infer them from the current dynamic path.
