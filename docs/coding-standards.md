# Coding Standards

These standards govern new and modified work. Conformance is introduced deliberately and separately from behavioral changes.

## General principles

- Preserve a single, clear responsibility for each type and module.
- Prefer plain, explicit code over speculative abstractions.
- Add framework layers for known recurring infrastructure when their responsibility, contract, and verification path are clear; they do not need to wait for multiple games to fail first.
- Do not add abstractions that have no defined responsibility, observable behavior, or near-term path to executable evidence.
- Distinguish facts implemented in source from intended direction in names, comments, and documentation.
- Avoid claims of correctness that lack automated or recorded evidence.

## Names and layout

- Use `GameFactory` as the namespace root for current source.
- Use PascalCase for C# namespaces, types, and filenames.
- Use lowercase or snake_case for directories, scenes, resources, and assets.
- Use one primary type per C# file and match the filename to that type.
- Keep reusable code under `factory/`; keep exploratory examples under `sandbox/`.
- Do not place game-specific rules, balance, content, progression, or presentation in factory modules.
- Treat case-only renames as explicit Git operations and verify them on a case-sensitive view.

## Formatting

- Use four spaces for C# indentation and no tabs.
- Use braces for multi-line control flow; keep terse guard clauses readable and consistent.
- Keep related statements together without decorative blocks of repeated separator comments.
- Keep lines at a readable width; wrap around semantic units rather than one token per line.
- Preserve UTF-8 and repository LF normalization.
- Keep basic whitespace and indentation rules machine-checkable through `.editorconfig`.

Until an automated formatter is adopted, avoid large formatting-only changes alongside behavior changes.

## Nullable and contracts

- Keep nullable reference types enabled.
- Use nullable annotations to reflect real absence, not to silence warnings.
- Validate public inputs at their boundary and give failures enough context to act on.
- Reserve exceptions for violated programmer contracts or unrecoverable local invariants; use result values for expected operational failure.
- Do not introduce structured error-code types until their stability and consumers justify them. Their design is currently open.

## Dependency direction

- Runtime, identity, peer, and player policy should remain independent of Godot where their contracts do not require engine types.
- Steam session code may depend on its focused Steam adapter and Godot multiplayer peer; do not introduce a generic transport/session layer without a concrete second use.
- Sandbox code may compose the Steam session path and use raw Godot APIs.
- Higher-level factory modules must not depend on sandbox code.
- Keep GodotSteam-specific calls inside the Steam bridge/adapter boundary.

## Lifecycle, ownership, and events

- Every disposable resource must have one documented owner.
- A subscriber with a shorter lifetime than its publisher must unsubscribe deterministically.
- Cleanup must be safe when called repeatedly and should leave state coherent if one cleanup action fails.
- Protect callbacks against stale events and invalid lifecycle states.
- Define and test legal state transitions rather than relying only on call order.
- Do not assume a remote disconnect event will perform required local cleanup.

`SteamSession` owns the Steam lobby and multiplayer-peer lifecycle it creates. It must clear a closing peer from Godot's `MultiplayerApi` before disposing it.

## Identity and authority

- Convert raw peer IDs to `PeerId` at the transport boundary.
- Never use `PeerId` as a persistent player identity.
- Keep local and server roles independent; a server can be local or remote.
- Make authority checks explicit at mutation boundaries.
- Treat server authority as the standard default for shared state, not as permission to hide alternative models.

## Godot integration

- Prefer composition for reusable node capabilities.
- Do not require gameplay objects to inherit from a factory networking base class without a reviewed need.
- Resolve required scene relationships early and fail with actionable messages.
- Register `NetworkObjectComponent` direct children in `_EnterTree`; initialize them only after sibling registration in the host's `_Ready`.
- Communicate component capabilities through interfaces, host generic behavior, and events where appropriate; attributes describe metadata and are not a messaging channel.
- Treat shipped component scenes as replaceable defaults rather than a fixed mandatory component list.
- Preserve direct access to Godot APIs for advanced or incomplete cases.
- Treat reflection and automatic scene configuration as validated infrastructure once used beyond a probe.

## Diagnostics

- Diagnostics should identify subsystem, operation, relevant identity, and outcome.
- Avoid relying on ad hoc console strings as the only signal for reusable infrastructure.
- Do not log secrets or unnecessary personal data.
- Automatic decisions should be inspectable without requiring a debugger.
- Structured diagnostics are planned; their schema is not yet settled.

## Tests and evidence

- Add or update tests with each behavioral contract.
- Prefer engine-independent unit tests for pure policy and value objects.
- Use Godot integration tests only where engine behavior is part of the contract.
- Use multiprocess scenarios for connection, disconnection, replication, spawning, and late joining.
- Keep manual probes for exploration, but never cite them as automated regression evidence.
- Record exact preconditions when reporting a manual observation.

See [testing-strategy.md](testing-strategy.md) for the planned test layers.

## Documentation and decisions

- Update the module map when responsibilities or maturity change.
- Mark planned capabilities as planned; do not document proposed APIs as present.
- Record durable architectural choices through the ADR process.
- Keep unresolved choices explicit. Current examples are composition-root form and structured error codes.
- Preserve raw-access and replacement guidance whenever a convenience layer is documented.

## Change discipline

- Separate mechanical normalization from behavioral changes where practical.
- Keep commits focused and reviewable.
- Do not commit generated cache or local editor configuration.
- Verify the narrowest relevant unit, integration, or scenario layer before claiming a change works.
- Preserve unrelated working-tree changes and never rewrite user work to simplify a patch.
