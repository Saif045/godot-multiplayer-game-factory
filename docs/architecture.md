# Architecture

This is the canonical architectural overview. It distinguishes implementation from direction; [networking foundation](networking-foundation.md) is supporting context.

## Current dependency shape

```text
Sandbox probes
├── raw Godot RPC, MultiplayerSpawner, and scene APIs
└── Factory
    ├── NetworkObject -> object capability components -> Godot APIs
    ├── PlayerLifecycle -> PeerRegistry, PlayerRegistry, gameplay delegates
    └── NetworkSession
        ├── RuntimeContext
        ├── PeerRegistry -> NetworkPeer -> PeerId
        └── INetworkTransport -> ENetTransport -> Godot ENet
└── Steam probe
    └── SteamSession -> ISteamAdapter -> GodotSteamAdapter -> GDScript bridge
        -> GodotSteam / SteamMultiplayerPeer -> Godot MultiplayerAPI
```

`NetworkSession` has no ENet or Godot dependency. `ENetTransport` is the current concrete adapter. Sandbox code may compose the layers and use raw Godot APIs while factory abstractions are incomplete.

## Steam listen-server foundation

`factory/steam/` is a Steam-specific platform boundary, not a platform-neutral online abstraction. `SteamSession` owns explicit initialization, host/join/leave sequencing, and assignment of a returned `MultiplayerPeer` to Godot's `MultiplayerApi`. It does not replace `NetworkSession`, ENet, `NetworkWorld`, object spawning, replication, or player lifecycle.

`ISteamAdapter` describes Steam lifecycle, identity, friends/presence, lobby discovery/configuration/membership, overlays, rich presence, peer-to-Steam-ID mapping, and declared seams for dedicated servers and auth tickets. The current `GodotSteamAdapter` uses only `Node.Call` and bridge signals. All GodotSteam-specific GDScript remains in `godot_steam_bridge.gd`; the typed C# layer does not reference vendor types directly. A future Steamworks-backed implementation can therefore replace the library without creating a fictitious cross-platform API.

The vendor dependency is GodotSteam 4.22 GDExtension, including `SteamMultiplayerPeer`, committed under `addons/godotsteam/` with its MIT license. The adapter uses development App ID 480. `SteamProbe` creates a friends-only four-member lobby, can create/assign a Steam listen-server peer, and can explicitly join a supplied lobby ID. Invite callbacks are surfaced; they do not automatically join an already active session. Real two-account Steam testing is required before this is accepted as replacement-transport evidence.

## Runtime, peers, transport, and sessions

`RuntimeContext` owns `Offline`, `Client`, `ListenServer`, and `DedicatedServer` role state. `PeerId` is a positive, transient transport ID; `1` is the current Godot server peer. `NetworkPeer` adds locality, and `PeerRegistry` is the process-local known-peer view. `PlayerId` is a positive session-scoped player identity; it is not persistent account or platform identity. Dynamic runtime object identity is provided by `NetworkObjectId` in the world layer.

`INetworkTransport` exposes lifecycle operations and normalized connection events. `ENetTransport` creates and assigns Godot ENet peers, translates IDs, forwards events, and cleans up its Godot subscriptions when its owner disposes it.

`NetworkSession` coordinates transport, runtime, and peer registry. It validates transitions, guards stale events, cleans collaborators on ends and failures, and is idempotently disposable. The session **borrows** its injected transport: it may call `Close()` for active session use but never calls `Dispose()`; the composition root owns transport disposal. Engine-independent tests cover this contract through a deterministic fake; they do not verify ENet.

## Network-object composition

`NetworkObject` is an open, generic Godot component host. It is a direct child of a gameplay host node and neither implements replication nor authority itself. Any direct `NetworkObjectComponent` child registers during `_EnterTree`; once siblings have registered, `NetworkObject._Ready()` initializes them. Invalid placement fails with an explicit exception.

Concrete component scenes are replaceable defaults. Components and host code communicate through capability interfaces such as `INetworkAuthority` and `INetworkReplication`, retrieved with `NetworkObject.GetComponent<T>()`; components use the same API to find other capabilities. A host can use its own generic behavior directly or obtain a host capability. Events are notifications, and attributes are metadata—not a component messaging mechanism.

`AuthorityComponent` implements `INetworkAuthority` around the host's Godot authority. `ReplicationComponent` implements `INetworkReplication`, owns the child `MultiplayerSynchronizer`, targets the gameplay host, and builds a `SceneReplicationConfig` from `[Replicated]` host properties. `[Replicated]` defaults to `Spawn = true` and `OnChange`; it does not require `[Export]`.

