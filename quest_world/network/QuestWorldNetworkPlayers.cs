using Godot;
using QuestWorld.Character;
using QuestWorld.Network;

public partial class QuestWorldNetworkPlayers : Node
{
    [Export]
    public NetworkSession? NetworkSession { get; set; }

    [Export]
    public Spawner? PlayerSpawner { get; set; }

    [Export]
    public CharacterPlayerController? LocalPlayerController { get; set; }

    private bool _initialized;

    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        if (NetworkSession is null)
        {
            GD.PushError($"{GetPath()}: NetworkSession is required.");
            return;
        }

        if (PlayerSpawner is null)
        {
            GD.PushError($"{GetPath()}: PlayerSpawner is required.");
            return;
        }

        NetworkSession.PeerConnected += OnPeerConnected;
        NetworkSession.PeerDisconnected += OnPeerDisconnected;
        NetworkSession.Connected += OnConnected;
        NetworkSession.Failed += OnFailed;
        _initialized = true;

        SynchronizeCurrentSession();
    }

    public override void _Process(double delta)
    {
        if (
            !_initialized
            || NetworkSession is null
            || NetworkSession.State != SessionState.Active
            || LocalPlayerController is null
            || NetworkSession.IsDedicatedServer
            || NetworkSession.LocalPeerId <= 0
        )
        {
            return;
        }

        Character? localPlayer = PlayerSpawner
            ?.GetSpawnRoot()
            ?.GetNodeOrNull<Character>(
                NetworkPlayerIdentity.GetPlayerName(NetworkSession.LocalPeerId)
            );
        if (
            localPlayer != null
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

    private void SynchronizeCurrentSession()
    {
        if (
            NetworkSession?.State == SessionState.Active
            && NetworkSession.IsServer
            && !NetworkSession.IsDedicatedServer
        )
        {
            SpawnPlayer(NetworkSession.LocalPeerId);
        }
    }

    private void OnConnected()
    {
        SynchronizeCurrentSession();
    }

    private void OnPeerConnected(long peerId)
    {
        if (NetworkSession?.IsServer == true)
        {
            SpawnPlayer((int)peerId);
        }
    }

    private void OnPeerDisconnected(long peerId)
    {
        if (NetworkSession?.IsServer != true)
        {
            return;
        }

        Character? player = PlayerSpawner
            ?.GetSpawnRoot()
            ?.GetNodeOrNull<Character>(NetworkPlayerIdentity.GetPlayerName((int)peerId));
        if (player != null)
        {
            player.QueueFree();
            GD.Print($"QuestWorldNetworkPlayers: despawned {player.Name}");
        }
    }

    private void OnFailed(string reason)
    {
        GD.PushError($"QuestWorldNetworkPlayers: {reason}");
    }

    private void SpawnPlayer(int peerId)
    {
        if (PlayerSpawner is null)
        {
            return;
        }

        string playerName = NetworkPlayerIdentity.GetPlayerName(peerId);
        Character? existingPlayer = PlayerSpawner
            .GetSpawnRoot()
            ?.GetNodeOrNull<Character>(playerName);

        if (existingPlayer is not null)
        {
            GD.PushWarning(
                $"QuestWorldNetworkPlayers: player {playerName} already exists; skipping spawn."
            );
            return;
        }

        Character? player =
            PlayerSpawner.Spawn(
                Transform3D.Identity.Translated(NetworkPlayerIdentity.GetSpawnPosition(peerId)),
                playerName
            ) as Character;

        if (player is null)
        {
            GD.PushError($"QuestWorldNetworkPlayers: failed to spawn {playerName}.");
            return;
        }

        GD.Print($"QuestWorldNetworkPlayers: spawned {playerName} at {player.Position}");
    }

    public override void _ExitTree()
    {
        if (_initialized && NetworkSession is not null)
        {
            NetworkSession.PeerConnected -= OnPeerConnected;
            NetworkSession.PeerDisconnected -= OnPeerDisconnected;
            NetworkSession.Connected -= OnConnected;
            NetworkSession.Failed -= OnFailed;
        }

        _initialized = false;
    }
}
