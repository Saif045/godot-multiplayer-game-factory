# Project Charter

## Mission

GameFactory is a reusable Godot C# foundation for rapidly building small-session online co-op games without rebuilding their recurring production infrastructure. It targets both player-hosted/listen-server and dedicated-server games, with server-authoritative shared gameplay as the normal default and Steam planned as a first-class online/platform integration.

The framework should make ordinary multiplayer behavior safe and extremely easy, make uncommon behavior explicit, and leave advanced users able to replace factory behavior or work directly with Godot.

The project compounds verified leverage. A solved standard problem should leave behind reusable code, tests, diagnostics, documentation, tooling, and engineering knowledge so later co-op games begin from a stronger, more production-shaped base. The goal is speed with decent-to-high quality, not maximizing framework size, abstraction count, or implementation time.

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

Defaults and reflection-driven behavior must expose enough validation and diagnostics to explain what was selected, rejected, or changed. The exploratory replication sandbox emits basic text logs; structured diagnostics are planned.

### Finish subsystems professionally

A subsystem is not complete merely because its happy path runs. Its contract, failure behavior, ownership, customization path, tests, diagnostics, documentation, and executable scenarios should agree.

## Scope

The long-term factory scope includes reusable concerns that recur across games:

- networking transport, sessions, peers, players, authority, replication, spawning, and world coordination;
- player lifecycle, late joining, disconnect/reconnect infrastructure, networked scene transitions, and session resets;
- Steam lobbies, friends, invites, and joining, through a suitable Godot integration where that creates real leverage;
- application bootstrap, game/session flow, host/join, menus, pause, loading, settings, input, audio, and standard error flows;
- common co-op interaction, item, pickup/drop, inventory, and character/player primitives only when gameplay proves them reusable;
- local configuration, save infrastructure, dedicated-server lifecycle/configuration, diagnostics, validation, and operational observability;
- unit, integration, and multiprocess scenario testing; and
- documentation, examples, platform/build tooling, and other recurring infrastructure a new small co-op game would otherwise recreate.

These categories express target scope, not current availability. The present implementation covers only an initial networking subset documented in [architecture.md](architecture.md).

## Outside the factory

The factory does not own the qualities that make an individual game distinct:

- unique core mechanics and game-specific rules;
- content and narrative;
- balance and progression;
- art direction and presentation;
- game-specific simulation or domain models.

For example, GameFactory may eventually provide interaction or pickup infrastructure, while a particular game's climbing mechanic, mountain rules, special items, and win conditions remain game code. Reusable technical primitives may support distinctive work without absorbing its product decisions.

## Development strategy

GameFactory is for navigating mostly solved game-development problems, not reinventing them. Before building a common subsystem, investigate relevant Godot libraries, plugins, templates, and open-source projects; then deliberately choose to **use**, **adapt/learn from**, or **build**. Maaack's Game Template is the kind of candidate worth evaluating for common game-shell work, and Steam functionality should evaluate existing Godot Steam integrations instead of recreating platform APIs.

External dependencies are welcome when they save substantial implementation or maintenance time, have a compatible license, are reasonably maintained, fit the Godot/GameFactory architecture, are not disproportionately large or fragile for their value, and leave a reasonable replacement or customization path. Do not add a dependency merely to avoid trivial code.

Prefer vertical, useful subsystems over disconnected micro-features. A player-lifecycle slice, for example, should establish and validate peer join, player creation, player spawn, ownership association, disconnect cleanup, and listen/dedicated behavior together. Real playable scenarios drive abstractions; networking grows when actual co-op systems require it.

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
- a new small co-op game can reach a playable, production-shaped state quickly without bypassing correctness;
- normal behavior is simple, while unusual behavior remains explicit and replaceable;
- ownership, lifecycle, failure, and authority can be explained from the implementation;
- solved work creates verified leverage through reuse, integration, or a deliberately chosen custom implementation.

The decisive measure is how quickly a new small co-op game can become playable and production-shaped. Neither architectural breadth nor elegance is sufficient on its own; accumulated, verified leverage is the goal.

## Decision discipline

Architectural decisions belong in reviewed architecture decision records when they become durable. Open questions must remain visibly open rather than being smuggled into documentation as settled design. Current open questions include the eventual composition-root form and the shape and maturity of structured error codes. `NetworkSession` transport ownership is settled: the session borrows the injected transport and its caller disposes it.

See [decisions/README.md](decisions/README.md) for the ADR process.