The `RawDoor` sandbox uses only `INetworkAuthority` and `INetworkReplication`, while retaining direct Godot RPC use for its probe-specific request path. Manual sandbox runs exercised ordinary state change and late joining. There is no automated Godot integration test for this component lifecycle or replication behavior yet.

## Network world and dynamic spawning

`NetworkWorld` coordinates the current collection-level runtime concerns: it allocates positive `NetworkObjectId` values from one server-owned sequence, registers bound objects, looks them up, and requests despawn. `NetworkSpawnGroupKind` is the single definition of available groups; on entry, `NetworkWorld` creates one deterministically named direct-child `NetworkSpawnGroup` per enum value. Each generated group owns one `MultiplayerSpawner` and acts as that spawner's spawn root.

`NetworkObject.SpawnGroup` is exported prefab metadata and defaults to `WorldObjects`. The server calls `NetworkWorld.Spawn(scene)` without selecting a group, or its typed overloads to provide an owner peer, optional shared `Variant` spawn data, and optional server-only configuration. The world validates the saved scene's Godot resource UID, instantiates the host off tree, reads and validates its routing metadata, allocates an ID, and routes through the generated matching group. The local `MultiplayerSpawner` spawn path reuses that exact host; remote peers resolve the UID and instantiate their own scene copies. Each host is bound to its world, ID, and owner peer before shared spawn data is applied. On the server, configuration then runs against that bound off-tree host. Only after those steps does the host enter the tree, register during `_EnterTree`, and initialize its components.

The spawn payload carries `NetworkObjectId`, the Godot resource UID for the prefab, `OwnerPeerId`, and shared `Variant` spawn data; it does not carry a resource path or callbacks. Non-nil spawn data requires the gameplay root to implement `INetworkSpawnInitializable`, which interprets the data before tree entry. `OwnerPeerId` is the peer represented by an object and does not change Godot multiplayer authority, which remains server-owned. The server-only configure callback is unrestricted local C# code and runs after binding/spawn-data application; it never runs remotely. Both paths are validated against freeing, tree insertion, and spawn-group changes. Authored/static objects, persistent identity, and a stable cross-project prefab-definition contract are not implemented. Manual sandbox runs exercised off-tree configuration, UID resolution, automatic routing, globally unique IDs across groups, spawn/despawn, late joining, replication, and authority. There is no automated Godot or multiprocess evidence for this layer.

## Player lifecycle

`PlayerLifecycle` is an engine-independent orchestration class over `PeerRegistry`, `PlayerRegistry`, and `RuntimeContext`. On an authoritative server, it allocates a positive session-scoped `PlayerId`, invokes gameplay's spawn delegate, and records the `PeerId` to `PlayerId` to `NetworkObjectId` association as a `NetworkPlayer`. It creates a player for the local server peer on a listen server, skips that peer on a dedicated server, and creates players for remote peers on either server mode. Clients do not authoritatively create players. On peer removal it requests despawn and removes the registry entry even if despawn throws.

The gameplay-specific spawn delegate owns the concrete player scene. The replication sandbox currently supplies `RawPlayer`, a minimal `Node3D` with replicated `PlayerId`, and spawns it with the owning peer before tree entry. `RawPlayer` logs the deliberate distinction between local ownership and server-held Godot authority. This is manual sandbox evidence only; it is not a general player-character or input system.

## Current composition roots

`NetworkProbe` and `ReplicationProbe` each construct runtime, peer registry, ENet transport, and session in `_Ready`, and release the session before the caller-owned transport in `_ExitTree`. This duplication is exploratory; a reusable composition-root form remains open.

## Planned direction

Networking is one implemented foundation within the broader GameFactory goal: rapidly building small-session online co-op games. The player-lifecycle foundation is implemented; it is deliberately limited to session-scoped identity, spawn/despawn orchestration, and ownership association. Steam listen-server integration is implemented but not yet runtime-accepted; dedicated Steam servers, Steam authentication, persistent identity, common game-shell infrastructure, co-op gameplay primitives, and reusable diagnostics/tooling remain target areas.

Godot integration tests, multiprocess scenarios, persistent player identity, structured diagnostics, application shell, packaging, and CI are also not implemented. Future common subsystems should first be evaluated against existing Godot libraries, plugins, templates, and open-source work, then adopted, adapted, or built only when a concrete playable scenario demonstrates the need.
