using System.Collections.Generic;
using Godot;
using QuestWorld.Character;
using QuestWorld.GameSession;
using GameSessionNode = QuestWorld.GameSession.GameSession;
using ProjectCharacter = global::Character;

namespace QuestWorld.Network;

public partial class QuestWorldNetworkPlayers : Node
{
    [Export]
    public GameSessionNode? GameSession { get; set; }

    [Export]
    public CharacterPlayerController? LocalPlayerController { get; set; }

    private readonly Dictionary<long, ProjectCharacter> _charactersByPeerId = new();
    private Spawner? _playerSpawner;
    private bool _initialized;

    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        if (GameSession is null)
        {
            GD.PushError($"{GetPath()}: GameSession is required.");
            return;
        }

        GameSession.PlayerLeft += OnPlayerLeft;
        GameSession.PlayerWorldReady += OnPlayerWorldReady;
        GameSession.WorldLoaded += OnWorldLoaded;
        GameSession.TravelCompleted += OnTravelCompleted;
        _initialized = true;

        RefreshCurrentWorld();
        SynchronizeCurrentSession();
    }

    public override void _Process(double delta)
    {
        if (!_initialized || GameSession is null)
        {
            return;
        }

        RefreshCurrentWorld();
        if (
            GameSession.NetworkSession?.State != SessionState.Active
            || LocalPlayerController is null
            || GameSession.NetworkSession.IsDedicatedServer
            || GameSession.NetworkSession.LocalPeerId <= 0
        )
        {
            return;
        }

        ProjectCharacter? localPlayer = GetCurrentWorldPlayer(
            GameSession.NetworkSession.LocalPeerId
        );
        if (
            localPlayer is not null
            && localPlayer.IsLocalNetworkAuthority
            && LocalPlayerController.ControlledCharacter != localPlayer
        )
        {
            LocalPlayerController.Possess(localPlayer);
            GD.Print($"QuestWorldNetworkPlayers: possessed local {localPlayer.Name}");
            GD.Print(
                $"QuestWorldNetworkPlayers: visible player count={_playerSpawner?.GetSpawnRoot()?.GetChildCount()}"
            );
        }
    }

    private void SynchronizeCurrentSession()
    {
        if (
            GameSession?.NetworkSession?.IsServer == true
            && GameSession.State == GameSessionState.Active
            && GameSession.CurrentWorld is not null
        )
        {
            foreach (PlayerState playerState in GameSession.PlayerStates)
            {
                SpawnPlayer(playerState);
            }
        }
    }

    private void OnPlayerLeft(long participantId, long peerId)
    {
        if (GameSession?.NetworkSession?.IsServer != true)
        {
            return;
        }

        if (
            _charactersByPeerId.TryGetValue(peerId, out ProjectCharacter? character)
            && IsInstanceValid(character)
        )
        {
            character.QueueFree();
            GD.Print(
                $"QuestWorldNetworkPlayers: despawned {QuestWorldNetworkIdentity.GetPlayerName((int)peerId)}"
            );
        }
    }

    private void OnPlayerWorldReady(PlayerState playerState)
    {
        SpawnPlayer(playerState);
    }

    private void OnWorldLoaded(long travelId, Node world)
    {
        RefreshCurrentWorld();
    }

    private void OnTravelCompleted(long travelId, Node world)
    {
        if (world is World currentWorld && GameSession?.NetworkSession?.IsServer == true)
        {
            currentWorld.InitializeAuthority();
        }

        RefreshCurrentWorld();
    }

    private void RefreshCurrentWorld()
    {
        Spawner? nextSpawner = GetCurrentWorldSpawner();
        if (nextSpawner == _playerSpawner)
        {
            return;
        }

        if (_playerSpawner is not null && GodotObject.IsInstanceValid(_playerSpawner))
        {
            _playerSpawner.Spawned -= OnPlayerSpawned;
        }

        _charactersByPeerId.Clear();
        _playerSpawner = nextSpawner;
        if (_playerSpawner is not null)
        {
            _playerSpawner.Spawned += OnPlayerSpawned;
            Node3D? spawnRoot = _playerSpawner.GetSpawnRoot();
            if (spawnRoot is not null)
            {
                foreach (Node child in spawnRoot.GetChildren())
                {
                    OnPlayerSpawned(child);
                }
            }
        }
    }

    private Spawner? GetCurrentWorldSpawner() =>
        GameSession?.CurrentWorld?.GetNodeOrNull<Spawner>("PlayerSpawner");

    private ProjectCharacter? GetCurrentWorldPlayer(long peerId) =>
        _playerSpawner
            ?.GetSpawnRoot()
            ?.GetNodeOrNull<ProjectCharacter>(QuestWorldNetworkIdentity.GetPlayerName((int)peerId));

    private void SpawnPlayer(PlayerState playerState)
    {
        if (
            GameSession?.NetworkSession?.IsServer != true
            || GameSession.State != GameSessionState.Active
            || GameSession.CurrentWorld is null
            || _playerSpawner is null
        )
        {
            return;
        }

        int peerId = (int)playerState.PeerId;
        if (_charactersByPeerId.ContainsKey(peerId) || GetCurrentWorldPlayer(peerId) is not null)
        {
            return;
        }

        string playerName = QuestWorldNetworkIdentity.GetPlayerName(peerId);
        ProjectCharacter? player =
            _playerSpawner.Spawn(
                Transform3D.Identity.Translated(QuestWorldNetworkIdentity.GetSpawnPosition(peerId)),
                playerName
            ) as ProjectCharacter;
        if (player is null)
        {
            GD.PushError($"QuestWorldNetworkPlayers: failed to spawn {playerName}.");
            return;
        }

        OnPlayerSpawned(player);
        GD.Print($"QuestWorldNetworkPlayers: spawned {playerName} at {player.Position}");
    }

    private void OnPlayerSpawned(Node node)
    {
        if (node is not ProjectCharacter character)
        {
            return;
        }

        if (!QuestWorldNetworkIdentity.TryGetPeerId(character.Name, out int peerId))
        {
            GD.PushError(
                $"QuestWorldNetworkPlayers: spawned Character {character.GetPath()} has no valid peer identity."
            );
            return;
        }

        ConfigurePlayerAuthority(character, peerId);
        _charactersByPeerId[peerId] = character;
        character.TreeExiting += () =>
        {
            if (
                _charactersByPeerId.TryGetValue(peerId, out ProjectCharacter? indexed)
                && indexed == character
            )
            {
                _charactersByPeerId.Remove(peerId);
            }
        };
    }

    private static void ConfigurePlayerAuthority(ProjectCharacter character, int peerId)
    {
        character.OwnerPeerId = peerId;
        character.SetMultiplayerAuthority(peerId);
    }

    public override void _ExitTree()
    {
        if (_initialized && GameSession is not null)
        {
            GameSession.PlayerLeft -= OnPlayerLeft;
            GameSession.PlayerWorldReady -= OnPlayerWorldReady;
            GameSession.WorldLoaded -= OnWorldLoaded;
            GameSession.TravelCompleted -= OnTravelCompleted;
        }

        if (_playerSpawner is not null && GodotObject.IsInstanceValid(_playerSpawner))
        {
            _playerSpawner.Spawned -= OnPlayerSpawned;
        }

        _charactersByPeerId.Clear();
        _playerSpawner = null;
        _initialized = false;
    }
}
