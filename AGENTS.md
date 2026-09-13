# GameFactory Fast Start

GameFactory is a reusable Godot C# foundation for small-session online co-op.
Server-authoritative shared gameplay is the default. Steam listen-server
transport and the two-account Hyper-V path are implemented and proven.

Build small reusable technical boundaries justified by playable slices. Do not
add custom rules, progression, a mini-engine, a generic command bus, transport
abstraction, or Netfox wrapper without multiple concrete uses.

## Proven stack

```text
SteamSession -> GodotSteamAdapter -> SteamMultiplayerPeer -> MultiplayerAPI
PeerRegistry / PlayerLifecycle -> NetworkWorld -> NetworkObject
NetworkObject -> NetfoxRollbackPlayerComponent -> RollbackSynchronizer
              -> TickInterpolator -> CharacterBody3D
```

Proven: Steam transport, NetworkWorld/player lifecycle, Netfox lifecycle,
split server-state/client-input authority, 3D rollback player, walking,
queued jumping, bidirectional observation, and Hyper-V A/B execution.
Interaction status: **implemented** and **locally validated**, but runtime
acceptance is **blocked/failed at startup**. `interactable_switch.tscn` lacks a
valid resource UID, so `NetworkWorld` correctly rejected its spawn before a
client launched. That asset issue is not evidence of a networking or authority
defect; do not call the interaction primitive runtime-proven until its fresh
acceptance run passes.

## Identity and authority

| Identity | Meaning |
|---|---|
| Steam/account ID | platform identity |
| `PeerId` | transient Godot transport identity |
| `PlayerId` | session gameplay identity |
| `NetworkObjectId` | runtime object identity |
| `NetworkObject.OwnerPeerId` | represented-owner metadata |

For rollback players, root state and Simulation authority are server peer `1`;
only Input has owning-peer authority. **Never recursively map `OwnerPeerId` to
the whole player node's Godot authority.** Clients never authoritatively spawn
players.

## Networking choice

| Problem | Default mechanism |
|---|---|
| Latency-sensitive locomotion | Netfox rollback |
| Player simulation input | Netfox input history |
| Visual smoothing | `TickInterpolator` |
| Discrete replicated state | `ReplicationComponent` / `MultiplayerSynchronizer` |
| Client asks server for action | reliable RPC + server validation |
| Spawn/despawn | `NetworkWorld` / `MultiplayerSpawner` |
| Steam connectivity | `SteamSession` / `SteamMultiplayerPeer` |

Continuous deterministic simulation belongs to Netfox. Discrete authoritative
commands/state use RPC, server validation, and ordinary replication; do not
put switches, doors, or inventory in player rollback history without evidence.

## Netfox rules

- Pinned to **v1.35.3**; do not blindly update upstream `main`.
- Steam is transport below Godot MultiplayerAPI; Netfox owns time sync,
  history, prediction, rollback/resimulation, and interpolation.
- Configure authority before Netfox child initialization.
  `NetfoxRollbackPlayerComponent` owns only root/lifecycle glue.
- Do not call `process_settings()` during normal player initialization.
- Every `_rollback_tick()` input/result value is synchronized or deterministic.
  `is_fresh == false` means resimulation, not necessarily correction.
- Keep history 64 and `enable_input_broadcast=false` unless evidence requires
  change. Use `NetworkTime.physics_factor` only around `move_and_slide()`.
- Queue edge input such as jump. Keep presentation, camera, and owner colors
  outside rollback state. Do canonical integration before heavy diagnostics.

## Work rules

Keep reusable code in `factory/`, experiments in `sandbox/`. Use PascalCase
for C# types/files and lowercase or snake_case for Godot paths. Preserve
unrelated changes, keep commits focused, update docs/module map, and push each
commit when requested.

Normal build work implements one coherent feature and runs narrow checks.
Hardening/investigation work freezes a build, asks one question, collects
narrow evidence, and changes one variable only after the attempt is terminal.

Any Godot/Steam/VM/process test follows `docs/testing-protocol.md` and
`docs/runtime-test-operator.md`: define assertions/timeouts, preflight, clean,
launch in order, classify PASS/FAIL/BLOCKED, capture evidence, tear down, and
verify host/VM cleanup. Never change source, retry, restart Steam, or change
VM settings during a frozen attempt.

