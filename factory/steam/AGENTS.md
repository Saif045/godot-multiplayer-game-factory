# Steam Session and Transport Scope

Read this guide and `docs/steam-integration.md` for `SteamSession`,
`GodotSteamAdapter`, lobby state, `SteamMultiplayerPeer`, or native-peer work.

- `SteamPlatform` owns the process-lifetime GodotSteam adapter.
  `SteamSession` owns one current online lobby and `MultiplayerPeer` lifecycle.
- Steam/account IDs are platform identity; Godot `PeerId` is a transient
  transport identity. Use adapter mapping APIs rather than conflating them.
- Steam is transport below Godot `MultiplayerAPI`; Netfox belongs above that
  boundary and is not a transport diagnosis target.
- Keep host/client peer creation and assignment observable. The relevant
  order is lobby creation/join -> peer creation -> MultiplayerAPI assignment
  -> native peer Connected -> Godot connection signal.
- Dedicated Steam hosting and Steam authentication are not implemented APIs.

For a connection failure, extract only lobby, peer, mapping, native networking,
and Godot-connection events from one known-good and one failed run. Check the
first divergence before changing source or restarting Steam.
