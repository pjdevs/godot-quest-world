using System.Collections.Generic;
using Godot;
using QuestWorld.Network;

namespace QuestWorld.GameSession;

[GlobalClass]
public partial class GameSession : Node
{
    private const string TravelIdMeta = "game_session_travel_id";
    private const string WorldPathMeta = "game_session_world_path";

    [Signal]
    public delegate void PlayerJoinedEventHandler(PlayerState playerState);

    [Signal]
    public delegate void PlayerLeftEventHandler(long participantId, long peerId);

    [Signal]
    public delegate void TravelStartedEventHandler(long travelId, string resourcePath);

    [Signal]
    public delegate void WorldLoadedEventHandler(long travelId, Node world);

    [Signal]
    public delegate void TravelCompletedEventHandler(long travelId, Node world);

    [Signal]
    public delegate void TravelFailedEventHandler(long travelId, string reason);

    [Export]
    public NetworkSession? NetworkSession { get; set; }

    [Export]
    public Node? Players { get; set; }

    [Export]
    public PackedScene? PlayerStateScene { get; set; }

    [Export]
    public MultiplayerSpawner? PlayerStateSpawner { get; set; }

    [Export]
    public Node? WorldContainer { get; set; }

    [Export]
    public MultiplayerSpawner? WorldSpawner { get; set; }

    private readonly List<PlayerState> _playerStates = new();
    private readonly Dictionary<long, PlayerState> _playersByParticipantId = new();
    private readonly Dictionary<long, PlayerState> _playersByPeerId = new();
    private bool _initialized;
    private bool _isAcceptingPlayers = true;
    private long _nextParticipantId = 1;
    private Node? _currentWorld;
    private bool _localWorldReady;
    private bool _worldLoadedEmitted;

    public GameSessionState State { get; private set; } = GameSessionState.Idle;

    public bool IsAcceptingPlayers
    {
        get => _isAcceptingPlayers;
        set
        {
            if (_isAcceptingPlayers == value)
            {
                return;
            }

            _isAcceptingPlayers = value;
            UpdateTransportAdmission();
        }
    }

    public IReadOnlyList<PlayerState> PlayerStates => _playerStates;

    public long CurrentTravelId { get; private set; }

    public Node? CurrentWorld => _currentWorld;

    public string CurrentWorldPath { get; private set; } = string.Empty;

    public bool IsLocalWorldReady => _localWorldReady;

    private bool IsOfflineSession =>
        NetworkSession?.LaunchOptions?.Mode == NetworkLaunchMode.Offline;

    public bool Initialize()
    {
        if (_initialized)
        {
            return State != GameSessionState.Failed;
        }

        if (!ValidateConfiguration())
        {
            State = GameSessionState.Failed;
            return false;
        }

        PlayerStateSpawner!.SpawnFunction = Callable.From<Variant, Node>(SpawnPlayerState);
        WorldSpawner!.SpawnFunction = Callable.From<Variant, Node>(SpawnWorld);
        PlayerStateSpawner.Spawned += OnPlayerStateSpawned;
        WorldSpawner.Spawned += OnWorldSpawned;
        NetworkSession!.PeerConnected += OnPeerConnected;
        NetworkSession.PeerDisconnected += OnPeerDisconnected;
        NetworkSession.Connected += OnConnected;
        NetworkSession.Disconnected += OnDisconnected;
        NetworkSession.Failed += OnNetworkFailed;
        _initialized = true;
        State = GameSessionState.Active;
        UpdateTransportAdmission();

        ReconcileCurrentNetworkSession();
        return State != GameSessionState.Failed;
    }

    public void Reset()
    {
        if (!_initialized)
        {
            return;
        }

        if (PlayerStateSpawner is not null)
        {
            PlayerStateSpawner.Spawned -= OnPlayerStateSpawned;
        }

        if (WorldSpawner is not null)
        {
            WorldSpawner.Spawned -= OnWorldSpawned;
        }

        if (NetworkSession is not null)
        {
            NetworkSession.PeerConnected -= OnPeerConnected;
            NetworkSession.PeerDisconnected -= OnPeerDisconnected;
            NetworkSession.Connected -= OnConnected;
            NetworkSession.Disconnected -= OnDisconnected;
            NetworkSession.Failed -= OnNetworkFailed;
        }

        foreach (PlayerState playerState in _playerStates.ToArray())
        {
            if (IsInstanceValid(playerState))
            {
                playerState.QueueFree();
            }
        }

        _playerStates.Clear();
        _playersByParticipantId.Clear();
        _playersByPeerId.Clear();
        if (_currentWorld is not null && IsInstanceValid(_currentWorld))
        {
            _currentWorld.QueueFree();
        }

        _currentWorld = null;
        CurrentTravelId = 0;
        CurrentWorldPath = string.Empty;
        _nextParticipantId = 1;
        _initialized = false;
        State = GameSessionState.Idle;
        UpdateTransportAdmission();
    }

