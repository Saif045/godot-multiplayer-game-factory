# ADR 0007: Canonical upstream and ancestry-preserving downstream distribution

- Status: Accepted
- Date: 2026-10-07
- Owners: GameFactory maintainers
- Related: [Consumer guide](../consuming-gamefactory.md), [release guide](../releasing-gamefactory.md), [ADR 0005](0005-one-assembly-until-boundaries-justify-packaging.md)

## Context

The accepted runtime foundation is `2ea72888989eed883b09065c741c6410aa17077d`.
Real games need exact provenance and a repeatable way to receive reusable fixes
without each maintaining its own permanent framework fork.

GameFactory currently spans `factory/`, multiple `addons/`, project configuration,
autoloads, InputMap defaults, shell integration, native GodotSteam binaries and
patches, Netfox, GodotGAS, Maaack, diagnostics, validation tooling, and host/VM
A/B tooling. It is not an isolated `addons/gamefactory/` directory. Moving it
into an addon or submodule would require substantial packaging work before the
first game has supplied evidence for the correct boundary.

## Decision

GameFactory is maintained in the canonical upstream Git repository
`Saif045/godot-multiplayer-game-factory`. Real games are separate downstream Git
repositories that retain its Git ancestry and a permanent `gamefactory` remote
pointing to that repository. Their `origin` remote points to the game repository.

Games consume explicit immutable release tags. The intended first release is
`gamefactory-v0.1`, pointing to the final documentation-complete commit with
runtime contents unchanged from the accepted baseline. This ADR does not create
that tag or claim it has been published.

The normal update is: fetch `gamefactory` tags, merge the chosen release tag on
a game update branch, resolve legitimate integration conflicts, validate the
game, and integrate the branch while preserving the release merge ancestry.
Do not squash release integrations, routinely merge upstream `master`, copy
files to update a game, or cherry-pick official releases.

Game-specific implementation defaults to `game/`. Shared root integration files
are manually reconciled. Temporary downstream edits to framework paths are
allowed for diagnosis, but reusable fixes must move upstream and return through
an official release before permanent downstream integration. Reproduction and
fix ownership may belong to different repositories.

## Rationale

This model provides exact provenance, explicit versions, normal Git conflict
detection, consistent updates across multiple games, and one canonical source
of reusable fixes. It preserves the current working composition without a
speculative runtime/package redesign and leaves later packaging options open.

## Alternatives considered

### GitHub template only

Useful for bootstrapping or later throwaway prototypes. Generated repositories
have independent history, losing the ancestry relationship needed for repeated
upstream merges; not selected as the long-term consumption model.

### Copy/ZIP

Simple initial bootstrap, but upgrades become manual file synchronization with
weak provenance and conflict detection. Not the primary update mechanism.

### Git submodule

Attractive when the reusable dependency occupies a clean directory boundary.
The present project spans root configuration, multiple addons, native
dependencies, tooling, and runtime source. Deferred until consumer evidence
justifies reorganizing those responsibilities.

### `addons/gamefactory/` Godot plugin

A possible future package, but installation/update behavior would need to cover
autoloads, InputMap, project settings, Netfox, GodotGAS, Maaack, GodotSteam,
native binaries, shell/export configuration, and validation tooling. That design
is deferred until real games establish requirements.

### NuGet

May eventually serve pure engine-independent C# portions. It cannot represent
the full Godot scene/addon/native/project composition, so it is not the primary
distribution mechanism.

## Consequences

Exact ancestry makes integrated releases visible to Git and permits one fix to
propagate to many independently validated games. Consumers also inherit starter
history and tooling, and must review shared configuration conflicts deliberately.
Local framework edits increase update cost and must not become silent forks.
Pre-1.0 upgrades are deliberate; no formal semantic-versioning or conflict-free
upgrade guarantee is made. Published tags are never moved.

## Validation and evidence

[Session reuse acceptance](../session-reuse-acceptance.md) establishes the runtime
baseline, not downstream merge acceptance. The first real game must validate the
manual bootstrap/update workflow before bulk propagation is automated. A defect
observable only in a game may use that game's exact commit and original
reproduction as integration evidence, alongside focused upstream validation.

## Compatibility and migration

Follow the ownership zones and commands in the consumer guide. Existing games
without shared ancestry need an explicit migration plan; do not normalize
unrelated histories with blind file copies or automatic conflict choices.
Future Runtime/Starter/tooling separation is a possibility, not a promised design.

## Open questions

- Which package boundary will real downstream games justify?
- What stable compatibility policy will be appropriate after v0.x?

## Follow-up work

Create the first real game from the reviewed release tag and validate the manual
consumer workflow. Bulk update automation remains deferred.

## Supersession

None.
