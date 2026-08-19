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

`NetworkWorld` now coordinates dynamic object identity, registry, lookup, spawn, and despawn. The server allocates positive `NetworkObjectId` values globally within the world. A direct-child `NetworkSpawnGroup` owns one `MultiplayerSpawner` and serves as its spawn root; multiple groups share the world's ID sequence.

The current spawn payload carries the ID and a scene resource path. The path is temporary prefab identity, not a stable definition contract. The spawned host is bound to its world and ID before entering the tree, then its `NetworkObject` registers. Manual sandbox runs exercised multi-group global IDs, spawn/despawn, and late join; automated Godot and multiprocess coverage does not exist.

## Future work

Stable prefab definitions, spawn initialization data, persistent players, authored/static objects, Godot integration tests, and multiprocess scenarios remain future work. Do not infer them from the current dynamic path.
