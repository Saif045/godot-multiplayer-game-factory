# Consuming GameFactory in a real game

GameFactory v0.1 is a versioned reusable Godot project foundation with a canonical
upstream Git repository and a defined downstream Git consumption model.
[ADR 0007](decisions/0007-upstream-downstream-distribution.md) establishes this
contract. The [module map](module-map.md) describes implemented responsibilities;
[current state](current-state.md) records accepted behavior and known limits.

## The upstream relationship

Each real game is a separate repository retaining GameFactory Git ancestry.
`origin` means this product; `gamefactory` means the reusable upstream foundation:

```text
origin       -> the actual game's repository
gamefactory  -> https://github.com/Saif045/godot-multiplayer-game-factory.git
```

Keep the `gamefactory` remote unless intentionally abandoning upstream updates.
Consume reviewed immutable tags, not ongoing `gamefactory/master` development.
Manual copies/ZIPs and cherry-picked official releases are not normal updates.
Shared ancestry lets Git recognize already integrated upstream changes and find
a meaningful merge base for later releases.

## Three ownership zones

| Zone | Paths and responsibilities | Downstream rule |
| --- | --- | --- |
| GameFactory-owned | `factory/`; `addons/netfox/`, `addons/netfox.internals/`, `addons/GodotGAS/`, `addons/godotsteam/`, `addons/maaacks_game_template/`, `addons/plugin_updater/`; `third_party/patches/godotsteam/`; `tests/GameFactory.Tests/`; framework diagnostics, `sandbox/` probes, validation and A/B infrastructure in `tools/` | Normally leave unchanged. Reusable changes return upstream; avoid permanent local forks. |
| Game-owned | `game/`, game assets, game tests and product documentation | Implement the product's mechanics/content here. |
| Shared integration | `project.godot`, `GameFactory.csproj`, `GameFactory.sln`, `.gitignore`, root project/export configuration such as `export_presets.cfg` | Review and reconcile framework/dependency requirements with game configuration. |

Use `game/` as the primary convention, with only the directories the game needs:

```text
game/
    gameplay/
    levels/
    characters/
    items/
    ui/
    progression/
    content/
    resources/
    tests/
```

Game assets may follow existing project asset conventions or a game-owned asset
root. Ownership matters more than forcing this exact internal organization.
Record product-specific paths clearly so later agents can distinguish them from
upstream infrastructure. Keep framework guide/reference material identifiable;
downstream README and agent instructions may add product-specific guidance.

Compose game-specific abilities, items, run rules, progression and levels from
`game/` using public factory boundaries and normal Godot nodes. Do not place a
mechanic in `factory/` because it might someday be reusable. A missing extension
seam can justify an upstream improvement when its reusable contract is clear.
Otherwise keep game-specific behavior in the game.

## Create a new game

These are instructions for use after `gamefactory-v0.1` has been published;
this documentation does not assert that the tag already exists. Create an empty
game remote repository without generated starter commits, then run:

```bash
git clone https://github.com/Saif045/godot-multiplayer-game-factory.git my-game
cd my-game
git switch -c main gamefactory-v0.1
git remote rename origin gamefactory
git remote add origin <NEW-GAME-REPOSITORY-URL>
git push -u origin main
```

Replace the URL placeholder with the game's repository URL. Verify remotes with
`git remote -v`. The game begins from the tagged release, rather than whatever
upstream `master` currently contains. The clone preserves upstream history;
renaming the remote and pushing a new branch does not sever it. Keep the initial
foundation intact while introducing product content under `game/` and reviewing
main-scene, input, naming and export changes as shared integration work.

## Record the consumer baseline

Each game creates its own `.gamefactory-version`:

```text
version=gamefactory-v0.1
commit=<exact GameFactory release commit SHA>
```

Obtain the full release commit with `git rev-parse gamefactory-v0.1^{commit}`
(quote the revision in shells that interpret braces). This means the peeled
commit, not the annotated tag object's SHA. Commit the marker in the game after
the baseline is integrated and validated. It is informational provenance for
humans, agents, scripts and CI, not a package manager. Git ancestry is authoritative.
Do not use the game HEAD SHA as the upstream release SHA. Update the marker only
after each release integration passes validation. No speculative v0.1 SHA or
consumer marker is added to the upstream repository before its final tag exists.

## Update one game

