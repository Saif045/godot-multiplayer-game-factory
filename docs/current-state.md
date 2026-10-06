# Current State

This is the compact source of current maturity and evidence. It is not a
replacement for architecture or protocol documentation.

## Proven foundation

- Steam listen-server transport, Godot `MultiplayerAPI`, `NetworkWorld`, and
  `PlayerLifecycle` have accepted two-account Hyper-V evidence.
- Steam lobby/peer lifecycle isolation is accepted for tested fresh join,
  same-lobby rejoin, new-lobby reuse, and role reversal; retained closed peers
  sent no stale handshakes.
- The reusable Netfox v1.35.3 `CharacterBody3D` player composition is
  acceptance-proven: split server-state/client-input authority, rollback,
  interpolation, bidirectional walking/jumping, and queued one-shot jump.
- The host-PC -> SSH/SCP -> GPU-P Hyper-V guest release and interactive-task
  path has verified build parity and cleanup behavior.

- Server-authoritative interaction is acceptance-proven: a local E press
  requests a target by `NetworkObjectId`; the server validates and toggles a
  replicated switch visible to the other peer. Deterministic per-owner player
  colors remain presentation-only.
- Server-authoritative pickup/carry/drop is acceptance-proven. It composes the interaction request path
  with one permanent server-owned carryable item. Held state is a replicated
  holder `NetworkObjectId`; each peer resolves its presentation `CarryAnchor`.
  The server computes world drop transforms and no carry state enters Netfox
  rollback. Both host and client have picked up, moved/jumped with, and
  dropped the item, with remote follow observed on the other peer.
- The first server-authoritative inventory slot is acceptance-proven. A single
  permanent, server-owned cube keeps its `NetworkObjectId` while transitioning
  explicitly between World, Carried, and Stored. The server validates one
  player slot, replicates only its stored-item identity for presentation, and
  never transfers item authority or places inventory state in GAS or Netfox
  rollback. Both owners completed carry → store → retrieve, with the remote
  peer observing the slot transitions.
- Equipment → GAS is acceptance-proven. The same permanent cube transitions
  between World, Carried, Stored, and Equipped without changing identity. The
  server alone grants and removes the `equipment_cube_move_speed` capability;
  ordinary GAS snapshots project its `6 → 12 → 6` speed change while Netfox
  continues to own only movement simulation. An Equip request with no stored
  item is rejected safely rather than constructing an invalid object identity.
- GodotGAS v1.0.6 is vendored under `addons/GodotGAS` and its editor plugin is
  enabled. GameFactory C# can create a real GodotGAS Ability System Component,
  call it, and read state back through the scoped GAS adapter.
- Networked GodotGAS Health is acceptance-proven: the represented owner makes a
  reliable, no-payload request; the server validates `OwnerPeerId`, activates
  the real `SelfDamage` ability, captures `GasSnapshot(Health)`, and replicates
  the ordinary authoritative projection. Non-server ASCs apply that snapshot
  as a local mirror. The host does not replay its own effect and Netfox remains
  outside the slice.
- The first GodotGAS × Netfox boundary is acceptance-proven through the
  temporary three-second SpeedBoost: canonical server GodotGAS owns a
  non-stackable `State.SpeedBoosted` effect and publishes the ordinary
  `GasMoveSpeed` projection. Netfox reads that current projection to calculate
  velocity, but never owns or restores it as rollback state; only movement
  position, velocity, and grounded state remain in rollback history. Both
  owners observed `6 → 18 → 6`, repeated activation was rejected while active,
  and the remote presentation retained the boost until authoritative expiry.
- Sprint + Stamina is acceptance-proven: Netfox records only sprint intent;
  server GodotGAS owns periodic Stamina drain/regeneration and the Sprinting /
  Exhausted tags; normal replication projects Stamina and sprint permission.
  Netfox reads that projection for `6 → 9 → 6` movement while Stamina and
  exhaustion remain outside rollback history.
- Predicted Dash is acceptance-proven: `dash_pressed` is a queued one-shot
  Netfox input, while the server alone activates the canonical GodotGAS Dash
  ability (25 Stamina, `Cooldown.Dash`, blocked by `State.Exhausted`). The
  server publishes only a small authorization revision and cooldown projection;
  Netfox owns the replayable dash duration/direction motion state. Local owner
  prediction begins from the input edge and the authorization revision lets
  normal rollback/reconciliation retain accepted motion or correct a rejected
  prediction. GAS effect/runtime objects remain outside rollback history.

- Player Vital Lifecycle is acceptance-proven: canonical server GAS Downed/Dead
  tags, HP50 teammate revive, server-owned 10-second bleedout and 2-second
  respawn to HP100/Stamina100 with unchanged player identity. Incapacitated
  actions are gated canonically; gravity continues. Carried items auto-drop,
  stored/equipped items and GAS capabilities survive death, and player despawn
  releases hidden inventory/equipment to World and removes equipment effects.

