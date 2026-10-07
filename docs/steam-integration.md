# Steam Integration

## Status

GameFactory has an accepted manual Steam listen-server path. Real two-account evidence covers host/join/leave, Godot peer join/disconnect, player lifecycle, world spawning/despawning, lobby-phase join/rejoin, server-authoritative interaction, replicated revision acknowledgement, and host-collected distributed diagnostics. This does not claim production App ID, shipping, or compatibility readiness.

The October 7 [session reuse acceptance](session-reuse-acceptance.md) covers repeated Lobby → Gameplay → Lobby reuse while retaining the session/peer, same-lobby rejoin, fresh round state, disconnect cleanup, terminal Leave, and same-process host/client role reversal. Pre-game/lobby joining is supported; gameplay-phase joining is intentionally unsupported. The ordinary Join Game discovery check passed because Gameplay hid the lobby, which returned after reopening. The direct already-connected `late_join_rejected` fallback branch was not deliberately forced in final acceptance; readiness phase rejection is separate evidence.

## Dependency and boundary

- GodotSteam GDExtension 4.22, built against Steamworks SDK 1.65.
- Source lives in `addons/godotsteam/` with the upstream MIT license.
- Windows x86_64 binaries include the documented close fix, recovered pending-peer dedupe, and callback lifecycle/connection ownership guards in [`third_party/patches/godotsteam/README.md`](../third_party/patches/godotsteam/README.md). Repeated session reuse and same-process role reversal are accepted on the tested path. Intermittent BadCert/native Steam behavior remains unresolved; later passing runs do not prove permanent resolution (see [transport history](current-state.md#transport-history)).
- Development uses Steam App ID 480 only.

`SteamPlatform` is a process-lifetime autoload that owns the `GodotSteamAdapter` and shuts the Steam singleton down only on application exit. Each scene-local `SteamSession` calls that shared `ISteamAdapter`; `GodotSteamAdapter` calls the project GDScript bridge, the only GameFactory code that knows GodotSteam's singleton and `SteamMultiplayerPeer`. A session installs the returned peer into Godot's `MultiplayerApi`. Existing Godot RPC, spawners, synchronizers, `NetworkWorld`, and player lifecycle remain above it and have no GodotSteam dependency.

The flow is explicit: initialize Steam once per application, then for every session host or join a friends-only lobby, create/assign its Steam peer, close and clear that peer from Godot, and leave the lobby. Peer teardown is idempotent: an already-disposed peer is treated as clean state, and a close/dispose error is recorded without preventing lobby leave. Incoming invites are surfaced rather than silently joining an active session. Steam IDs and Godot peer IDs remain distinct.

## Manual probes

Normal launch enters the main menu and online lobby shell. The explicit sandbox launcher defaults to `steam-gameplay`; `--run=steam` selects the focused lobby probe. The probe supports `--steam-host`, `--steam-lobby=<id>`, and interactive host/invite/join/leave actions. The launcher intentionally selects registered scenes rather than unsupported arbitrary `--scene` overrides.

`tools/ab_test/run.ps1` can run the existing gameplay probe as a two-account acceptance scenario before future networking work begins. It extracts the active host lobby ID from `ab_test.scenario/host_ready` structured diagnostics, generates and SCPs a per-run client configuration, then triggers a preconfigured interactive VM task. The test-only `--test-scenario=steam_basic` path waits for actual peer/player/world state, mutates the authoritative probe door, and records host/client completion markers. It does not add production gameplay behavior or automate Steam through GUI input.

`sandbox/steam/steam_native_rehost_probe.tscn` remains a dependency smoke: with Steam active, verify `create_host(0)`, `close()`, then `create_host(0)` succeeds. It validates the vendored peer's teardown independently of gameplay.

Dedicated Steam servers, Steam authentication, host migration, production App ID setup, and export/shipping configuration remain future work. The normal application has Main Menu, Host Game, Join Game, and a lobby shell.

## Development launch and overlay

Development uses App ID 480. GodotSteam auto-initialization is explicitly
disabled; SteamPlatform owns the adapter and initializes Steam once.

Direct and scheduled/harness launches are supported for development. Steam
networking may work, but overlay attachment is not guaranteed and is not
networking-health evidence. Steam-owned launch has been observed to attach
the overlay.

Invite Friends opens the Steam invite overlay when available. Otherwise it
stays in the lobby and displays: "Steam Overlay is unavailable. Your friend
can still join from Join Game." Running-app invite handling remains supported.

A real production App ID, Steam launch normalization / RestartAppIfNecessary,
and cold-start invite behavior are deferred until an actual game has its own
Steam App ID. The ignored local development steam_appid.txt is not part of
the shipping design.
