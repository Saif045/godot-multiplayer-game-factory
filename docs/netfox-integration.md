# Netfox Phase 1

## Scope and status

Netfox Phase 1 installs the native GDScript Netfox addon and adds an isolated
Steam-backed NetworkTime probe. It does not introduce movement, prediction,
rollback gameplay, GAS, NetfoxSharp, Noray, or a replication migration.

The intended ownership boundary is:

```text
Steam / GodotSteam
    connection and packet transport
        -> Godot MultiplayerApi
            -> Netfox NetworkEvents and NetworkTime
                -> GameFactory sandbox diagnostics and acceptance scenarios
```

`SteamSession` remains the only owner of lobby creation, joining, and the
`SteamMultiplayerPeer` installed into Godot. `NetworkEvents` is the only owner
of `NetworkTime.start()` and `NetworkTime.stop()`; the sandbox does not call
either method itself. Existing `MultiplayerSynchronizer` replication remains
unchanged.

## Pinned dependency

| Field | Value |
|---|---|
| Dependency | `foxssake/netfox` |
| Version | `v1.35.3` |
| Upstream archive | `netfox.v1.35.3.zip` |
| Archive SHA-256 | `aba89f4e43031cadd643483904dd0844ea89368f72815cad343d02a21f795fb7` |
| Vendored paths | `addons/netfox/`, `addons/netfox.internals/` |
| Excluded | `netfox.noray`, NetfoxSharp, Netfox extras |

The archive is preserved upstream under the normal addon layout. Both upstream
editor plugins are enabled in `project.godot`; they manage the five Netfox
autoloads: `NetworkTime`, `NetworkTimeSynchronizer`, `NetworkRollback`,
`NetworkEvents`, and `NetworkPerformance`. Their generated autoload entries are
not hand-authored replacements for the plugin mechanism.

On a fresh import Godot may need one clean editor restart after the plugins
first add their interdependent autoloads. The first activation can compile
scripts before all autoload names exist; the next editor startup must be clean
before treating the addon as enabled.

## Phase-1 probe and evidence

`sandbox/netfox/netfox_time_probe.tscn` is selected by:

```text
--run=netfox
--test-scenario=netfox_time_sync
```

It uses the existing `SteamPlatform` and `SteamSession`. The probe observes
Netfox's actual signals and records them through `GameLog`; it does not create a
second logger or session abstraction. Relevant `netfox.time` events are:

```text
probe_ready
initial_sync_complete
client_sync_complete
tick_progress
client_sample_sent
client_sample_received
stopped
```

Metrics include role, lobby and Godot peer IDs, tick/time values, local and
remote tick/time, client RTT, configured tickrate, initial-sync timing, and
monotonic-tick evidence. The probe waits for 30 ticks after initial sync before
reporting a sample, so it does not treat singleton existence as a running tick
loop.

`tools/ab_test/run.ps1 -Scenario netfox_time_sync` keeps the existing A--F
Steam/Godot checkpoints, then proves host/client time sync, host observation of
client sync, monotonic host/client ticks, a client RTT sample, and
`NetworkEvents`-owned stop events. A Steam/Godot failure before Netfox time
sync is attributed to its earlier layer rather than Netfox.

Use repeated sampling only after a baseline attempt succeeds:

```powershell
.\tools\ab_test\run.ps1 -Scenario steam_basic
.\tools\ab_test\run_suite.ps1 -Scenario netfox_time_sync -Attempts 3
```

Each process scenario follows `docs/testing-protocol.md`; the suite preserves
separate per-attempt evidence and cleanup results.
