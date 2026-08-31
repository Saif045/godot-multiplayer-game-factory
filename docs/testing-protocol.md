# Agent Test Execution Protocol

This document defines the mandatory execution protocol for tests that launch
runtime processes or interact with external systems.

Examples include:

- Godot runtime tests;
- host/client multiplayer tests;
- Steam A/B tests;
- VM tests;
- integration and acceptance tests;
- benchmarks;
- dedicated-server tests; and
- external-service tests.

Unit tests that simply run and exit normally do not require the full runtime
protocol, although the same principles still apply.

The purpose is to prevent incomplete, ambiguous, or abandoned test runs. A test
is not run merely because its processes were launched.

Every runtime test must reach one terminal state--`PASS`, `FAIL`, or
`BLOCKED`--and then perform cleanup.

## Core invariant

Every runtime test follows this lifecycle:

```text
DEFINE
  -> PREFLIGHT
  -> CLEAN
  -> LAUNCH
  -> VERIFY STARTUP
  -> EXECUTE
  -> OBSERVE AND ASSERT
  -> PASS | FAIL | BLOCKED
  -> CAPTURE EVIDENCE
  -> TEARDOWN
  -> VERIFY CLEANUP
  -> REPORT
```

Skipping a phase requires an explicit recorded reason. Launching processes and
then stopping work is never a valid result.

## 1. Define the test before launch

Before starting runtime processes, establish the test contract:

- **Purpose:** the question the test answers.
- **Participants:** every process, machine, account, or service involved.
- **Start conditions:** observable readiness for each participant. Process
  existence is normally insufficient.
- **Actions:** the scenario to execute after startup.
- **Assertions:** the conditions that define `PASS`.
- **Failure conditions:** known terminal errors, impossible states, exits, and
  timeouts.
- **Timeouts:** every wait is bounded.
- **Cleanup:** every test-owned process or resource that must be stopped.

For example, a multiplayer test may require a host Steam lobby ID, a client
that has joined that lobby, `ConnectedToServer`, and a replicated state change
observed by the client. A window appearing is not an assertion unless window
startup is specifically the subject of the test.

## 2. Preflight

Before modifying or launching anything:

1. Generate or identify the run ID.
2. Establish the artifact and log location.
3. Verify required executables and configuration.
4. Verify automatically checkable external prerequisites, including remote
   machine reachability where relevant.
5. Verify the intended build is the build being tested.
6. Record configuration needed to reproduce the run.

If a mandatory prerequisite fails, the result is `BLOCKED`. Do not continue
into the scenario.

## 3. Start from a known clean state

Runtime tests must not inherit accidental state from an earlier attempt.

Before launch:

1. Stop stale processes owned by the test system.
2. Verify that those processes exited.
3. Remove or isolate stale test runtime state when the scenario requires it.
4. Do not kill unrelated user applications or services.
5. Do not kill Steam unless the test explicitly requires restarting Steam.

For multi-machine tests, clean every participant. For example, confirm both
the host and VM have no GameFactory test process before beginning.

## 4. Launch in dependency order

Start participants in the order required by the scenario. Do not launch every
participant blindly at once.

For example:

```text
launch host
  -> wait for host ready
  -> obtain current lobby ID
  -> configure client
  -> launch client
  -> wait for client readiness
```

Verify every launch step before beginning a dependent step. “Process started”
and “system ready” are different states.

## 5. Every wait needs an observable condition

Prefer a wait for a measurable condition or timeout over arbitrary sleeping.
Valid observations include structured logs, process exit, engine signals,
network state, generated files, scenario markers, and measurable game state.

Fixed sleeps are permitted only when no meaningful readiness condition exists,
and the reason must be recorded. No wait may be indefinite.

## 6. Execute and observe the actual scenario

Once runtime processes are active, the agent is actively conducting a test. It
must continue observing relevant evidence until the attempt reaches a terminal
state.

The agent must not:

- launch applications and then abandon them;
- treat startup as proof of success;
- leave applications open while switching to unrelated work;
- stop observing before evaluating assertions; or
- claim success without executing the defined scenario.

Startup is not the test unless startup itself is explicitly under test. Use
deterministic scenario hooks rather than GUI input automation whenever
possible. If a new hypothesis arises during an attempt, finish and clean the
current attempt before configuring a new one.

## 7. Terminal results

Every runtime test terminates as exactly one of:

