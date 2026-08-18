# ADR 0005: Keep One Assembly until Boundaries Justify Packaging

- Status: Accepted
- Date: 2026-08-18
- Owners: Project maintainers
- Related: [Project charter](../project-charter.md), [Module map](../module-map.md)

## Context

GameFactory is at an early foundation stage. The repository currently has one Godot C# project and one solution project. Runtime, networking, replication experiment, and sandbox code compile in that assembly. No package publication, module-selection mechanism, compatibility policy, or release process exists.

Splitting assemblies can enforce dependencies and support selective distribution, but it also adds project references, build configuration, packaging choices, and version coordination. The present implementation does not yet provide stable subsystem contracts or usage evidence sufficient to choose those boundaries.

## Decision

GameFactory will keep production and sandbox source in the existing assembly until concrete dependency, testing, distribution, or reuse requirements justify a split.

Directories and namespaces will continue to communicate responsibility within that assembly. New assemblies or packages require a documented boundary, dependency direction, ownership model, and validation need. This decision does not prohibit a separate test project or external development tool when its runtime and dependency needs are materially different.

## Rationale

One assembly minimizes structural overhead while the framework's contracts are still being established. It keeps refactoring inexpensive and avoids presenting directory organization as a prematurely stable package architecture. Requiring evidence for a split makes later packaging reflect real consumers and dependency boundaries.

## Alternatives considered

### Split by every planned module now

Projects for runtime, networking, replication, application shell, platform adapters, and other planned areas could advertise modularity. Most of those areas do not exist, and their boundaries would be speculative.

### Separate engine-independent and Godot-dependent production assemblies immediately

The current source has a meaningful conceptual seam, especially around session policy and transport. However, there are no engine-independent tests or distribution requirements yet, and the contracts are scheduled for refit. A split now would turn an evolving implementation detail into build structure prematurely.

### Publish packages from the start

Early packages could exercise consumption workflows, but they would require versioning, compatibility, and release decisions that the project has deliberately not made.

## Consequences

### Positive

- Structural refactoring remains simple while responsibilities and contracts mature.
- The repository avoids unused projects and speculative package boundaries.
- Directory and namespace normalization can proceed independently of packaging.

### Negative

- Assembly boundaries do not currently enforce dependency direction.
- Consumers cannot select or reference separately packaged production modules.
- Sandbox code shares an assembly with reusable framework code.

### Neutral or operational

- A separate test project or scenario-runner tool may be added because those are verification tools with distinct execution requirements, not premature production packages.
- Future assembly splits require an ADR or an explicit superseding decision supported by evidence.

## Validation and evidence

The checked-in solution and project file confirm the current single-assembly structure. There is no package metadata, test project, or consumer evidence demonstrating a necessary production split. Validation for a future change should include dependency analysis, build/test impact, and at least one concrete consumption or isolation requirement.

## Compatibility and migration

This decision preserves the present build structure and requires no migration. No packaging or binary compatibility policy exists.

## Open questions

- Which subsystem boundary will first require independent compilation or distribution is unknown.
- The eventual relationship between reusable framework code, examples, and project assembly tooling remains future work.

## Follow-up work

- Keep namespace and directory responsibilities visible during mechanical normalization.
- Revisit assembly boundaries when tests, external consumers, platform adapters, or module assembly create measurable pressure.

## Supersession

None.
