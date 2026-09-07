using System.Collections.Generic;
using Godot;
using QuestWorld.Character;
using QuestWorld.Network;

public partial class QuestWorldNetworkSession : NetworkSession
{
    [Export]
    public CharacterPlayerController? LocalPlayerController { get; set; }

    [Export]
    public Spawner? PlayerSpawner { get; set; }

    private bool _configurationValid;
    private bool _integrationSignalsConnected;

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
            GD.PushError($"QuestWorldNetworkSession: {parseError}");
            GetTree().Quit(2);
            return;
        }

        if (PlayerSpawner is null)
        {
            GD.PushError(
                "QuestWorldNetworkSession: QuestWorldWorld and QuestWorldWorld.PlayerSpawner are required."
            );
            _configurationValid = false;
            return;
        }

        ConnectIntegrationSignals();
        _configurationValid = Start(launchOptions!);
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
            GD.Print($"QuestWorldNetworkSession: possessed local {localPlayer.Name}");
            GD.Print(
                $"QuestWorldNetworkSession: visible player count={PlayerSpawner?.GetSpawnRoot()?.GetChildCount()}"
            );
        }
    }

    private void ConnectIntegrationSignals()
    {
        if (_integrationSignalsConnected)
        {
            return;
        }

        PeerConnected += OnPeerConnected;
        PeerDisconnected += OnPeerDisconnected;
        Connected += OnConnected;
        Failed += OnFailed;
        _integrationSignalsConnected = true;
    }

    private void OnConnected()
    {
        if (IsServer && !IsDedicatedServer)
        {
            SpawnPlayer(LocalPeerId);
        }
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
            GD.Print($"QuestWorldNetworkSession: despawned {player.Name}");
        }
    }

    private void OnFailed(string reason)
    {
        _configurationValid = false;
        GD.PushError($"QuestWorldNetworkSession: {reason}");
    }

    private void SpawnPlayer(int peerId)
    {
        string playerName = NetworkPlayerIdentity.GetPlayerName(peerId);
        Character? existingPlayer = PlayerSpawner
            ?.GetSpawnRoot()
            ?.GetNodeOrNull<Character>(playerName);

        if (existingPlayer is not null)
        {
            GD.PushWarning(
                $"QuestWorldNetworkSession: player {playerName} already exists; skipping spawn."
            );
            return;
        }

        Character? player =
            PlayerSpawner?.Spawn(
                Transform3D.Identity.Translated(NetworkPlayerIdentity.GetSpawnPosition(peerId)),
                playerName
            ) as Character;

        if (player is null)
        {
            GD.PushError($"QuestWorldNetworkSession: failed to spawn {playerName}.");
            return;
        }

        GD.Print($"QuestWorldNetworkSession: spawned {playerName} at {player.Position}");
    }

    public override void _ExitTree()
    {
        if (_integrationSignalsConnected)
        {
            PeerConnected -= OnPeerConnected;
            PeerDisconnected -= OnPeerDisconnected;
            Connected -= OnConnected;
            Failed -= OnFailed;
            _integrationSignalsConnected = false;
        }

        base._ExitTree();
    }
}
