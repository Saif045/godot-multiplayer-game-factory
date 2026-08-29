# Steam Integration

## Status

GameFactory has an accepted manual Steam listen-server path. A real two-account run exercised host/join/leave, Godot peer join/disconnect, player lifecycle, world spawning/despawning, late join, server-authoritative door interaction, replicated revision acknowledgement, and host-collected distributed diagnostics. This does not claim production App ID, shipping, or compatibility readiness.

## Dependency and boundary

- GodotSteam GDExtension 4.22, built against Steamworks SDK 1.65.
- Source lives in `addons/godotsteam/` with the upstream MIT license.
- Windows x86_64 binaries include the documented `SteamMultiplayerPeer::_close()` re-host patch in [`third_party/patches/godotsteam/README.md`](../third_party/patches/godotsteam/README.md).
- Development uses Steam App ID 480 only.

`SteamPlatform` is a process-lifetime autoload that owns the `GodotSteamAdapter` and shuts the Steam singleton down only on application exit. Each scene-local `SteamSession` calls that shared `ISteamAdapter`; `GodotSteamAdapter` calls the project GDScript bridge, the only GameFactory code that knows GodotSteam's singleton and `SteamMultiplayerPeer`. A session installs the returned peer into Godot's `MultiplayerApi`. Existing Godot RPC, spawners, synchronizers, `NetworkWorld`, and player lifecycle remain above it and have no GodotSteam dependency.

The flow is explicit: initialize Steam once per application, then for every session host or join a friends-only lobby, create/assign its Steam peer, close and clear that peer from Godot, and leave the lobby. Peer teardown is idempotent: an already-disposed peer is treated as clean state, and a close/dispose error is recorded without preventing lobby leave. Incoming invites are surfaced rather than silently joining an active session. Steam IDs and Godot peer IDs remain distinct.

## Manual probes

The exported development launcher defaults to `--run=steam-gameplay`; `--run=steam` selects the focused lobby probe. The probe supports `--steam-host`, `--steam-lobby=<id>`, and interactive host/invite/join/leave actions. The launcher intentionally selects registered scenes rather than unsupported arbitrary `--scene` overrides.

`sandbox/steam/steam_native_rehost_probe.tscn` remains a dependency smoke: with Steam active, verify `create_host(0)`, `close()`, then `create_host(0)` succeeds. It validates the vendored peer's teardown independently of gameplay.

Dedicated Steam servers, Steam authentication, host migration, production App ID setup, export/shipping configuration, and an application/menu shell are planned only when a concrete need justifies them.
