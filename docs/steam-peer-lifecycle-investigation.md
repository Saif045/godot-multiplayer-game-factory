# Steam peer lifecycle investigation — 2026-10-03

Current result: native closed-peer lifecycle isolation is accepted for the
tested fresh/rejoin/new-lobby/role-reversal paths on `d28f7ad`. The retained-object
native probe and final gameplay regression check passed. An unrecovered fresh
certificate failure remains preserved; a separately authorized Steam restart
comparison then passed. The original second full four-join cycle was not
completed. BadCert root cause and long-term connection reliability remain
unproven. Games are stopped and cleanup verified. Sections below preserve the
investigation chronology and evidence limits.

Initial status: **BLOCKED for runtime evidence / deferred by operator**. The operator
requested diagnostics and a plan for a later run. No gameplay processes were
launched, no transport fix was attempted, and no commit was made. Baseline:
`e11c8ade9bdd0972bcd5625ad9a0d48275417b88`.

## Pre-runtime findings and evidence limits

Root cause: **unproven**. The first evidenced failing boundary is peer assignment
to Godot followed by an invalid scene packet before client readiness. Lobby
readiness remains intact. Neither duplicate native connections nor stale native
callbacks can be inferred from the existing managed creation logs.

The latest attempt's VM `client/godot.log` contains startup and main-menu events
only. Its nested `client/2026-10-02_22-52-47.663_.../game.jsonl` describes the
host-machine Steam account and matches the host's operations. It cannot serve
as independent VM connection evidence. Future timelines must identify each
participant by local Steam ID and process ID, not artifact directory name.

Captured host-machine timeline from
`artifacts/ab_tests/client_join_exit_20261003_001/attempt_001/host/godot.log`
(times UTC on October 2; Cairo time is three hours later):

| Time | Observation |
| --- | --- |
| 22:53:25.722 | Lobby `109775243730985660` created, owner `76561199844019034`. |
| 22:53:25.733 / .736 | Host peer created Connected / assigned to MultiplayerAPI. |
| 22:53:25.741 | Host preparation ready; shell generation `9bd9543b6ddf4169a140a43e23cdfc4d`. |
| 22:53:28.283 | Other Steam account `76561199111792813` expected; no ready signal captured. |
| 22:53:54.872 / .875 | Peer closing / closed. |
| 22:53:54.880 / .883 | MultiplayerAPI cleared / managed peer disposed. |
| 22:53:54.886 | Session Ready after lobby leave returns. |
| 22:53:59.868 | Same process joins lobby `109775243730985822`, owner `76561199111792813`. |
| 22:53:59.872 / .875 | Client peer created Connecting / assigned. |
| 22:53:59.879 | Shell generation `0704b2d664a44402ab1fe968d85a5a6a`. |
| 22:54:01.447 | Invalid packet, `process_simplify_path` (timestamp from captured engine event). |
| 22:54:12.113–.123 | Client close, clear, dispose, then Ready. |
| 22:54:36.353–.359 | Host recreated Connected in new lobby `109775243730986026`. |
| 22:54:38.272–.282 | Host close, clear, dispose, then Ready. |
| 22:54:40.411 / .416 / .419 | Another client lobby join / peer creation / assignment. Lobby `109775243730985940`. |
| after assignment | Two invalid scene-packet errors; no client ready signal captured. |

Fresh-process reproducibility: **not established by reliable two-sided evidence**.
Reuse reproduction: failures exist after host → leave → client transitions;
this proves occurrence after reuse, not exclusive correlation or causation.
Earlier `shell_host_preparation_20261003_003` also reached `participant_ready`
2/2 at 22:39:27.373 and 22:42:04.741; the latter included invalid packets and
therefore is not a clean transport PASS.

## Native provenance

The captured export selected
`libgodotsteam.windows.template_debug.x86_64.dll`, 2,679,296 bytes, SHA-256
`3de60848bb856869f5c4e2e3a31e90bd9c7bf4fad312ad33c0782a4baca2cf24`.
The `.gdextension` maps Windows debug x86_64 to
`addons/godotsteam/win64/libgodotsteam.windows.template_debug.x86_64.dll`.
`build/test_steam` contains this debug DLL. A live loaded-module path has not
been inspected in this investigation; do that in the next attempt.