## Latest runtime evidence

Player Vital Lifecycle run `player_vital_lifecycle_20261006_081300` used one
fresh immutable export `gf_629f3e3a_c79372e6ca6e`, manifest
`3aaed13da6d78b69ea1b70709f16d87f2ca9dc7c7dc87457ffd32c60e4a946b1`.
Operator reports and structured evidence together pass the complete slice:
attempt 002 proves bidirectional HP50 revival, action restrictions, ~10s/~2s
bleedout/respawn with stable identities, automatic carry drops, and existing
gameplay; attempt 004 proves equipped-client disconnect, player removal,
equipment effect removal (12 -> 6), and recoverable cube 3; attempt 006 proves
client-owned equipped death/respawn to HP100 and speed12 with unchanged player
and item identity. The ordinary respawn revision/tick is consumed inside
rollback simulation; only reset consumption joins physical history. See
[Netfox integration](netfox-integration.md#player-vital-respawn-boundary).
Accepted logs have zero severe matches. Verify/Stop passed and independent
checks confirmed host=0 / VM=0. Evidence is under
`artifacts/ab_tests/player_vital_lifecycle_20261006_081300/attempt_002`,
`attempt_004`, and `attempt_006` (`evidence.json` and host/client logs).
Deviation: missed manual steps required immutable retries, with no re-export,
source changes, or Steam restart. Attempt 001 stopped at sandbox Task Scheduler
access; attempts 003/005 closed the client before equipped respawn and are
preserved as incomplete checks. Cheap validation passed: C# build, 73 unit
tests, real GAS/composed-player probe (stale request rejection and both hidden
inventory cleanup cases), Netfox history/motion probe, and `git diff --check`.

App 480 development supports direct/harness launch; Steam overlay is not
guaranteed unless Steam owns launch. Invite Friends falls back to Join Game
when the overlay is unavailable. Real-App-ID launch normalization remains
future work. See [Steam integration](steam-integration.md).

Windows startup investigation at `62a8cfc` did not reproduce reported
`0xC0000142 / STATUS_DLL_INIT_FAILED`: 109/109 controlled startup probes passed
before reboot and 109/109 after reboot, including editor-closed and scheduled
task/profile/renderer comparisons. The defect remains unresolved and may recur;
these are baseline results, not post-fix acceptance. Diagnostic-folder discovery
caused separate duplicate Steam editor registrations and was excluded with
`.gdignore`. See [Windows startup investigation](windows-startup-investigation.md)
for evidence, limitations, and the capture procedure if the failure returns.

Netfox lifecycle run `netfox_lifecycle_fix_20261003_02`, attempt 001, passed
normal-shell gameplay (56.8 seconds common PC-host gameplay, then 75.7 seconds
with VM hosting in the same processes), movement/jump/switch acceptance, and
verified host/VM cleanup on build `gf_f7c129f4_d838f603ed97`. Exact traces show
one start, after-sync, and stop per peer/session, correct server-state/owner-input
authority, five unique autoloads, and clean rollback callback teardown.
Historical duplicate-start/self-RPC errors did not reproduce; their origin is
unproven. A confirmed host-close double stop was fixed in Netfox NetworkEvents
with immediate role-paired stop routing. No transport or RPC-target changes
were needed. See [Netfox lifecycle investigation](netfox-lifecycle-investigation.md)
for deviations, local vendor delta, upstream checks, and remaining warnings.

Steam lifecycle run `native_lifecycle_acceptance_d28f7ad_20261003_02` tested
unchanged `d28f7ad` runtime inputs. Attempt 001 passed all four lobby/peer flows.
Attempt 003, after an authorized Steam restart on both machines, passed fresh
join to ConnectedToServer and ready 2/2, Lobby → Start, mutual visibility,
movement/jumping, bidirectional switch interaction, and clean Leave. Host/VM
cleanup was verified. The retained-object native re-host probe also passed.
Details: [Steam peer lifecycle investigation](steam-peer-lifecycle-investigation.md).

Equipment → GAS run `equipment_guard_retest_20260926_034700` passed fresh
export, VM parity, two-player topology, visual host/VM verification, and
cleanup on immutable build `gf_e37a5552_dcc0c761c013`. Both owners completed
World → Carried → Stored → Equipped → Stored → Carried; authoritative logs
show `6 → 12 → 6` GAS speed projections, replicated remote movement, and a
safe empty-slot `equip_rejected` with no engine errors. Evidence:
`artifacts/ab_tests/equipment_guard_retest_20260926_034700/attempt_001/evidence.json`
and its captured host/client logs.

Free-play run `carry_freeplay_retry_20260914_193933` passed infrastructure
startup, two-player topology, and verified cleanup on immutable build
`gf_240d7720_7984bcc0f537`. The operator visually confirmed bidirectional
movement/jumping, switch changes from either participant, and host/client
carry and drop. Structured logs agree: authoritative switch transitions,
replicated remote switch visuals, host- and client-owned pickup/drop cycles,
replicated holder state, remote-follow observations, and no severe events.

Evidence: `artifacts/ab_tests/carry_freeplay_retry_20260914_193933/result.json`,
its captured client JSONL, and the corresponding host run JSONL.

Networked GAS Health run `gas_network_activation_retry_20260914_211000` passed
infrastructure, topology, manifest parity, and cleanup on immutable build
`gf_0c265dd3_2ae74507f072` (`0c265dd`). The operator confirmed both players'
HP changes on both views through 0. Structured logs prove server-only real
GodotGAS activation after owner validation, authoritative
`GasSnapshot(Health)` publication, non-server snapshot mirror application, no
host double-apply, no rejected requests, and no severe events. Evidence:
`artifacts/ab_tests/gas_network_activation_retry_20260914_211000/result.json`

GAS × Netfox SpeedBoost run `gas_speedboost_boundary_20260915_023000` passed
infrastructure, immutable-build parity, topology, and cleanup on
`5bcfbaf`. The operator confirmed the exact host/client and remote visual
contract. Structured evidence shows server-only owner activation, repeated
active-window rejections, authoritative `6 → 18 → 6` effect lifecycles, and
client speed projections that remain at `18` instead of being rollback-reset
in the same frame. No severe events occurred. Evidence:
`artifacts/ab_tests/gas_speedboost_boundary_20260915_023000/result.json` and
its captured host/client JSONL.

Sprint + Stamina run `gas_stamina_sprint_20260915_024000` passed on immutable
build `4f213f5`. Both owners drained `100 → 0` while sprinting, were exhausted
at zero and returned to speed 6, regenerated after release, cleared exhaustion
at 27, and sprinted again. Replicated client projections matched server state;
no severe events occurred. Evidence:
`artifacts/ab_tests/gas_stamina_sprint_20260915_024000/result.json` and its
captured host/client JSONL.

Predicted Dash run `gas_dash_20260915_235900` passed infrastructure, fresh
immutable-build parity, topology, and verified cleanup. The operator visually
passed Dash on both participants. Structured evidence shows the client-owner
input and immediate predicted-start events, server-only canonical acceptance,
each accepted activation's `25`-Stamina cost and `0.75s` cooldown projection,
and replicated authorization revisions consumed by the normal Netfox motion
path. Host-owned activations followed the same server-owned lifecycle. No
severe events occurred. Evidence:
`artifacts/ab_tests/gas_dash_20260915_235900/result.json` and its captured
host/client logs.

Inventory run `inventory_slot_replication_20260915_004100` passed
infrastructure, fresh immutable-build parity, topology, and verified cleanup.
The operator visually passed the same host/client flow as the preceding run.
Structured evidence proves that the server accepted store/retrieve for both
owners, the cube retained `NetworkObjectId` `3`, and the client received the
replicated slot state `0 → 3 → 0` for each owner. No severe events occurred.
Evidence: `artifacts/ab_tests/inventory_slot_replication_20260915_004100/result.json`
and its captured host/client JSONL.

## Transport history

BadCert remains unresolved: attempt 002 of the Steam lifecycle run failed
before Godot admission with certificate errors and unrecovered connection /
rendezvous timeouts. The later Steam-restart comparison passed, but does not
prove the cause, GameFactory ownership, or permanent resolution. The original
second full four-join cycle remains incomplete. Preserve this failure separately
from the accepted lifecycle isolation. The subsequent Netfox investigation
accepted the normal gameplay/process-reuse lifecycle described above.

The free-play launch immediately preceding the accepted run reached lobby
membership and peer assignment but remained native `Connecting` for 120
seconds. It was cleanly terminated; the next unchanged retry reached two-player
topology and passed. Treat this as an intermittent Steam/native transport
symptom, not a carry, interaction, Netfox, or movement defect. Preserve the
evidence and investigate it only if it recurs.

## Recent commits

- `d28f7ad` — native GodotSteam callback lifecycle isolation, rebuilt DLLs,
  reproducible patches, and opt-in diagnostics.
- `45fcd36` — export-helper completion fix and lifecycle/gameplay acceptance record.
- `81458f2` — server-authoritative carryable item.
- `240d772` — carry acceptance checkpoint sequencing (superseded for normal
  free play by the infrastructure-only harness).
- `574b16d` — infrastructure-only A/B harness and operator workflow.
- `0c265dd` — accepted server-authoritative networked GodotGAS Health slice.
- `5bcfbaf` — accepted non-stackable GodotGAS SpeedBoost / Netfox boundary.
- `4f213f5` — accepted GAS Sprint + Stamina / Netfox boundary.
- latest — accepted predicted GAS Dash / Netfox boundary.
- latest — accepted server-authoritative one-slot inventory state machine.

## Working tree

One unrelated untracked GodotSteam temporary DLL may be present locally; do not
stage it with documentation changes.
