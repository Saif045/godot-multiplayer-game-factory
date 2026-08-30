# Architecture

This is the canonical architectural overview. It distinguishes current implementation from planned work.

## Current dependency shape

```text
Steam and Steam-gameplay probes
└── SteamSession -> ISteamAdapter -> GodotSteamAdapter -> GDScript bridge
    -> GodotSteam / SteamMultiplayerPeer -> Godot MultiplayerApi
        ├── PeerRegistry / PlayerLifecycle / PlayerRegistry
        ├── NetworkWorld -> NetworkObject -> authority and replication components
        └── gameplay RPC, MultiplayerSpawner, and MultiplayerSynchronizer
```

Steam/Godot `MultiplayerPeer` is the accepted online session path. There is no generic transport or generic session coordinator: neither had a second concrete use and both duplicated the actual Steam/Godot lifecycle. The `SteamPlatform` autoload owns the process-lifetime GodotSteam adapter and shuts Steam down once at application exit. Each `SteamSession` owns one friends-only lobby and the peer it assigns to Godot's `MultiplayerApi`; leaving a gameplay scene does not reinitialize Steam. This is Steam-specific by design.

## Application shell

Maaack Game Template is the vendored GDScript application-shell dependency. It supplies maintained main/pause/options menus, persistent local settings, input remapping, loading UI, menu navigation, audio/UI helpers, credits, and local save/global-state/level patterns. `factory/shell/` is intentionally thin C# glue: bootstrap selects the normal shell or an explicit `--run` probe, the host flow composes the retained Steam gameplay probe, and leaving asks that probe to perform its existing `SteamSession` teardown before Maaack returns to the menu. The project-owned options composition includes Controls, Audio, and Video only: Maaack remaps and persists the project `InputMap`; GameFactory defines the default action names; future games define action behavior. Maaack's example Game tab and currently inert sensitivity sliders are not composed.

Maaack facilities are local application/UI tools, not multiplayer authority. Future lobby/run phases, progression, win/loss, and results remain GameFactory-owned server-authoritative concepts. Maaack's corresponding example helpers are available for evaluation but are not composed into the current networked flow.

Gameplay code is not coupled to GodotSteam. `ISteamAdapter` and `GodotSteamAdapter` isolate the bridge; raw Godot networking remains usable above that boundary. The current adapter covers lifecycle, local identity, lobbies, overlay/presence operations, and Steam-ID/peer mapping. Dedicated Steam servers and Steam authentication are not implemented APIs; `RuntimeMode.DedicatedServer` remains a valid future gameplay role.

## Runtime identities and gameplay layers

`RuntimeContext` names `Offline`, `Client`, `ListenServer`, and `DedicatedServer`. `PeerId` is a positive transient Godot peer ID; `PeerRegistry` is a local view of known peers. `PlayerId` is a positive session-scoped player identity, not a Steam/account identity. `NetworkObjectId` identifies a dynamic runtime object. These identities remain separate.

`PlayerLifecycle` is engine-independent server-side orchestration over runtime, peer, and player registries plus gameplay spawn/despawn delegates. Listen servers create the local server player; dedicated servers do not. Clients never authoritatively create players.

`NetworkObject` is an open component host attached beneath a gameplay node. Default authority and replication component scenes are replaceable, discovered through capability interfaces. `ReplicationComponent` owns its `MultiplayerSynchronizer` and interprets `[Replicated]` metadata; gameplay can still use direct Godot RPC where appropriate.

`NetworkWorld` server-allocates positive IDs, owns dynamic object registration, and routes scene instantiation through generated `MultiplayerSpawner` groups. It binds object identity and represented owner peer before tree entry. Owner-peer metadata does not transfer Godot authority from the server. Static/authored object registration and persistent identity are planned.

## Evidence and direction

The Steam-gameplay probe has both manual two-account evidence and a concrete host-PC-to-VM acceptance scenario covering lobby membership, native/Godot connection, player lifecycle, NetworkWorld spawn/despawn, server-authoritative door mutation, replicated-revision acknowledgement, and distributed diagnostics. The external harness requires two real Steam accounts and is not a generic Godot integration framework or CI coverage.

Future work must be justified by playable co-op slices. Potential areas include dedicated Steam servers when there is a real deployment need, persistent identity, Godot integration/multiprocess testing, CI, packaging, and gameplay primitives. Do not reintroduce a platform-neutral transport/session layer without a concrete second implementation that needs it.