    public bool Travel(PackedScene? worldScene)
    {
        if (!_initialized)
        {
            return FailTravel("GameSession must be initialized before travel.");
        }

        if (NetworkSession?.IsServer != true)
        {
            return FailTravel("Only the server can initiate travel.");
        }

        if (State != GameSessionState.Active)
        {
            return FailTravel("A world travel is already in progress.");
        }

        if (worldScene is null || string.IsNullOrWhiteSpace(worldScene.ResourcePath))
        {
            return FailTravel("Travel requires a loadable world resource path.");
        }

        string resourcePath = worldScene.ResourcePath;
        PackedScene? preflight = ResourceLoader.Load<PackedScene>(resourcePath);
        if (preflight is null)
        {
            return FailTravel($"Unable to load world scene '{resourcePath}'.");
        }

        Node? preflightInstance = preflight.Instantiate();
        if (preflightInstance is null)
        {
            return FailTravel($"Unable to instantiate world scene '{resourcePath}'.");
        }

        preflightInstance.Free();
        long travelId = ++CurrentTravelId;
        CurrentWorldPath = resourcePath;
        State = GameSessionState.Traveling;
        _localWorldReady = false;
        _worldLoadedEmitted = false;
        UpdateTransportAdmission();
        EmitSignal(SignalName.TravelStarted, travelId, resourcePath);

        if (_currentWorld is not null && IsInstanceValid(_currentWorld))
        {
            _currentWorld.Free();
        }

        _currentWorld = null;
        Godot.Collections.Dictionary<string, Variant> spawnData = new()
        {
            ["travel_id"] = travelId,
            ["resource_path"] = resourcePath,
        };
        Node? world = WorldSpawner!.Spawn(spawnData) as Node;
        if (world is null && IsOfflineSession)
        {
            world = SpawnWorld(spawnData);
            if (world is not null)
            {
                WorldContainer!.AddChild(world, true);
            }
        }

        if (world is null)
        {
            State = GameSessionState.Failed;
            UpdateTransportAdmission();
            EmitSignal(
                SignalName.TravelFailed,
                travelId,
                $"Unable to spawn world scene '{resourcePath}'."
            );
            return false;
        }

        _currentWorld = world;
        ScheduleWorldReadinessCheck();
        return true;
    }

    public bool TryGetPlayerStateByPeerId(long peerId, out PlayerState? playerState) =>
        _playersByPeerId.TryGetValue(peerId, out playerState);

    public bool TryGetPlayerStateByParticipantId(
        long participantId,
        out PlayerState? playerState
    ) => _playersByParticipantId.TryGetValue(participantId, out playerState);

