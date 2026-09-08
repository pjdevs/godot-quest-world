namespace QuestWorld.Tests.NetworkSessionTests;

using System;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.Network;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Network")]
public sealed class NetworkSessionControlTest
{
    private static int _nextPort = 47930;

    [TestCase]
    public async Task ServerCanToggleConnectionAdmission()
    {
        SessionFixture fixture = await CreateFixture();
        try
        {
            AssertThat(fixture.Server.SetAcceptingConnections(false)).IsTrue();
            AssertThat(fixture.Server.Multiplayer.MultiplayerPeer!.RefuseNewConnections).IsTrue();

            AssertThat(fixture.Server.SetAcceptingConnections(true)).IsTrue();
            AssertThat(fixture.Server.Multiplayer.MultiplayerPeer!.RefuseNewConnections).IsFalse();
        }
        finally
        {
            fixture.Close();
        }
    }

    [TestCase]
    public async Task ClientCannotControlServerAdmission()
    {
        SessionFixture fixture = await CreateFixture(includeClient: true);
        try
        {
            AssertThat(fixture.Client!.SetAcceptingConnections(false)).IsFalse();
        }
        finally
        {
            fixture.Close();
        }
    }

    [TestCase]
    public async Task InvalidDisconnectRequestIsRejected()
    {
        SessionFixture fixture = await CreateFixture(includeClient: true);
        try
        {
            AssertThat(fixture.Server.DisconnectPeer(0)).IsFalse();
            AssertThat(fixture.Server.DisconnectPeer((long)int.MaxValue + 1)).IsFalse();
            AssertThat(fixture.Server.DisconnectPeer(fixture.Server.LocalPeerId)).IsFalse();
            AssertThat(fixture.Client!.DisconnectPeer(fixture.Server.LocalPeerId)).IsFalse();
        }
        finally
        {
            fixture.Close();
        }
    }

    private static async Task<SessionFixture> CreateFixture(bool includeClient = false)
    {
        int port = _nextPort++;
        Node root = new() { Name = "NetworkSessionControl" };
        Node serverBranch = new() { Name = "Server" };
        root.AddChild(serverBranch);
        NetworkSession server = new() { Name = "NetworkSession" };
        serverBranch.AddChild(server);

        ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        await runner.SimulateFrames(1);
        SceneTree tree = root.GetTree();
        tree.SetMultiplayer(MultiplayerApi.CreateDefaultInterface(), serverBranch.GetPath());
        AssertThat(server.Start(ParseOptions("--host", "--port", port.ToString()))).IsTrue();

        NetworkSession? client = null;
        if (includeClient)
        {
            Node clientBranch = new() { Name = "Client" };
            root.AddChild(clientBranch);
            client = new NetworkSession { Name = "NetworkSession" };
            clientBranch.AddChild(client);
            tree.SetMultiplayer(MultiplayerApi.CreateDefaultInterface(), clientBranch.GetPath());
            AssertThat(client.Start(ParseOptions("--client", "--port", port.ToString()))).IsTrue();
        }

        await runner.SimulateFrames(1);
        return new SessionFixture(runner, server, client);
    }

    private static NetworkLaunchOptions ParseOptions(params string[] arguments) =>
        NetworkLaunchOptions.TryParse(
            arguments,
            out NetworkLaunchOptions? options,
            out string error
        )
            ? options!
            : throw new InvalidOperationException(error);

    private sealed record SessionFixture(
        ISceneRunner Runner,
        NetworkSession Server,
        NetworkSession? Client
    )
    {
        public void Close()
        {
            Client?.Stop();
            Server.Stop();
        }
    }
}
