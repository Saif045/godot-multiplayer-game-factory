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

## Working tree

One unrelated untracked GodotSteam temporary DLL may be present locally; do not
stage it with documentation changes.
