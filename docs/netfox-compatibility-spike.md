# Netfox persistent-receiver compatibility spike

Status: local compatibility spike PASS; Steam integration accepted on 2026-10-07.
The sections below preserve the original investigation checkpoint. Subsequent
exports, A/B gameplay and focused guard/role acceptance are recorded in
[session reuse acceptance](session-reuse-acceptance.md). The exact source pin
remains frozen; no further upgrade was performed.

## Preserved state and provenance

Baseline: `e462e709d6175de5e9c6dd9fa7b9fec6edf338a3`.
Ignored evidence: `artifacts/netfox_compatibility_spike/` contains status,
diff/stat/binary patch, untracked inventory, WIP hashes and diagnostic backups.
Original round implementation: four `factory/shell` files and
`sandbox/launcher/SandboxLauncher.cs`. Prior diagnostics: `sandbox/shell/`.
Spike changes are the Netfox vendor replacement, project autoload/config
compatibility, probe compatibility/regression work, and this report.

Before replacement both plugins report v1.35.3, tag commit
`c7fc6ea52b4e9087c980d9ca1f8c3826e3a6caa4`; original release archive SHA-256
`aba89f4e43031cadd643483904dd0844ea89368f72815cad343d02a21f795fb7`.
`vendored-before.zip` and `vendored-before-hashes.csv` preserve exact local files.
Comparison against tag source is saved in `vendor-local-deltas.patch` and
`internals-local-deltas.patch`. Local code deltas: last-known-input accessor,
role-paired NetworkEvents shutdown, opt-in lifecycle tracing in seven files.
Release CONTRIBUTORS/import metadata are also inventoried.

Candidate pin: `38f59778b02bfd1a3dedc7dcc985945d7058858d` (2026-09-26),
plugin version 1.49.3, upgrade notes still label these changes **Unreleased**.
This is a source pin, not a claim of release acceptance. Selected after server
redesign `d6ddde4c57574c51d101d18f109d7426743b6be3`, command transfer-mode fix
`f2f808758815a6f91a3a985464526bf6d0a6aeb3`, stop/reset fix `a069726`, and
snapshot sanitization fix included at the pin. The following HEAD Rapier-only
change is outside this spike's selection.

## Source gate before replacement

Old receiver: RollbackSynchronizer creates a child
_RollbackHistoryTransmitter; its `_submit_input` unreliable RPC lives under
the player. Proven prior local_001 arrivals targeted deleted child paths.

New receive chain, all at the pin:

1. NetworkSynchronizationServer registers `_handle_input` as an unreliable
   command. `_synchronize_input` uses its redundant snapshot serializer.
2. NetworkCommandServer is an autoload. Its _RPCTransport child receives
   `_submit_unreliable`; player removal does not remove this child.
3. `_handle_command` dispatches the command ID to `_handle_input`.
4. _RedundantSnapshotSerializer reads sized snapshots; _DenseSnapshotSerializer
   reads identity and a sized node payload **before** resolving identity.
5. NetworkIdentityServer resolves numeric references in its local-ID map,
   or names in its registered-name map. It never asks Godot to find the old
   player NodePath. Unknown identity logs a warning and skips the consumed data.
6. RollbackSynchronizer._exit_tree deregisters simulation, synchronization,
   identity, and history. Identity deregistration erases local-ID/name/peer maps.
   Empty stale snapshots cannot insert properties for a replacement player.
7. `_make_id` increments the persistent server allocator. Deregistration and
   NetworkTime.stop do not reset it. `clear()` explicitly resets it, but no
   automatic caller exists and GameFactory does not call it. Distinct monotonic
   NetworkObject paths protect full-name references too. This guarantee assumes
   no manual clear or same-path re-registration during the live transport.

Upstream tests cover registration/deregistration, unknown identity resolution,
serializers, history and synchronization separately; they do not prove the
deliberately late transport packet scenario. Runtime regression is required.

## Migration surface

