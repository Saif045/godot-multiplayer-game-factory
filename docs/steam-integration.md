# Steam Integration

## Status

GameFactory has an accepted manual Steam listen-server path. A real two-account run exercised host/join/leave, Godot peer join/disconnect, player lifecycle, world spawning/despawning, late join, server-authoritative door interaction, replicated revision acknowledgement, and host-collected distributed diagnostics. This does not claim production App ID, shipping, or compatibility readiness.

## Dependency and boundary

- GodotSteam GDExtension 4.22, built against Steamworks SDK 1.65.
- Source lives in `addons/godotsteam/` with the upstream MIT license.
- Windows x86_64 binaries include the documented close fix, recovered pending-peer dedupe, and callback lifecycle/connection ownership guards in [`third_party/patches/godotsteam/README.md`](../third_party/patches/godotsteam/README.md). Repeated runtime acceptance remains incomplete; see the linked patch record for validation limits.
- Development uses Steam App ID 480 only.

`SteamPlatform` is a process-lifetime autoload that owns the `GodotSteamAdapter` and shuts the Steam singleton down only on application exit. Each scene-local `SteamSession` calls that shared `ISteamAdapter`; `GodotSteamAdapter` calls the project GDScript bridge, the only GameFactory code that knows GodotSteam's singleton and `SteamMultiplayerPeer`. A session installs the returned peer into Godot's `MultiplayerApi`. Existing Godot RPC, spawners, synchronizers, `NetworkWorld`, and player lifecycle remain above it and have no GodotSteam dependency.

The flow is explicit: initialize Steam once per application, then for every session host or join a friends-only lobby, create/assign its Steam peer, close and clear that peer from Godot, and leave the lobby. Peer teardown is idempotent: an already-disposed peer is treated as clean state, and a close/dispose error is recorded without preventing lobby leave. Incoming invites are surfaced rather than silently joining an active session. Steam IDs and Godot peer IDs remain distinct.

## Manual probes

The exported development launcher defaults to `--run=steam-gameplay`; `--run=steam` selects the focused lobby probe. The probe supports `--steam-host`, `--steam-lobby=<id>`, and interactive host/invite/join/leave actions. The launcher intentionally selects registered scenes rather than unsupported arbitrary `--scene` overrides.

`tools/ab_test/run.ps1` can run the existing gameplay probe as a two-account acceptance scenario before future networking work begins. It extracts the active host lobby ID from `ab_test.scenario/host_ready` structured diagnostics, generates and SCPs a per-run client configuration, then triggers a preconfigured interactive VM task. The test-only `--test-scenario=steam_basic` path waits for actual peer/player/world state, mutates the authoritative probe door, and records host/client completion markers. It does not add production gameplay behavior or automate Steam through GUI input.

`sandbox/steam/steam_native_rehost_probe.tscn` remains a dependency smoke: with Steam active, verify `create_host(0)`, `close()`, then `create_host(0)` succeeds. It validates the vendored peer's teardown independently of gameplay.

Dedicated Steam servers, Steam authentication, host migration, production App ID setup, and export/shipping configuration remain future work. The normal application has Main Menu, Host Game, Join Game, and a lobby shell.

## Launch policy and overlay capability

`SteamLaunchOptions.Development` is the explicit current configuration: App ID
480, development mode, direct launch allowed, no automatic Steam relaunch.
Neither a debug/release template nor overlay availability selects production.
The adapter passes that ID to `Steam.steamInitEx(app_id, true)` once, then reads
the user and actual App ID. Connecting Godot signals in the bridge's `_ready`
does not invoke Steamworks interfaces. Project settings explicitly disable
`steam/initialization/processes/initialize_on_startup`; the vendored default is
also false. Embedded callbacks are enabled by the explicit initialization call.

Development supports direct EXE, scheduled-task, and host/VM harness launch
with an already usable Steam client. Overlay attachment is not guaranteed and
is never networking-health evidence. Join Game remains the reliable development
join path. Invite acceptance in an already running application is supported
when the overlay exists; these changes do not repeat two-account invite
acceptance or claim cold-start acceptance.

Invite Friends checks the live overlay capability at each click. When unavailable,
the normal lobby displays: "Steam Overlay is unavailable. Your friend can still
join from Join Game." The lobby and networking remain active. When available,
it opens the existing Steam invite dialog. Startup logs separate
`steam.launch/policy` from `steam.platform/ready` and record configured/actual
App ID, policy, development mode, Steam initialization, Steam user ID, and
overlay capability. CWD and executable-adjacent `steam_appid.txt` presence are
recorded separately without paths. Overlay capability changes and toggle
callbacks have separate structured events; an initial false can become true
after attachment.

Valve's [API startup contract](https://partner.steamgames.com/doc/sdk/api)
requires `SteamAPI_RestartAppIfNecessary` as the first Steamworks call before
initialization. True means quit the calling process promptly; Steam starts its
registered installed application, which may be a different executable. False
means continue. A development `steam_appid.txt` makes restart return false.
Calling restart with 480 would normalize to Steam's registered Spacewar entry,
not GameFactory, so GameFactory never invokes it for development.

Valve's [overlay requirements](https://partner.steamgames.com/doc/features/overlay)
explain that Steam-owned launch hooks the process automatically. In direct
development launch, initialization must precede graphics-device creation for
the overlay to hook that creation. GameFactory's autoload initialization runs
after Godot graphics startup. This is consistent with the operator's observation
that their existing non-Steam GameFactory shortcut has an overlay while direct
launch does not; API/networking success alone cannot prove attachment. A
non-Steam shortcut is a development comparison, not real-App-ID launch or
cold-start invite acceptance.

