# Module Map

This inventory describes tracked paths and current responsibilities. Directories, scenes, resources, and assets use lowercase or snake_case; C# namespaces, types, and filenames use PascalCase.

## Core modules

| Path | Responsibility | Status |
|---|---|---|
| `factory/runtime/` | Runtime modes and context. | Implemented |
| `factory/networking/peers/` | Transient `PeerId`, peer model, and local registry. | Implemented |
| `factory/networking/players/` | Session-scoped player IDs, registry, and server-side lifecycle delegates. | Implemented |
| `factory/networking/objects/` | Compositional network object host, authority, and replication components. | Implemented |
| `factory/networking/netfox/` | Reusable Netfox rollback-player lifecycle/split-authority glue and proven visible `CharacterBody3D` composition under `player_3d/`. | Implemented / acceptance-proven |
| `factory/networking/world/` | Dynamic object IDs, generated spawn groups, and world spawn/despawn routing. | Implemented |
| `factory/steam/` | Process-lifetime Steam platform owner plus scene-local session/lobby/peer boundary and GodotSteam adapter bridge. | Implemented listen-server path |
| `factory/diagnostics/` | Structured process logs, exported-build identity, replication confirmation, and distributed session evidence. | Implemented |
| `factory/gameplay/interaction/` | Narrow server-authoritative player interaction request/validation contract and replicated sample switch. | Implemented / acceptance-proven |
| `factory/gameplay/carry/` | Server-owned carryable world item and per-player one-item carrier/drop request boundary. | Implemented / acceptance-proven |
| `factory/gameplay/inventory/` | Server-owned one-slot store/retrieve and equipment transitions; stable item identity and source-owned GAS capability grants/removal, with death/despawn cleanup. | Implemented / acceptance-proven |
| `factory/gameplay/gas/` | GodotGAS adapter and authoritative capability/attribute/tag projections; player-vital Downed/revive/Dead/respawn lifecycle and gameplay action gates. Physical reset consumption and dash motion remain in the Netfox player composition. | Implemented / acceptance-proven |
| `factory/shell/` | Maaack composition, host/join/lobby readiness, reusable Lobby → Gameplay → Lobby rounds retaining the Steam session/peer, and terminal Leave. | Implemented / reuse acceptance-proven |
| `addons/GodotGAS/` | Vendored GodotGAS v1.0.6 abilities, effects, attributes, and tags; GameFactory retains networking/authority ownership. | Implemented dependency |
| `addons/maaacks_game_template/` | Vendored Maaack Game Template: local menus, settings, remapping, loading, audio, and optional local game helpers. | Implemented dependency |
| `addons/plugin_updater/` | Vendored Maaack Plugin Updater required by the full template's GDScript classes. | Implemented dependency |
| `addons/netfox/`, `addons/netfox.internals/` | Netfox source pin `38f59778b02bfd1a3dedc7dcc985945d7058858d` (reports 1.49.3) and internals; persistent command/synchronization/history/identity servers, rollback, time lifecycle, and interpolation. | Implemented / acceptance-proven dependency |

`factory/steam/SteamPlatform` owns the process-lifetime GodotSteam adapter; `SteamSession` owns one current online lobby and `MultiplayerPeer` lifecycle. No generic transport or generic network-session module exists. `RuntimeMode.DedicatedServer` is retained as an intended gameplay role, but dedicated Steam hosting is not implemented.

## Development and verification

| Path | Responsibility | Evidence |
|---|---|---|
| `sandbox/steam/` | Steam/lobby smoke, native re-host dependency smoke, and manual or test-only two-account gameplay acceptance probe. | Steam acceptance laboratory |
| `sandbox/netfox/` | Steam-backed Netfox time/2D diagnostic probes plus a small 3D visual acceptance probe. `NetworkWorld` owns identity/spawning; the player prefab owns Netfox input, history, rollback and presentation. | Phase-1 gameplay integration |
| `sandbox/launcher/` | Registered development/exported launcher. | `--run=steam`, `--run=steam-gameplay`, `--run=netfox`, `--run=netfox-gameplay`, or `--run=netfox-player-3d` |
| `sandbox/shell/` | Focused round reuse/readiness and held stale-input regression probes with bounded local runners. | Session-reuse and persistent-receiver evidence |
| `tests/GameFactory.Tests/` | Engine-independent xUnit tests for pure policy, values, registries, and diagnostics. | Automated baseline |
| `tools/` | Clean/configurable export and manifest helpers plus the build-parity-gated host-PC-to-VM Steam A/B acceptance harness. | Developer tooling / external-environment acceptance |

The repository has no generic ENet adapter or integration framework, persistent identity, shipping packaging workflow, or CI configuration. Focused local multiprocess regressions exist for round reuse and stale input. Maaack's global-state/progression/win-loss helpers are installed but intentionally not authoritative multiplayer systems.

## Supporting files

`GameFactory.csproj` configures Godot SDK 4.7.1, .NET 8, and conditional Android .NET 9. `project.godot` configures the normal shell plus explicit probe routing. `docs/` contains the charter, architecture, terminology, testing strategy, coding standards, this map, Steam notes, and ADRs.
