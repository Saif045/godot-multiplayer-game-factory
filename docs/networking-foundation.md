# Networking Foundation

GameFactory's implemented online path is Steam listen-server networking through Godot's `MultiplayerApi` and GodotSteam's `SteamMultiplayerPeer`.

## Session path

`SteamPlatform` owns process-lifetime Steam initialization; `SteamSession` coordinates lobby creation/join/leave and the peer assigned to the active Godot multiplayer API. It calls `ISteamAdapter`; `GodotSteamAdapter` is the typed C# facade over the project-owned GDScript bridge, which is the only layer calling GodotSteam.

This is intentionally a Steam-specific boundary. A former generic ENet transport and generic `NetworkSession` were removed because they had no useful second implementation and obscured ownership. The session's actual owner is now clear: `SteamSession` owns its Steam lobby and `MultiplayerPeer` lifecycle.

## Gameplay path

Once the peer is installed, gameplay uses ordinary Godot multiplayer facilities. `PeerRegistry` tracks process-local peers; `PlayerLifecycle` translates authoritative peer changes into session-scoped players; `NetworkWorld` dynamically spawns registered `NetworkObject` gameplay roots; component-owned synchronizers and direct RPC provide replication.

The server remains authoritative for shared world state. A represented object owner peer is metadata, not a transfer of Godot authority. The explicit split between peer, player, and network-object IDs prevents platform account identity from leaking into gameplay identity.

## Evidence and limits

Two-account Steam listen-server acceptance has exercised lobby join/leave, player and object lifecycle, lobby-phase join/rejoin, repeated round/session reuse, same-process role reversal, authority, replication acknowledgement, and diagnostics. Gameplay-phase joining is intentionally unsupported; see [session reuse acceptance](session-reuse-acceptance.md). Engine-independent policies have xUnit coverage; the concrete host-PC-to-VM Steam scenario is externally automated with build-parity and staged diagnostics, while a generic Godot integration framework and CI remain planned. Dedicated servers and authentication are future work, not declared adapter seams.