- **PASS:** all required assertions succeeded.
- **FAIL:** the scenario ran, but an assertion failed, a terminal error
  occurred, or a required condition timed out.
- **BLOCKED:** the intended scenario could not begin because a prerequisite or
  environmental requirement was unavailable.

Do not call a test `PASS` because an executable launched, a window appeared, a
lobby was created, no obvious error was seen, or the test was stopped before
assertions completed.

## 8. Failure and success handling

On `FAIL`:

1. Stop advancing the scenario.
2. Record the failed stage and deepest successfully completed stage.
3. Capture live evidence that could disappear during shutdown.
4. Preserve relevant logs, errors, and process state.
5. Tear down all test-owned processes.
6. Verify cleanup, collect final artifacts, and report `FAIL`.

On `PASS`:

1. Record the completed assertions.
2. Capture required evidence.
3. Tear down all test-owned processes.
4. Verify cleanup, collect artifacts, and report `PASS`.

Do not automatically retry a failed attempt unless repeated attempts are part
of the defined test or retry behaviour itself is being tested. A retry is a new
attempt with its own ID and evidence.

## 9. Cleanup is mandatory

Cleanup behaves like a `finally` block. It runs after `PASS`, `FAIL`,
`BLOCKED`, timeout, exception, or partial startup.

Best-effort cleanup attempts every relevant action even if one action fails:

```text
try stop host
try stop VM client
try close test-owned server
try collect logs
```

One failed cleanup action must not prevent attempts to clean the others. Then
verify process state. If cleanup fails, report it explicitly. The only
exception is an explicit user request to leave a process running for manual
inspection.

## 10. Evidence before interpretation

Use logs and observable state. Do not infer that a lower layer succeeded merely
because a higher-level operation was attempted.

For example:

```text
Steam lobby joined
  != SteamMultiplayerPeer connected
  != Godot ConnectedToServer
  != GameFactory lifecycle ready
  != replication passed
```

Report the deepest proven stage. Attribute a failure only where evidence
supports it; otherwise use `unknown`.

Typical layers are:

```text
harness
build
vm_control
steam
godotsteam
godot_multiplayer
gamefactory_lifecycle
replication
netfox
simulation
gas
gameplay
unknown
```

## 11. Manual interaction

If a test requires human input that the agent cannot provide safely or
deterministically:

1. Reach the exact state requiring the action.
2. State precisely what the user must do.
3. Do not claim completion.
4. Resume observation after the action.
5. Continue through a terminal state and normal cleanup.

Do not silently substitute a different test.

## 12. Repeated and stress tests

Repeated testing consists of separate clean attempts. Each attempt receives its
own ID, start time, result, failure stage, and relevant timing data.

```text
attempt 1 -> PASS -> cleanup
attempt 2 -> FAIL -> cleanup
attempt 3 -> PASS -> cleanup
```

Do not reuse one host/client pair as independent runs unless the test explicitly
targets persistent-session behaviour.

When repeated attempts are intended to measure the same exported binary, a
suite may export and prove multi-machine build parity once before its first
attempt. Every later attempt must still start clean, validate that its local
manifest hash equals the suite's recorded immutable manifest, execute the full
scenario, capture its own artifacts, and verify cleanup. Skipping parity does
not permit skipping per-attempt preflight or runtime assertions.

## Required final report

For every runtime test, report at least:

```text
Test:
Run or attempt ID:
Result: PASS | FAIL | BLOCKED
Assertions:
Deepest successful stage:
Failed stage:
Failure layer:
Relevant evidence:
Cleanup result:
Artifact or log location:
```

For repeated tests also report attempts, passes, failures, and the failure
distribution. Do not report only “worked” or “didn't work”.

## Runtime test checklist

Before launch:

- [ ] Purpose, participants, assertions, failures, and timeout defined.
- [ ] Run ID and artifact location established.
- [ ] Prerequisites verified.
- [ ] Stale test-owned processes removed and clean state verified.

During the run:

- [ ] Launch order followed and readiness verified.
- [ ] Relevant logs and state observed.
- [ ] Actual scenario executed and assertions evaluated.
- [ ] Every wait bounded.

At termination:

- [ ] `PASS`, `FAIL`, or `BLOCKED` selected.
- [ ] Evidence captured.
- [ ] Test-owned processes stopped and cleanup verified.
- [ ] Artifacts preserved and result reported.

If any mandatory item is incomplete, the test run itself is incomplete.
