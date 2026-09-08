using Godot;

namespace NetworkPlugin;

public partial class NetworkSession : Node
{
    [Signal]
    public delegate void PeerConnectedEventHandler(long peerId);

    [Signal]
    public delegate void PeerDisconnectedEventHandler(long peerId);

    [Signal]
    public delegate void ConnectedEventHandler();

    [Signal]
    public delegate void DisconnectedEventHandler();

    [Signal]
    public delegate void FailedEventHandler(string reason);

    private bool _multiplayerSignalsConnected;
    private MultiplayerPeer? _peer;

    public NetworkLaunchOptions? LaunchOptions { get; private set; }

    public bool IsServer =>
        LaunchOptions?.Mode
            is NetworkLaunchMode.Offline
                or NetworkLaunchMode.Server
                or NetworkLaunchMode.Host;

    public bool IsDedicatedServer => LaunchOptions?.Mode == NetworkLaunchMode.Server;

    public int LocalPeerId { get; private set; }

    public SessionState State { get; private set; } = SessionState.Stopped;

    public bool Start(NetworkLaunchOptions options)
    {
        if (State is not SessionState.Stopped)
        {
            GD.PushWarning($"NetworkSession: cannot start from state {State}.");
            return false;
        }

        LaunchOptions = options;
        ConnectMultiplayerSignals();

        bool started = options.Mode switch
        {
            NetworkLaunchMode.Offline => StartOffline(),
            NetworkLaunchMode.Host => StartHost(),
            NetworkLaunchMode.Server => StartDedicatedServer(),
            NetworkLaunchMode.Client => StartClient(),
            _ => false,
        };

        if (!started)
        {
            Fail("Unable to start the network session.");
            return false;
        }

        GD.Print(
            $"NetworkSession: mode={options.Mode}, address={options.Address}, port={options.Port}"
        );
        return true;
    }

    public bool SetAcceptingConnections(bool accepting)
    {
        if (!IsServer || Multiplayer.MultiplayerPeer is not MultiplayerPeer peer)
        {
            return false;
        }

        peer.RefuseNewConnections = !accepting;
        return true;
    }

    public bool DisconnectPeer(long peerId)
    {
        if (
            !IsServer
            || peerId <= 0
            || peerId > int.MaxValue
            || peerId == LocalPeerId
            || Multiplayer.MultiplayerPeer is not MultiplayerPeer peer
        )
        {
            return false;
        }

        peer.DisconnectPeer((int)peerId);
        return true;
    }

    public void Stop()
    {
        if (State is SessionState.Stopped or SessionState.Stopping)
        {
            return;
        }

        bool notifyDisconnected = State != SessionState.Failed;
        State = SessionState.Stopping;
        SetProcess(false);
        CleanupPeer();
        State = SessionState.Stopped;

        if (notifyDisconnected)
        {
            EmitSignal(SignalName.Disconnected);
        }
    }

    private bool StartOffline()
    {
        LocalPeerId = 1;
        State = SessionState.Active;
        EmitSignal(SignalName.Connected);
        return true;
    }

    private bool StartHost()
    {
        if (!StartServer())
        {
            return false;
        }

        LocalPeerId = 1;
        State = SessionState.Active;
        EmitSignal(SignalName.Connected);
        return true;
    }

    private bool StartDedicatedServer()
    {
        if (!StartServer())
        {
            return false;
        }

        LocalPeerId = 1;
        State = SessionState.Active;
        EmitSignal(SignalName.Connected);
        return true;
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
        State = SessionState.Connecting;
        GD.Print($"NetworkSession: connecting to {options.Address}:{options.Port}");
        return true;
    }

    private void ConnectMultiplayerSignals()
    {
        if (_multiplayerSignalsConnected)
        {
            return;
        }

        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
        _multiplayerSignalsConnected = true;
    }

    private void OnPeerConnected(long peerId)
    {
        EmitSignal(SignalName.PeerConnected, peerId);
    }

    private void OnPeerDisconnected(long peerId)
    {
        EmitSignal(SignalName.PeerDisconnected, peerId);
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
        EmitSignal(SignalName.Connected);
    }

    private void OnConnectionFailed()
    {
        Fail("Connection failed.");
    }

    private void OnServerDisconnected()
    {
        GD.Print("NetworkSession: server disconnected; stopping session.");
        Stop();
    }

    private void Fail(string reason)
    {
        State = SessionState.Failed;
        SetProcess(false);
        CleanupPeer();
        EmitSignal(SignalName.Failed, reason);
    }

    private void CleanupPeer()
    {
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
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            Stop();
            GetTree().Quit();
        }
    }

    public override void _ExitTree()
    {
        Stop();
    }
}
