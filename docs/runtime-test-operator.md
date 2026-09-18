# Runtime Test Operator Protocol

## Role

For an interactive gameplay session, the harness is only operational
infrastructure. The operator plays freely and reports what was visible; the
agent reviews the preserved structured logs after cleanup. Feature acceptance
requires those two sources to agree. The harness does not decide whether a
gameplay feature passed.

This protocol applies to runtime, multiplayer, VM, Steam, Netfox, benchmark,
and other integration or acceptance attempts. It complements the lifecycle in
[`testing-protocol.md`](testing-protocol.md).

## Running the Hyper-V A/B harness

The A/B harness is a universal PowerShell interface. It does not depend on a
specific agent or an open caller session. Start a 3D player manual-test session
with host logs visible:

```powershell
.\tools\ab_test\run.ps1 -Mode Health
# Confirms the configured VM can return a nonce-bearing remote PowerShell marker
# and has an enabled GameFactoryClient scheduled task. This does not launch a game.

.\tools\ab_test\run.ps1 -Mode Launch `
    -Scenario netfox_player_3d `
    -RunId carryable_item_YYYYMMDD_HHMMSS `
    -ShowHostConsole
```

For normal gameplay work, request bounded VM recovery and, when a clean Steam
starting point is useful, fresh transport preparation:

```powershell
.\tools\ab_test\run.ps1 -Mode Launch -Scenario netfox_player_3d -RecoverVm -FreshTransport -ShowHostConsole
```

`-RecoverVm` first runs Health. Only an unhealthy VM gets one restart attempt;
the harness then waits for SSH and verifies the existing interactive desktop
and enabled `GameFactoryClient` task again. It never changes VM networking or
autologon. `-FreshTransport` is pre-attempt only: it stops stale game
processes, restarts host Steam and VM Steam in its existing interactive user
session, waits for Steam's post-restart `Logged On` connection-log signal plus
a stable host `steam.exe` + `steamwebhelper.exe` interactive process set, and
rechecks VM Health. It then runs one bounded host GameFactory `--run=steam`
IPC probe and requires `steam.session/ready`; process/session readiness alone
is not enough. Exact Steam online state still cannot be verified robustly
through available local interfaces. The VM is limited to interactive-session
process readiness because a second interactive probe would create a visible
client process.

For Steam/native investigation, do not add `-FreshTransport` unless the test
explicitly calls for a fresh-session comparison. Never restart Steam inside a
live attempt. Preserve a native-handshake failure, stop it, then create a
separate immutable-build retry if needed:

```powershell
.\tools\ab_test\run.ps1 -Mode Stop -RunId <RunId>
.\tools\ab_test\run.ps1 -Mode Retry -RunId <RunId> -RecoverVm -FreshTransport -ShowHostConsole
```

Replace the run ID with a unique timestamp-like label. The harness exports the
current immutable build when needed, verifies host/guest manifest parity, then
launches the host and the VM client. It captures both structured logs and
writes the infrastructure result to:

```text
artifacts/ab_tests/<RunId>/result.json
```

`Launch` exits after `AB_READY` while the host and VM games remain alive. It
only establishes the immutable build, VM parity, Steam/Godot connection, and
two-player topology; it never waits for an operator marker or evaluates
gameplay. Its persistent state is:

```text
artifacts/ab_tests/<RunId>/run_state.json
artifacts/ab_tests/<RunId>/attempt_001/state.json
```

The VM release cache automatically reuses a verified matching runtime artifact.
Documentation and A/B-harness-only commits do not invalidate an existing export;
runtime-input changes do. Do not add `-SkipExport` or force flags simply to make
a run faster.

For `netfox_player_3d`, the harness is infrastructure-only. After `AB_READY`,
use the game freely while it remains open:

1. Confirm both players can see each other; move and jump on both machines.
2. Toggle the switch from each machine.
3. Have host and client each pick up the green cube, move/jump with it, and
   drop it; repeat or vary interactions naturally if useful.
4. Report visual behavior to the agent. The agent evaluates the preserved
   structured timeline for authority, replication, holder state, follow, drop,
   errors, and cleanup.

When play is complete, collect generic evidence without changing the live
session:

```powershell
.\tools\ab_test\run.ps1 -Mode Verify -RunId <RunId>
```

`Verify` defaults to the latest attempt; use `-Attempt <number>` to inspect an
older one. It never rebuilds, relaunches, stops a process, or declares gameplay
PASS/FAIL. It writes `attempt_<n>/evidence.json`, a compact event/timeline/error
index with paths to all raw logs. The operator and agent compare that evidence
with the visual report.

Stop only when the session is finished or intentionally abandoned:

```powershell
.\tools\ab_test\run.ps1 -Mode Stop -RunId <RunId>
```

`Stop` preserves logs/evidence, terminates host and VM processes, verifies
cleanup, and marks the attempt stopped. It is intended to be idempotent. For a
bad environmental attempt, stop it, repair the VM/Steam environment manually,
then create a fresh isolated attempt with the same captured build:

```powershell
.\tools\ab_test\run.ps1 -Mode Retry -RunId <RunId> -ShowHostConsole
```

Recovery actions are metadata only and are saved in the attempt/run state as
`vm_health_initial`, `vm_restart_attempted`, `vm_health_after_restart`,
`fresh_transport_requested`, both Steam restart results, and
`steam_readiness_result`. They do not constitute gameplay acceptance.

Each attempt has its own `evidence_attempt_id` and VM Godot-log namespace.
Topology checks accept only structured events with that ID and a UTC timestamp
at or after that attempt began. A stale or unprovenanced log is preserved for
diagnosis but cannot satisfy current-attempt readiness.

Retries never mix logs: attempts live under
`artifacts/ab_tests/<RunId>/attempt_001`, `attempt_002`, and so on.

The harness preserves evidence and infrastructure health only; feature
acceptance is always the operator's visual report plus post-run log review.

## Frozen-attempt rule

Once an attempt starts, the following infrastructure inputs are frozen:

- source code;
- tested build;
- runtime configuration;
- test scenario;
- build/reuse policy; and
- process/cleanup policy.

Changing a frozen item ends the validity of that attempt. Finish it, capture
evidence, clean up, and report its terminal result before any separate task
changes the system.

## Attempt lifecycle and acceptance

The harness records an operational lifecycle, never gameplay PASS/FAIL:

- **running:** `Launch` or `Retry` reached `AB_READY`; games remain live.
- **stopped:** `Stop` preserved evidence and verified process cleanup.
- **failed / blocked:** setup could not complete; the recorded stage and reason
  identify the infrastructure boundary.

Feature acceptance is a separate human/agent conclusion based on the visual
report and `Verify` evidence. A run that reached `AB_READY` is not an accepted
gameplay result merely because it opened.

## Mandatory stop behavior

At an infrastructure terminal condition:

1. Stop advancing the scenario.
2. Record the failing infrastructure boundary, if any.
3. Capture immediately relevant evidence.
4. Tear down all test-owned processes.
5. Verify cleanup.
6. Report the terminal result and artifact location.
7. Stop.

Diagnosis from already captured evidence is allowed only when the test plan
asks for it. Do not change the system or begin another experiment.

## Prohibited behavior during acceptance testing

Unless the explicit test plan permits an action, do not:

- modify source, test scripts, scenarios, or configuration;
- retry an attempt;
- restart Steam;
- restart or reconfigure the VM;
- change network adapters or settings;
- increase timeouts or introduce sleeps;
- run another scenario or manual debugging tool;
- apply a workaround or fix a newly discovered bug; or
- leave test-owned processes running.

Repeated reliability attempts are not retries only when their count and
continue-or-stop policy are specified before the suite starts.

## Default classification and response

| Condition | Result and layer | Required response |
| --- | --- | --- |
| VM or SSH unavailable | `BLOCKED / vm_control` | Capture the remote error, clean up where possible, report, stop. |
| Build unavailable or parity cannot be established | `BLOCKED / build` | Capture the build/parity evidence, clean up, report, stop. |
| Steam unavailable during preflight | `BLOCKED / steam` | Capture the exact error, clean up, report, stop. |
| Steam initialization fails after scenario launch | `FAIL / steam` | Capture the exact error, clean up, report, stop. |
| Lobby creation or join fails | `FAIL / steam` | Capture structured and native evidence, clean up, report, stop. |
| Peer is created but Godot never connects | `FAIL / godot_multiplayer` or the deepest evidenced lower layer | Capture peer-state and native evidence, clean up, report, stop. |
| Unexpected runtime exception | `FAIL` at the deepest evidenced layer | Capture exception and logs, clean up, report, stop. |

Use `unknown` when evidence cannot support a narrower owner. Do not change a
classification to hide an intermittent result.

## Bug policy

Finding a bug during an acceptance test does not authorize fixing it. Record
what failed, where it failed, relevant evidence, and the likely responsible
layer only when supported. A fix is a separate implementation task.

## Cleanup

Cleanup is mandatory after PASS, FAIL, BLOCKED, timeout, exception, and partial
startup. Verify at minimum:

```text
host test processes = 0
VM test processes   = 0
```

If cleanup cannot be verified, report that independently from the scenario
result.

## Required report

For an infrastructure session, report these minimum fields:

```text
Test:
Run or attempt ID:
Lifecycle: running | stopped | failed | blocked
Infrastructure boundary:
Operator visual report:
Post-run log review:
Relevant evidence:
Cleanup result:
Artifact or log location:
```

For suites, also report requested attempts, completed attempts, passes,
failures, blocked attempts, and failed-stage distribution.
