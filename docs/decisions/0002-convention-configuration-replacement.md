# ADR 0002: Provide Convention, Configuration, and Replacement Paths

- Status: Accepted
- Date: 2026-08-18
- Owners: Project maintainers
- Related: [Project charter](../project-charter.md), [Architecture](../architecture.md), [ADR 0001](0001-godot-native-framework.md)

## Context

Reusable infrastructure must make ordinary behavior inexpensive without trapping games whose requirements differ. A default-only abstraction is easy to begin with but becomes restrictive. A fully manual abstraction preserves flexibility but makes every game repeat standard setup and safety decisions.

The charter identifies three intended levels of use. The current source only partially demonstrates them: session code supplies a coordinated path over a replaceable transport boundary, while sandbox code can still use Godot directly. The experimental replication component currently has an automatic annotation path and exposes its synchronizer, but it does not yet provide a complete explicit configuration mode and currently replaces authored replication configuration.

## Decision

Each substantial GameFactory capability will be designed with three paths when the domain supports meaningful variation:

1. **Convention** supplies a safe, low-boilerplate default for common behavior.
2. **Explicit configuration** exposes supported variations and consequential choices.
3. **Full replacement or raw access** lets specialized code replace the framework boundary or use the underlying Godot facility.

The paths are a design test, not a demand for three speculative APIs in every type. A feature must be implemented from concrete requirements, and documentation must say which paths actually exist. Convenience behavior must remain inspectable through validation or diagnostics appropriate to its risk.

## Rationale

This model balances adoption cost with long-term flexibility. It lets common games benefit from strong defaults, keeps recognized differences visible, and prevents framework convenience from becoming an architectural dead end.

## Alternatives considered

### Convention only

Defaults alone minimize initial surface area, but force users to fork or bypass a subsystem as soon as a legitimate variation appears. Hidden policy also becomes difficult to inspect.

### Configuration for every detail

Exposing every mechanism as an option maximizes nominal flexibility but transfers framework complexity to every caller and weakens the value of safe defaults.

### Replacement only

Small interfaces with custom implementations can be flexible, but requiring replacement for common variations creates repeated integration work and inconsistent behavior across games.

## Consequences

### Positive

- Ordinary use can remain concise and consistent.
- Supported uncommon behavior remains explicit and reviewable.
- Advanced games retain a path beyond the framework's built-in policy.

### Negative

- Subsystems require more deliberate contract and lifecycle design.
- Configuration and replacement paths add testing and documentation obligations.
- The three paths can drift unless they are validated against shared invariants.

### Neutral or operational

- Not every planned path exists today; missing paths must be stated rather than implied.
- New options require evidence of a recognized variation and should not become speculative switches.

## Validation and evidence

Current evidence is architectural and partial. `INetworkTransport` provides an existing replacement boundary, and sandbox probes demonstrate direct Godot access. The replication experiment demonstrates convention but not the complete three-path model. No automated tests currently validate equivalent lifecycle or safety behavior across paths.

Each completed subsystem should document its available paths and test shared invariants. Automated diagnostics or reports should verify consequential automatic choices where appropriate.

## Compatibility and migration

This ADR records a design constraint and introduces no API migration. The project has no defined compatibility policy. Existing experimental behavior may change during the planned refit when its configuration and replacement paths are made explicit.

## Open questions

- The exact configuration and replacement contracts remain subsystem-specific.

## Follow-up work

- Apply this model explicitly when hardening replication, without documenting proposed types as implemented.
- Identify the convention, supported configuration, raw-access path, and diagnostics in each future subsystem's documentation and tests.

## Supersession

None.

## Implementation update (2026-08-19)

The network-object replication path currently supplies an automatic convention through replaceable default component scenes, while the sandbox retains raw Godot access. A separate manual-configuration API is not implemented and must not be inferred from this record.
