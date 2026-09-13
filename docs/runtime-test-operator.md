# Runtime Test Operator Protocol

## Role

During an acceptance test, the agent executes a predefined experiment. The test
plan is authoritative: do not reinterpret, expand, redesign, or repair it
while it is running.

This protocol applies to runtime, multiplayer, VM, Steam, Netfox, benchmark,
and other integration or acceptance attempts. It complements the lifecycle in
[`testing-protocol.md`](testing-protocol.md).

## Running the Hyper-V A/B harness

From the repository root in PowerShell, run the 3D player acceptance scenario
with host logs visible:

```powershell
.\tools\ab_test\run.ps1 `
    -Scenario netfox_player_3d `
    -RunId carryable_item_YYYYMMDD_HHMMSS `
    -ShowHostConsole
```

Replace the run ID with a unique timestamp-like label. The harness exports the
current immutable build when needed, verifies host/guest manifest parity, then
launches the host and the VM client. It writes the terminal result to:

```text
artifacts/ab_tests/<RunId>/result.json
```

Use the normal command above for a retry of the *same* committed build. The
VM release cache automatically reuses a verified matching manifest; do not add
`-SkipExport` or force flags simply to make a run faster. A source/configuration
change requires a new frozen attempt and normally produces a new export.

For `netfox_player_3d`, act only after each printed `manual ... ready` prompt:

1. On the host game window, move with WASD and jump with Space until host
   movement and jump are observed remotely.
2. Focus the VM game window, then move with WASD and jump with Space until
   client-local movement and jump are observed remotely. Focus matters: VM
   console or PowerShell focus does not provide game input.
3. Complete the existing switch prompts: approach the center switch and press
   E once on the prompted participant.
4. Complete carry M: host approaches the green cube, presses E, moves while
   carrying, then presses Q to drop.
5. Complete carry N with the VM client using the same E, move, Q sequence.

Do not perform inputs early or alter Steam, VM, settings, source, timeout, or
harness options after the attempt has started. Let the harness own teardown and
use `result.json`, host console logs, and client logs as the acceptance evidence.

## Frozen-attempt rule

Once an attempt starts, the following are frozen:

- source code;
- tested build;
- runtime configuration;
- test scenario;
- assertions; and
- timeout and retry policy.

Changing a frozen item ends the validity of that attempt. Finish it, capture
evidence, clean up, and report its terminal result before any separate task
changes the system.

## Terminal states

Every attempt ends as exactly one of:

- **PASS:** all required assertions succeeded.
- **FAIL:** the scenario began and a required assertion failed, a terminal
  runtime error occurred, or a required condition timed out.
- **BLOCKED:** the intended scenario could not begin because an external
  prerequisite was unavailable, such as a VM, SSH, Steam, required build, or
  build parity.

## Mandatory stop behavior

At the first terminal condition:

1. Stop advancing the scenario.
2. Record the failed checkpoint and deepest successful checkpoint.
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
| Netfox assertion fails after Godot connects | `FAIL / netfox` | Capture Netfox and transport evidence, clean up, report, stop. |
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

Report exactly these minimum fields:

```text
Test:
Run or attempt ID:
Result: PASS | FAIL | BLOCKED
Assertions:
Deepest successful checkpoint:
Failed checkpoint:
Failure layer:
Relevant evidence:
Cleanup result:
Artifact or log location:
```

For suites, also report requested attempts, completed attempts, passes,
failures, blocked attempts, and failed-stage distribution.
