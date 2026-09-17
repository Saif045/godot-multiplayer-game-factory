# Qwen + Aider Operating Guide

Qwen is the primary bounded implementation worker. Codex handles difficult
research, review, native/plugin internals, and architecture escalation. Assume
the task already contains the planner's decision; implement it rather than
rediscovering the project architecture.

## Start a task

From the repository root, start the configured model with:

```powershell
aider
```

Use a fresh Aider session for each bounded task. Give Qwen the goal, accepted
boundaries, prohibited scope, and validation contract; do not paste planner
history. The repo configuration selects the model, edit format, context budget,
permanent guides, and explicit `/test` command.

## Load only relevant context

`AGENTS.md` contains the global rules. Start with the task, then read only the
nearest scoped guide and the named or directly relevant implementation files.
Use `docs/current-state.md` when current maturity or accepted evidence matters.
Search before opening large files. Do not recursively read documentation,
planner history, old A/B artifacts, generated files, or unrelated subsystems.

Guide routing:

- `factory/networking/AGENTS.md`: identity, authority, network objects, spawn,
  player lifecycle, and replication.
- `factory/networking/netfox/AGENTS.md`: Netfox rollback player work.
- `factory/gameplay/AGENTS.md`: interactions and authoritative gameplay state.
- `factory/gameplay/gas/AGENTS.md`: GodotGAS integration boundary.
- `factory/steam/AGENTS.md`: Steam sessions, lobbies, adapters, and native peer.
- `tools/ab_test/AGENTS.md`: host/VM runtime tests and evidence handling.
- `docs/current-state.md`: compact accepted project status, not a history index.

In Aider, add the scoped guide read-only and add only the files being changed:

```text
/read factory/gameplay/AGENTS.md
/add factory/gameplay/path/to/relevant_file.cs
```

## Workflow

1. Restate the bounded contract and prohibited scope.
2. Inspect the routed guide and relevant code with targeted searches.
3. Implement the smallest coherent change.
4. Run cheap targeted validation while iterating.
5. Fix ordinary failures caused by the change.
6. Before completion, run the task's required build/tests and `git diff --check`.
7. Report changes, decisions or deviations, validation, and commit SHA.

Use `/test` for the configured serial, shared-compiler-disabled test command;
automatic testing is intentionally disabled so it does not run the full suite
after every attempted edit. Use narrower commands during iteration when the
task permits them.

Use the universal validation interface before completion. Normally invoke
`/run powershell -ExecutionPolicy Bypass -File .\tools\validate.ps1 -Mode Auto`;
add a focused command such as
`/run powershell -ExecutionPolicy Bypass -File .\tools\validate.ps1 -Mode Probe -Probe Gas`
when the task calls for it. See [`../testing.md`](../testing.md) for the common
semantics; do not duplicate its policy here.

For manual A/B work, use `tools/ab_test/run.ps1 -Mode Launch|Verify|Retry|Stop`:
return after `AB_READY`, later inspect `Verify -RunId` evidence with the
operator's visual report, then `Stop`. The harness does not judge gameplay
acceptance.

## Do not

- change accepted architecture merely to make a task easier;
- touch unrelated subsystems or perform speculative broad refactors;
- invent local or vendor APIs instead of inspecting their source;
- load the entire repository or large documentation sets;
- treat presentation data as authoritative or rollback state; or
- claim an unrun validation passed.

## Escalate to Codex

Stop and report concrete evidence when two or three materially different
hypotheses fail, native/plugin internals are implicated, ownership or
replication boundaries remain unclear, a fix changes an accepted architecture
boundary, or third-party behavior cannot be established confidently from
local documentation and source.
