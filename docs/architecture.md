# Architecture

This is the canonical architectural overview. It distinguishes implementation from direction; [networking foundation](networking-foundation.md) is supporting context.

## Current dependency shape

```text
Sandbox probes
├── raw Godot RPC, MultiplayerSpawner, and scene APIs
└── Factory
    ├── NetworkObject -> object capability components -> Godot APIs
    └── NetworkSession
        ├── RuntimeContext
        ├── PeerRegistry -> NetworkPeer -> PeerId
        └── INetworkTransport -> ENetTransport -> Godot ENet
```

`NetworkSession` has no ENet or Godot dependency. `ENetTransport` is the current concrete adapter. Sandbox code may compose the layers and use raw Godot APIs while factory abstractions are incomplete.

## Runtime, peers, transport, and sessions

`RuntimeContext` owns `Offline`, `Client`, `ListenServer`, and `DedicatedServer` role state. `PeerId` is a positive, transient transport ID; `1` is the current Godot server peer. `NetworkPeer` adds locality, and `PeerRegistry` is the process-local known-peer view. Persistent player and runtime object identity do not exist.

`INetworkTransport` exposes lifecycle operations and normalized connection events. `ENetTransport` creates and assigns Godot ENet peers, translates IDs, forwards events, and cleans up its Godot subscriptions when its owner disposes it.

`NetworkSession` coordinates transport, runtime, and peer registry. It validates transitions, guards stale events, cleans collaborators on ends and failures, and is idempotently disposable. The session **borrows** its injected transport: it may call `Close()` for active session use but never calls `Dispose()`; the composition root owns transport disposal. Engine-independent tests cover this contract through a deterministic fake; they do not verify ENet.

## Network-object composition

`NetworkObject` is an open, generic Godot component host. It is a direct child of a gameplay host node and neither implements replication nor authority itself. Any direct `NetworkObjectComponent` child registers during `_EnterTree`; once siblings have registered, `NetworkObject._Ready()` initializes them. Invalid placement fails with an explicit exception.

Concrete component scenes are replaceable defaults. Components and host code communicate through capability interfaces such as `INetworkAuthority` and `INetworkReplication`, retrieved with `NetworkObject.GetComponent<T>()`; components use the same API to find other capabilities. A host can use its own generic behavior directly or obtain a host capability. Events are notifications, and attributes are metadata—not a component messaging mechanism.

`AuthorityComponent` implements `INetworkAuthority` around the host's Godot authority. `ReplicationComponent` implements `INetworkReplication`, owns the child `MultiplayerSynchronizer`, targets the gameplay host, and builds a `SceneReplicationConfig` from `[Replicated]` host properties. `[Replicated]` defaults to `Spawn = true` and `OnChange`; it does not require `[Export]`.

The `RawDoor` sandbox uses only `INetworkAuthority` and `INetworkReplication`, while retaining direct Godot RPC use for its probe-specific request path. Manual sandbox runs exercised ordinary state change and late joining. There is no automated Godot integration test for this component lifecycle or replication behavior yet.

## Current composition roots

`NetworkProbe` and `ReplicationProbe` each construct runtime, peer registry, ENet transport, and session in `_Ready`, and release the session before the caller-owned transport in `_ExitTree`. This duplication is exploratory; a reusable composition-root form remains open.

## Planned direction

The next major problem is a network-world layer for stable spawn definitions and runtime spawn/despawn behavior. It must not be inferred from the current object component host. Godot integration tests, multiprocess scenarios, persistent player/object identity, structured diagnostics, application shell, packaging, and CI are also not implemented.
