# Netfox Phase 1

## Scope and status

Netfox Phase 1 installs the native GDScript Netfox addon, adds an isolated
Steam-backed NetworkTime probe, and includes an interactive two-player movement
sandbox. It does not introduce GAS, NetfoxSharp, Noray, or a replication migration.

The intended ownership boundary is:

```text
Steam / GodotSteam
    connection and packet transport
        -> Godot MultiplayerApi
            -> Netfox NetworkEvents and NetworkTime
                -> GameFactory sandbox spawning and interactive movement
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

The vendored `RollbackSynchronizer.get_last_known_input()` has one local
source-level correction: it calls `_PropertyHistoryBuffer.get_latest_tick()`
rather than `keys()`. The latter is a `Dictionary` API and causes a runtime
error because `_PropertyHistoryBuffer` is a `RefCounted` wrapper. This keeps
the documented public accessor usable for diagnostics; it does not alter
history retention, authority, prediction, transport, or gameplay behavior.

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

## Interactive movement playground

`sandbox/netfox/netfox_gameplay_probe.tscn` is selected by `--run=netfox-gameplay`.
Launch one instance with `--steam-host`, then a second instance with the printed
`--steam-lobby=<id>`. Each process controls only its own bright outlined marker
with WASD. The host marker is blue and a client-owned marker is orange.

The sandbox follows Netfox's responsive-player-movement pattern: `Input` gathers
WASD once per `NetworkTime.before_tick_loop`; `Simulation` advances only from
that input in `_rollback_tick`; `RollbackSynchronizer` records
`Input:movement` and `Simulation:simulated_position`; and `TickInterpolator`
smooths that same state property for presentation. Player root and simulation
remain server-owned, while only `Input` uses the owning peer's authority. The
scene roots and authorities are established by the reusable
`NetfoxRollbackPlayerComponent`, a direct `NetworkObject` child. It runs in
`_EnterTree()` before the host's Netfox children perform their normal
enter/ready initialization; the sandbox does not manually invoke
`process_settings()`. The component owns only this split-authority/root glue;
the prefab retains its explicit Netfox property lists and its sandbox-specific
input, simulation, and presentation behavior.

This is a manual playground, not the former deterministic divergence/convergence
acceptance scenario. Its small `netfox.movement` logs report player spawn and
configuration, input activation, and rate-limited local/remote movement.

The component extraction smoke `netfox_component_20260912_195800` verified the
split authority topology on both peers and visual bidirectional movement. The
existing harness recorded a false-negative failure at
`host_movement_observed_by_client`: it required one rate-limited
`remote_player_moved` event within its observation window, although the client
showed advancing host-player state history, a changing presentation, and later
emitted that remote movement event. That acceptance predicate is tracked as a
separate harness concern; it is not evidence that the component failed to
replicate movement.

When investigating a two-account run, each player additionally writes one
`netfox.history_age/sample` and one `netfox.transport_cadence/sample` event per
second. They record Netfox time/rollback ticks, known input/state ticks and
ages, prediction state, peer status, available Godot peer packets, and the
number and size of known-history advances in the preceding sample window.
The probe also writes an explicit `steam.peer_status` record when the native
peer status changes and once per second, so the transport handshake can be
attributed before Netfox player/topology diagnostics begin.
`netfox.history_age/threshold_crossed` is emitted once per player/history kind
at 32, 48, 56, and 64 ticks. These are diagnostics only: they do not alter
Netfox history size, authority, prediction, input broadcast, movement, Steam,
or Godot polling.

`netfox.reconciliation` records correction windows rather than every non-fresh
Netfox tick. A `correction_window_started` event is emitted only when a
non-fresh re-simulation produces a different result for the same simulation
tick. Once per second at most, `correction_window_summary` reports that
window's first/last replay ticks, replayed-tick count, number of same-tick
corrections, maximum correction magnitude, maximum presentation error, and
whether the visual state returned within the convergence bound.
`max_same_tick_correction_pixels` is the distance between a previously cached
and a re-simulated result for the same simulation tick; it is not a guessed
transport or clock offset. The current
scene draws the separate `Presentation` node, which copies the completed
simulation state after a tick loop and is then driven by `TickInterpolator`.
`presentation_sample` records the visual transform's distance from the
rollback simulation and its one-second maximum. After a non-zero correction,
the next correction-window summary reports whether the visual state returned
within an 8-pixel bound. This preserves simulation and authority ownership
while making visual convergence observable.

Use repeated sampling only after a baseline attempt succeeds:

```powershell
.\tools\ab_test\run.ps1 -Scenario steam_basic
.\tools\ab_test\run_suite.ps1 -Scenario netfox_time_sync -Attempts 3
```

Each process scenario follows `docs/testing-protocol.md`; the suite preserves
separate per-attempt evidence and cleanup results.

For `netfox_gameplay`, the A/B harness establishes each manual checkpoint with
the shared event UTC rather than process-local elapsed time. It requires the
source player to show a post-input simulated position change and the receiving
peer to show a post-input change in that remote player's
presentation-versus-simulation distance. This prevents pre-connection input
from satisfying a later stage and avoids treating state-tick traffic alone as
movement evidence.
