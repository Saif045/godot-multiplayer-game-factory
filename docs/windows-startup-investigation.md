# Windows startup investigation

## Status — 2026-10-03

The reported intermittent `0xC0000142 / STATUS_DLL_INIT_FAILED` remains
**unresolved and unreproduced**. No failing module or native initialization
boundary was identified. Successful repeated launches do not prove it cannot
recur. No DLL replacement, periodic rebuild, retry workaround, Steam restart,
or production networking/lifecycle change was made.

The operator last observed it during a GameFactoryHost/A/B launch; an exact
failure timestamp was unavailable. Investigation began at clean `62a8cfc`.

## Frozen package and startup assertions

Fresh debug export: `build/windows_startup_20261003`, build
`gf_62a8cfc4_e51db19bd643`; manifest SHA-256
`b323ac2c7f5ee870dce4aafdf3b6d171e7254bfad91fc97ef25d20622066eaee`.
The reused comparison package was the previously accepted
`gf_f7c129f4_d838f603ed97` export in `build/test_steam`.
Both packages were checked against their actual manifests before/after testing.

Existing `--run=startup-isolation-unmapped` handling provided a diagnostic
without source changes: require a Godot log with build identity and the
intentional unknown-target diagnostic, run-specific structured logging, and
natural exit **2** within 15 seconds. The intentional Godot ERROR is expected.
This includes normal GodotSteam/.NET/SteamPlatform/GameShell initialization
but creates no lobby and enters no gameplay. A separate normal-shell probe
required Steam/shell readiness and natural exit **0** using `--quit-after 300`.

## Repeated evidence

| Controlled comparison | Before reboot | After reboot |
| --- | --- | --- |
| Direct startup, both EXEs | 80/80 | 80/80 |
| GameFactoryHost, normal profile | 6/6 | 6/6 |
| Same task, isolated A/B profile | 6/6 | 6/6 |
| Same task/profile, compatibility renderer | 6/6 | 6/6 |
| Reused export, both EXEs | 10/10 | 10/10 |
| Normal-shell graphical launch | 1/1 | 1/1 |
| Total | **109/109** | **109/109** |

Before reboot, direct probes covered normal Windows access and restricted tool
execution. After reboot, the first direct sequence had the editor present for
5 launches and absent for 35; a separate sequence passed 20 launches per EXE
with the editor verified closed. It is not an editor-open controlled suite.
The same binaries were used after reboot, without rebuilding or clearing
runtime caches. Reboot: 18:53:37 Cairo time; retest: approximately 19:09–19:14.

Steam remained running throughout. Editor/.NET/Steam processes were inventoried
without indiscriminate termination. Scheduled probes used the unchanged limited
interactive GameFactoryHost task and runner, temporarily supplied diagnostic
config, disabled the log-tail window, and restored original config/status.
The runner's three-second alive check is not the diagnostic success boundary.
All test-owned game processes were cleaned up; no VM was launched.

Application/System/CodeIntegrity queries found no matching startup failure
during the suites. Older WER records contain other crash statuses, including
an access violation in coreclr; those are not evidence for `0xC0000142`.
Steam-off, Explorer, DLL-addition/replacement transitions, and a full VM/A/B
shell session were not tested. No post-fix acceptance is claimed.

## Dependency observations

Actual PCK contents and live modules confirmed the debug GodotSteam mapping.
The exported/project debug DLL hash is
`da20f384da6911b31cfb32951831206e7b6f93ff9c9b4d6472551d985f1e4d08`;
Steam API hash is
`8de54d32508e216c9135b8bf025749243d44e404c1c22a8e5fe35acecabe7a9c`.
Provenance is in [the GodotSteam patch record](../third_party/patches/godotsteam/README.md).

Inspected native binaries are x86_64; GodotSharp is AnyCPU managed IL, not an
x86 native mismatch. Embedded .NET runtime is 8.0.22, native version
`8.0.2225.52707`. Loaded coreclr/hostfxr/hostpolicy under
`LOCALAPPDATA/data_GameFactory_windows_x86_64` matched the embedded hashes
before and after reboot. Direct PE imports were inventoried; not every
dynamically loaded/transitive dependency was independently validated.

