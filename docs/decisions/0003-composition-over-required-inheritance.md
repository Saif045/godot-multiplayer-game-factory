# ADR 0003: Prefer Composition over Required Inheritance

- Status: Accepted
- Date: 2026-08-18
- Owners: Project maintainers
- Related: [Project charter](../project-charter.md), [Architecture](../architecture.md), [ADR 0001](0001-godot-native-framework.md)

## Context

Gameplay nodes already participate in Godot's type and scene hierarchies. Requiring every networked gameplay type to inherit from a GameFactory base class would consume its available C# base class, couple domain code to framework lifecycle decisions, and make incremental adoption harder.

The current replication experiment demonstrates composition: a `NetworkObject` child reflects properties on its parent gameplay node and owns a child `MultiplayerSynchronizer`. This implementation is experimental and currently assumes a fixed hierarchy and a `Node3D` scene root. It does not establish a finished component contract.

## Decision

GameFactory will prefer components and injected collaborators over requiring gameplay types to inherit from a framework-wide base class.

Required inheritance may still be used inside a tightly scoped implementation when Godot or C# makes it necessary, but it must not become the universal admission requirement for multiplayer participation. Components must respect Godot node lifecycles and make their host assumptions explicit and validated.

## Rationale

Composition preserves gameplay type freedom, supports incremental adoption, and makes individual networking responsibilities easier to replace or omit. It also aligns with Godot's scene composition model.

## Alternatives considered

### Require a universal network-entity base class

A common base could centralize lifecycle hooks and shared fields. It would also constrain every gameplay hierarchy, encourage unrelated responsibilities to accumulate in one type, and make raw Godot or alternative framework paths harder to combine.

### Use only static helpers or global services

Helpers avoid inheritance but do not naturally express per-object lifetime, configuration, and scene ownership. Global services can coordinate systems, but they do not replace object-level composition.

### Mandate a particular component hierarchy now

Standardizing today's experimental `NetworkObject` hierarchy would create a premature contract. Its hierarchy, validation, and dimension dependence are known refit targets.

## Consequences

### Positive

- Gameplay nodes retain their own inheritance choices.
- Networking capabilities can be added, removed, and tested as focused collaborators.
- Framework and raw Godot behavior can coexist in a scene.

### Negative

- Components need explicit host discovery, validation, and lifecycle rules.
- Scene configuration errors may occur unless diagnostics are strong.
- Communication between components can require more explicit dependencies than a shared base class.

### Neutral or operational

- Composition does not mean every responsibility must be a Godot node; ordinary injected .NET collaborators remain appropriate.
- Shared behavior should not migrate into a universal base class merely for convenience.

## Validation and evidence

The `NetworkObject` and `RawDoor` sandbox composition is current implementation evidence, but only as an experimental probe. It shows that a gameplay node can use replication support without inheriting from a factory base. Automated tests and generalized scene validation do not exist yet.

Future component work should verify host compatibility, lifecycle ordering, clear failure diagnostics, and use from more than one Godot spatial dimension where relevant.

## Compatibility and migration

No migration is required. This ADR avoids imposing a new base class. The project has no defined compatibility policy for its experimental component APIs.

## Open questions

- The final reusable composition-root and component initialization forms remain undecided.

## Follow-up work

- Make the replication component dimension-independent.
- Validate its required host and synchronizer relationships.
- Add tests and executable scenarios for component lifecycle and replication behavior.

## Supersession

None.
