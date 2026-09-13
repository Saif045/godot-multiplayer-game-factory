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

## Implemented, pending runtime acceptance

- Server-authoritative pickup/carry/drop composes the interaction request path
  with one permanent server-owned carryable item. Held state is a replicated
  holder `NetworkObjectId`; each peer resolves its presentation `CarryAnchor`.
  The server computes world drop transforms and no carry state enters Netfox
  rollback. Its first two-account acceptance run remains pending.

## Latest runtime evidence

The fresh two-account acceptance run on `e8baba1` passed transport/lifecycle,
distinct player presentation, host and client walking/jumping observed by the
other peer, and both directions of server-authoritative switch interaction.
The immutable host/guest build was `gf_e8baba1b_9ab955e4903e` with manifest
`62ff40e4f22fd017993d2a241e88fa2942ed9bb780b32f26cdf4430b1d89743a`; cleanup
was verified for both processes.

The preceding `client_jump_visible_on_host` failure was a probe false negative:
the host log already showed the remote client airborne but sampled it only
after upward velocity had become negative. `e8baba1` moves that acceptance
observation to a debounced frame-cadence grounded-to-airborne edge while
retaining the 0.5-second diagnostic samples; it changes no player networking
or gameplay state.

Evidence: `artifacts/ab_tests/jump_observability_20260913_124200/result.json`.

## Transport history

Three earlier frozen interaction-build attempts on `gf_9ad446b1_30e79f95ef50`
reached lobby membership, peer creation, and MultiplayerAPI assignment, then
remained in native `Connecting` for 120 seconds. They were cleanly terminated.
The later successful diagnostic run means this is an intermittent native-peer
issue, not a currently reproducible invariant. Investigate it evidence-first
if it recurs; do not attribute it to Netfox or interaction.

## Recent commits

- `9ad446b` — valid UID for the interaction switch scene.
- `295f0fe` — transport-only Steam peer/mapping diagnostics.
- `d0ed7b2` — app bootstrap UID metadata and Maaack translations registration.
- `e8baba1` — frame-cadence, debounced jump acceptance observation.

## Working tree

Clean after `e8baba1` before this documentation update.
