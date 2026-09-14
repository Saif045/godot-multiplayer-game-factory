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

## Working tree

One unrelated untracked GodotSteam temporary DLL may be present locally; do not
stage it with documentation changes.
