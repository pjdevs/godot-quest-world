using System.Collections.Generic;
using DummyCharacterPlugin;
using GameSessionPlugin;
using Godot;
using NetworkPlugin;

namespace QuestWorld.Network;

public partial class PlayerCharacterSpawnManager : Node
{
    [Export]
    public World? World { get; set; }

    [Export]
    public CharacterPlayerController? LocalPlayerController { get; set; }

    [Export]
    public Spawner? PlayerSpawner { get; set; }

    private readonly Dictionary<long, QuestWorldCharacter> _charactersByPeerId = new();
    private bool _initialized;

    private GameSession? _gameSession;

    public override void _Ready()
    {
        if (World is null)
        {
            GD.PushError($"{GetPath()}: World is required.");
            return;
        }

        if (PlayerSpawner is null)
        {
            GD.PushError($"{GetPath()}: PlayerSpawner is required.");
            return;
        }

        World.GameSessionAttached += Initialize;

        if (World.GameSession is not null)
        {
            Initialize(World.GameSession);
        }
    }

    public override void _Process(double delta)
    {
        if (!_initialized || _gameSession is null)
        {
            return;
        }

        if (
            _gameSession.NetworkSession?.State != SessionState.Active
            || LocalPlayerController is null
            || _gameSession.NetworkSession.IsDedicatedServer
            || _gameSession.NetworkSession.LocalPeerId <= 0
        )
        {
            return;
        }

        QuestWorldCharacter? localPlayer = GetCurrentWorldPlayer(
            _gameSession.NetworkSession.LocalPeerId
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
                $"QuestWorldNetworkPlayers: visible player count={PlayerSpawner?.GetSpawnRoot()?.GetChildCount()}"
            );
        }
    }

    public void Initialize(GameSession gameSession)
    {
        if (_initialized)
        {
            return;
        }

        _gameSession = gameSession;
        _initialized = true;

        PlayerSpawner?.Spawned += OnPlayerSpawned;
        Node3D? root = PlayerSpawner?.GetSpawnRoot();
        if (root is not null)
        {
            foreach (Node child in root.GetChildren())
            {
                OnPlayerSpawned(child);
            }
        }

        _gameSession.PlayerLeft += OnPlayerLeft;
        _gameSession.PlayerWorldReady += OnPlayerWorldReady;

        SynchronizeCurrentSession();
    }

    private void SynchronizeCurrentSession()
    {
        if (
            _gameSession?.NetworkSession?.IsServer == true
            && _gameSession.State == GameSessionState.Active
        )
        {
            foreach (PlayerState playerState in _gameSession.PlayerStates)
            {
                SpawnPlayer(playerState);
            }
        }
    }

    private void OnPlayerLeft(long participantId, long peerId)
    {
        if (_gameSession?.NetworkSession?.IsServer != true)
        {
            return;
        }

        if (
            _charactersByPeerId.TryGetValue(peerId, out QuestWorldCharacter? character)
            && IsInstanceValid(character)
        )
        {
            character.QueueFree();
            GD.Print(
                $"QuestWorldNetworkPlayers: despawned {PlayerNetworkIdentity.GetPlayerName((int)peerId)}"
            );
        }
    }

    private void OnPlayerWorldReady(PlayerState playerState)
    {
        SpawnPlayer(playerState);
    }

    private QuestWorldCharacter? GetCurrentWorldPlayer(long peerId) =>
        PlayerSpawner
            ?.GetSpawnRoot()
            ?.GetNodeOrNull<QuestWorldCharacter>(PlayerNetworkIdentity.GetPlayerName((int)peerId));

    private void SpawnPlayer(PlayerState playerState)
    {
        if (
            _gameSession?.NetworkSession?.IsServer != true
            || _gameSession.State != GameSessionState.Active
            || PlayerSpawner is null
        )
        {
            return;
        }

        int peerId = (int)playerState.PeerId;
        if (_charactersByPeerId.ContainsKey(peerId) || GetCurrentWorldPlayer(peerId) is not null)
        {
            return;
        }

        string playerName = PlayerNetworkIdentity.GetPlayerName(peerId);
        QuestWorldCharacter? player =
            PlayerSpawner.Spawn(
                Transform3D.Identity.Translated(PlayerNetworkIdentity.GetSpawnPosition(peerId)),
                playerName
            ) as QuestWorldCharacter;
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
        if (node is not QuestWorldCharacter character)
        {
            return;
        }

        if (!PlayerNetworkIdentity.TryGetPeerId(character.Name, out int peerId))
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
                _charactersByPeerId.TryGetValue(peerId, out QuestWorldCharacter? indexed)
                && indexed == character
            )
            {
                _charactersByPeerId.Remove(peerId);
            }
        };
    }

    private static void ConfigurePlayerAuthority(QuestWorldCharacter character, int peerId)
    {
        character.OwnerPeerId = peerId;
        character.SetMultiplayerAuthority(peerId);
    }

    public override void _ExitTree()
    {
        if (_initialized && _gameSession is not null)
        {
            _gameSession.PlayerLeft -= OnPlayerLeft;
            _gameSession.PlayerWorldReady -= OnPlayerWorldReady;
        }

        PlayerSpawner?.Spawned -= OnPlayerSpawned;
        World?.GameSessionAttached -= Initialize;

        _charactersByPeerId.Clear();
        _gameSession = null;
        _initialized = false;
    }
}
