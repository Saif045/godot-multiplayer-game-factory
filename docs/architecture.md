# Architecture

This is the canonical overview of the repository architecture. It distinguishes current implementation from project direction. The earlier [networking foundation](networking-foundation.md) remains a focused supporting note.

## Current dependency shape

```text
Sandbox probes
├── Godot RPC, MultiplayerSpawner, and scene APIs
└── Factory types
    ├── NetworkObject -> Godot replication and reflection
    └── NetworkSession
        ├── RuntimeContext
        ├── PeerRegistry -> NetworkPeer -> PeerId
        └── INetworkTransport
            └── ENetTransport -> Godot MultiplayerApi -> ENetMultiplayerPeer
```

`NetworkSession` has no direct dependency on ENet or Godot. `ENetTransport` is the concrete boundary adapter. The sandbox is allowed to assemble those layers and to use raw Godot features while abstractions are incomplete.

## Runtime role

`RuntimeContext` owns the process's current `RuntimeMode`:

| Mode | Server-capable | Client-capable | Meaning |
|---|---:|---:|---|
| `Offline` | No | No | No active session role |
| `ListenServer` | Yes | Yes | Authority and a local client role in one process |
| `Client` | No | Yes | Connected or connecting to remote authority |
| `DedicatedServer` | Yes | No | Authority with no implied local player |

The context's mutation methods are assembly-internal, and `NetworkSession` is their only caller in the current source. Dedicated mode currently changes runtime semantics; a complete headless application shell or export workflow is not implemented.

## Peer model

`PeerId` converts a raw positive Godot peer identifier into a value type at the transport boundary. Value `1` is treated as the server. `NetworkPeer` combines that identifier with locality; `IsServer` and `IsLocal` are independent properties.

`PeerRegistry` is the process-local view of known peers. It provides idempotent addition, rejects conflicting locality for an existing ID, publishes add/remove events, and publishes removals while clearing.

Peer IDs are transient connection identifiers. There is no persistent player identity or runtime network-object identity model in the source today.

## Transport boundary

`INetworkTransport` exposes lifecycle operations and normalized connection events. `ENetTransport` is its only implementation. It:

- creates a Godot ENet server or client;
- assigns the peer to `MultiplayerApi`;
- translates raw peer IDs into `PeerId`;
- forwards Godot connection events;
- closes and disposes its ENet peer;
- unsubscribes from Godot events when the adapter is disposed.

The boundary permits future replacement without changing session policy, but no second transport currently demonstrates that replacement. Errors are currently nullable strings in result values rather than structured codes.

## Session lifecycle

`NetworkSession` coordinates the transport, runtime context, and peer registry.

### Hosting

```text
Offline -> Starting -> Running

          failure -> Failed
```

After the transport starts successfully, the session selects listen or dedicated runtime mode, obtains the local peer ID, registers it as local, and enters `Running`.

### Joining

```text
Offline -> Connecting -- ConnectedToServer --> Running
                       -- failure/loss ------> Failed
```

A successful client transport initialization does not mean the connection is established. The session remains `Connecting` until the transport reports `ConnectedToServer`, at which point it registers the local peer and enters `Running`.

### Ending

`Leave` is valid for a client while connecting or running. `ShutdownHost` is valid for a server while starting or running. Intentional endings pass through `Stopping`, close the transport, clear peers, reset runtime role, and return to `Offline`. The last intentional end reason remains available.

Connection failures and server loss clean the same collaborators and enter `Failed`. `ResetFailure` clears the recorded failure and returns to `Offline`.

A remote client disconnecting removes that peer but does not fail a server session. A client losing its server is interpreted through the dedicated server-disconnected event.

### Current lifecycle gaps

- `NetworkSession` does not implement disposal or unsubscribe from transport events.
- Ownership of the injected transport is not specified.
- Cleanup and event callbacks are not exception-safe.
- Transport events are not all guarded by explicit state/role invariants.
- Legal state transitions are encoded procedurally rather than checked by a transition model.

These are hardening work, not guarantees supplied by the existing code.

## Replication composition

The current `NetworkObject` is an experimental Godot component placed as a child of a gameplay node. Its scene includes a child `MultiplayerSynchronizer` whose replication root points to the gameplay host.

During `_EnterTree`, `NetworkObject`:

1. takes its parent as the host;
2. resolves its `MultiplayerSynchronizer` child;
3. reflects over host properties;
4. selects properties carrying `[Replicated]`;
5. requires each selected property to also carry `[Export]`;
6. creates a new `SceneReplicationConfig` with spawn and replication modes;
7. assigns that configuration and emits text diagnostics.

It also exposes the host's multiplayer authority and the synchronizer. The `RawDoor` sandbox object demonstrates one use of this composition alongside a raw Godot RPC and `MultiplayerSpawner`.

Current constraints include a fixed hierarchy, a Node3D reusable scene root, uncached reflection, automatic-only configuration, replacement of any authored configuration, and string-only diagnostics. Generalized spawning, late-join guarantees, and production-ready replication validation are not implemented.

## Raw Godot access

The factory is a layer over Godot, not an exclusive gateway. The current replication probe directly uses `RpcId`, `MultiplayerSpawner`, `MultiplayerSynchronizer`, and `MultiplayerApi`. That demonstrates available raw access, although the project has not yet formalized replacement contracts for every planned subsystem.

## Current composition roots

`NetworkProbe` and `ReplicationProbe` each construct `RuntimeContext`, `PeerRegistry`, `ENetTransport`, and `NetworkSession` in their `_Ready` method and tear down the active role in `_ExitTree`. This setup is duplicated exploratory code. The final reusable composition-root form remains an open decision.

## Planned direction

The refit is intended to add, in order:

- mechanical namespace, path, placement, and formatting normalization;
- engine-independent lifecycle tests and a fake transport;
- explicit disposal, ownership, cleanup safety, transition rules, and peer invariants;
- automatic and manual replication paths, validation, metadata caching, structured diagnostics, broader node compatibility, and tests;
- reusable multiprocess scenario execution;
- a network-world layer with stable spawn definitions, runtime spawn/despawn, late-join behavior, cleanup, and a custom spawning path.

Longer-term scope also includes player identity, application shell, platform adapters, build tooling, module assembly, and broader diagnostics. None of these planned capabilities should be inferred as present.
