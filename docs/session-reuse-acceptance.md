# Session reuse and Netfox upgrade acceptance

Accepted 2026-10-07 with operator reports plus host/VM runtime evidence.
Netfox source is pinned to `38f59778b02bfd1a3dedc7dcc985945d7058858d`,
reporting 1.49.3. Its upstream upgrade notes still say Unreleased; retain this
exact pin. The persistent command receiver safely skips removed identities.

## Immutable checkpoints

`netfox_round_reuse_20261007_050603` used build `gf_e462e709_1dec9eefb0a5`,
content SHA256 `1dec9eefb0a550b8fc581db94df0e12f0d6b2b3a81c655977dafd5ac8b511ccb`.
Attempt 001 covered repeated round return, state freshness, graceful client
Leave, host continuation, gameplay lobby hiding, same-client rejoin, and host
terminal Leave. Attempt 002 force-terminated VM process4792 while player2 owned
equipped cube4 with speed12. The host removed player2, removed the equipment
effect to speed6, cleared cube ownership, recovered the cube, operated the
switch, and returned to an empty lobby with Start enabled. The operator
confirmed normal host movement and return. No source changes occurred.

The operator's three named rounds correspond to runtime rounds2/3/4 after
earlier manual session actions. Host-assigned client input identity samples
were12/18/24/30 on player paths2/6/10/14; the host session, lobby and native peer
remained live across those rounds. Actual late input18 was rejected safely
through `_submit_unreliable → _handle_command → _handle_input` during teardown.

`readiness_roles_20261007_054650_run` used diagnostic build
`gf_e462e709_3650a13e71dc`, content SHA256
`3650a13e71dccf8370666e44b8e9ef27e6dfa129d6eaffd7b5a077890fdf3795`.
Production `factory/`, Netfox vendor and `project.godot` hashes matched the
first checkpoint. The only added execution path is an opt-in sandbox sender
selected by `--run=shell-readiness` / harness `-ShellReadinessProbe`; default
shell behavior is unchanged. It reads revision/phase and sends the actual
`ClientReadyForPhaseRpc` on the real client shell. It never edits receiver or
readiness state. This second export is a deliberate diagnostic deviation from
the original single-export plan, authorized to finish the missing checks.

At02:49:32.171Z the PC host rejected revision0 at current revision1, phaseLobby,
from peer1812162647; readiness remained2/2 and Start stayed enabled. It also
rejected a readiness RPC in Gameplay. Then both processes returned to menus
and swapped roles: PC process26132 became client985564430; VM process10940
became host. Both process IDs and start times remained unchanged. The operator
confirmed fresh vitals/inventory/equipment, movement/jump, switch/cube, lobby
return and terminal VM host Leave. At02:51:16.772Z the VM host likewise rejected
revision0/current1 with readiness2/2. Both NetworkEvents role starts/stops
were balanced, and client time synchronization restarted in the new roles.

## Result and limits

The requested gameplay A/B, stale-readiness and role-reversal checks PASS.
All attempts have no Godot cached-node or invalid-packet RPC errors; upstream
unknown-identity warnings remain visible and are evidence of safe rejection.
Gameplay late join was tested through the ordinary Join Game discovery path:
no lobby was displayed during Gameplay; it returned after lobby reopening.
The direct `late_join_rejected` connected-peer branch was not forced by this
test. Do not equate readiness phase rejection with that separate branch.

VM WASAPI startup audio errors, UID path fallbacks, timing and UI owner warnings
are preserved separately; this is not a claim of entirely error-free logs.
No changes or Steam/VM restarts occurred inside either frozen checkpoint.
Verify/Stop and independent process checks confirmed host0/VM0 after both A/B
attempts and after the focused checkpoint. Source freeze verification passed.

Evidence is under `artifacts/ab_tests/<run>/attempt_<n>/`: `evidence.json`, raw
host/client logs, state, and operator-checkpoints. Preparation/freeze records
for the focused run are in `artifacts/ab_tests/readiness_roles_20261007_054650/`.
The earlier A/B-only report's missing guard/role checks are superseded by the
focused checkpoint. Cheap validation: build0 warnings/errors, 73 tests,
lifecycle/history-motion/reuse probes, real held stale-input regression, and
diff hygiene. Detailed local regression provenance is in
[the compatibility report](netfox-compatibility-spike.md).