Tracked release SHA-256:
`32b46c68759f10514886d010e2fd6532f8a66f47377814d61fb49762ecd2c932`.
Steam API SHA-256:
`8de54d32508e216c9135b8bf025749243d44e404c1c22a8e5fe35acecabe7a9c`.
Documented version: GodotSteam v4.22-gde, upstream
`64e94003cadd48c891be3f8126506b0feed847f2`, Steamworks SDK 1.65,
Godot 4.7.1 .NET, Windows x86_64.

Important discovery: ignored local `.tmp-godotsteam-v422` is at that upstream
commit and contains additional source changes. Its debug and release output
DLLs exactly match both tracked DLL hashes. This is strong provenance evidence,
not independent proof that a current source rebuild produces those bytes.
Its delta adds `pending_steam_ids` and guards `add_peer` against pending IDs
and existing Steam IDs. Incoming callback handling still lacks a general
connection-ownership filter, and `upgrade_peer` emits admission without an
idempotence guard. The tracked patch only corrects the close singleton guard;
it does not reproduce this additional dedupe delta.

Recovered source delta and hashes are preserved at:
`artifacts/investigations/steam_peer_lifecycle_20261003/`:
`recovered-local-native-source.diff`, `native-hashes.json`, and
`captured-host-timeline.json`. No SDK files or native binaries were changed.

Native hypothesis, **not a proven cause**: `_poll` interprets messages as
four-byte `PeerIDPacket` handshakes only while connection user data is <= 0.
Later packets go to Godot. An extra handshake after user data has been set
could therefore become an invalid scene packet. Multiple callback recipients
or repeated admission are candidates to distinguish with actual native-handle
and transport-signal evidence. Do not discard errors or change readiness.

## Questions the next trace must answer

1. Count distinct native handles reaching Connecting for one logical join.
   Callbacks show handles, not every internal ConnectP2P call; duplicate creation
   without a callback needs native source instrumentation in a separate task.
2. Compare transport `peer_connected` and Godot `godot_peer_connected` counts
   for the same instance, Steam identity, and Godot peer ID. Packet payloads
   are not exposed by this trace; duplicate packet claims require deeper evidence.
3. Require `peer_object_released` after Leave. Weak references deliberately
   avoid retaining the object; callback logs alone cannot prove destruction.
4. Inspect `after_peer_clear` and `before_peer_assignment`; Godot may expose
   an OfflineMultiplayerPeer after clearing, which must not be mistaken for
   the previous Steam peer.
5. Look for transport signals tagged with old object IDs/generations. The bridge
   is process-lifetime and intentionally observes global Steam callbacks; a
   callback with an old handle alone is not proof an old peer consumed it.
6. Compare close → clear → dispose → lobby_leave_requested/returned. Steam
   LeaveLobby returns synchronously; this does not prove remote leave propagation.
7. Compare `remote_steam_id`/`remote_peer_id` mappings across handles and instances.
8. Compare five fresh-process attempts against explicit reuse sequences below.
9. Dedupe source was recovered locally, but its actual effectiveness remains
   unvalidated. Matching binaries plus source inspection cannot prove runtime
   duplicate-connection protection.

## Prepared diagnostics

`--steam-transport-trace` enables timestamped JSONL categories
`steam.transport_trace` and `steam.session_trace`. Native events carry handle,
listener, old/new states, remote Steam identity, user data and end reason.
Peer events carry instance IDs and generations; managed events include session
instance/attempt, assigned peer identity/status and Godot connection signals.
All trace events include process ID and local Steam ID. Existing shell logs
provide shell `session_generation` and readiness events. Signal timestamps
record observer receipt; other subscribers can act earlier within the same
Godot signal dispatch.