| Dependency | Impact | Treatment / evidence needed |
| --- | --- | --- |
| root/simulation authority 1; represented owner Input | No API change | Preserve component and explicit prefab state/input lists |
| NetworkEvents owns time lifecycle | Behavior sensitive | Retain role-paired local correction; run lifecycle test |
| 5 autoloads become 12 | Mechanical | Match pinned plugin autoload list, no new transport owner |
| per-prefab input broadcast/full-state settings | Mechanical | Deprecated; explicit equivalent project settings, history64 |
| prediction/reconciliation and history recording | Behavior sensitive | New global servers; movement and physical replay probes |
| dash/respawn consumption revisions | No gameplay API change, behavior sensitive | Keep production state lists; update recorder-based probe to server history |
| spawn/despawn and allocator | Behavior sensitive | Existing world reset unchanged; assert identity deregistration and no reuse |
| same-process role reversal/time reset | Behavior sensitive | Local correction plus new reset functions; full runtime acceptance later |
| history-age diagnostics | Mechanical/semantic | get_last_known_state now returns age; use server latest-tick API |
| opt-in vendor tracing | Mechanical | Old per-player transmitter removed; retain event trace, new sandbox packet evidence |
| old diagnostics/probes | Mechanical | Preserve old tap; new persistent-target regression separate |
| input gathering/tick hooks, physics_factor, interpolation root | No exposed API change | Existing movement probe |
| Steam/native peers, GAS/discrete replication, round ACK semantics | No change | Outside spike |

Source gate: GO for local experiment, pending runtime evidence. This is not a
GO for Steam A/B or acceptance.

