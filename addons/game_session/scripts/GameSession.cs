using System.Collections.Generic;
using System.Linq;
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

    [Signal]
    public delegate void PlayerWorldReadyEventHandler(PlayerState playerState);

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

    private readonly GameSessionParticipantRegistry _participants = new();
    private readonly HashSet<long> _pendingLateJoinPeers = new();
    private bool _initialized;
    private bool _isAcceptingPlayers = true;
    private long _nextParticipantId = 1;
    private long _nextTravelId;
    private Node? _currentWorld;
    private bool _isCurrentWorldReady;
    private GameSessionTravelState? _activeTravel;

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

    public IReadOnlyList<PlayerState> PlayerStates => _participants.Players;

    public long CurrentTravelId { get; private set; }

    public Node? CurrentWorld => _currentWorld;

    public string CurrentWorldPath { get; private set; } = string.Empty;

    public bool IsLocalWorldReady => _isCurrentWorldReady;

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
        WorldSpawner.Despawned += OnWorldDespawned;
        NetworkSession!.PeerConnected += OnPeerConnected;
        NetworkSession.PeerDisconnected += OnPeerDisconnected;
        NetworkSession.Connected += OnConnected;
        NetworkSession.Disconnected += OnDisconnected;
        NetworkSession.Failed += OnNetworkFailed;
        _initialized = true;
        State = GameSessionState.Idle;
        return true;
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
            WorldSpawner.Despawned -= OnWorldDespawned;
        }

        if (NetworkSession is not null)
        {
            NetworkSession.PeerConnected -= OnPeerConnected;
            NetworkSession.PeerDisconnected -= OnPeerDisconnected;
            NetworkSession.Connected -= OnConnected;
            NetworkSession.Disconnected -= OnDisconnected;
            NetworkSession.Failed -= OnNetworkFailed;
        }

        foreach (PlayerState playerState in _participants.Players.ToArray())
        {
            if (IsInstanceValid(playerState))
            {
                playerState.QueueFree();
            }
        }

        _participants.Clear();
        _pendingLateJoinPeers.Clear();
        if (_currentWorld is not null && IsInstanceValid(_currentWorld))
        {
            _currentWorld.QueueFree();
        }

        _currentWorld = null;
        CurrentTravelId = 0;
        CurrentWorldPath = string.Empty;
        _isCurrentWorldReady = false;
        _activeTravel = null;
        _nextParticipantId = 1;
        _nextTravelId = 0;
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
        long travelId = ++_nextTravelId;
        State = GameSessionState.Traveling;
        _activeTravel = new GameSessionTravelState(
            travelId,
            resourcePath,
            _participants
                .Players.Where(playerState => playerState.PeerId != NetworkSession.LocalPeerId)
                .Select(playerState => playerState.PeerId)
        );
        UpdateTransportAdmission();
        EmitSignal(SignalName.TravelStarted, travelId, resourcePath);
        if (!IsOfflineSession)
        {
            Rpc(nameof(BeginTravel), travelId, resourcePath);
        }

        RetireCurrentWorld();
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

        return AdoptCurrentWorld(world, travelId, resourcePath);
    }

    public bool TryGetPlayerStateByPeerId(long peerId, out PlayerState? playerState) =>
        _participants.TryGetByPeerId(peerId, out playerState);

    public bool TryGetPlayerStateByParticipantId(
        long participantId,
        out PlayerState? playerState
    ) => _participants.TryGetByParticipantId(participantId, out playerState);

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

        if (NetworkSession.State != SessionState.Stopped)
        {
            return FailConfiguration(
                "NetworkSession must be stopped before GameSession initializes."
            );
        }

        if (WorldContainer.GetChildCount() != 0)
        {
            return FailConfiguration("WorldContainer must be empty before GameSession starts.");
        }

        if (PlayerStateSpawner.GetNodeOrNull(PlayerStateSpawner.SpawnPath) != Players)
        {
            return FailConfiguration("PlayerStateSpawner.SpawnPath must point to Players.");
        }

        if (WorldSpawner.GetNodeOrNull(WorldSpawner.SpawnPath) != WorldContainer)
        {
            return FailConfiguration("WorldSpawner.SpawnPath must point to WorldContainer.");
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

    private void OnConnected()
    {
        State = GameSessionState.Active;
        UpdateTransportAdmission();

        if (
            NetworkSession?.IsServer == true
            && !NetworkSession.IsDedicatedServer
            && NetworkSession.LocalPeerId > 0
        )
        {
            AdmitParticipant(NetworkSession.LocalPeerId);
        }
    }

    private void OnPeerConnected(long peerId)
    {
        if (NetworkSession?.IsServer == true)
        {
            if (State != GameSessionState.Active || !IsAcceptingPlayers)
            {
                NetworkSession.DisconnectPeer(peerId);
                return;
            }

            AdmitParticipant(peerId);
        }
    }

    private void OnPeerDisconnected(long peerId)
    {
        if (NetworkSession?.IsServer == true)
        {
            _activeTravel?.RemovePeer(peerId);
            _pendingLateJoinPeers.Remove(peerId);
            RemoveParticipant(peerId);
            TryCompleteTravel();
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
            || _participants.TryGetByPeerId(peerId, out _)
        )
        {
            return;
        }

        if (!CanJoin(peerId, out string reason))
        {
            GD.PushWarning($"{GetPath()}: rejected peer {peerId}: {reason}");
            NetworkSession.DisconnectPeer(peerId);
            return;
        }

        long participantId = _nextParticipantId++;
        Godot.Collections.Dictionary<string, Variant> spawnData = new()
        {
            ["participant_id"] = participantId,
            ["peer_id"] = peerId,
        };
        PlayerState? playerState = PlayerStateSpawner!.Spawn(spawnData) as PlayerState;
        if (playerState is null && IsOfflineSession)
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

        if (CurrentWorld is not null && State == GameSessionState.Active)
        {
            _pendingLateJoinPeers.Add(peerId);
        }
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

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void BeginTravel(long travelId, string resourcePath)
    {
        if (
            NetworkSession?.IsServer == true
            || travelId <= 0
            || string.IsNullOrWhiteSpace(resourcePath)
            || State != GameSessionState.Active
        )
        {
            return;
        }

        State = GameSessionState.Traveling;
        EmitSignal(SignalName.TravelStarted, travelId, resourcePath);

        if (CurrentTravelId != travelId)
        {
            CurrentTravelId = travelId;
            CurrentWorldPath = resourcePath;
            _isCurrentWorldReady = false;
        }
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
            if (NetworkSession?.IsServer != true)
            {
                QueueWorldLoadFailureReport(
                    travelId,
                    $"Unable to load world scene '{resourcePath}'."
                );
            }

            return null!;
        }

        world.Name = $"World_{travelId}";
        world.SetMeta(TravelIdMeta, travelId);
        world.SetMeta(WorldPathMeta, resourcePath);
        return world;
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void WorldReady(long travelId)
    {
        if (NetworkSession?.IsServer != true || travelId != CurrentTravelId)
        {
            return;
        }

        long senderPeerId = Multiplayer.GetRemoteSenderId();
        if (!_participants.TryGetByPeerId(senderPeerId, out PlayerState? playerState))
        {
            return;
        }

        if (_activeTravel is not null && _activeTravel.Id == travelId)
        {
            if (_activeTravel.MarkPeerReady(senderPeerId))
            {
                TryCompleteTravel();
            }

            return;
        }

        if (State == GameSessionState.Active && _pendingLateJoinPeers.Remove(senderPeerId))
        {
            EmitSignal(SignalName.PlayerWorldReady, playerState);
        }
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void CompleteTravel(long travelId)
    {
        if (
            NetworkSession?.IsServer == true
            || State != GameSessionState.Traveling
            || travelId != CurrentTravelId
            || !_isCurrentWorldReady
            || _currentWorld is null
        )
        {
            return;
        }

        State = GameSessionState.Active;
        EmitSignal(SignalName.TravelCompleted, travelId, _currentWorld);
    }

    public void ReportWorldLoadFailure(long travelId, string reason)
    {
        if (NetworkSession?.IsServer == true || IsOfflineSession)
        {
            return;
        }

        RpcId(1, nameof(WorldLoadFailed), travelId, reason);
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void WorldLoadFailed(long travelId, string reason)
    {
        if (NetworkSession?.IsServer != true || travelId != CurrentTravelId)
        {
            return;
        }

        long senderPeerId = Multiplayer.GetRemoteSenderId();
        if (!_participants.TryGetByPeerId(senderPeerId, out _))
        {
            return;
        }

        GD.PushError(
            $"{GetPath()}: peer {senderPeerId} failed to load travel {travelId}: {reason}"
        );
        NetworkSession.DisconnectPeer(senderPeerId);
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

        long travelId = node.GetMeta(TravelIdMeta).AsInt64();
        string resourcePath = node.GetMeta(WorldPathMeta).AsString();
        if (!AdoptCurrentWorld(node, travelId, resourcePath))
        {
            return;
        }

        bool isGlobalTravel = _activeTravel?.Id == travelId;
        if (isGlobalTravel)
        {
            State = GameSessionState.Traveling;
        }
    }

    private void OnWorldDespawned(Node node) => ClearCurrentWorld(node);

    private bool AdoptCurrentWorld(Node world, long travelId, string resourcePath)
    {
        if (_currentWorld is not null && IsInstanceValid(_currentWorld) && _currentWorld != world)
        {
            FailRuntime("A second managed world was spawned before the previous world retired.");
            return false;
        }

        _currentWorld = world;
        CurrentTravelId = travelId;
        CurrentWorldPath = resourcePath;
        _isCurrentWorldReady = false;
        world.TreeExiting += () => ClearCurrentWorld(world);
        ScheduleWorldReadinessCheck();
        return true;
    }

    private void RetireCurrentWorld()
    {
        if (_currentWorld is null || !IsInstanceValid(_currentWorld))
        {
            _currentWorld = null;
            CurrentWorldPath = string.Empty;
            _isCurrentWorldReady = false;
            return;
        }

        Node world = _currentWorld;
        ClearCurrentWorld(world);
        world.Free();
    }

    private void ClearCurrentWorld(Node world)
    {
        if (_currentWorld != world)
        {
            return;
        }

        _currentWorld = null;
        CurrentWorldPath = string.Empty;
        _isCurrentWorldReady = false;
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
            || _isCurrentWorldReady
            || _currentWorld.GetMeta(TravelIdMeta, 0L).AsInt64() != CurrentTravelId
        )
        {
            return;
        }

        _isCurrentWorldReady = true;
        EmitSignal(SignalName.WorldLoaded, CurrentTravelId, _currentWorld);
        if (NetworkSession?.IsServer == true)
        {
            _activeTravel?.MarkLocalWorldReady();
            TryCompleteTravel();
        }
        else if (!IsOfflineSession)
        {
            RpcId(1, nameof(WorldReady), CurrentTravelId);
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
        if (State == GameSessionState.Traveling && !_isCurrentWorldReady)
        {
            CompleteLocalWorldReadiness();
        }
    }

    private void TryCompleteTravel()
    {
        if (
            NetworkSession?.IsServer != true
            || _activeTravel is null
            || State != GameSessionState.Traveling
            || !_activeTravel.IsComplete
            || _currentWorld is null
            || !_isCurrentWorldReady
        )
        {
            return;
        }

        long travelId = _activeTravel.Id;
        Node world = _currentWorld;
        _activeTravel = null;
        State = GameSessionState.Active;
        UpdateTransportAdmission();
        Rpc(nameof(CompleteTravel), travelId);
        EmitSignal(SignalName.TravelCompleted, travelId, world);
        foreach (PlayerState playerState in _participants.Players.ToArray())
        {
            EmitSignal(SignalName.PlayerWorldReady, playerState);
        }
    }

    private void FailRuntime(string reason)
    {
        GD.PushError($"{GetPath()}: {reason}");
        State = GameSessionState.Failed;
        UpdateTransportAdmission();
    }

    private void QueueWorldLoadFailureReport(long travelId, string reason)
    {
        if (!IsInsideTree())
        {
            return;
        }

        GetTree().CreateTimer(0.0).Timeout += () => ReportWorldLoadFailure(travelId, reason);
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
            || !_participants.TryAdd(playerState)
        )
        {
            return false;
        }

        playerState.TreeExiting += () => UnregisterPlayerState(playerState);
        EmitSignal(SignalName.PlayerJoined, playerState);
        return true;
    }

    private bool UnregisterPlayerState(PlayerState playerState)
    {
        if (!_participants.Remove(playerState))
        {
            return false;
        }

        EmitSignal(SignalName.PlayerLeft, playerState.ParticipantId, playerState.PeerId);
        return true;
    }

    private void RemoveParticipant(long peerId)
    {
        if (!_participants.TryGetByPeerId(peerId, out PlayerState? playerState))
        {
            return;
        }

        UnregisterPlayerState(playerState);
        if (IsInstanceValid(playerState))
        {
            playerState.QueueFree();
        }
    }

    private void UpdateTransportAdmission()
    {
        if (NetworkSession?.IsServer != true)
        {
            return;
        }

        NetworkSession.SetAcceptingConnections(
            _isAcceptingPlayers && State == GameSessionState.Active
        );
    }

    public override void _ExitTree()
    {
        Reset();
    }
}
