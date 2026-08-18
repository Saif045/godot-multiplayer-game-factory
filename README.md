# Multiplayer Game Factory

Multiplayer Game Factory is a Godot C# project for building reusable multiplayer and application infrastructure. Its goal is to make common player-hosted and dedicated-server flows safe and inexpensive to adopt while keeping unusual behavior explicit and raw Godot APIs available.

The repository is at an early foundation stage. It is not presented as production-ready, and it does not yet define platform-support, compatibility, release, or licensing guarantees.

## What exists today

The implemented source currently provides:

- process roles for offline, client, listen-server, and dedicated-server execution;
- a positive, typed `PeerId`, a `NetworkPeer` model, and an observable peer registry;
- an `INetworkTransport` boundary and one ENet adapter over Godot's multiplayer API;
- session startup, connection, intentional shutdown, failure, and reset behavior;
- an experimental `NetworkObject` component that builds a Godot replication configuration from annotated host properties;
- manual connection and replication probe scenes under `sandbox/`.

The project does not yet contain automated tests, a fake transport, a multiprocess scenario runner, a general network-world or spawn service, persistent player identity, a complete application shell, packaging, or CI evidence. These and other long-term capabilities are direction, not implemented features.

## Current repository configuration

The project file currently selects Godot .NET SDK 4.7.1 and targets .NET 8, with a conditional .NET 9 target when `GodotTargetPlatform` is `android`. The Godot project currently enables C#, Forward Plus rendering, Jolt Physics, and Windows D3D12. These facts describe the checked-in configuration; they are not a support matrix.

The configured main scene is the connection probe:

`res://sandbox/connection/network_probe.tscn`

Both probes inspect Godot user arguments and recognize:

- `--server` for a listen server;
- `--dedicated-server` for a dedicated-server runtime role;
- `--client` for a client connecting to `127.0.0.1:7000`.

The replication probe is `res://sandbox/replication/replication_probe.tscn`, but it is not the configured main scene. The repository contains no checked-in runner that selects scenes or launches multiple processes, so this document does not prescribe an unverified command line. The probes are exploratory tools rather than automated tests.

## Design philosophy

The framework follows a three-level customization model:

1. **Convention:** common multiplayer behavior should have safe, low-boilerplate defaults.
2. **Explicit configuration:** recognized uncommon behavior should remain visible and configurable.
3. **Full replacement or raw access:** advanced systems must be able to bypass or replace a factory layer and use Godot directly.

Other governing principles are:

- prefer composition over a required gameplay inheritance hierarchy;
- isolate session policy from concrete transport technology;
- keep transient peer identity distinct from persistent player identity and runtime object identity;
- use server-authoritative shared state as the intended default, without preventing deliberately different designs;
- keep automatic decisions inspectable;
- build tests, diagnostics, validation, and documentation as parts of a subsystem rather than later polish;
- keep game-specific mechanics, balance, content, progression, and presentation outside the factory.

These are project commitments. Not all are implemented yet.

## Repository map

- `factory/Core/` — process role and runtime context.
- `factory/networking/Core/` — networking value types.
- `factory/networking/Peers/` — peer model and registry.
- `factory/networking/Sessions/` — session lifecycle policy.
- `factory/networking/Transport/` — transport contract and ENet adapter.
- `factory/networking/objects/` — early replication component and annotation.
- `sandbox/connection/` — manual connection lifecycle probe.
- `sandbox/replication/` — manual spawn, RPC, and property-replication probe.
- `docs/` — project policy and architecture documentation.

Path casing above follows Git's tracked names. Casing and namespace configuration have known inconsistencies recorded in the [module map](docs/module-map.md).

## Documentation

- [Project charter](docs/project-charter.md)
- [Architecture](docs/architecture.md) — canonical architectural overview
- [Terminology](docs/terminology.md)
- [Module map](docs/module-map.md)
- [Coding standards](docs/coding-standards.md)
- [Testing strategy](docs/testing-strategy.md)
- [Architecture decision records](docs/decisions/README.md)
- [Networking foundation](docs/networking-foundation.md) — the earlier focused networking note, retained as supporting context

## Immediate refit direction

The next work is planned in connected stages:

1. establish the project documentation and engineering standards;
2. normalize namespace spelling, path casing, module placement, and formatting without changing behavior;
3. add engine-independent unit tests and a fake transport around the existing lifecycle contracts;
4. harden lifecycle ownership, disposal, cleanup, transitions, and invariants;
5. finish the replication abstraction with explicit automatic and manual paths, validation, diagnostics, tests, and dimension-independent composition;
6. add reusable multiprocess scenario infrastructure;
7. begin a network-world layer for stable spawn definitions and runtime spawn/despawn behavior.

Everything after the documentation stage is planned work. Names and contracts remain subject to architectural review until implemented and recorded.
