using System.Collections.Generic;
using Godot;
using QuestWorld.Character;

public partial class NetworkSession : Node
{
    private enum SessionState
    {
        Stopped,
        Connecting,
        Active,
        Stopping,
        Failed,
    }

    private const string DefaultPlayerScenePath = "res://quest_world/character/Character.tscn";

    [Export]
    public PackedScene PlayerScene { get; set; } = null!;

    [Export]
    public NodePath PlayersPath { get; set; } = new("../Players");

    [Export]
    public NodePath PlayerSpawnerPath { get; set; } = new("../PlayerSpawner");

    [Export]
    public NodePath LocalPlayerControllerPath { get; set; } = new("../PlayerController");

    private Node3D _players = null!;
    private MultiplayerSpawner _playerSpawner = null!;
    private CharacterPlayerController _localPlayerController = null!;
    private NetworkLaunchOptions _launchOptions = null!;
    private bool _configurationValid;
    private bool _multiplayerSignalsConnected;
    private MultiplayerPeer? _peer;
    private int _localPeerId;
    private SessionState _sessionState;

    public NetworkLaunchOptions LaunchOptions => _launchOptions;

    public bool IsServer =>
        _launchOptions.Mode is NetworkLaunchMode.Server or NetworkLaunchMode.Host;

    public bool IsDedicatedServer => _launchOptions.Mode == NetworkLaunchMode.Server;

    public int LocalPeerId => _localPeerId;

    public override void _Ready()
    {
        List<string> commandLineArguments = new(OS.GetCmdlineArgs());
        commandLineArguments.AddRange(OS.GetCmdlineUserArgs());
        _configurationValid = NetworkLaunchOptions.TryParse(
            commandLineArguments,
            out _launchOptions,
            out string parseError
        );
        if (!_configurationValid)
        {
            GD.PushError($"NetworkSession: {parseError}");
            _sessionState = SessionState.Failed;
            GetTree().Quit(2);
            return;
        }

        _players = GetNodeOrNull<Node3D>(PlayersPath)!;
        _playerSpawner = GetNodeOrNull<MultiplayerSpawner>(PlayerSpawnerPath)!;
        _localPlayerController = GetNodeOrNull<CharacterPlayerController>(
            LocalPlayerControllerPath
        )!;
        if (_players == null || _playerSpawner == null)
        {
            GD.PushError("NetworkSession: Players and PlayerSpawner nodes are required.");
            _configurationValid = false;
            _sessionState = SessionState.Failed;
            return;
        }

        if (PlayerScene == null)
        {
            PlayerScene = GD.Load<PackedScene>(DefaultPlayerScenePath);
        }

        _playerSpawner.SpawnPath = _playerSpawner.GetPathTo(_players);
        _playerSpawner.AddSpawnableScene(PlayerScene.ResourcePath);
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
        _multiplayerSignalsConnected = true;

        switch (_launchOptions.Mode)
        {
            case NetworkLaunchMode.Offline:
                _localPeerId = 1;
                _sessionState = SessionState.Active;
                SpawnPlayer(1);
                break;
            case NetworkLaunchMode.Host:
                if (StartServer())
                {
                    _localPeerId = 1;
                    _sessionState = SessionState.Active;
                    SpawnPlayer(1);
                }
                else
                {
                    _sessionState = SessionState.Failed;
                }
                break;
            case NetworkLaunchMode.Server:
                _sessionState = StartServer() ? SessionState.Active : SessionState.Failed;
                break;
            case NetworkLaunchMode.Client:
                _sessionState = StartClient()
                    ? SessionState.Connecting
                    : SessionState.Failed;
                break;
        }

        GD.Print(
            $"NetworkSession: mode={_launchOptions.Mode}, address={_launchOptions.Address}, port={_launchOptions.Port}"
        );
    }

    public override void _Process(double delta)
    {
        if (
            !_configurationValid
            || _sessionState != SessionState.Active
            || _localPlayerController == null
            || IsDedicatedServer
            || _localPeerId <= 0
        )
        {
            return;
        }

        Character localPlayer = _players.GetNodeOrNull<Character>(
            NetworkPlayerIdentity.GetPlayerName(_localPeerId)
        )!;
        if (
            localPlayer != null
            && localPlayer.IsMultiplayerAuthority()
            && _localPlayerController.ControlledCharacter != localPlayer
        )
        {
            _localPlayerController.Possess(localPlayer);
            GD.Print($"NetworkSession: possessed local {localPlayer.Name}");
            GD.Print($"NetworkSession: visible player count={_players.GetChildCount()}");
        }
    }

    private bool StartServer()
    {
        ENetMultiplayerPeer peer = new();
        Error result = peer.CreateServer(_launchOptions.Port, _launchOptions.MaxPlayers);
        if (result != Error.Ok)
        {
            GD.PushError(
                $"NetworkSession: unable to start server on port {_launchOptions.Port}: {result}"
            );
            return false;
        }

        _peer = peer;
        Multiplayer.MultiplayerPeer = peer;
        GD.Print($"NetworkSession: server listening on UDP {_launchOptions.Port}");
        return true;
    }

    private bool StartClient()
    {
        ENetMultiplayerPeer peer = new();
        Error result = peer.CreateClient(_launchOptions.Address, _launchOptions.Port);
        if (result != Error.Ok)
        {
            GD.PushError(
                $"NetworkSession: unable to connect to {_launchOptions.Address}:{_launchOptions.Port}: {result}"
            );
            return false;
        }

        _peer = peer;
        Multiplayer.MultiplayerPeer = peer;
        GD.Print($"NetworkSession: connecting to {_launchOptions.Address}:{_launchOptions.Port}");
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

        Character player = _players.GetNodeOrNull<Character>(
            NetworkPlayerIdentity.GetPlayerName((int)peerId)
        )!;
        if (player != null)
        {
            player.QueueFree();
            GD.Print($"NetworkSession: despawned {player.Name}");
        }
    }

    private void SpawnPlayer(int peerId)
    {
        string playerName = NetworkPlayerIdentity.GetPlayerName(peerId);
        if (_players.GetNodeOrNull<Character>(playerName) != null)
        {
            return;
        }

        Character player = PlayerScene.Instantiate<Character>();
        player.Name = playerName;
        player.Position = NetworkPlayerIdentity.GetSpawnPosition(peerId);
        _players.AddChild(player, true);
        GD.Print($"NetworkSession: spawned {playerName} at {player.Position}");
    }

    private void OnConnectedToServer()
    {
        if (_sessionState != SessionState.Connecting || _peer == null)
        {
            return;
        }

        _localPeerId = (int)_peer.GetUniqueId();
        _sessionState = SessionState.Active;
        GD.Print($"NetworkSession: connected as peer {_localPeerId}");
    }

    private void OnConnectionFailed()
    {
        GD.PushError("NetworkSession: connection failed.");
        StopSession();
    }

    private void OnServerDisconnected()
    {
        GD.PushError("NetworkSession: server disconnected.");
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
        if (_sessionState is SessionState.Stopping or SessionState.Stopped)
        {
            return;
        }

        _sessionState = SessionState.Stopping;
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
        _localPeerId = 0;
        _peer = null;

        if (Multiplayer.MultiplayerPeer != null)
        {
            Multiplayer.MultiplayerPeer = null;
        }

        peer?.Close();
        _sessionState = SessionState.Stopped;
    }
}