The flag also enables existing vendor peer-level diagnostics. It changes
observability only. Leave, readiness, networking policy, GAS, and Netfox code
are unchanged. The harness switch `-SteamTransportTrace` forwards the flag to
both participants and preserves it across Retry/Verify/Stop.

## Later runtime contract

Narrow question: does one supported pre-game join produce one native handle
and one transport/Godot admission, and does that change after Leave/reuse?
Changed variable versus existing runs: trace instrumentation only. Steam must
remain running; no fresh-transport restart or VM recovery during the suite.

Preflight: Health; both accounts ready; zero stale test-owned GameFactory
processes; export this diagnostic build; verify manifest/dependencies and
host/VM parity; record manifest, account/PID ownership, loaded DLL path/hash
and attempt ID. Freeze source/config/build through each attempt.

```powershell
.\tools\ab_test\run.ps1 -Mode Health
.\tools\ab_test\run.ps1 -Mode Launch -Scenario shell_manual -SteamTransportTrace -RunId peer_lifecycle_<timestamp>
```

Fresh suite: five isolated attempts, fresh game processes each time, same
immutable exported build. Host clicks Host Game; wait at most 30 seconds for
`host_preparation_ready` before client clicks Join Game and selects that lobby.
Wait at most 60 seconds from peer assignment for ConnectedToServer,
`client_phase_ready_sent`, and host `participant_ready` 2/2. Never click Start.
FAIL immediately on duplicate admission, invalid packet, disconnect, or timeout.
Preserve Verify evidence and native logs, then Stop and verify zero host/VM
test processes. Continue to the next prescribed attempt only after cleanup;
use Retry without changing the build or trace mode. Stop the suite on BLOCKED
or unverifiable cleanup. Report all five individual outcomes, not best outcome.

Reuse suite: two additional fresh process pairs on that same build. In each,
perform Host → Join → 2/2; client Leave; rejoin the same lobby → 2/2; both Leave;
host creates a new lobby → client Join → 2/2. Allow 15 seconds for each local
teardown/release and membership update, then normal 30/60-second host/join
bounds. Preserve each segment separately with its object IDs and generations;
do not count segments as independent fresh-process runs. On any terminal error,
abort that pair, Verify, Stop, and verify cleanup. Proceed to the second pair
only after cleanup. Do not retry a failed join in place until it happens to work.

PASS requires clean readiness, one admission per remote peer, no stale peer
signals/object after Leave, and no invalid packets. Process/window startup
alone is not PASS. Interpret raw callback ownership carefully. If native
source changes are needed, stop and report the observed failing sequence,
required source change, reproducible patch/build inputs, and acceptance plan
before requesting a separate native patch/rebuild decision.

## Validation performed

- C# build: PASS, zero warnings/errors after final changes.
- GDScript direct `--check-only` bridge parse: PASS, exit 0; PID 35024 exited.
- Headless editor parse: exit 0, no script parse errors; PID 21864 exited.
  Existing certificate-store/GAS editor warnings remain outside this scope.
- Harness PowerShell AST parse: PASS.
- `git diff --check`: PASS.
- VM Health: PASS (`admin@172.21.114.152`, enabled GameFactoryClient task).
- Two-account runtime: BLOCKED/deferred by operator; zero new gameplay attempts.
- No GameFactory processes launched by this investigation. Prior captured attempt
  reports cleanup verified. Future host/VM runtime cleanup remains to be tested.
- Minimal fix: none justified yet. Diagnostics are uncommitted. HEAD remains
  `e11c8ade9bdd0972bcd5625ad9a0d48275417b88`.

## Runtime update — same-attempt evidence

Run: `peer_lifecycle_20261003_narrow_02`, attempt 003. Build
`gf_e11c8ade_931e90adb16a`, manifest SHA-256
`8bd0f86e6ca42c0e43b916a8572cb8dbd286048135617a43d169bbcf4f6e45ec`.
Actual export manifest and VM parity passed; native debug DLL hash is unchanged
from the value above. No live loaded-module inspection was completed.
Attempts 001/002 stopped before gameplay at host-task accessibility; the documented
limited interactive task was reconciled for Saif, then the harness ran outside
the sandbox. No Steam restart, VM recovery, or runtime source edits occurred.

