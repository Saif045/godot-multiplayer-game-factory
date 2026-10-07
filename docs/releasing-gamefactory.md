# Releasing GameFactory

A release is an immutable Git version boundary in the canonical
`Saif045/godot-multiplayer-game-factory` repository. It begins only from an
accepted GameFactory commit. [ADR 0007](decisions/0007-upstream-downstream-distribution.md)
defines distribution; the [consumer guide](consuming-gamefactory.md) defines
how games integrate releases.

## Version policy

The intended first release is `gamefactory-v0.1`. This guide does not create it
or assert publication. Narrow compatible fixes should favor patch-like tags
such as `gamefactory-v0.1.1` and `gamefactory-v0.1.2`; larger pre-1.0 capability
changes may use `gamefactory-v0.2` or `gamefactory-v0.3`.

There is no formal semantic-versioning compatibility guarantee for v0.x. Every
release describes important affected surfaces and migration needs; consumers
upgrade deliberately, and conflict-free updates are not promised. A stable
policy may eventually justify `gamefactory-v1.0`, but is not defined here.

Once published, a release tag must never move. If the release is wrong, publish
another tag; never force-update `gamefactory-v0.1` or another published version.
The tag identifies the exact commit, not latest development state. A GitHub
Release may later provide human-readable notes; the Git tag remains the technical
boundary.

## Acceptance before release

```text
issue -> focused upstream fix -> tests/probes
      -> required runtime acceptance -> documentation/migration notes
      -> accepted release commit -> immutable tag -> push commit and tag
```

Use the narrowest validation appropriate to affected behavior, following
[validation commands](testing.md) and [testing strategy](testing-strategy.md).
For source changes, run an appropriate build/test and `git diff --check`; add
focused regressions where they cover meaningful reusable invariants. Vendor or
configuration changes require relevant import/dependency checks. For exports,
validate the actual manifest and dependencies.

Runtime/manual acceptance must run and pass before committing an implementation
when that acceptance is required. Builds, headless checks and unit passes do not
replace it. Every Godot/Steam/VM/process test follows [testing protocol](testing-protocol.md):
assertions/timeouts, preflight, clean state, ordered launch, terminal
PASS/FAIL/BLOCKED, preserved evidence, teardown and verified cleanup. Host/VM
execution also follows the [runtime operator protocol](runtime-test-operator.md).
A failure in an earlier transport layer does not establish a later gameplay defect.

A documentation-only closure uses diff hygiene, full diff/scope review, link
checks and baseline provenance checks; it does not need a new Steam A/B run when
accepted runtime contents remain unchanged. Describe previous acceptance as prior
evidence, not newly executed checks.

A downstream-only reproducer is valid integration evidence. Record the game
commit, consumed version, original reproduction, candidate result and focused
upstream validation. Do not require importing the whole game into the sandbox.
The reusable fix still requires canonical upstream review and acceptance.

## Release record

Before publication, prepare notes containing:

| Field | Required information |
| --- | --- |
| Release tag | Exact immutable version name |
| Commit | Full peeled commit SHA targeted by the tag |
| Changes | Important fixes/features and affected runtime/vendor/tool surfaces |
| Migration | Required caller, scene, input, autoload, configuration or dependency changes; explicitly state when none are required |
| Shared integration | Whether project/assembly/solution/export settings changed and how consumers reconcile them |
| Validation | Commands actually run, terminal results, runtime evidence and cleanup; distinguish prior acceptance from this release's checks |
| Known limitations | Remaining issues and compatibility boundaries, including downstream-only reproduction limits |

Keep notes in a reviewed repository document or the release publication record.
If committing repository notes before creating the tag, do not guess a self-referential
final commit SHA; resolve and record it after the final commit exists. A published
release record can provide that exact SHA without amending the accepted commit.
If code changes after required acceptance, reevaluate affected checks before release.

## Maintainer publication procedure

These commands are examples for an authorized release operation, not instructions
to publish during this documentation task. Verify a clean working tree, inspect
the final diff and accepted evidence, and confirm the target branch and remote.
Do not tag an unaccepted implementation or stage unrelated work.

For the first release, the accepted runtime baseline is
`2ea72888989eed883b09065c741c6410aa17077d`. The tag must target the final
documentation-complete commit descending from it, with runtime/vendor/config/test/tool
contents unchanged. It must not target the older runtime-only commit. Verify:

```bash
git status --short
git branch --show-current
git remote -v
git merge-base --is-ancestor 2ea72888989eed883b09065c741c6410aa17077d HEAD
git diff --name-only 2ea72888989eed883b09065c741c6410aa17077d HEAD
git rev-parse HEAD
```

Review the complete changed-path list against documentation-only scope, including
root `AGENTS.md`; it must contain no runtime, vendor, configuration, tests, tooling
or binaries. Save the full final SHA as the reviewed target. Check both local and
remote tags for an existing version before creating it:

```bash
git tag --list gamefactory-v0.1
git ls-remote --tags origin refs/tags/gamefactory-v0.1
```

If the tag already exists, verify it and stop rather than moving or recreating
it. After explicit publication authorization, push the accepted master commit,
create an annotated tag on the exact reviewed SHA, verify it, and push only that tag:

```bash
git push origin master
git tag -a gamefactory-v0.1 <REVIEWED-FINAL-COMMIT-SHA> -m "GameFactory v0.1"
git rev-parse 'gamefactory-v0.1^{commit}'
git push origin refs/tags/gamefactory-v0.1
git ls-remote --tags origin refs/tags/gamefactory-v0.1 'refs/tags/gamefactory-v0.1^{}'
```

Replace the SHA placeholder; do not execute it literally. Verify the peeled
remote commit equals the reviewed commit. For later releases, substitute the
reviewed version and commit and use that release's acceptance/migration record.
Do not push all unrelated local tags or use force. Then publish optional GitHub
Release notes and direct consumers to the exact tag and validation requirements.

## v0.1 evidence and limitations

[Current state](current-state.md), [session reuse acceptance](session-reuse-acceptance.md),
and [Netfox compatibility provenance](netfox-compatibility-spike.md) document the
accepted runtime. The Netfox source pin remains
`38f59778b02bfd1a3dedc7dcc985945d7058858d`, reporting 1.49.3; no upgrade is part
of release closure. Recorded validation includes 73 tests and the accepted runtime
checks; these are prior results, not reruns implied by this guide.

Lobby-phase join/rejoin and repeated rounds are accepted. Gameplay-phase joining
is intentionally unsupported; discovery hides the lobby, and the direct already-connected
rejection fallback was not forced in final acceptance. Intermittent BadCert/native
Steam behavior remains unresolved. Windows `0xC0000142` is unreproduced and
unresolved. Production App ID/Steam launch normalization remains deferred.
v0.1 is not a production/shipping guarantee, packaged addon/submodule/NuGet
dependency, dedicated-server-complete framework, or formal compatibility promise.
The downstream manual workflow still needs its first real-game validation.
