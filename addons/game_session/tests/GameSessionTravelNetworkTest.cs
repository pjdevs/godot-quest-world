namespace QuestWorld.Tests.GameSessionTests;

using System.Collections.Generic;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.GameSession;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Network")]
public sealed class GameSessionTravelNetworkTest
{
    [TestCase]
    public async Task TravelWaitsForRemoteReadinessAndCompletesExactlyOnce()
    {
        GameSessionTestFixtures.NetworkFixture fixture = await GameSessionTestFixtures.Connect();
        try
        {
            List<string> events = new();
            fixture.Client.GameSession.WorldLoaded += (_, _) => events.Add("client-loaded");
            fixture.Server.GameSession.TravelCompleted += (_, _) => events.Add("server-completed");
            int clientCompletions = 0;
            int playerWorldReadyCount = 0;
            fixture.Client.GameSession.TravelCompleted += (_, _) => clientCompletions++;
            fixture.Server.GameSession.PlayerWorldReady += _ => playerWorldReadyCount++;
            PackedScene world = GD.Load<PackedScene>(
                "res://addons/game_session/tests/fixtures/WorldA.tscn"
            );

            AssertThat(fixture.Server.GameSession.Travel(world)).IsTrue();
            AssertThat(fixture.Server.GameSession.State).IsEqual(GameSessionState.Traveling);
            for (int frame = 0; frame < 120; frame++)
            {
                await fixture.Pump();
                if (fixture.Server.GameSession.State == GameSessionState.Active)
                {
                    break;
                }
            }

            AssertThat(fixture.Server.GameSession.State).IsEqual(GameSessionState.Active);
            AssertThat(fixture.Client.GameSession.State).IsEqual(GameSessionState.Active);
            AssertThat(clientCompletions).IsEqual(1);
            AssertThat(playerWorldReadyCount).IsEqual(2);
            AssertThat(events).IsEqual(new List<string> { "client-loaded", "server-completed" });
        }
        finally
        {
            fixture.Close();
        }
    }

    [TestCase]
    public async Task LateJoinReconstructsTheCurrentWorldWithoutASecondGlobalTravel()
    {
        GameSessionTestFixtures.NetworkFixture fixture = await GameSessionTestFixtures.Connect();
        try
        {
            PackedScene world = GD.Load<PackedScene>(
                "res://addons/game_session/tests/fixtures/WorldA.tscn"
            );
            AssertThat(fixture.Server.GameSession.Travel(world)).IsTrue();
            await WaitForActive(fixture);
            int existingClientCompletions = 0;
            fixture.Client.GameSession.TravelCompleted += (_, _) => existingClientCompletions++;
            List<string> lateEvents = new();
            fixture.Server.GameSession.PlayerWorldReady += playerState =>
            {
                if (
                    playerState.PeerId != 1
                    && playerState.PeerId != fixture.ClientApi.GetUniqueId()
                )
                {
                    lateEvents.Add("server-ready");
                }
            };

            GameSessionTestFixtures.LatePeer late = await fixture.JoinLate();
            late.GameSession.WorldLoaded += (_, _) => lateEvents.Add("late-loaded");
            await fixture.Pump(24);

            AssertThat(fixture.Server.GameSession.PlayerStates.Count).IsEqual(3);
            AssertThat(late.GameSession.PlayerStates.Count).IsEqual(3);
            AssertThat(late.GameSession.State).IsEqual(GameSessionState.Active);
            AssertThat(late.GameSession.CurrentWorldPath).IsEqual(world.ResourcePath);
            AssertThat(late.GameSession.CurrentWorld is not null).IsTrue();
            AssertThat(existingClientCompletions).IsEqual(0);
            AssertThat(lateEvents.Contains("server-ready")).IsTrue();
        }
        finally
        {
            fixture.Close();
        }
    }

    [TestCase]
    public async Task ClientWorldFailureRemovesOnlyTheFailedParticipant()
    {
        GameSessionTestFixtures.NetworkFixture fixture = await GameSessionTestFixtures.Connect();
        try
        {
            PackedScene world = GD.Load<PackedScene>(
                "res://addons/game_session/tests/fixtures/WorldA.tscn"
            );
            AssertThat(fixture.Server.GameSession.Travel(world)).IsTrue();
            await WaitForActive(fixture);
            long failedPeerId = fixture.ClientApi.GetUniqueId();
            fixture.Client.GameSession.ReportWorldLoadFailure(
                fixture.Client.GameSession.CurrentTravelId,
                "test failure"
            );
            for (int frame = 0; frame < 120; frame++)
            {
                await fixture.Pump();
                if (fixture.Server.GameSession.PlayerStates.Count == 1)
                {
                    break;
                }
            }

            AssertThat(fixture.Server.GameSession.PlayerStates.Count).IsEqual(1);
            AssertThat(fixture.Server.GameSession.TryGetPlayerStateByPeerId(failedPeerId, out _))
                .IsFalse();
        }
        finally
        {
            fixture.Close();
        }
    }