Suppose the game uses `gamefactory-v0.1` and an official `gamefactory-v0.1.1`
is published. Begin on the game's current main branch with a clean working tree;
commit or separately preserve unrelated work before starting:

```bash
git switch main
git status
git fetch gamefactory --tags
git switch -c chore/gamefactory-v0.1.1
git merge --no-ff gamefactory-v0.1.1
```

Read the chosen version's release record and migration requirements (the
[release guide](releasing-gamefactory.md) defines that record) and verify its
exact commit. A tag-fetch conflict is a
provenance issue to investigate, not permission to force-replace a tag.

1. Inspect conflicts and classify them using the ownership rules below.
2. Reconcile shared integration files; inspect all framework-owned path changes.
3. Build and run the game's appropriate automated tests.
4. Run focused runtime acceptance when affected behavior requires it. Follow
   [testing protocol](testing-protocol.md) for Godot/Steam/process tests and the
   [operator protocol](runtime-test-operator.md) for host/VM work. Record actual
   results, evidence and cleanup; startup or a unit pass cannot replace gameplay evidence.
5. After successful validation, update `.gamefactory-version` with the tag and
   exact release commit, and commit that marker and any remaining integration changes.
6. Integrate the update branch into the game's main branch preserving ancestry.

If the merge stops for conflicts, resolve and stage the intended files, then
complete the merge commit with `git commit`. Keep the update branch out of main
until required acceptance passes. An unconflicted `git merge` may already have
created the merge commit; the validated marker can be a subsequent focused commit.
If abandoning an in-progress merge, `git merge --abort` returns to the pre-merge
state when started clean; preserve any needed diagnostic work first.

**Do not squash GameFactory release-update merges. Do not replace them with
cherry-picks of individual official GameFactory commits.** Preserve the release
ancestry when merging the update PR too; a squash or rebase integration that
discards its merge relationship defeats the purpose. Product feature PRs can
follow the game's usual policy.

```text
GameFactory tag ------------------+
                                  |
game-specific history ------------+-- merge commit
```

Git can now tell that the game contains the upstream release. Verify with
`git merge-base --is-ancestor gamefactory-v0.1.1 HEAD` (exit 0 means contained).
Do not routinely merge `gamefactory/master`: it is development state, while a
release tag is a reviewed boundary. Debugging against unreleased upstream work
must be an explicit temporary exception followed by official release integration.

## A Game 5 reproduction can reveal an upstream defect

This is the central repair rule: the repository that reproduces a problem and
the repository that owns the fix need not be the same repository.

Suppose Game 5's special climbing mechanic triggers a player-lifecycle bug that
the standalone factory sandbox cannot reproduce:

```text
Game 5 mechanic -> exposes lifecycle defect -> GameFactory owns lifecycle code
reproduction owner = Game 5
fix owner          = GameFactory
```

That is legitimate integration evidence. Record the exact Game 5 commit,
consumed release, steps, assertions, failure evidence, and environment before
changing variables. Create a temporary diagnosis branch:

```bash
git switch -c bug/gamefactory-lifecycle-ordering
```

On this branch, edits to `game/`, `factory/` and shared integration files are
allowed when needed to diagnose the failure. Temporary framework edits must
not silently become permanent Game 5 ownership. Preserve the original failed
run and follow the repository's investigation discipline after terminal tests.

### Isolate ownership and transfer the candidate

If it is only a game bug, fix Game 5 without changing GameFactory. If it is a
framework bug, move the reusable fix upstream. If both are wrong, create separate
framework and game fixes rather than combining their ownership permanently.

When possible, isolate the experimental framework fix in a commit touching only
GameFactory-owned paths, for example `TEMP: fix lifecycle ordering exposed by
Game 5 climbing`. It is transfer material, not the official game update. In the
actual canonical GameFactory checkout, supported transfer options include:

```bash
git remote add game5 <GAME-5-REPOSITORY-URL>
git fetch game5 bug/gamefactory-lifecycle-ordering
git switch -c codex/fix-lifecycle-ordering master
git cherry-pick <FOCUSED-TEMPORARY-FIX-COMMIT>
```

Use an existing remote if already configured; inspect the focused commit before
applying it. Shared ancestry often makes transfer straightforward, but differing
versions can still require reconciliation. Export/apply a focused patch or
carefully recreate the minimal upstream change when necessary. Review the final
diff in GameFactory, excluding Game 5's mechanic and unrelated configuration.
This candidate-transfer cherry-pick is distinct from consuming an official
release, which must use an ancestry-preserving merge.

