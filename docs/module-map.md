# Module Map

This inventory describes tracked paths and current responsibilities. Directories, scenes, resources, and assets use lowercase or snake_case; C# namespaces, types, and filenames use PascalCase.

## Core modules

| Path | Responsibility | Status |
|---|---|---|
| `factory/runtime/` | Runtime modes and context. | Implemented |
| `factory/networking/peers/` | Transient `PeerId`, peer model, and local registry. | Implemented |
| `factory/networking/players/` | Session-scoped player IDs, registry, and server-side lifecycle delegates. | Implemented |
| `factory/networking/objects/` | Compositional network object host, authority, and replication components. | Implemented |
| `factory/networking/world/` | Dynamic object IDs, generated spawn groups, and world spawn/despawn routing. | Implemented |
| `factory/steam/` | Process-lifetime Steam platform owner plus scene-local session/lobby/peer boundary and GodotSteam adapter bridge. | Implemented listen-server path |
| `factory/diagnostics/` | Structured process logs, replication confirmation, and distributed session evidence. | Implemented |
| `factory/shell/` | Minimal C# bootstrap/host/leave flow plus project-owned Maaack options composition around the retained Steam gameplay probe. | Implemented shell slice |
| `addons/maaacks_game_template/` | Vendored Maaack Game Template: local menus, settings, remapping, loading, audio, and optional local game helpers. | Implemented dependency |
| `addons/plugin_updater/` | Vendored Maaack Plugin Updater required by the full template's GDScript classes. | Implemented dependency |

`factory/steam/SteamPlatform` owns the process-lifetime GodotSteam adapter; `SteamSession` owns one current online lobby and `MultiplayerPeer` lifecycle. No generic transport or generic network-session module exists. `RuntimeMode.DedicatedServer` is retained as an intended gameplay role, but dedicated Steam hosting is not implemented.

## Development and verification

| Path | Responsibility | Evidence |
|---|---|---|
| `sandbox/steam/` | Steam/lobby smoke, native re-host dependency smoke, and manual or test-only two-account gameplay acceptance probe. | Steam acceptance laboratory |
| `sandbox/launcher/` | Registered development/exported launcher. | `--run=steam` or `--run=steam-gameplay` |
| `tests/GameFactory.Tests/` | Engine-independent xUnit tests for pure policy, values, registries, and diagnostics. | Automated baseline |
| `tools/` | Local build helpers plus the host-PC-to-VM Steam A/B acceptance harness. | Developer tooling / external-environment acceptance |

The repository has no generic ENet adapter, Godot integration harness, multiprocess runner, persistent identity, packaging workflow, or CI configuration. Maaack's global-state/progression/win-loss helpers are installed but intentionally not authoritative multiplayer systems.

## Supporting files

`GameFactory.csproj` configures Godot SDK 4.7.1, .NET 8, and conditional Android .NET 9. `project.godot` configures the normal shell plus explicit probe routing. `docs/` contains the charter, architecture, terminology, testing strategy, coding standards, this map, Steam notes, and ADRs.