Both raw `host/godot.log` and `client/godot.log` now have independently verified
local Steam IDs and process IDs from this attempt. Host-machine PID 22312 is
Steam account `76561199844019034`; VM PID 4500 is `76561199111792813`.

### Fresh join: PASS for the pre-game connection segment

Lobby `109775243747207130`, PC host / VM client:

| UTC time | Observation |
| --- | --- |
| 02:57:26.553 | Host preparation ready. |
| 02:57:30.295 / .307 | VM generation-1 peer created / assigned Connecting. |
| 02:57:30.328 → 02:57:32.308 | VM native handle `138592096`: 0→1→2→3, once each. |
| 02:57:30.378 → 02:57:32.294 | PC native handle `94033148`: 0→1→2→3, once each. |
| 02:57:32.365 / .370 | PC transport / Godot admission of VM peer `2074602627`, once each. |
| 02:57:32.386 | VM transport admission of server peer `1`, Steam account `76561199844019034`. |
| 02:57:32.390–.392 | VM ready RPC, ConnectedToServer observer, Godot admission of peer 1. |
| 02:57:32.405 | Host participant_ready 2/2. |

One observed native connection handle per endpoint, one admission per endpoint,
and no pre-readiness invalid packet. This does not prove that no ConnectP2P call
failed before generating a callback, or establish reliability across fresh runs.

### Scope deviation and reuse failure

`Start` committed at 02:57:33.840, after the successful pre-game segment, and
closed discovery. This invalidates a strict no-Start Leave → same-lobby rejoin
comparison. Before evidence collection completed, the processes also performed
Leave and reversed roles: VM became host, PC became client in a new lobby.
This is captured failure evidence, not acceptance of the requested narrow reuse
procedure. Do not use later gameplay events to diagnose Netfox or GAS.

VM generation-1 object: unsigned instance ID `9223372143021000442`, GDScript
signed representation `-9223371930688551174`, local peer ID `2074602627`.
These are the same instance: normalize IDs modulo 2^64 when correlating languages.

| UTC time | Observation |
| --- | --- |
| 02:57:50.553 / .554 | VM old client peer close requested / status Disconnected. |
| 02:57:50.564 / .565 | VM MultiplayerAPI cleared / managed peer disposed. |
| 02:57:50.565 | VM lobby leave requested / returned. |
| 02:57:50.574 | Weak observer still resolves old native peer, status 0. |
| 02:57:57.894 | PC old host object released; no corresponding VM old-object release is captured. |
| 02:58:14.250–.253 | VM generation-2 host peer created and ready in lobby `109775243747207296`; new local ID 1. |
| 02:58:22.082–.095 | PC generation-2 client created / assigned, new local ID `2038757086`. |
| 02:58:22.063 | VM handle `1332833479` goes 0→1; observer resolves both old and new native peer objects. |
| 02:58:22.879 | VM handle 1→2; vendor callback warning appears twice. |
| 02:58:24.151 | VM handle 2→3; vendor handler executes twice and prints two successful peer-ID ping sends. |
| 02:58:24.170 / .173 | PC admits remote Steam `76561199111792813` as peer `2074602627`, the old client ID, instead of current server ID 1. |
| immediately after admission | PC reports Invalid packet received, process_simplify_path. |
| 02:58:24.185 / .186 | VM new host admits current PC client ID `2038757086`, once. |
| through subsequent leave | PC never records ConnectedToServer or client_phase_ready_sent for this join. |

Reuse segment: **FAIL / native peer lifecycle**, with the procedural limitation
above. One new observed handle per endpoint (`1332833479` VM, `2456804227` PC),
not multiple native connections. Duplicate native callback *consumers* and
duplicate successful handshake sends on the same connection are evidenced.
Godot admission occurs once but uses the stale represented server identity.
The exact reason the closed VM native object retains a reference is not proven.

