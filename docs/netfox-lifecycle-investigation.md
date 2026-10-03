# Netfox lifecycle investigation — 2026-10-03

## Result and scope

Started from clean `origin/master` / `f7c129f`. Repeated normal-shell gameplay
did not reproduce the historical duplicate `NetworkTime.start()`, self
`_submit_tickrate`, self `_set_timestamp`, self `_submit_full_state`, Invalid
packet, missing rollback-node, or signal-disconnect errors. Their historical
cause remains unproven; absence in these runs does not prove transport
contamination.

Opt-in tracing did expose a separate double-stop defect on host Leave.
The accepted correction keeps NetworkEvents as the only lifecycle owner,
preserves the server-state / owner-input split, and changes no RPC targets,
history limits, gameplay semantics, transport, or joining policy.

## First baseline and failing boundary

Run `netfox_baseline_f7c129f_20261003_01`, attempt 003, used fresh export
`gf_f7c129f4_f83b0ddcd9a7`. The first PC-host gameplay interval was
`04:17:15.314–04:18:04.345 UTC` on the VM, about 49 seconds. The operator
confirmed movement/jump/switch behavior. No targeted Netfox errors appeared.
Two same-process exploratory repetitions also had no targeted errors: a
VM-host run lasted about 44 seconds; a PC-host, host-first exit lasted only
10 seconds and emitted seven client history-limit warnings at
`04:21:34.3575843–04:21:34.5608751 UTC`. Host Leave was
`04:21:34.518 UTC`; this temporal proximity does not establish causation.
The short host-first run is not 30-second acceptance. No history setting was
changed. Cleanup was verified.

Attempts 001/002 stopped before game launch at host-task setup, after build
parity. The standard limited interactive task was registered and the harness
run outside the sandbox to access it. These are infrastructure outcomes,
not Netfox reproductions.

The first measured lifecycle failure was in
`netfox_trace_f7c129f_20261003_02`, attempt 001:

| Host event | UTC | NetworkTime state before call |
| --- | --- | --- |
| server start → time start | 04:33:36.028 | inactive (0) |
| after-sync | 04:33:36.033 | active (2) |
| native close notification → client-stop → time stop | 04:34:53.235 | active (2) |
| frame detection → server-stop → time stop | 04:34:53.362 | inactive (0) |

There was no preceding client-start on this host. Netfox v1.35.3 blindly
mapped `MultiplayerAPI.server_disconnected` to client-stop, although the
listen-server close path notified that signal too. The server frame check
then independently stopped the same lifecycle. This failure is at the
NetworkEvents role/event boundary, not player authority or RPC visibility.
The client had one start, after-sync, and stop. Each of the five autoloads
had one instance. Both players on both peers connected/disconnected their
rollback callbacks exactly once; all observed state and handshake targets
were valid remote peers or broadcast 0.

## Local correction and diagnostics

The only new behavioral vendor delta is `addons/netfox/network-events.gd`:

- Remember an active client lifecycle when connected-to-server fires.
- On server-disconnected, immediately stop the active server role and clear
  its cached flag, or stop the active client role and clear its flag.
- A notification without either active role emits no lifecycle stop.
- Keep the existing server frame check as fallback. Keep duplicate-start
  warnings intact; do not make `NetworkTime.start()` silently idempotent.

An intermediate client-only gate removed the second host stop but delayed
the first until the frame check. Run `netfox_lifecycle_fix_20261003_01`
exposed a time-loop logger call after peer detachment. Immediate server-role
routing closes that window; that intermediate build was not accepted.

