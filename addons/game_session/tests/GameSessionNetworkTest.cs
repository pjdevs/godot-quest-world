namespace QuestWorld.Tests.GameSessionTests;

using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.GameSession;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Network")]
public sealed class GameSessionNetworkTest
{
    [TestCase]
    public async Task ServerAdmitsRemotePeerAndReplicatesExactlyOnePersistentState()
    {
        GameSessionTestFixtures.NetworkFixture fixture = await GameSessionTestFixtures.Connect();
        try
        {
            AssertThat(fixture.Server.GameSession.PlayerStates.Count).IsEqual(2);
            AssertThat(fixture.Client.GameSession.PlayerStates.Count).IsEqual(2);
            AssertThat(fixture.Server.GameSession.TryGetPlayerStateByPeerId(1, out _)).IsTrue();
            AssertThat(
                    fixture.Server.GameSession.TryGetPlayerStateByPeerId(
                        fixture.ClientApi.GetUniqueId(),
                        out _
                    )
                )
                .IsTrue();
        }
        finally
        {
            fixture.Close();
        }
    }

    [TestCase]
    public async Task AdmissionDisabledDoesNotCreateAStateForTheConnectingPeer()
    {
        GameSessionTestFixtures.NetworkFixture fixture = await GameSessionTestFixtures.Connect(
            acceptingPlayers: false
        );
        try
        {
            AssertThat(fixture.Server.GameSession.PlayerStates.Count).IsEqual(0);
            AssertThat(fixture.Client.GameSession.PlayerStates.Count).IsEqual(0);
        }
        finally
        {
            fixture.Close();
        }
    }

    [TestCase]
    public async Task CustomCanJoinRejectsRemotePeerWithoutCreatingTransientState()
    {
        GameSessionTestFixtures.NetworkFixture fixture = await GameSessionTestFixtures.Connect(
            rejectRemotePlayers: true
        );
        try
        {
            AssertThat(fixture.Server.GameSession.PlayerStates.Count).IsEqual(1);
            AssertThat(fixture.Client.GameSession.PlayerStates.Count).IsEqual(0);
        }
        finally
        {
            fixture.Close();
        }
    }

    [TestCase]
    public async Task DisconnectRemovesTheParticipantAndItsIndexes()
    {
        GameSessionTestFixtures.NetworkFixture fixture = await GameSessionTestFixtures.Connect();
        try
        {
            long remotePeerId = fixture.ClientApi.GetUniqueId();
            fixture.Client.Network.Stop();
            for (int frame = 0; frame < 60; frame++)
            {
                await fixture.Pump();
                if (fixture.Server.GameSession.PlayerStates.Count == 1)
                {
                    break;
                }
            }

            AssertThat(fixture.Server.GameSession.PlayerStates.Count).IsEqual(1);
            AssertThat(fixture.Server.GameSession.TryGetPlayerStateByPeerId(remotePeerId, out _))
                .IsFalse();
        }
        finally
        {
            fixture.Close();
        }
    }

    [TestCase]
    public async Task AdmissionIntentMirrorsIntoTransportRefusal()
    {
        GameSessionTestFixtures.NetworkFixture fixture = await GameSessionTestFixtures.Connect();
        try
        {
            MultiplayerPeer serverPeer = fixture.Server.Network.Multiplayer.MultiplayerPeer!;
            fixture.Server.GameSession.IsAcceptingPlayers = false;
            AssertThat(serverPeer.RefuseNewConnections).IsTrue();

            fixture.Server.GameSession.IsAcceptingPlayers = true;
            AssertThat(serverPeer.RefuseNewConnections).IsFalse();
        }
        finally
        {
            fixture.Close();
        }
    }
}
