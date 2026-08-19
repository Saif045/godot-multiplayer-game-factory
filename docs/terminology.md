# Terminology

Terms marked **implemented** appear in current source; **planned** terms guide future design.

## Runtime and lifecycle

**Runtime mode** — **Implemented.** `Offline`, `Client`, `ListenServer`, or `DedicatedServer`.

**Session** — **Implemented.** `NetworkSession` coordinates transport facts, runtime role, and local peers.

**Transport** — **Implemented boundary.** `INetworkTransport` starts/joins, exposes local identity, closes active use, and reports normalized facts. `ENetTransport` is the only production adapter.

**Borrowed transport** — **Implemented ownership rule.** `NetworkSession` never disposes its injected transport; its caller owns disposal.

## Identity and authority

**Peer ID** — **Implemented.** Positive transient transport ID, represented by `PeerId`; `1` is the server in the current Godot model.

**Player ID** — **Planned.** Persistent player identity; it must not be substituted with `PeerId`.

**Network-object ID** — **Implemented experimental runtime identity.** `NetworkObjectId` is a positive server-allocated ID for a dynamic object in a `NetworkWorld`. It is not a persistent player identity.

**Multiplayer authority** — **Godot capability used today.** `AuthorityComponent` exposes a host node's Godot authority through `INetworkAuthority`; it does not create a new authority model.

## Objects and replication

**Network object** — **Implemented experimental composition.** `NetworkObject` is an open component host under a gameplay node. It is not a fixed component bundle, universal base class, or network identity.

**Network-object component** — **Implemented.** A direct `NetworkObjectComponent` child. It registers in `_EnterTree`; the host initializes registered siblings in `_Ready`.

**Capability interface** — **Implemented pattern.** A contract such as `INetworkAuthority` or `INetworkReplication`, used for host/component lookup instead of concrete component dependencies.

**Host node** — **Implemented relation.** The direct gameplay-node parent of a `NetworkObject`; it is unrelated to a network server.

**Replication** — **Implemented experimental configuration.** `ReplicationComponent` owns a `MultiplayerSynchronizer` and derives its configuration from `[Replicated]` properties on the host.

**`[Replicated]`** — **Implemented metadata.** Marks a host property. Its default mode is `OnChange` and spawn behavior is enabled. `[Export]` is not required.

**Replication notification** — **Implemented.** `INetworkReplication.Synchronized` and `DeltaSynchronized` events report synchronizer notifications; they are not general component messaging.

**Replicated existence** — **Implemented experimental dynamic path.** `NetworkSpawnGroup` owns a `MultiplayerSpawner`; its current resource-path payload is temporary. Manual sandbox evidence covers spawning, despawning, and late joining only.

**Network world** — **Implemented experimental dynamic runtime layer.** `NetworkWorld` allocates IDs, registers dynamic `NetworkObject` instances, looks them up, and coordinates spawn/despawn through direct-child `NetworkSpawnGroup` nodes. It does not define player spawning, persistence, authored/static objects, initial spawn data, or stable prefab definitions.

## Evidence

**Unit test** — **Implemented baseline.** Engine-independent xUnit tests cover peers and sessions through a deterministic fake transport.

**Godot integration test** — **Planned.** Automated scene-tree/engine checks do not exist.

**Probe** — **Implemented manual tool.** A sandbox scene for direct exploration, not automated regression evidence.

**Multiprocess scenario** — **Planned.** Automated multi-process orchestration; no runner exists.
