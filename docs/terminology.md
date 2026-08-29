# Terminology

Terms marked **implemented** appear in current source; **planned** terms guide future design.

## Runtime and session

**Runtime mode** — **Implemented.** `Offline`, `Client`, `ListenServer`, or `DedicatedServer`.

**Steam session** — **Implemented.** `SteamSession` coordinates Steam initialization, lobby host/join/leave, and installation/cleanup of the Steam `MultiplayerPeer` in Godot's `MultiplayerApi`.

**Steam adapter** — **Implemented boundary.** `ISteamAdapter` and its GodotSteam implementation isolate platform calls below the gameplay layer. It is not a platform-neutral transport contract.

## Identity and authority

**Peer ID** — **Implemented.** Positive transient Godot multiplayer ID, represented by `PeerId`; `1` is the current server.

**Player ID** — **Implemented session-scoped identity.** `PlayerId` is a positive value allocated by authoritative `PlayerLifecycle`. It is neither `PeerId` nor a persistent Steam/account identity.

**Network-object ID** — **Implemented runtime identity.** `NetworkObjectId` is a positive server-allocated ID for a dynamic object in `NetworkWorld`; it is not player identity.

**Owner peer ID** — **Implemented spawn metadata.** `NetworkObject.OwnerPeerId` identifies the represented peer. It does not transfer Godot multiplayer authority from the server.

**Network player** — **Implemented relationship.** `NetworkPlayer` records a `PlayerId`, `PeerId`, and player `NetworkObjectId`; `PlayerRegistry` owns lookup.

**Multiplayer authority** — **Godot capability used today.** `AuthorityComponent` exposes host-node authority through `INetworkAuthority`; it does not create another authority model.

## Objects and replication

**Network object** — **Implemented composition.** `NetworkObject` is an open component host below a gameplay node, not a universal base class or fixed component bundle.

**Capability interface** — **Implemented pattern.** A contract such as `INetworkAuthority` or `INetworkReplication` used to find component capabilities without concrete dependencies.

**Replication** — **Implemented configuration.** `ReplicationComponent` owns a `MultiplayerSynchronizer` configured from `[Replicated]` properties on its host.

**Network world** — **Implemented dynamic runtime layer.** `NetworkWorld` allocates IDs, registers dynamic objects, and coordinates generic spawn/despawn through generated `NetworkSpawnGroup` nodes.

## Evidence

**Unit test** — **Implemented.** Engine-independent xUnit tests for value types, registries, player lifecycle, diagnostics, and other pure policy.

**Probe** — **Implemented manual tool.** A sandbox scene for exploration and acceptance, not automated regression evidence.

**Godot integration test / multiprocess scenario** — **Planned.** No automated engine or multi-process runner exists.
