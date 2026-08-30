# GameFactory

GameFactory is a reusable Godot C# foundation for rapidly building small-session online co-op games. It supports player-hosted/listen-server and future dedicated-server games, with server-authoritative shared gameplay as the default.

It is infrastructure, not a custom engine or a game: individual games retain their mechanics, content, progression, balance, and art. The factory preserves normal Godot workflows and direct Godot access where a reusable layer is not justified.

## What exists today

- runtime roles, typed transient `PeerId`, and a process-local `PeerRegistry`;
- session-scoped `PlayerId`, player registry, and server-side player lifecycle orchestration;
- compositional `NetworkObject`, authority/replication components, and dynamic `NetworkWorld` spawning;
- Steam listen-server flow: `SteamSession` -> `ISteamAdapter` -> `GodotSteamAdapter` -> GDScript bridge -> `SteamMultiplayerPeer`;
- structured local and distributed diagnostics; and
- Maaack Game Template shell infrastructure (menus, settings, input remapping, loading, pause, UI audio, and local save helpers); and
- a host-PC-to-VM Steam A/B acceptance harness, manual Steam gameplay probes, and engine-independent xUnit coverage for pure policy and data layers.

`NetworkObjectId` is runtime object identity; `PlayerId` is session-scoped player identity; `PeerId` is the transient Godot multiplayer peer identity. They are deliberately distinct. `NetworkObject` is not a universal gameplay base class.

The accepted online path is Steam/Godot `MultiplayerPeer`, not a generic transport framework. `SteamSession` owns Steam lobby and peer lifecycle. Gameplay remains above Godot's `MultiplayerApi`, `PeerRegistry`, `PlayerLifecycle`, `NetworkWorld`, and object components, and does not call GodotSteam directly.

Real two-account listen-server acceptance has exercised peer join/leave, player lifecycle, dynamic world spawn/despawn, late join, server-authoritative door interaction, replicated revision acknowledgement, and distributed diagnostics. This is strong manual evidence, not a release or compatibility guarantee. Persistent identity, authored/static network objects, CI, packaging, and dedicated Steam servers remain planned.

## Current configuration

The project uses Godot .NET SDK 4.7.1 and .NET 8, with conditional .NET 9 for Android. It vendors GodotSteam 4.22 under `addons/godotsteam/`; Windows binaries are rebuilt from Steamworks SDK 1.65 with the documented re-host teardown patch in `third_party/patches/godotsteam/`.

Normal launch enters the Maaack-backed main menu. `Host Game` uses Maaack's loading path to enter the retained Steam gameplay acceptance scene. `Esc` opens Maaack's pause/options UI; leaving first calls the existing Steam-session teardown then returns to the menu. Explicit `--run=steam-gameplay` and `--run=steam` still bypass the shell for focused development probes. Both use development App ID 480 only.

The project supplies configurable default input actions—`move_forward`, `move_backward`, `move_left`, `move_right`, `jump`, `sprint`, `crouch`, `interact`, `primary_action`, `secondary_action`, `drop_item`, and `ping`—with keyboard/mouse and controller bindings. Maaack's Controls UI owns remapping, reset, and local persistence; its startup configuration autoload reapplies saved bindings on the next launch. These are defaults for future co-op games, not implemented movement or mandatory gameplay APIs; individual games may add, remove, or reinterpret actions.

The project vendors Maaack Game Template `bd17ed931190dd32f15d97b5d9d1e0ecc94f3844` (version `1.6.0-dev-2`, MIT) and its required Maaack Plugin Updater `b3908ffe0e336500156fe1cfca2b30bbd0e18484` (version `0.5.1`, MIT) under `addons/`. Update by reviewing a pinned upstream revision, replacing only the upstream addon folders, rerunning the Godot smoke, and preserving each upstream license/attribution. Maaack owns local shell/UI features; it does not define authoritative multiplayer lobby, run, progression, win/loss, or results state.

## Design philosophy

1. Convention for common behavior.
2. Explicit configuration for recognized variations.
3. Replacement or raw Godot access where the domain warrants it.

The project prefers composition over mandatory gameplay inheritance, small settled vertical slices over speculative abstractions, and deliberate investigation of useful Godot libraries/plugins/templates before rebuilding common subsystems.

## Repository map

- `factory/runtime/` — process role and runtime context.
- `factory/networking/peers/` — peer value and registry.
- `factory/networking/players/` — session-scoped player identity and lifecycle.
- `factory/networking/objects/` and `world/` — compositional objects and dynamic world spawning.
- `factory/steam/` — Steam-specific platform/session boundary.
- `factory/diagnostics/` — local and distributed diagnostic records.
- `factory/shell/` — thin C# application-flow bridge around Maaack UI and Steam teardown.
- `addons/maaacks_game_template/` — vendored generic shell dependency; `addons/plugin_updater/` is its required updater dependency.
- `sandbox/steam/` — manual Steam and Steam-gameplay acceptance probes.
- `tests/GameFactory.Tests/` — engine-independent xUnit tests.
- `tools/ab_test/run.ps1` — host-PC-to-VM Steam acceptance harness (requires the documented local VM/Steam setup).

## Documentation

- [Project charter](docs/project-charter.md)
- [Architecture](docs/architecture.md)
- [Terminology](docs/terminology.md)
- [Module map](docs/module-map.md)
- [Coding standards](docs/coding-standards.md)
- [Testing strategy](docs/testing-strategy.md)
- [Architecture decision records](docs/decisions/README.md)
- [Networking foundation](docs/networking-foundation.md)
- [Steam integration](docs/steam-integration.md)