### Confirmed source-level defect; stop before native changes

Recovered source location:
`.tmp-godotsteam-v422/godotsteam_multiplayer_peer.cpp`, upstream commit above plus
the preserved local delta. Its `network_connection_status_changed` accepts
process-wide connection callbacks without rejecting a closed instance or proving
connection ownership. The Connecting branch creates a pending packet peer even
for another instance's handle. The Connected branch then sends that instance's
`get_unique_id()` when it finds the newly inserted handle. `_close` leaves native
callback registration active until destruction; it does not make the retained
object inert to later callbacks. The old client therefore sends `2074602627`
while the new host sends 1 on the same new connection. The receiving client's
stale peer admission is observed, not hypothetical.

The undocumented `pending_steam_ids` / existing-Steam-ID guard applies within
`add_peer` on one instance. It cannot protect against this cross-instance
callback consumption and handshake contamination. This is the concrete limit
of the recovered dedupe change; no DLL was edited/rebuilt.

Required source-level contract: a closed peer must not consume connection/lobby
callbacks or send handshakes, even when a valid reference survives; an active
peer must process only its own outgoing handles or incoming handles belonging
to its listener. Register/unregister or explicit lifecycle/ownership guards
must enforce that contract. Outgoing handles should be retained at creation so
ownership can be checked before callback-driven packet-peer construction.
Reopening must restore observation once, without reactivating an old generation.
Do not patch by hiding invalid packets or weakening readiness.

Two successful vendor ping sends plus the source's four-byte PeerIDPacket explain
the stale-ID contamination. Raw packet bytes were not captured. The subsequent
invalid scene packet being the second four-byte handshake is a source-backed
inference, not a packet capture result. Payload proof would require source-level
native observability in a separately approved patch/rebuild task.

Validation plan for a separate native task: preserve reproducible source patches
for all actual deltas, build debug/release with pinned upstream/submodule/SDK,
verify manifest/DLL parity, and run a fresh no-Start join followed by client
Leave → same-lobby rejoin once. Also deliberately retain a closed peer across
the next host/client creation to prove it emits no handshake and consumes no
foreign handle; verify host re-create and role reversal separately. Require
server ID 1, exactly one admission/handshake per active connection, no invalid
packets, ready 2/2, object/callback teardown evidence and cleanup.

Final state: **stopped**, host/VM cleanup verified by Stop. Verify preserved
392 structured entries and both native logs. Strict pre-game rejoin acceptance:
**BLOCKED by scope deviation**, not silently substituted with role reversal.
No implementation fix or diagnostics commit. HEAD remains `e11c8ad`.

## Native fix acceptance, attempt 002 (2026-10-03)

The subsequent authorized native implementation adds lifecycle and connection
ownership guards, preserves the recovered deltas as reproducible patches, and
rebuilds debug/release DLLs. The retained-object close/reopen native probe passed.
Full repeated shell acceptance remains incomplete; nothing is committed or pushed.

Evidence: `artifacts/ab_tests/native_lifecycle_fix_20261003_01/attempt_002`.
The immutable manifest is
`2bd506112b47f8146cef793a7b5208dcae14bc35b164336a8a23d051a0d01d2f`.

- Fresh join reached ConnectedToServer at 03:18:34.588 UTC and ready 2/2.
  However, its first native handles (VM `2659378033`, PC `919593435`)
  failed before admission with reason 4003, unknown certificate CA key
  `13151444190962006299`. The existing vendor BadCert retry then connected
  VM `3284378022` / PC `2855829413`. Each successful endpoint sent one
  handshake; the failed handles sent none.
- Strict same-lobby rejoin reached ConnectedToServer at 03:20:11.378 UTC
  and ready 2/2 in lobby `109775243748477624`, with Start untouched.
  VM `2211090300` / PC `4230737651` were the new connection handles.
  The closed VM object `9223372133323769380` was deliberately retained
  and observed beside new object `9223372166358108126`. Only the new
  object sent the handshake, using peer ID `1408605476`; server ID was 1.
  No stale handshake, duplicate admission, or invalid packet was captured.