### Validate and release upstream

A candidate working in Game 5 is necessary evidence, but insufficient alone.
Run narrow upstream build/tests/probes, add a generic regression for the reusable
invariant when practical, and run required runtime acceptance before committing
the implementation. Do not import Game 5's entire climbing mechanic or distort
framework architecture just to make the full reproducer standalone.

The evidence can state: Game 5 at an exact commit reproduces the defect; the
candidate removes it there; upstream regression/unit/headless validation passes;
and a focused generic invariant is covered where practical. State clearly when
Game 5 supplies the only realistic full integration reproduction. After upstream
review and acceptance, publish the fix as an immutable release such as
`gamefactory-v0.1.1`, following the [release process](releasing-gamefactory.md).

### Return through the official release

Do not merge the temporary bug branch into Game 5 main merely because it works.
Preserve any separate game fix, return to the clean product line, and run:

```bash
git switch main
git fetch gamefactory --tags
git switch -c chore/gamefactory-v0.1.1
git merge --no-ff gamefactory-v0.1.1
```

Resolve integration conflicts and rerun the **original Game 5 reproduction**
against the official release. Reapply only the separately owned game fix if
needed. Update the marker after validation and integrate the release branch
without squashing. Only then retire/delete the temporary diagnosis branch once
its useful evidence and game changes are preserved. The official release now
owns the fix; the experiment is no longer authoritative.

Keeping six permanent local copies of framework repairs produces six custom
forks. Moving the fix upstream once lets the same reviewed release reach all games.

## Update six games

Each game receives the same release independently:

```text
Game 1 -> chore/gamefactory-v0.1.1
Game 2 -> chore/gamefactory-v0.1.1
Game 3 -> chore/gamefactory-v0.1.1
Game 4 -> chore/gamefactory-v0.1.1
Game 5 -> chore/gamefactory-v0.1.1
Game 6 -> chore/gamefactory-v0.1.1
```

Apply the one-game workflow in each repository, with its own conflict review,
baseline marker, tests and runtime acceptance. Passing in Game 5 does not accept
the update in the other five games. A useful propagation report is:

```text
Game1 PASS
Game2 PASS
Game3 BLOCKED shared configuration conflict
Game4 PASS
Game5 PASS
Game6 PASS
```

These are desired report examples, not actual validation results. Future tooling
may fetch tags, check baselines, create branches, merge requested tags, run each
game's validation, open PRs and report conflicts. It must not hide conflicts or
choose resolutions automatically. No such bulk automation is implemented here;
the first real game should validate the manual workflow first.

## Interpret conflicts by ownership

| Conflict | Meaning and response |
| --- | --- |
| `factory/networking/players/PlayerLifecycle.cs` or another framework path | Suspicious downstream modification. Determine whether it should have been upstreamed, is an obsolete experiment to discard, or is a reusable missing change to upstream first. Do not blindly keep the game's version. |
| `project.godot` or another shared surface | Often legitimate. Combine required factory/dependency/autoload settings with game inputs, rendering, project and export requirements. Do not mechanically choose `ours` or `theirs`. |
| `game/climbing/ClimbingController.cs` or another product path | A normal upstream release should not touch it. Investigate why the release contains game-specific paths before resolving. |

## Version boundaries and future packaging

The intended first tag is `gamefactory-v0.1`; narrow patch-like releases may be
`gamefactory-v0.1.1`/`gamefactory-v0.1.2`, and larger capability releases may be
`gamefactory-v0.2`/`gamefactory-v0.3`. No formal semantic-versioning compatibility
guarantee exists for v0.x. Fixes favor compatibility, larger changes need migration
notes, affected surfaces must be described, and games upgrade deliberately.
Published tags never move; a wrong release is corrected by another tag.

v0.1 is not a NuGet library, single Godot addon, Git submodule, package-manager
dependency, generic engine-independent multiplayer SDK, production Steam shipping
guarantee, dedicated-server-complete framework, or promise of conflict-free v0.x
upgrades. Its project-wide composition is why packaging is deferred.

Real games may later justify `addons/gamefactory/` and a separation into
GameFactory Runtime, Starter defaults, and tooling. A submodule or other package
could then fit. This is a possibility requiring consumer evidence, not a promise
of the final architecture or work to do before the first game.
