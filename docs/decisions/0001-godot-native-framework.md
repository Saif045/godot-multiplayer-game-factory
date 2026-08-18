# ADR 0001: Use Godot-Native Framework Boundaries

- Status: Accepted
- Date: 2026-08-18
- Owners: Project maintainers
- Related: [Project charter](../project-charter.md), [Architecture](../architecture.md)

## Context

GameFactory needs to remove repeated multiplayer and application work without becoming a second game engine or networking stack. Godot already supplies scene composition, node lifecycles, multiplayer authority, RPCs, spawning, synchronization, and transport integration.

The current repository follows those boundaries. `ENetTransport` adapts Godot's ENet and multiplayer APIs, the replication experiment configures `MultiplayerSynchronizer`, and sandbox code uses `MultiplayerSpawner` and RPCs directly. The framework does not currently wrap every Godot capability, and its existing abstractions are not yet complete or production-ready.

## Decision

GameFactory will be a Godot-native framework. It may add policy, coordination, validation, diagnostics, and reusable workflows around Godot APIs, but it will not recreate Godot's scene, node, multiplayer, or transport systems as competing infrastructure.

Framework boundaries must preserve practical access to the relevant Godot capability. A subsystem may expose a focused abstraction when that abstraction isolates policy or enables testing, as `INetworkTransport` currently does, but an abstraction must have a concrete responsibility beyond renaming Godot concepts.

This decision does not require every framework type to depend directly on Godot. Engine-independent policy and value types remain desirable where they create clear boundaries.

## Rationale

Building on Godot keeps the framework compatible with the engine's lifecycle and tooling, reduces duplicated machinery, and lets games use the broader Godot ecosystem. Focused engine-independent seams still allow important policies to be tested without running the engine.

## Alternatives considered

### Build an engine-agnostic multiplayer framework

An engine-agnostic core could theoretically serve more runtimes. It would also require generalized scene, object, lifecycle, authority, and serialization models before this project has evidence that another engine is needed. That cost conflicts with the project's purpose as a Godot C# framework.

### Wrap every Godot multiplayer API

A comprehensive facade could make all access look uniform. It would add pass-through abstractions, lag behind Godot features, and make advanced behavior harder to reach without demonstrating a policy or testing benefit.

### Use Godot APIs directly everywhere

This minimizes framework code, but leaves session policy, failure handling, reusable validation, and engine-independent testing duplicated across games. The existing session and transport separation already demonstrates a useful middle boundary.

## Consequences

### Positive

- Games can use native Godot nodes, scenes, RPCs, and multiplayer facilities alongside framework code.
- Framework effort stays focused on recurring policy and coordination problems.
- Engine-independent seams can be introduced selectively where their responsibility is clear.

### Negative

- GameFactory is intentionally coupled to Godot and its lifecycle semantics.
- Changes in Godot APIs may require adapter or integration changes.
- Some verification will require running Godot rather than ordinary .NET tests.

### Neutral or operational

- Documentation must distinguish framework guarantees from behavior supplied by Godot.
- New abstractions require justification in terms of policy, validation, diagnostics, reuse, or testability.

## Validation and evidence

Current evidence is limited to repository structure and manual sandbox probes: session policy depends on `INetworkTransport`, the ENet adapter owns Godot-specific transport work, and replication composes native Godot multiplayer nodes. There are no automated tests or multiprocess scenarios yet.

Future changes should be checked for continued raw Godot access, focused abstraction responsibilities, and executable integration evidence where engine behavior is involved.

## Compatibility and migration

This records the current architectural direction and requires no migration. The project has no defined compatibility policy or published support matrix.

## Open questions

- Which future subsystems warrant engine-independent boundaries is decided from concrete testing and replacement needs.

## Follow-up work

- Add engine-independent tests around established policy boundaries.
- Add Godot integration and multiprocess evidence for behavior that depends on engine lifecycle or networking.
- Document the raw-access path when each future subsystem is designed.

## Supersession

None.
