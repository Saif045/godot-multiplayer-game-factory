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
