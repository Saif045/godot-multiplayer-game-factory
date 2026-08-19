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

**Runtime network-object identity** — **Planned.** Stable identity for an object instance. `NetworkObject` does not provide one.

**Multiplayer authority** — **Godot capability used today.** `AuthorityComponent` exposes a host node's Godot authority through `INetworkAuthority`; it does not create a new authority model.

## Objects and replication

**Network object** — **Implemented experimental composition.** `NetworkObject` is an open component host under a gameplay node. It is not a fixed component bundle, universal base class, or network identity.

**Network-object component** — **Implemented.** A direct `NetworkObjectComponent` child. It registers in `_EnterTree`; the host initializes registered siblings in `_Ready`.

**Capability interface** — **Implemented pattern.** A contract such as `INetworkAuthority` or `INetworkReplication`, used for host/component lookup instead of concrete component dependencies.

**Host node** — **Implemented relation.** The direct gameplay-node parent of a `NetworkObject`; it is unrelated to a network server.

**Replication** — **Implemented experimental configuration.** `ReplicationComponent` owns a `MultiplayerSynchronizer` and derives its configuration from `[Replicated]` properties on the host.

**`[Replicated]`** — **Implemented metadata.** Marks a host property. Its default mode is `OnChange` and spawn behavior is enabled. `[Export]` is not required.

**Replication notification** — **Implemented.** `INetworkReplication.Synchronized` and `DeltaSynchronized` events report synchronizer notifications; they are not general component messaging.

**Replicated existence** — **Manual sandbox evidence only.** The replication probe uses raw `MultiplayerSpawner`; there is no generalized factory spawning service.

**Network world** — **Planned.** Future coordination for spawn definitions, runtime objects, joining peers, and cleanup. Its API is not defined.

## Evidence

**Unit test** — **Implemented baseline.** Engine-independent xUnit tests cover peers and sessions through a deterministic fake transport.

**Godot integration test** — **Planned.** Automated scene-tree/engine checks do not exist.

**Probe** — **Implemented manual tool.** A sandbox scene for direct exploration, not automated regression evidence.

**Multiprocess scenario** — **Planned.** Automated multi-process orchestration; no runner exists.