Pinned source references: [command transport](https://github.com/foxssake/netfox/blob/38f59778b02bfd1a3dedc7dcc985945d7058858d/addons/netfox/servers/network-command-server.gd),
[synchronization](https://github.com/foxssake/netfox/blob/38f59778b02bfd1a3dedc7dcc985945d7058858d/addons/netfox/servers/network-synchronization-server.gd),
[identity](https://github.com/foxssake/netfox/blob/38f59778b02bfd1a3dedc7dcc985945d7058858d/addons/netfox/servers/network-identity-server.gd),
[dense serializer](https://github.com/foxssake/netfox/blob/38f59778b02bfd1a3dedc7dcc985945d7058858d/addons/netfox/serializers/dense-snapshot-serializer.gd),
[history](https://github.com/foxssake/netfox/blob/38f59778b02bfd1a3dedc7dcc985945d7058858d/addons/netfox/servers/network-history-server.gd),
[upgrade notes](https://github.com/foxssake/netfox/blob/38f59778b02bfd1a3dedc7dcc985945d7058858d/docs/upgrading.md).

## PROVEN

`late_005/result.json` is PASS, both owned processes exit0, cleanup_live0,
port24872 unoccupied. The production player prefab and OnlineGameplayWorld run
through actual ENet, SceneMultiplayer, command/identity/synchronization/history
servers; no mocked receiver, history merger, or identity lookup.

The host tap captures exactly one already-received, normal unreliable command
packet. Remaining traffic continues. It returns the original bytes only after
the host and client clear round1, and round2 registers a replacement input.
The capture/release hash is identical and the RPC receiver's instance ID is
unchanged. The actual `_submit_unreliable` -> `_handle_command` -> `_handle_input`
stack runs. The observer is connected after the actual command handler.

The held command is the library's input command6. All three redundant snapshots
refer to retired numeric input identity6. That identity no longer resolves by
number or name; the replacement uses identity12. The serializer returns empty
snapshots, stored old-tick history contains no replacement input, and its live
movement remains neutral. Both peers then observe movement of the new player.
Read-only inspection after simulation proves repeated simulation of a production
Simulation node's tick on both peers (`ROLLBACK_REPLAY_OBSERVED`).

No engine errors, invalid/cached RPC targets, resource leaks, transport/time
replacement, input reliability changes, or production delays are used. Expected
upstream unknown-identity warnings remain visible, including observer re-decode
warnings; they are not filtered or suppressed in the game.

## INFERRED

The architecture addresses the original Steam failure because the same
SceneMultiplayer/library receive chain is exercised over ENet. Steam transport
and the complete interactive acceptance matrix are not proven by this probe.
Unknown full-name references use the same safe skip branch by source inspection;
the deliberately held packet exercises the normal established numeric path.
Never manually clear identity tables or reuse player paths under a live peer.

## CHANGED

Both core addon directories replaced coherently from one pin. Only code delta
against the pin is the already-existing NetworkEvents lifecycle correction and
its event tracing, retained byte-for-byte from v1.35.3. Godot regenerates icon
import metadata and removes upstream orphan UIDs during import; provenance
distinguishes these from code changes. Old accessor correction is superseded by
new upstream history APIs. Old transmitter/time/rollback trace hooks are retired;
event tracing and the sandbox command observations remain.

Project adds the seven plugin-defined autoloads and explicit global equivalents
for history64/input-broadcastfalse/full-state24. Sandbox history-age diagnostics
use the new latest-state-tick API; the vital-motion probe uses actual server
history. New sandbox files implement one late-input probe and bounded runners.
Original round implementation and prior sandbox diagnostics retain identical
SHA-256 hashes from the preserved inventory.

## NOT CHANGED

Steam/GodotSteam/native transport, gameplay authority, production input/state
property lists, GAS projections, dash/respawn rules, and round reuse semantics.
No tombstones, new production networking abstraction, error suppression, export,
full host/VM A/B, commit, or push. NetworkEvents continues to own time lifecycle.

## VALIDATION

* C# build: PASS, zero warnings/errors; final build includes the regression.
* Existing unit suite: 73/73 PASS.
* Godot import: second clean import required by interdependent autoload setup.
  Initial runtime cheap_001 was FAIL (stale class cache); preserved.
* cheap_002: lifecycle pairing, physical reset/history replay/movement resume,
  and three local reuse rounds all PASS, no engine errors/leaks; cleanup0.
* late_001/002: scenario assertions pass but tap reference cycle fails exit;
  verbose-only run identifies diagnostic peer/script retention. Cleanup fix
  disconnects the tap's forwarding callbacks, without changing the library.
* late_003: PASS with clean exit and immutable held-packet evidence.
* late_004: automatic rollback replay confirmed; FAIL on client terminal
  `on_despawn_receive` errors because the harness locally freed replicated
  players while host terminal despawns arrived. Preserved. Harness terminal
  teardown now uses existing authoritative reset/clear acknowledgement.
* late_005: full final regression PASS, both peers replay and move, clean exit.
* steam_final_late_001: subsequent final gate FAIL before packet capture;
  client movement/replay conjunction timed out. The observer had required
  spontaneous repeated simulation without requesting a rewind, making the
  precondition depend on timing. No Steam export or A/B started from that gate.
* replay_fix_001: authorized sandbox-only correction PASS. Movement is asserted
  separately, then `before_loop` requests rewind of an already-simulated tick
  with recorded state/input via `notify_resimulation_start`. Matching requested
  and repeated ticks are observed on both peers; the original immutable stale
  input still skips safely, replacement movement passes, and cleanup is clean.
  This tests actual replay without assuming the connection naturally causes it.
* `git diff --check`: PASS; original WIP hash verification PASS.

Each attempt uses a fresh evidence directory, bounded10s assertions/30s process
timeout, dependency-ordered listener/client launch, test-owned process cleanup,
and preserved stdout/stderr/result. The capture hold is test fault injection,
not a production teardown delay. No VM process was launched by this spike.

## REMAINING RISKS AND GO / NO-GO

GO to prepare a fresh frozen Steam A/B integration checkpoint, after separate
authorization to execute it. Stop here at the implementation gate. This does
not accept round reuse or permit a commit. Required remaining evidence includes
repeated two-account round returns, leave/rejoin, unexpected disconnect, host
terminal close, same-process role reversal, and existing gameplay/dash/respawn.

The pin identifies source reporting1.49.3, while upgrade notes still say
Unreleased. Global prediction/history/interpolation changed substantially, and
old NFTRACE transmitter/subscription counters no longer exist. Identity `clear`
or same-path reuse would void the live-peer stale-reference guarantee. Upstream
logs warnings for safe stale skips; full integration must distinguish those
from actual engine or invalid-packet errors without suppression.