DLLs do not have an established periodic-refresh requirement. Restarting
processes after replacing a native DLL can be appropriate; rebuilding unchanged
DLLs is not a demonstrated remedy. Our GodotSteam configuration does not opt
into editor hot reload. After a real dependency change, close processes using
it, deploy the documented dependency set, then export and verify a fresh
package. See [Windows DLL initialization](https://learn.microsoft.com/en-us/windows/win32/dlls/dllmain).

## Separate editor-discovery cleanup

The editor screenshot showed duplicate Steam registrations and duplicate
`GodotSteamPlugin` script classes. `.godot/extension_list.cfg` included both
the legitimate addon and a diagnostic `.gdextension` extracted into `artifacts`
during this investigation. Temporary source directories also contained plugin
class copies. This is a concrete editor-discovery problem, not an established
cause of the earlier native startup failure.

`artifacts/.gdignore` now prevents Godot from importing diagnostic captures;
Git ignoring a directory alone does not exclude it from Godot. Local
`.gdignore` markers also exclude the existing `.tmp-godotsteam-v422` and
`.tmp-godotsteam-v422-clean` folders, preserving their contents. Their temporary
source trees and markers are not committed. The generated extension list was
cleaned locally; it is not tracked. Future scratch source trees inside this
project must have `.gdignore` before editor discovery, or live outside it.
See [Godot folder exclusions](https://docs.godotengine.org/en/stable/tutorials/best_practices/project_organization.html).

The yellow GodotGAS icon-UID warnings fall back to valid text paths and were
not investigated as native startup failures.

Final cleanup validation passed one bounded, isolated Godot headless editor
import: natural exit 0, no duplicate-registration/global-class errors, no
temporary plugin classes in the regenerated class cache, and exactly one
extension entry (`res://addons/godotsteam/godotsteam.gdextension`). Cleanup
verified no editor/game processes remained. Evidence:
`artifacts/windows_startup/finalize_editor_import/result.json`. This validates
editor discovery; it is not native-failure or multiplayer acceptance.

## If it happens again

1. **Preserve the failing state first.** Record exact Cairo/UTC time, screenshot,
   failing EXE path, wrapper and child PIDs, exact process exit/status code,
   launch mechanism, arguments, working directory, APPDATA/LOCALAPPDATA, and
   whether Steam/editor/.NET processes were present. If a DLL or environment
   changed, identify the specific change. Do not rebuild, clear caches,
   restart Steam, or relaunch repeatedly before capture.
2. Freeze the existing export: save its manifest, SHA-256 of both EXEs/PCK and
   adjacent native DLLs, available Godot/structured logs, and A/B run/attempt
   state plus host runner status. Determine whether any Godot logging began.
3. Inspect Application Error, WER, .NET Runtime, SideBySide, System/Application
   Popup, and CodeIntegrity records for that exact time. Capture faulting module
   path/version/offset and report/bucket IDs if available. Other status codes
   must stay separate.
4. Capture live process/module evidence, then terminate only test-owned
   processes and verify cleanup using the existing A/B Stop path when applicable.
   Preserve large traces under ignored artifacts, not Git.
5. If normal logs/events cannot identify the module, define one bounded
   loader-level experiment (for example, an available debugger/loader trace)
   on the same immutable package. Follow the [investigation protocol](investigation-protocol.md)
   and [testing protocol](testing-protocol.md); change one variable per attempt.
6. Implement a fix only after the boundary is concrete. Then require at least
   20 consecutive startup-ready launches, no unexpected exits/native crash
   events, several scheduled-task launches, and a normal A/B shell launch.
   Networking/native-runtime changes also require the relevant gameplay
   acceptance. Do not commit a fix before its required acceptance passes.

## Preserved local evidence and deviations

- `artifacts/windows_startup/20261003_01/report.md`: initial suites, hashes,
  PCK/native inventory, module captures, per-attempt logs and cleanup.
- `artifacts/windows_startup/20261003_postreboot/report.md`: retest, reboot/process
  evidence, loaded-module parity, final manifest and cleanup records.
- `artifacts/build_exports/20261003_050256_751`: fresh export evidence.

Initial diagnostic mistakes (wrong structured-log lookup and a too-short exit
observation) are preserved separately and excluded from counts. Initial
overlapping renderer/reused-package trials were also excluded and repeated
sequentially. The reported totals cover only corrected controlled suites.
Large raw artifacts and diagnostic scripts remain local and ignored.