### Production integration point (deliberately not activated)

`SteamLaunchOptions.Production(realAppId)` rejects unset/zero and 480. No real
GameFactory App ID is configured. Its pure decision seam can return Continue or
RelaunchRequested using a fake restart delegate in unit tests; test IDs never
reach Steam. This defines policy, **not an operational production bootstrap**.

The exact GodotSteam 4.22 binding is `restartAppIfNecessary(uint32_t app_id)`;
`godotsteam.cpp` binds it under that name and forwards to Valve. However,
`register_types.cpp` constructs the Steam singleton at CORE initialization
before GameFactory autoloads. Its `STEAM_CALLBACK`/`CCallback` constructor
members call `SteamAPI_RegisterCallback` (SDK `steam_api_internal.h`) even with
auto-init disabled. Therefore an autoload restart would not be the first
Steamworks call. No restart bridge or late relaunch/quit wiring was added.

Production requires a focused early native bootstrap before constructing that
singleton (and before Steam initialization): evaluate restart with the real ID;
on true, terminate before GameShell/networking; on false, proceed to ordinary
initialization. Validate that ordering and process exit on an installed real
Steam application before activating production configuration. Overlay is
expected under correct Steam launch, subject to user settings and attachment
delay. Remove `steam_appid.txt` from the depot. Steam launch registration plus
existing `+connect_lobby <id>` parsing and future `+connect` handling can then
receive real cold-start invite acceptance; 480 cannot validate it.

### Development file, export, and working-directory policy

The source checkout's machine-local, ignored `steam_appid.txt` contains `480`.
It is not committed and must not be removed to force a restart. The current
`tools/build_test_client.ps1` invokes `--export-debug` and does not copy the file.
Its development export manifest contains EXE, console wrapper, PCK, debug
GodotSteam DLL, and `steam_api64.dll`; the file is absent beside the executable.
Explicit `steamInitEx(480, true)` sets SteamAppId/SteamGameId before InitEx in
the vendor implementation, allowing that export to initialize without the file.
Godot's all-resources preset does not include arbitrary TXT files unless an
include filter requests them; the current include filter is empty.

The Windows preset maps debug and release to their matching GodotSteam DLLs;
both use the same GameFactory development policy. Only the debug helper export
has been runtime-validated for this task; release/depot packaging is not claimed.
The host runner uses its configured export directory as CWD; the VM runner uses
the executable's containing immutable release directory. Direct-launch CWD
depends on the caller; the task's local direct comparison uses the export
directory. This distinction matters because Valve searches CWD for the file.

### Local acceptance

`--run=steam-launch-acceptance` selects a bounded single-account sandbox driver
that opens the real Main Menu and Join Game screen, hosts through the actual
Host Game button, verifies the connected host peer/lobby, clicks Invite Friends
if the overlay is unavailable, checks its visible fallback and retained lobby,
leaves, and quits. It records App ID/user, live overlay availability, callback
count, and terminal status. It does not prove a second-account join, visual
Shift+Tab, or cold-start invite acceptance. The outer operator must verify the
export manifest/dependencies, impose a process timeout, preserve logs, and
verify cleanup following the normal testing protocol.

On 2026-10-03, final immutable development export `gf_a9899d77_dfdd1e20e445`
passed direct and existing `GameFactoryHost` scheduled-task acceptance. Both
reported successful initialization, actual App ID 480, the same Steam user,
overlay disabled after the attachment observation window, zero toggle callbacks,
Main Menu/Join Game discovery/Host Game success, and visible invite fallback
without leaving the lobby. Both left cleanly; the outer process cleanup check
passed, runner config was restored, and no VM was launched. Evidence:
`artifacts/steam_launch/test_steam-Direct-final/` and
`artifacts/steam_launch/test_steam-Scheduled-final/` (`result.json`, stdout,
stderr, engine logs), with per-file manifest hashes checked before each launch.
All 80 C# unit tests passed, including seven launch-policy/invite-feedback cases.
Native production relaunch/process-exit tests are deliberately deferred with
the early bootstrap; no test calls restart on 480.

The operator then checked this final EXE directly and through the existing
non-Steam shortcut: Shift+Tab was unavailable directly and worked through
Steam. Structured run `8d09f8d7` agrees: initialization succeeded at App 480,
overlay availability changed from false to true after about two seconds, and
open/close toggle callbacks fired. Direct run `8b01f4bb` initialized at App 480
with overlay false and no availability/toggle event. Both reached Main Menu;
the operator closed both, and no GameFactory process remained. Preserved logs
and the operator report are under `artifacts/steam_launch/manual_comparison/`.
The startup snapshot alone would have missed the delayed attachment.

Both automated acceptance exits emitted a three-instance ObjectDB leak warning.
This task did not investigate that unrelated teardown warning; process cleanup
was verified independently. No production launch, second-account join, release
depot, or cold-start invite claim is inferred from this comparison.

The unchanged baseline `gf_a9899d77_21d8f5bdcd3d` established initialization and
lobby hosting under both launch methods. Its first direct observer attempt
failed its overlay-observation assertion because exported binaries ignore an
external `--script` override; it is preserved as a harness observability failure,
not a Steam/networking failure. The process was stopped and subsequently
confirmed absent. The registered sandbox target above provides the final-build
observation. Baseline evidence remains under `artifacts/steam_launch/`.
