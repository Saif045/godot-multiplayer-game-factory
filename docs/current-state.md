# Current State

This is the compact source of current maturity and evidence. It is not a
replacement for architecture or protocol documentation.

## Proven foundation

- Steam listen-server transport, Godot `MultiplayerAPI`, `NetworkWorld`, and
  `PlayerLifecycle` have accepted two-account Hyper-V evidence.
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

## Latest runtime evidence

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

The free-play launch immediately preceding the accepted run reached lobby
membership and peer assignment but remained native `Connecting` for 120
seconds. It was cleanly terminated; the next unchanged retry reached two-player
topology and passed. Treat this as an intermittent Steam/native transport
symptom, not a carry, interaction, Netfox, or movement defect. Preserve the
evidence and investigate it only if it recurs.

## Recent commits

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
