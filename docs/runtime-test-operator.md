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

From the repository root in PowerShell, run the 3D player manual-test session
with host logs visible:

```powershell
.\tools\ab_test\run.ps1 `
    -Scenario netfox_player_3d `
    -RunId carryable_item_YYYYMMDD_HHMMSS `
    -ShowHostConsole
```

Replace the run ID with a unique timestamp-like label. The harness exports the
current immutable build when needed, verifies host/guest manifest parity, then
launches the host and the VM client. It captures both structured logs and
writes the infrastructure result to:

```text
artifacts/ab_tests/<RunId>/result.json
```

The VM release cache automatically reuses a verified matching runtime artifact.
Documentation and A/B-harness-only commits do not invalidate an existing export;
runtime-input changes do. Do not add `-SkipExport` or force flags simply to make
a run faster.

For `netfox_player_3d`, the harness is infrastructure-only. It verifies startup
through the two-player topology, then does not assert, sequence, or judge
gameplay events. Use the game freely while it remains open:

1. Confirm both players can see each other; move and jump on both machines.
2. Toggle the switch from each machine.
3. Have host and client each pick up the green cube, move/jump with it, and
   drop it; repeat or vary interactions naturally if useful.
4. Report visual behavior to the agent. The agent evaluates the preserved
   structured timeline for authority, replication, holder state, follow, drop,
   errors, and cleanup.

When play is complete, the agent creates the completion marker printed by the
harness (by default `operator_finished.complete` in that run's artifact folder).
The harness then collects logs and cleans both participants. Feature acceptance
comes from visual confirmation plus the agent's post-run log review, not from
the harness result alone.

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

## Terminal states

Every harness session ends as exactly one of:

- **PASS:** the infrastructure session started, was ended by the operator, and
  cleanup/log preservation completed. This is not feature acceptance.
- **FAIL:** infrastructure failed after launch, such as a terminal runtime
  error or failed cleanup.
- **BLOCKED:** the intended scenario could not begin because an external
  prerequisite was unavailable, such as a VM, SSH, Steam, required build, or
  build parity.

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
Result: PASS | FAIL | BLOCKED
Infrastructure boundary:
Operator visual report:
Post-run log review:
Relevant evidence:
Cleanup result:
Artifact or log location:
```

For suites, also report requested attempts, completed attempts, passes,
failures, blocked attempts, and failed-stage distribution.