    protected virtual bool CanJoin(long peerId, out string reason)
    {
        if (!IsAcceptingPlayers || State != GameSessionState.Active)
        {
            reason = "The game session is not accepting players.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool ValidateConfiguration()
    {
        if (NetworkSession is null)
        {
            return FailConfiguration("NetworkSession is required.");
        }

        if (Players is null)
        {
            return FailConfiguration("Players is required.");
        }

        if (PlayerStateScene is null)
        {
            return FailConfiguration("PlayerStateScene is required.");
        }

        if (PlayerStateSpawner is null)
        {
            return FailConfiguration("PlayerStateSpawner is required.");
        }

        if (WorldContainer is null)
        {
            return FailConfiguration("WorldContainer is required.");
        }

        if (WorldSpawner is null)
        {
            return FailConfiguration("WorldSpawner is required.");
        }

        if (WorldContainer.GetChildCount() > 1)
        {
            return FailConfiguration("WorldContainer may contain at most one managed world.");
        }

        if (PlayerStateSpawner.GetNodeOrNull(PlayerStateSpawner.GetPathTo(Players)) != Players)
        {
            return FailConfiguration("PlayerStateSpawner must point to Players through SpawnPath.");
        }

        if (WorldSpawner.GetNodeOrNull(WorldSpawner.GetPathTo(WorldContainer)) != WorldContainer)
        {
            return FailConfiguration(
                "WorldSpawner must point to WorldContainer through SpawnPath."
            );
        }

        Node? probe = PlayerStateScene.Instantiate();
        if (probe is not PlayerState)
        {
            probe?.Free();
            return FailConfiguration("PlayerStateScene root must inherit PlayerState.");
        }

        probe.Free();
        return true;
    }

    private bool FailConfiguration(string reason)
    {
        GD.PushError($"{GetPath()}: {reason}");
        return false;
    }

    private void ReconcileCurrentNetworkSession()
    {
        if (NetworkSession?.State != SessionState.Active || !NetworkSession.IsServer)
        {
            return;
        }

        if (!NetworkSession.IsDedicatedServer && NetworkSession.LocalPeerId > 0)
        {
            AdmitParticipant(NetworkSession.LocalPeerId);
        }
    }

    private void OnConnected() => ReconcileCurrentNetworkSession();

    private void OnPeerConnected(long peerId)
    {
        if (NetworkSession?.IsServer == true)
        {
            AdmitParticipant(peerId);
        }
    }

    private void OnPeerDisconnected(long peerId)
    {
        if (NetworkSession?.IsServer == true)
        {
            RemoveParticipant(peerId);
        }
    }

    private void OnDisconnected()
    {
        if (State == GameSessionState.Active)
        {
            State = GameSessionState.Idle;
        }

        UpdateTransportAdmission();
    }

    private void OnNetworkFailed(string reason)
    {
        GD.PushError($"{GetPath()}: NetworkSession failed: {reason}");
        State = GameSessionState.Failed;
    }

    private void AdmitParticipant(long peerId)
    {
        if (
            !_initialized
            || NetworkSession?.IsServer != true
            || State != GameSessionState.Active
            || _playersByPeerId.ContainsKey(peerId)
        )
        {
            return;
        }

        if (!CanJoin(peerId, out string reason))
        {
            GD.PushWarning($"{GetPath()}: rejected peer {peerId}: {reason}");
            NetworkSession.Multiplayer.MultiplayerPeer?.DisconnectPeer((int)peerId);
            return;
        }

        long participantId = _nextParticipantId++;
        Godot.Collections.Dictionary<string, Variant> spawnData = new()
        {
            ["participant_id"] = participantId,
            ["peer_id"] = peerId,
        };
        PlayerState? playerState = PlayerStateSpawner!.Spawn(spawnData) as PlayerState;
        if (playerState is null && NetworkSession.Multiplayer.MultiplayerPeer is null)
        {
            playerState = SpawnPlayerState(spawnData) as PlayerState;
            if (playerState is not null)
            {
                Players!.AddChild(playerState, true);
            }
        }

        if (playerState is null || !RegisterPlayerState(playerState))
        {
            GD.PushError($"{GetPath()}: failed to create PlayerState for peer {peerId}.");
            playerState?.QueueFree();
            return;
        }

        EmitSignal(SignalName.PlayerJoined, playerState);
    }

    private Node SpawnPlayerState(Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary)
        {
            return null!;
        }

        Godot.Collections.Dictionary payload = data.AsGodotDictionary();
        if (
            !payload.TryGetValue("participant_id", out Variant participantValue)
            || !payload.TryGetValue("peer_id", out Variant peerValue)
            || participantValue.VariantType != Variant.Type.Int
            || peerValue.VariantType != Variant.Type.Int
        )
        {
            return null!;
        }

        long participantId = participantValue.AsInt64();
        long peerId = peerValue.AsInt64();
        if (participantId <= 0 || peerId <= 0 || PlayerStateScene is null)
        {
            return null!;
        }

        Node? node = PlayerStateScene.Instantiate();
        if (node is not PlayerState playerState)
        {
            node?.Free();
            return null!;
        }

        playerState.InitializeIdentity(participantId, peerId);
        playerState.Name = $"PlayerState_{participantId}";
        return playerState;
    }

    private Node SpawnWorld(Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary)
        {
            return null!;
        }

        Godot.Collections.Dictionary payload = data.AsGodotDictionary();
        if (
            !payload.TryGetValue("travel_id", out Variant travelValue)
            || !payload.TryGetValue("resource_path", out Variant pathValue)
            || travelValue.VariantType != Variant.Type.Int
            || pathValue.VariantType != Variant.Type.String
        )
        {
            return null!;
        }

        long travelId = travelValue.AsInt64();
        string resourcePath = pathValue.AsString();
        if (travelId <= 0 || string.IsNullOrWhiteSpace(resourcePath))
        {
            return null!;
        }

        PackedScene? scene = ResourceLoader.Load<PackedScene>(resourcePath);
        Node? world = scene?.Instantiate();
        if (world is null)
        {
            return null!;
        }

        world.Name = $"World_{travelId}";
        world.SetMeta(TravelIdMeta, travelId);
        world.SetMeta(WorldPathMeta, resourcePath);
        return world;
    }

