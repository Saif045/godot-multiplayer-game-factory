# GameFactory Agent Guidance

## Product boundary

GameFactory is a reusable Godot C# foundation for rapidly building small-session online co-op games. It targets player-hosted/listen-server and dedicated-server games; server-authoritative shared gameplay is the default. Steam is a planned first-class online/platform target, not a current implementation.

Build reusable infrastructure and primitives, not a custom mini-engine or game-specific mechanics, content, rules, balance, progression, or art.

## Working principles

- Preserve normal Godot workflows and direct Godot access where a factory layer is incomplete or unnecessary.
- Prefer convention, then explicit configuration, then replacement or raw access where the domain warrants those paths.
- Prefer composition over required gameplay inheritance.
- Keep peer, player, and network-object identities distinct.
- Avoid speculative abstractions. Use real playable co-op scenarios to justify reusable systems.
- Implement coherent, settled vertical slices rather than unrelated micro-features.
- Before rebuilding a common subsystem, investigate valuable existing Godot libraries, plugins, templates, and open-source projects. Deliberately choose to use, adapt/learn from, or build; do not add dependencies merely to avoid trivial code.
- Do not make product or architectural decisions silently. Surface material alternatives and uncertainties for review.

## Repository discipline

- The repository and its documentation are the source of truth for implemented behavior. Mark planned work as planned; do not invent APIs or claims of completion.
- Follow the naming rules in `docs/coding-standards.md`: PascalCase for C# namespaces, types, and filenames; lowercase or snake_case for directories, scenes, resources, and assets.
- Keep reusable code under `factory/` and exploration under `sandbox/`.
- Preserve unrelated working-tree changes. Keep commits focused.
- Run the narrowest relevant build and tests before committing, and report the commands and results.
- Update the module map and relevant documentation when implementation responsibilities or maturity change.

## Runtime test protocol

Any test that launches Godot, host/client processes, multiplayer peers, VMs,
external services, benchmarks, or other persistent runtime processes MUST follow
`docs/testing-protocol.md` and `docs/runtime-test-operator.md`.

This is mandatory. In particular:

- define assertions before launch;
- distinguish process startup from test success;
- give every wait an observable condition and timeout;
- never abandon an active test;
- finish every attempt as PASS, FAIL, or BLOCKED;
- capture evidence before diagnosis;
- tear down test-owned processes after PASS, FAIL, timeout, or exception; and
- verify cleanup before reporting completion.

Do not leave host/client applications running unless the user explicitly asks
for that behavior.

## Task execution and turn completion

When an implementation task is sufficiently specified, execute it in the
current work turn. A user-visible final response ends that work turn; no
implementation continues invisibly afterward.

Do not end an implementation turn merely to report that work has started,
continues, or will happen next. Inspection, a partial edit, or a build of an
incomplete slice is not a terminal checkpoint. Continue through the requested
implementation and its required local validation in the same turn.

End a turn only when the requested implementation and validation are complete,
an explicit planner stop boundary has been reached, a genuine blocker requires
user action, or the runtime test protocol requires a PASS, FAIL, or BLOCKED
report. Do not send unsolicited status updates while implementation is in
progress; tool activity is the progress record.

During an active acceptance test, act as a test operator, not an autonomous
debugger. Do not modify source, alter the scenario, retry, restart Steam,
reconfigure the VM or network, fix newly discovered bugs, or run alternate
experiments unless the explicit test plan permits it. Complete the attempt as
PASS, FAIL, or BLOCKED; capture evidence; clean up; verify cleanup; report; and
stop.

Investigation and fixes are separate tasks. They must follow
`docs/investigation-protocol.md`.