    [TestCase]
    public async Task DisconnectDuringTravelReleasesThePendingBarrier()
    {
        GameSessionTestFixtures.NetworkFixture fixture = await GameSessionTestFixtures.Connect();
        try
        {
            long disconnectedPeerId = fixture.ClientApi.GetUniqueId();
            bool disconnectedParticipantBecameReady = false;
            fixture.Server.GameSession.PlayerWorldReady += playerState =>
            {
                if (playerState.PeerId == disconnectedPeerId)
                {
                    disconnectedParticipantBecameReady = true;
                }
            };
            PackedScene world = GD.Load<PackedScene>(
                "res://addons/game_session/tests/fixtures/WorldB.tscn"
            );

            AssertThat(fixture.Server.GameSession.Travel(world)).IsTrue();
            fixture.Client.Network.Stop();
            await fixture.Pump(24);

            AssertThat(fixture.Server.GameSession.State).IsEqual(GameSessionState.Active);
            AssertThat(fixture.Server.GameSession.PlayerStates.Count).IsEqual(1);
            AssertThat(disconnectedParticipantBecameReady).IsFalse();
        }
        finally
        {
            fixture.Close();
        }
    }

    [TestCase]
    public async Task DedicatedServerWaitsForItsLocalWorldAndRemoteReadiness()
    {
        GameSessionTestFixtures.NetworkFixture fixture = await GameSessionTestFixtures.Connect(
            dedicatedServer: true
        );
        try
        {
            PackedScene world = GD.Load<PackedScene>(
                "res://addons/game_session/tests/fixtures/WorldB.tscn"
            );

            AssertThat(fixture.Server.GameSession.PlayerStates.Count).IsEqual(1);
            AssertThat(fixture.Server.GameSession.Travel(world)).IsTrue();
            AssertThat(fixture.Server.GameSession.State).IsEqual(GameSessionState.Traveling);
            await WaitForActive(fixture);

            AssertThat(fixture.Server.GameSession.PlayerStates.Count).IsEqual(1);
            AssertThat(fixture.Client.GameSession.State).IsEqual(GameSessionState.Active);
        }
        finally
        {
            fixture.Close();
        }
    }

    [TestCase]
    public async Task DuplicateAndStaleReadinessAcknowledgementsAreIgnored()
    {
        GameSessionTestFixtures.NetworkFixture fixture = await GameSessionTestFixtures.Connect();
        try
        {
            PackedScene world = GD.Load<PackedScene>(
                "res://addons/game_session/tests/fixtures/WorldA.tscn"
            );
            AssertThat(fixture.Server.GameSession.Travel(world)).IsTrue();
            await WaitForActive(fixture);
            long travelId = fixture.Client.GameSession.CurrentTravelId;

            fixture.Client.GameSession.RpcId(1, nameof(GameSession.TravelReady), travelId);
            fixture.Client.GameSession.RpcId(1, nameof(GameSession.TravelReady), travelId - 1);
            await fixture.Pump(12);

            AssertThat(fixture.Server.GameSession.State).IsEqual(GameSessionState.Active);
            AssertThat(fixture.Client.GameSession.State).IsEqual(GameSessionState.Active);
        }
        finally
        {
            fixture.Close();
        }
    }

    [TestCase]
    public async Task LateJoinFailureRemovesOnlyTheJoiningParticipant()
    {
        GameSessionTestFixtures.NetworkFixture fixture = await GameSessionTestFixtures.Connect();
        try
        {
            PackedScene world = GD.Load<PackedScene>(
                "res://addons/game_session/tests/fixtures/WorldA.tscn"
            );
            AssertThat(fixture.Server.GameSession.Travel(world)).IsTrue();
            await WaitForActive(fixture);
            GameSessionTestFixtures.LatePeer late = await fixture.JoinLate();
            long failedPeerId = late.Api.GetUniqueId();

            late.GameSession.ReportWorldLoadFailure(
                late.GameSession.CurrentTravelId,
                "late join test failure"
            );
            for (int frame = 0; frame < 120; frame++)
            {
                await fixture.Pump();
                if (fixture.Server.GameSession.PlayerStates.Count == 2)
                {
                    break;
                }
            }

            AssertThat(fixture.Server.GameSession.PlayerStates.Count).IsEqual(2);
            AssertThat(fixture.Server.GameSession.TryGetPlayerStateByPeerId(failedPeerId, out _))
                .IsFalse();
            AssertThat(fixture.Client.GameSession.State).IsEqual(GameSessionState.Active);
        }
        finally
        {
            fixture.Close();
        }
    }

    private static async Task WaitForActive(GameSessionTestFixtures.NetworkFixture fixture)
    {
        for (int frame = 0; frame < 120; frame++)
        {
            await fixture.Pump();
            if (fixture.Server.GameSession.State == GameSessionState.Active)
            {
                break;
            }
        }

        AssertThat(fixture.Server.GameSession.State).IsEqual(GameSessionState.Active);
        AssertThat(fixture.Client.GameSession.State).IsEqual(GameSessionState.Active);
    }
}
