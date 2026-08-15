using System.Collections.Generic;
using Godot;
using QuestWorld.Character;

public partial class NetworkSession : Node
{
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

    public NetworkLaunchOptions LaunchOptions => _launchOptions;

    public bool IsServer =>
        _launchOptions.Mode is NetworkLaunchMode.Server or NetworkLaunchMode.Host;

    public bool IsDedicatedServer => _launchOptions.Mode == NetworkLaunchMode.Server;

    public int LocalPeerId => (int)Multiplayer.GetUniqueId();

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
            return;
        }

        if (PlayerScene == null)
        {
            PlayerScene = GD.Load<PackedScene>(DefaultPlayerScenePath);
        }

        _playerSpawner.SpawnPath = _playerSpawner.GetPathTo(_players);
        _playerSpawner.AddSpawnableScene(PlayerScene.ResourcePath);
        Multiplayer.PeerConnected += peerId => OnPeerConnected((int)peerId);
        Multiplayer.PeerDisconnected += peerId => OnPeerDisconnected((int)peerId);
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;

        switch (_launchOptions.Mode)
        {
            case NetworkLaunchMode.Offline:
                SpawnPlayer(1);
                break;
            case NetworkLaunchMode.Host:
                if (StartServer())
                {
                    SpawnPlayer(1);
                }
                break;
            case NetworkLaunchMode.Server:
                StartServer();
                break;
            case NetworkLaunchMode.Client:
                StartClient();
                break;
        }

        GD.Print(
            $"NetworkSession: mode={_launchOptions.Mode}, address={_launchOptions.Address}, port={_launchOptions.Port}"
        );
    }

    public override void _Process(double delta)
    {
        if (!_configurationValid || _localPlayerController == null || IsDedicatedServer)
        {
            return;
        }

        Character localPlayer = _players.GetNodeOrNull<Character>(
            NetworkPlayerIdentity.GetPlayerName(LocalPeerId)
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

        Multiplayer.MultiplayerPeer = peer;
        GD.Print($"NetworkSession: connecting to {_launchOptions.Address}:{_launchOptions.Port}");
        return true;
    }

    private void OnPeerConnected(int peerId)
    {
        if (IsServer)
        {
            SpawnPlayer(peerId);
        }
    }

    private void OnPeerDisconnected(int peerId)
    {
        if (!IsServer)
        {
            return;
        }

        Character player = _players.GetNodeOrNull<Character>(
            NetworkPlayerIdentity.GetPlayerName(peerId)
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
        GD.Print($"NetworkSession: connected as peer {LocalPeerId}");
    }

    private void OnConnectionFailed()
    {
        GD.PushError("NetworkSession: connection failed.");
    }

    private void OnServerDisconnected()
    {
        GD.PushError("NetworkSession: server disconnected.");
    }
}
