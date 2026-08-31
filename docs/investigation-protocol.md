# Investigation Protocol

Investigation is a separate task from acceptance testing. It begins only after
an attempt has reached PASS, FAIL, or BLOCKED, cleanup has been performed, and
the task explicitly authorizes investigation.

## Default mode: read-only

Unless explicitly authorized otherwise, investigation may:

- inspect artifacts, logs, source, and dependency source;
- compare passing and failing attempts;
- search authoritative upstream documentation or issues;
- form hypotheses; and
- propose a controlled next experiment.

It must not:

- modify production code or the test harness;
- change VM, Steam, or network configuration;
- introduce workarounds;
- restart services; or
- rerun arbitrary experiments.

## Proposing another experiment

When existing evidence is insufficient, define before running anything:

```text
Hypothesis:
Single changed variable:
Exact procedure:
Expected observation:
PASS interpretation:
FAIL interpretation:
Cleanup:
```

Then wait for approval or a new explicit task. An approved experiment follows
the Runtime Test Operator Protocol as a new frozen acceptance attempt.

## Fixes

A confirmed fix is a separate implementation task. It may include narrow
build and unit-test validation caused by that change. A later acceptance task
must prescribe the exact runtime test to rerun; investigation never grants an
implicit retry.
