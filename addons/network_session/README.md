# Network Session Addon

Generic ENet session lifecycle for offline, host, dedicated-server and client modes.

`NetworkSession` owns only the `MultiplayerPeer` lifecycle and peer/session signals. It does not
spawn gameplay actors or possess local characters. Call `Start(NetworkLaunchOptions)` and
`Stop()` from an integration layer.
