using System.Collections.Generic;
using Godot;
using QuestWorld.Character;

public partial class NetworkSession : Node
{
    public enum SessionState
    {
        Stopped,
        Connecting,
        Active,
        Stopping,
        Failed,
    }

    [Export]
    public CharacterPlayerController? LocalPlayerController { get; set; }

    [Export]
    public PlayerSpawner? PlayerSpawner { get; set; }

    private bool _configurationValid;
    private bool _multiplayerSignalsConnected;
    private MultiplayerPeer? _peer;

    public NetworkLaunchOptions? LaunchOptions { get; private set; }

    public bool IsServer =>
        LaunchOptions?.Mode is NetworkLaunchMode.Server or NetworkLaunchMode.Host;

    public bool IsDedicatedServer => LaunchOptions?.Mode == NetworkLaunchMode.Server;

    public int LocalPeerId { get; private set; } = 0;
    public SessionState State { get; private set; }

    public void Initialize()
    {
        List<string> commandLineArguments = [with(OS.GetCmdlineArgs()), .. OS.GetCmdlineUserArgs()];
        _configurationValid = NetworkLaunchOptions.TryParse(
            commandLineArguments,
            out NetworkLaunchOptions? launchOptions,
            out string parseError
        );
        if (!_configurationValid)
        {
            GD.PushError($"NetworkSession: {parseError}");
            State = SessionState.Failed;
            GetTree().Quit(2);
            return;
        }

        LaunchOptions = launchOptions;

        if (PlayerSpawner is null)
        {
            GD.PushError(
                "NetworkSession: QuestWorldWorld and QuestWorldWorld.PlayerSpawner are required."
            );
            _configurationValid = false;
            State = SessionState.Failed;
            return;
        }

        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
        _multiplayerSignalsConnected = true;

        switch (launchOptions!.Mode)
        {
            case NetworkLaunchMode.Offline:
                LocalPeerId = 1;
                State = SessionState.Active;
                SpawnPlayer(1);
                break;
            case NetworkLaunchMode.Host:
                if (StartServer())
                {
                    LocalPeerId = 1;
                    State = SessionState.Active;
                    SpawnPlayer(1);
                }
                else
                {
                    State = SessionState.Failed;
                }
                break;
            case NetworkLaunchMode.Server:
                if (StartServer())
                {
                    State = SessionState.Active;
                }
                else
                {
                    State = SessionState.Failed;
                }
                break;
            case NetworkLaunchMode.Client:
                State = StartClient() ? SessionState.Connecting : SessionState.Failed;
                break;
        }

        GD.Print(
            $"NetworkSession: mode={launchOptions.Mode}, address={launchOptions.Address}, port={launchOptions.Port}"
        );
    }

    public override void _Process(double delta)
    {
        if (
            !_configurationValid
            || State != SessionState.Active
            || LocalPlayerController is null
            || IsDedicatedServer
            || LocalPeerId <= 0
        )
        {
            return;
        }

        Character? localPlayer = PlayerSpawner
            ?.GetSpawnRoot()
            ?.GetNodeOrNull<Character>(NetworkPlayerIdentity.GetPlayerName(LocalPeerId));
        if (
            localPlayer != null
            && localPlayer.IsMultiplayerAuthority()
            && LocalPlayerController.ControlledCharacter != localPlayer
        )
        {
            LocalPlayerController.Possess(localPlayer);
            GD.Print($"NetworkSession: possessed local {localPlayer.Name}");
            GD.Print(
                $"NetworkSession: visible player count={PlayerSpawner?.GetSpawnRoot()?.GetChildCount()}"
            );
        }
    }

    private bool StartServer()
    {
        NetworkLaunchOptions options = LaunchOptions!;
        ENetMultiplayerPeer peer = new();
        Error result = peer.CreateServer(options.Port, options.MaxPlayers);
        if (result != Error.Ok)
        {
            GD.PushError(
                $"NetworkSession: unable to start server on port {options.Port}: {result}"
            );
            return false;
        }

        _peer = peer;
        Multiplayer.MultiplayerPeer = peer;
        GD.Print($"NetworkSession: server listening on UDP {options.Port}");
        return true;
    }