## Implementation workflow

1. Read the relevant module and its focused documentation before altering a
   networking boundary.
2. State the smallest coherent contract: role, authority, state ownership,
   success evidence, and cleanup condition.
3. Prefer an existing Godot/Netfox primitive before adding a factory wrapper.
4. Keep domain code composed from normal nodes and components. A component is
   worthwhile when it is a focused reusable capability, not a disguised
   application controller.
5. Make a focused change and update architecture/module documentation whenever
   responsibility, maturity, or a supported workflow changes.
6. Run the narrowest relevant build/test/check. Never describe an unrun test as
   passing.
7. Review the diff for authority leaks, cross-layer coupling, generated files,
   and unrelated changes before staging.
8. Commit only the task's files. Do not sweep the working tree into a commit.

## Validation workflow

For a source-only change, use the relevant compile/test set and
`git diff --check`. For an export change, validate the actual export manifest
and its runtime dependencies, not merely a directory left by an older build.

For an A/B run, record these facts before launch:

- immutable build id and manifest hash;
- host/guest build parity;
- required Steam accounts/session state;
- scenario assertions, input/operator prompts, and each timeout;
- artifact directory and test-owned process identities.

At the end, retain the host and guest logs plus `result.json`. Report the
terminal boundary, not a broad guess. `PASS` means all stated assertions and
cleanup passed. `FAIL` means a stated assertion failed with evidence.
`BLOCKED` means required external preconditions were unavailable before a
meaningful attempt. A process opening is only startup evidence.

## Failure layering

Read failures from the bottom of the dependency stack upward:

1. export/build identity and executable dependencies;
2. Steam session and lobby membership;
3. native Steam peer creation/handshake;
4. Godot `MultiplayerAPI` connection events;
5. GameFactory player/world lifecycle;
6. Netfox topology, clocks, input/history, and rollback;
7. scenario gameplay and visuals.

Do not infer a Netfox defect from a transport failure, or infer a gameplay
failure from an invalid scene UID that stopped world setup. Conversely, a
harness's layer label is a checkpoint classification; inspect structured logs
for the first concrete engine/application error too.

## Hyper-V operator model

The two-account path is host PC ↔ SSH/SCP ↔ GPU-P Hyper-V guest. The guest
receives immutable releases under
`C:\GameFactoryBuilds\releases\<manifest-hash>` and starts an interactive
scheduled task so Godot has a real desktop/Steam session. It is not VirtualBox
and must not rely on a shared-folder contract.

The guest must use its designated Windows/Steam account. SSH verifies control
and file transfer; it does not turn a noninteractive service session into a
usable Steam/Godot graphics session. A missing DLL, blank renderer, or Steam
client failure is an export/guest environment issue until artifacts prove
otherwise.

## Important don’ts

- Do not change `NetworkWorld` global multiplayer authority to express player
  ownership.
- Do not use a peer id as a durable player id or Steam account identity.
- Do not synchronize cosmetic material/color state through rollback.
- Do not create a second prediction/rollback scheduler beside Netfox.
- Do not use a generic event bus to hide direct ownership/lifecycle links.
- Do not diagnose by repeatedly rerunning the same failed frozen scenario.
- Do not restart Steam, reconfigure the VM, or alter source mid-attempt.
- Do not treat manual visual observation as a replacement for asserted logs,
  and do not dismiss visual observations when a scenario intentionally needs
  operator input.
- Do not call a feature proven until its documented acceptance contract passes.

Diagnose layers in order: Steam lobby -> native peer -> Godot connection ->
GameFactory lifecycle -> Netfox -> gameplay. An earlier failure is not
evidence against a later layer; harness failure can be observability-only.

## Deeper references

Read only the deeper document relevant to the task:

- ordinary scoped feature: this file;
- Netfox/player work: also `docs/netfox-integration.md`;
- Steam/session work: also `docs/steam-integration.md`;
- runtime A/B work: also `docs/runtime-test-operator.md` and
  `docs/testing-protocol.md`;
- architecture boundary change: also `docs/architecture.md` and relevant ADRs;
- investigation/fix boundary: `docs/investigation-protocol.md`.
