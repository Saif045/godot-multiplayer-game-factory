# GameFactory Validation

`tools/validate.ps1` is the common local validation interface for people and agents. It is model-agnostic: Codex, Qwen/Aider, and a normal PowerShell terminal invoke the same commands.

```powershell
.\tools\validate.ps1 -Mode List
.\tools\validate.ps1 -Mode Quick
.\tools\validate.ps1 -Mode Auto
.\tools\validate.ps1 -Mode Headless
.\tools\validate.ps1 -Mode Probe -Probe Gas
.\tools\validate.ps1 -Mode ExportSmoke
.\tools\validate.ps1 -Mode Full
```

Every invoked check reports `PASS`, `FAIL`, or `SKIP`, its command, and a short reason. The summary only describes checks that actually ran. A/B is always skipped by this wrapper because it is an operator-driven lifecycle.

## Validation layers

| Layer | Existing command/mechanism | What it proves | Cost and requirements |
| --- | --- | --- | --- |
| Hygiene | `git diff --check` | Changed tracked text has no whitespace errors. | Cheap; no engine/network. |
| Build | `dotnet build GameFactory.csproj --disable-build-servers -m:1 -p:UseSharedCompilation=false` | C# project compiles with the reliable serial/shared-compiler-disabled settings. | Cheap; .NET SDK. |
| Unit regression | `dotnet test tests/GameFactory.Tests/GameFactory.Tests.csproj --disable-build-servers -m:1 -p:UseSharedCompilation=false` | Deterministic policy/value/diagnostics coverage (currently 71 tests). | Cheap; no Godot scene, Steam, graphics, or VM. |
| Godot headless | `Godot ... --headless --path <repo> --editor --quit` | Project/addon/script import and editor initialization can load headlessly. | Local Godot console executable; no graphics or VM. |
| GAS probe | `Godot ... --headless --path <repo> -- --run=gas-interop` | The self-terminating GodotGAS adapter/effect lifecycle contract. | About 20 seconds; local Godot, no Steam/VM. |
| Export smoke | `tools/build_test_client.ps1`, then a bounded exported headless boot | Managed Windows export is complete and the exported runtime remains alive through a short boot window. | Slower; Godot export templates/.NET publish. Not visual UX proof. |
| Native/vendor smoke | `sandbox/steam/steam_native_rehost_probe.tscn` | A logged-in Steam peer can host, close, and host again. | Manual Steam dependency smoke; not generic automation because it requires a real Steam account. |
| A/B infrastructure | `tools/ab_test/run.ps1 -Mode Launch|Verify|Retry|Stop` | Immutable build parity, host/VM launch, Steam/Godot topology, evidence and teardown. | Real accounts, graphics, VM, SSH/SCP, and an operator. |
| A/B gameplay acceptance | Human observation + `Verify` evidence + reasoning | Actual visible feature behavior and whether the logs make sense. | Operator-driven; the harness never returns gameplay PASS/FAIL. |

The unit suite being green is regression evidence, not proof that Godot scenes, Steam transport, an exported build, or gameplay behavior work.

## Modes and Auto policy

`Quick` runs hygiene, deterministic build, and unit regression. `Headless`, `Probe -Probe Gas`, and `ExportSmoke` run exactly their named layer. `Full` runs the reasonable non-A/B local stack: Quick, Headless, GAS probe, and export smoke.

`Auto` is a deterministic minimum policy based on changed paths (or explicit `-ChangedPath` paths for review/testing). It prints selections and reasons:

| Changed surface | Auto minimum |
| --- | --- |
| Documentation only | Hygiene |
| PowerShell tooling, including `tools/ab_test/` | Hygiene + PowerShell parser for changed tools |
| C# or `project.godot` | Quick |
| Shell, GAS, Netfox, addon, or project config | Quick when source changed + Headless |
| GAS implementation/probe | Quick + Headless + GAS probe |
| Steam or locally maintained GodotSteam native surface | Quick + Headless + ExportSmoke; add manual native/transport validation when required |

`Auto` is a floor, not a ceiling. A feature task may add a focused probe, export smoke, or A/B session when risk and the task contract require it.

## Failure workflow

Run the smallest relevant project-defined check while iterating. If a failure is clearly caused by the current change, fix it and rerun that check. If unclear, inspect focused evidence; do not weaken a check, silently skip a required check, or modify unrelated pre-existing warnings to make the result green. After two or three materially different failed hypotheses, or when a fix crosses an accepted architecture/native boundary, report the evidence and escalate rather than thrashing.

The older [testing strategy](testing-strategy.md) records detailed historical coverage and limitations. [Testing protocol](testing-protocol.md) governs every runtime process test; [runtime test operator protocol](runtime-test-operator.md) governs the A/B lifecycle.

## A/B workflow

The wrapper never starts A/B automatically. Use the dedicated universal interface:

```powershell
.\tools\ab_test\run.ps1 -Mode Launch -Scenario netfox_player_3d -ShowHostConsole
# human plays both participants
.\tools\ab_test\run.ps1 -Mode Verify -RunId <RunId>
# human/agent interprets visual report plus generic evidence
.\tools\ab_test\run.ps1 -Mode Stop -RunId <RunId>
# after environmental repair, if needed
.\tools\ab_test\run.ps1 -Mode Retry -RunId <RunId> -ShowHostConsole
```

`Verify` only prepares evidence. Gameplay acceptance is:

```text
human visual observation + structured host/client evidence + human/agent reasoning
```

## VM endpoint setting

The Hyper-V guest address is machine-local. Copy `tools/ab_test/vm-endpoint.example.psd1` to `tools/ab_test/vm-endpoint.local.psd1` and edit `Target` whenever the guest IP changes. The local file is intentionally gitignored. `run.ps1` reads it unless `-VmAlias` is explicitly supplied, so an operator can still override it for a one-off run.