- The local test contract's stronger "one native handle per endpoint per
  phase" assertion failed on the fresh join's recovered BadCert attempt.
  This is distinct from the user's one-successful-handshake lifecycle
  criteria. Do not silently discard failed native handles or classify the
  whole suite as PASS. New-lobby reuse, prescribed role reversal, the second
  clean process cycle, and final gameplay regression check remain unrun.

Terminal result: **FAIL against the written handle-count contract**, with
successful fresh and same-lobby Godot admissions. Stop verified both host/VM
cleanup. No Steam restart, retry-policy change, or Netfox investigation occurred.
Future acceptance must explicitly distinguish failed pre-admission native
attempts from duplicate successful connections before starting another frozen
attempt.

After the partial acceptance and BadCert distinction were reported, the user
explicitly requested pushing this state. This overrides the earlier instruction
to wait for full acceptance before committing/pushing; it does not turn the
incomplete suite into a PASS. The callback lifecycle fix, rebuilt DLLs,
reproducible patches, bounded opt-in diagnostics, and native probe are included.
Validation already completed: debug/release native builds, clean application of
the complete patch series to pinned upstream, exported host/VM DLL parity,
native retained-object probe, and GameFactory Quick validation (source hygiene,
managed build, unit regression). No shell/readiness or Netfox changes are included.

## Final acceptance and fresh-session comparison

Run `native_lifecycle_acceptance_d28f7ad_20261003_02` used unchanged runtime
inputs from `d28f7ad35f55bd0e533cc4a89123a6ff6f4d57fc`, build
`gf_d28f7ad3_994c5ca461d1`, manifest
`3dcd9f2a3dee492ba94d0474ec058567f863496401ad228a2f4c1543d91a9773`.
Only the build helper changed: it exports through the matching editor directly,
avoiding the Windows console wrapper's wait for persistent compiler descendants,
and preserves full exporter logs. Direct and harness exports passed; no
`--quit`, timeout increase, runtime changes, or in-attempt workaround was used.

- Attempt 001 **PASS**: fresh join, strict same-lobby rejoin, both Leave/new
  lobby, and role reversal. Four connected handles, four successful pings, and
  four Godot admissions per machine; no stale handshake, invalid packet, or
  BadCert. Retained old native objects remained inert. Clean Leave and Stop
  cleanup verified.
- Attempt 002 **FAIL / native Steam connection**: fresh generation-1 peers
  encountered BadCert 4003; VM retry failed with 5002 (missing certificate,
  self-signed disallowed), followed by connection/rendezvous timeouts 5003/5008.
  No successful handshake or Godot admission occurred. Operator screenshots
  preserve a Steam NetworkingSockets matching-listener assertion absent from
  the Godot logs. Both lobby members existed, but the host remained 1/2 ready.
  Stop cleanup verified. This does not prove a GameFactory-owned defect.
- Attempt 003 **PASS**, explicitly authorized separate comparison: restart
  Steam on both machines before launch; same immutable export and trace flags.
  Fresh join reached ConnectedToServer and ready 2/2, with one connected
  handle/ping/admission per endpoint and no certificate or transport errors.
  After verified readiness, Start entered gameplay on both machines. Host
  reported two players/four network objects. Operator confirmed mutual
  visibility, movement/jumping on each, and switch interaction from each.
  Server accepted peer 1 and peer `1428263598` switch requests and the client
  replicated both changes. Clean Leave returned both to menus; Stop verified
  cleanup. VM audio initialization errors are preserved separately.

The lifecycle fix and tested gameplay path are accepted. Five joins passed
across two successful process attempts; the failed certificate attempt remains
recorded. This is **not** a claim that both original four-join cycles passed or
that BadCert is resolved. The successful restart comparison supports session
state dependence but does not establish its cause. BadCert investigation is
deferred and Netfox investigation remains separate. Detailed results and raw
evidence live in each attempt's `acceptance.txt`, `evidence.json`, and host/client
logs under the run above.
