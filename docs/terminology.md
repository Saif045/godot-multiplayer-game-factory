# Terminology

Terms marked **implemented** appear in current source; **planned** terms guide future design.

## Runtime and lifecycle

**Runtime mode** — **Implemented.** `Offline`, `Client`, `ListenServer`, or `DedicatedServer`.

**Session** — **Implemented.** `NetworkSession` coordinates transport facts, runtime role, and local peers.

**Transport** — **Implemented boundary.** `INetworkTransport` starts/joins, exposes local identity, closes active use, and reports normalized facts. `ENetTransport` is the only production adapter.

**Borrowed transport** — **Implemented ownership rule.** `NetworkSession` never disposes its injected transport; its caller owns disposal.

## Identity and authority

**Peer ID** — **Implemented.** Positive transient transport ID, represented by `PeerId`; `1` is the server in the current Godot model.

**Player ID** — **Implemented session-scoped identity.** `PlayerId` is a positive value allocated by authoritative `PlayerLifecycle` for a player in the current session. It must not be substituted with `PeerId`, and it is not persistent account or Steam identity.

**Network-object ID** — **Implemented experimental runtime identity.** `NetworkObjectId` is a positive server-allocated ID for a dynamic object in a `NetworkWorld`. It is not a persistent player identity.

**Owner peer ID** — **Implemented experimental spawn metadata.** `NetworkObject.OwnerPeerId` identifies the peer represented by a spawned object. It is known before tree entry and replicated in spawn metadata; it does not transfer Godot multiplayer authority from the server.

**Network player** — **Implemented server-side relationship.** `NetworkPlayer` records the session's `PlayerId`, transient `PeerId`, and player `NetworkObjectId`. `PlayerRegistry` owns lookup by player and peer identity.

**Multiplayer authority** — **Godot capability used today.** `AuthorityComponent` exposes a host node's Godot authority through `INetworkAuthority`; it does not create a new authority model.

## Objects and replication

**Network object** — **Implemented experimental composition.** `NetworkObject` is an open component host under a gameplay node. It is not a fixed component bundle, universal base class, or network identity.

**Network-object component** — **Implemented.** A direct `NetworkObjectComponent` child. It registers in `_EnterTree`; the host initializes registered siblings in `_Ready`.

**Capability interface** — **Implemented pattern.** A contract such as `INetworkAuthority` or `INetworkReplication`, used for host/component lookup instead of concrete component dependencies.

**Host node** — **Implemented relation.** The direct gameplay-node parent of a `NetworkObject`; it is unrelated to a network server.

**Replication** — **Implemented experimental configuration.** `ReplicationComponent` owns a `MultiplayerSynchronizer` and derives its configuration from `[Replicated]` properties on the host.

**`[Replicated]`** — **Implemented metadata.** Marks a host property. Its default mode is `OnChange` and spawn behavior is enabled. `[Export]` is not required.

**Replication notification** — **Implemented.** `INetworkReplication.Synchronized` and `DeltaSynchronized` events report synchronizer notifications; they are not general component messaging.

**Replicated existence** — **Implemented experimental dynamic path.** `NetworkSpawnGroup` owns a `MultiplayerSpawner`. Its payload carries runtime object identity, the prefab's Godot resource UID, and owner peer ID. Manual sandbox evidence covers spawning, despawning, late joining, and off-tree server configuration only.

**Network world** — **Implemented experimental dynamic runtime layer.** `NetworkWorld` allocates IDs, registers dynamic `NetworkObject` instances, looks them up, and coordinates generic spawn/despawn through direct-child `NetworkSpawnGroup` nodes. `PlayerLifecycle` supplies player-specific spawn policy; the world itself does not define persistence, authored/static objects, initial general spawn data, or stable prefab definitions.

**Network spawn-group kind** — **Implemented experimental routing metadata.** `NetworkSpawnGroupKind` is the single list of runtime spawn categories. `WorldObjects` is its default value. A `NetworkObject` prefab selects one through its exported `SpawnGroup` property, while `NetworkWorld` generates one matching runtime group and spawner per enum value.

**Spawn configuration callback** — **Implemented experimental server-side initialization.** `NetworkWorld.Spawn<T>(PackedScene, Action<T>)` gives gameplay the actual off-tree authoritative host before binding and tree entry. Callback code does not run remotely; client-visible state must be replicated or supplied by a future explicit spawn-data contract.

## Evidence

**Unit test** — **Implemented baseline.** Engine-independent xUnit tests cover peer, session, and player lifecycle policy through a deterministic fake transport.

**Godot integration test** — **Planned.** Automated scene-tree/engine checks do not exist.

**Probe** — **Implemented manual tool.** A sandbox scene for direct exploration, not automated regression evidence.

**Multiprocess scenario** — **Planned.** Automated multi-process orchestration; no runner exists.