    private void OnWorldSpawned(Node node)
    {
        if (
            !node.HasMeta(TravelIdMeta)
            || !node.HasMeta(WorldPathMeta)
            || node.GetMeta(TravelIdMeta).VariantType != Variant.Type.Int
            || node.GetMeta(WorldPathMeta).VariantType != Variant.Type.String
        )
        {
            GD.PushError($"{GetPath()}: replicated world has invalid spawn metadata.");
            return;
        }

        _currentWorld = node;
        CurrentTravelId = node.GetMeta(TravelIdMeta).AsInt64();
        CurrentWorldPath = node.GetMeta(WorldPathMeta).AsString();
        if (State == GameSessionState.Active)
        {
            State = GameSessionState.Traveling;
        }

        ScheduleWorldReadinessCheck();
    }

    private void ScheduleWorldReadinessCheck()
    {
        GetTree().CreateTimer(0.0).Timeout += CompleteLocalWorldReadiness;
    }

    public void CompleteLocalWorldReadiness()
    {
        if (
            _currentWorld is null
            || !IsInstanceValid(_currentWorld)
            || _worldLoadedEmitted
            || _currentWorld.GetMeta(TravelIdMeta, 0L).AsInt64() != CurrentTravelId
        )
        {
            return;
        }

        _localWorldReady = true;
        _worldLoadedEmitted = true;
        EmitSignal(SignalName.WorldLoaded, CurrentTravelId, _currentWorld);
        if (IsOfflineSession)
        {
            State = GameSessionState.Active;
            UpdateTransportAdmission();
            EmitSignal(SignalName.TravelCompleted, CurrentTravelId, _currentWorld);
        }
    }

    private bool FailTravel(string reason)
    {
        GD.PushWarning($"{GetPath()}: {reason}");
        EmitSignal(SignalName.TravelFailed, CurrentTravelId, reason);
        return false;
    }

    public override void _Process(double delta)
    {
        if (State == GameSessionState.Traveling && !_worldLoadedEmitted)
        {
            CompleteLocalWorldReadiness();
        }
    }

    private void OnPlayerStateSpawned(Node node)
    {
        if (node is not PlayerState playerState || !RegisterPlayerState(playerState))
        {
            GD.PushError($"{GetPath()}: replicated PlayerState has invalid identity.");
        }
    }

    private bool RegisterPlayerState(PlayerState playerState)
    {
        if (
            !IsInstanceValid(playerState)
            || playerState.ParticipantId <= 0
            || playerState.PeerId <= 0
            || _playersByParticipantId.ContainsKey(playerState.ParticipantId)
            || _playersByPeerId.ContainsKey(playerState.PeerId)
        )
        {
            return false;
        }

        _playerStates.Add(playerState);
        _playersByParticipantId.Add(playerState.ParticipantId, playerState);
        _playersByPeerId.Add(playerState.PeerId, playerState);
        playerState.TreeExiting += () => UnregisterPlayerState(playerState);
        return true;
    }

    private void UnregisterPlayerState(PlayerState playerState)
    {
        if (!_playersByParticipantId.Remove(playerState.ParticipantId))
        {
            return;
        }

        _playersByPeerId.Remove(playerState.PeerId);
        _playerStates.Remove(playerState);
        EmitSignal(SignalName.PlayerLeft, playerState.ParticipantId, playerState.PeerId);
    }

    private void RemoveParticipant(long peerId)
    {
        if (!_playersByPeerId.TryGetValue(peerId, out PlayerState? playerState))
        {
            return;
        }

        playerState.QueueFree();
    }

    private void UpdateTransportAdmission()
    {
        if (NetworkSession?.IsServer != true)
        {
            return;
        }

        if (NetworkSession.Multiplayer.MultiplayerPeer is MultiplayerPeer peer)
        {
            peer.RefuseNewConnections = !_isAcceptingPlayers || State != GameSessionState.Active;
        }
    }

    public override void _ExitTree()
    {
        Reset();
    }
}
