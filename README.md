# GameFactory

GameFactory is a Godot C# project for reusable multiplayer and application infrastructure. It aims to make ordinary player-hosted and dedicated-server flows safe to adopt, while leaving unusual behavior explicit and raw Godot APIs available.

The repository is an early foundation, not a production-ready framework. It has no platform-support, compatibility, release, or licensing guarantee.

## What exists today

- process roles, typed transient `PeerId`, peer registry, session lifecycle, and an ENet transport adapter;
- engine-independent xUnit coverage for the peer and session foundation through a deterministic test-only fake transport;
- a compositional `NetworkObject` host with replaceable authority and replication component scenes;
- `[Replicated]` metadata that configures a component-owned `MultiplayerSynchronizer` for host properties; and
- a server-allocated `NetworkObjectId` plus `NetworkWorld` registry and `NetworkSpawnGroup` spawning foundation; and
- manual connection and replication sandbox probes.

`NetworkSession` borrows its injected transport: it can close active session use, but its caller owns transport disposal. `NetworkObject` is not a universal gameplay base class, and `NetworkObjectId` is runtime identity rather than player identity. `NetworkWorld` currently covers dynamic runtime spawning only. Persistent player identity, authored/static network objects, spawn initialization data, Godot integration tests, multiprocess scenarios, CI, packaging, and an application shell remain planned.

The replication sandbox has been manually exercised for normal state change and late joining. That is exploratory evidence, not automated Godot test coverage.

## Current configuration

The project uses Godot .NET SDK 4.7.1 and .NET 8, with conditional .NET 9 for Android. The configured main scene is `res://sandbox/connection/network_probe.tscn`. Both probes recognize `--server`, `--dedicated-server`, and `--client`; the replication probe is not the configured main scene and no checked-in multiprocess runner prescribes a command line.

## Design philosophy

1. **Convention** for common, low-boilerplate behavior.
2. **Explicit configuration** for recognized variations.
3. **Replacement or raw Godot access** for advanced behavior.

The project prefers composition over a required gameplay hierarchy, keeps peer/player/object identities separate, treats server-authoritative shared state as the default direction, and keeps game-specific mechanics outside the factory.

## Repository map

- `factory/runtime/` — process role and runtime context.
- `factory/networking/peers/` — peer value and peer registry.
- `factory/networking/sessions/` — session lifecycle policy.
- `factory/networking/transport/` — transport contract and ENet adapter.
- `factory/networking/objects/` — generic component host and default object components.
- `factory/networking/world/` — runtime object registry and spawn groups.
- `sandbox/` — manual connection and replication probes.
- `tests/GameFactory.Tests/` — engine-independent xUnit tests and fake transport.
- `docs/` — architecture and engineering records.

Directories, scenes, resources, and assets use lowercase or snake_case. C# namespaces, types, and filenames use PascalCase.

## Documentation

- [Project charter](docs/project-charter.md)
- [Architecture](docs/architecture.md)
- [Terminology](docs/terminology.md)
- [Module map](docs/module-map.md)
- [Coding standards](docs/coding-standards.md)
- [Testing strategy](docs/testing-strategy.md)
- [Architecture decision records](docs/decisions/README.md)
- [Networking foundation](docs/networking-foundation.md)