Upstream checks on 2026-10-03: the
[latest-release endpoint](https://github.com/foxssake/netfox/releases/latest)
resolved to v1.35.3, and the checked
[main NetworkEvents source](https://raw.githubusercontent.com/foxssake/netfox/main/addons/netfox/network-events.gd)
still emitted client-stop unconditionally from server-disconnected. No newer
compatible correction was verified. No upgrade was performed. The existing
`get_last_known_input()` correction remains unchanged.

`factory/networking/netfox/netfox_lifecycle_trace.gd` is observations only,
enabled by `--netfox-lifecycle-trace` in either Godot argument list. The A/B
harness `-NetfoxLifecycleTrace` switch passes it to both peers and preserves
it in immutable-build retries. Default runs emit no trace records.

Diagnostic hooks in the seven task-named Netfox scripts record event/API
identity, call stacks for time start/stop, autoload instances, handshake
send/receive targets, authority at rollback enter, visibility lists, and
callback connect/disconnect/exit. State RPC records are sampled once per
second per instance/event/target; exact per-target totals are emitted on
time stop as `rpc_counts`. Positive targets and broadcast 0 retain their
original semantics. These hooks do not filter peers or route RPCs.

The first diagnostic build mistakenly read only user arguments, so
`netfox_trace_f7c129f_20261003_01` had no trace records. The next diagnostic
build queried peer APIs after detachment, causing instrumentation errors.
Both problems were corrected and their artifacts preserved. The accepted
build checks `has_multiplayer_peer()` before querying peer identity/list/role.

## Accepted runtime evidence

Run `netfox_lifecycle_fix_20261003_02`, attempt 001, tested the final dirty
working-tree runtime on base `f7c129f`:

- Build: `gf_f7c129f4_d838f603ed97`.
- Manifest SHA-256:
  `6a2fffde655ef2c9d09f7751508e150a0b947c126d078f62c9572d91944d2b0e`.
- Fresh export, five-file dependency manifest, and host/VM parity passed.
- Both participants emitted trace-ready before gameplay.

| Gameplay session | PC role / peer | VM role / peer | Common gameplay interval (UTC) | Start / after-sync / stop, each peer |
| --- | --- | --- | --- | --- |
| First gameplay | server / 1 | client / 1956146721 | 04:45:41.156–04:46:37.921 (56.8 s) | 1 / 1 / 1 |
| Same-process second gameplay | client / 1626513505 | server / 1 | 04:46:58.812–04:48:14.521 (75.7 s) | 1 / 1 / 1 |

The operator completed the requested movement, jump, and bidirectional switch
actions and reported expected visuals. Logs confirm ready 2/2, Start, two
players, authoritative switch changes, and Leave. The operator chose role
reversal for the second session; record this deviation rather than claiming
two consecutive PC-host sessions. A host-only lobby closed before the first
gameplay and an extra joined lobby after the two gameplay sessions also
stopped once per active peer. Neither is counted as gameplay acceptance.

For every measured lifecycle, start state was inactive and after-sync emitted
once. Server-close notifications now emit server-stop immediately; the frame
check emits no second stop. The same autoload/API instances persisted through
process reuse. Old player instances exited; new player instances connected
once with no prior connection. No sampled state RPC appeared after its
player's exit. All root and synchronizer authorities were 1; Input authority
matched its represented owner before Netfox initialization.

The server tickrate broadcast used target 0, then its peer-connected send used
the remote client ID. Client tickrate and timestamp requests targeted 1; the
server timestamp reply targeted the observed remote sender. Host visibility
contained only the client; client visibility contained only 1. Full states
used target 0 or the remote peer, and diffs used the remote peer. Exact
per-target RPC totals and timestamps are preserved in the trace/review JSON.
No positive state/handshake target equaled its local peer.

Required error counts were zero on both completed sessions: duplicate start,
the three self-RPC errors, Invalid packet, missing rollback nodes, and
Netfox signal-disconnect errors. No history-limit warning or detached-peer
logger error appeared in the final run. Remaining unrelated startup warnings
were VM WASAPI/dummy audio and a GAS resource-UID path fallback. BadCert did
not occur; its separate transport limitation remains unresolved.

`Stop` verified host/VM test-process cleanup after the extra lobby was left.
Evidence is under
`artifacts/ab_tests/netfox_lifecycle_fix_20261003_02/attempt_001/`:
`evidence.json`, `state.json`, `netfox_acceptance_review.json`, both
`godot.log` and `netfox_trace.json` files, PC `host/local_game.jsonl`, and VM
`client/game.jsonl`. The PC local JSONL was explicitly preserved from the
export after cleanup because the harness's earlier copied run directory
contained only startup records; raw Godot logs remained complete.

Local validation: the bounded headless GDScript regression
`tests/netfox/network_events_lifecycle_test.gd` passed active-server immediate
stop, inactive/repeated notifications, two client lifecycles, and detached-peer
observations. Test-process cleanup, PowerShell parsing, final export compilation,
and `git diff --check` passed. Runtime evidence, not this regression, establishes
the gameplay acceptance.