    private bool StartClient()
    {
        NetworkLaunchOptions options = LaunchOptions!;
        ENetMultiplayerPeer peer = new();
        Error result = peer.CreateClient(options.Address, options.Port);
        if (result != Error.Ok)
        {
            GD.PushError(
                $"NetworkSession: unable to connect to {options.Address}:{options.Port}: {result}"
            );
            return false;
        }

        _peer = peer;
        Multiplayer.MultiplayerPeer = peer;
        GD.Print($"NetworkSession: connecting to {options.Address}:{options.Port}");
        return true;
    }

    private void OnPeerConnected(long peerId)
    {
        if (IsServer)
        {
            SpawnPlayer((int)peerId);
        }
    }

    private void OnPeerDisconnected(long peerId)
    {
        if (!IsServer)
        {
            return;
        }

        Character? player = PlayerSpawner
            ?.GetSpawnRoot()
            ?.GetNodeOrNull<Character>(NetworkPlayerIdentity.GetPlayerName((int)peerId));
        if (player != null)
        {
            player.QueueFree();
            GD.Print($"NetworkSession: despawned {player.Name}");
        }
    }

    private void SpawnPlayer(int peerId)
    {
        string playerName = NetworkPlayerIdentity.GetPlayerName(peerId);
        Character? existingPlayer = PlayerSpawner
            ?.GetSpawnRoot()
            ?.GetNodeOrNull<Character>(playerName);

        if (existingPlayer is not null)
        {
            GD.PushWarning($"NetworkSession: player {playerName} already exists; skipping spawn.");
            return;
        }

        Character? player = PlayerSpawner?.Spawn(
            Transform3D.Identity.Translated(NetworkPlayerIdentity.GetSpawnPosition(peerId)),
            playerName
        );

        if (player is null)
        {
            GD.PushError($"NetworkSession: failed to spawn {playerName}.");
            return;
        }

        GD.Print($"NetworkSession: spawned {playerName} at {player.Position}");
    }

    private void OnConnectedToServer()
    {
        if (State != SessionState.Connecting || _peer == null)
        {
            return;
        }

        LocalPeerId = (int)_peer.GetUniqueId();
        State = SessionState.Active;
        GD.Print($"NetworkSession: connected as peer {LocalPeerId}");
    }

    private void OnConnectionFailed()
    {
        GD.PushError("NetworkSession: connection failed.");
        StopSession();
    }

    private void OnServerDisconnected()
    {
        GD.Print("NetworkSession: server disconnected; stopping session.");
        StopSession();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            StopSession();
            GetTree().Quit();
        }
    }

    public override void _ExitTree()
    {
        StopSession();
    }

    private void StopSession()
    {
        if (State is SessionState.Stopping or SessionState.Stopped)
        {
            return;
        }

        State = SessionState.Stopping;
        SetProcess(false);

        if (_multiplayerSignalsConnected)
        {
            Multiplayer.PeerConnected -= OnPeerConnected;
            Multiplayer.PeerDisconnected -= OnPeerDisconnected;
            Multiplayer.ConnectedToServer -= OnConnectedToServer;
            Multiplayer.ConnectionFailed -= OnConnectionFailed;
            Multiplayer.ServerDisconnected -= OnServerDisconnected;
            _multiplayerSignalsConnected = false;
        }

        MultiplayerPeer? peer = _peer ?? Multiplayer.MultiplayerPeer;
        LocalPeerId = 0;
        _peer = null;

        if (Multiplayer.MultiplayerPeer != null)
        {
            Multiplayer.MultiplayerPeer = null;
        }

        peer?.Close();
        State = SessionState.Stopped;
    }
}
