# Project Charter

## Mission

GameFactory is a professional-grade personal framework for creating Godot C# multiplayer games without rebuilding standard infrastructure for every project. It also serves as a deliberate environment for developing durable networking and software-architecture expertise.

The framework should make ordinary multiplayer behavior safe and extremely easy, make uncommon behavior explicit, and leave advanced users able to replace factory behavior or work directly with Godot.

The project is intended to compound. A solved standard problem should leave behind reusable code, tests, diagnostics, documentation, tooling, and engineering knowledge so that later multiplayer experiments and games begin from stronger ground. The framework itself is therefore a serious long-term product and learning project; its size or development time is not a failure when the result is coherent, observable, tested, and genuinely reusable.

This charter describes the intended project. The [module map](module-map.md) identifies what the repository actually implements today.

## Governing model

Every substantial capability should aim for three levels of use:

1. **Convention** provides a defensible default with minimal gameplay boilerplate.
2. **Explicit configuration** exposes recognized variations without hiding consequential choices.
3. **Full replacement or raw access** permits specialized implementations without fighting the framework.

A convenience layer is incomplete if it offers no practical escape hatch. An escape hatch is incomplete if normal use requires understanding all of the underlying machinery.

## Architectural commitments

### Layer over Godot

The factory builds policy, coordination, validation, and reusable workflows over Godot's APIs. It does not attempt to create a competing engine or networking stack. Direct Godot capabilities remain legitimate at the appropriate boundary.

### Prefer composition

Gameplay types should acquire networking capabilities through components and collaborators where practical. A game object should not have to inherit from a factory-wide network-entity base class merely to participate in replication.

### Separate identities

Transient transport peer identity, persistent player identity, and runtime network-object identity solve different problems. They must not be treated as interchangeable. Only transient `PeerId` exists in the current source; the other identity models remain future design work.

### Make authority deliberate

Server-authoritative shared state is the intended default for standard multiplayer behavior. This is a design direction, not a claim that the current foundation enforces authority for arbitrary gameplay. Alternate authority models must be explicit and replaceable.

### Keep one source of truth

Runtime role, session lifecycle, and transport facts should each have an identifiable owner. Derived convenience properties are welcome; parallel mutable representations are not.

### Make automation observable

Defaults and reflection-driven behavior must expose enough validation and diagnostics to explain what was selected, rejected, or changed. The current replication component emits basic text logs; structured diagnostics are planned.

### Finish subsystems professionally

A subsystem is not complete merely because its happy path runs. Its contract, failure behavior, ownership, customization path, tests, diagnostics, documentation, and executable scenarios should agree.

## Scope

The long-term factory scope includes reusable concerns that recur across games:

- networking transport, sessions, peers, players, authority, replication, spawning, and world coordination;
- late joining, disconnect/reconnect infrastructure, networked scene transitions, and session resets;
- application bootstrap and runtime composition;
- reusable host/join, main-menu, pause, loading, settings, input, audio/video, and standard error flows;
- local configuration, save infrastructure, and dedicated-server lifecycle/configuration;
- diagnostics, validation, and operational observability;
- unit, integration, and multiprocess scenario testing;
- platform adapters and build/export tooling;
- documentation, examples, module selection, versioning, and project assembly.

These categories express scope, not current availability. The present implementation covers only an initial subset documented in [architecture.md](architecture.md).

## Outside the factory

The factory does not own the qualities that make an individual game distinct:

- game rules and mechanics;
- content and narrative;
- balance and progression;
- art direction and presentation;
- game-specific simulation or domain models.

Reusable technical primitives may support those areas without absorbing their product decisions.

## Quality bar

A reusable capability should have:

- a clear responsibility and dependency boundary;
- a safe and documented default;
- an explicit configuration path where variation is expected;
- a viable replacement or raw-access path;
- defined lifecycle and ownership semantics;
- useful validation and diagnostics;
- automated evidence at the appropriate test level;
- documentation that distinguishes implemented behavior from planned direction;
- at least one executable scenario when cross-process behavior is involved.

The repository has not reached this bar across all current code. The refit plan exists to close those gaps incrementally.

## Success criteria

The factory is succeeding when:

- standard multiplayer and application work becomes materially cheaper in each later project;
- previously solved failures remain solved through tests or diagnostics;
- a new multiplayer experiment can reach useful playtesting quickly without bypassing correctness;
- normal behavior is simple, while unusual behavior remains explicit and replaceable;
- ownership, lifecycle, failure, and authority can be explained from the implementation;
- framework development produces durable networking and architecture knowledge rather than diagrams without executable evidence.

Shipping a particular game quickly is not the sole metric. Neither architectural breadth nor elegance is sufficient on its own; accumulated, verified leverage is the goal.

## Decision discipline

Architectural decisions belong in reviewed architecture decision records when they become durable. Open questions must remain visibly open rather than being smuggled into documentation as settled design. Current open questions include the eventual composition-root form and the shape and maturity of structured error codes. `NetworkSession` transport ownership is settled: the session borrows the injected transport and its caller disposes it.

See [decisions/README.md](decisions/README.md) for the ADR process.
