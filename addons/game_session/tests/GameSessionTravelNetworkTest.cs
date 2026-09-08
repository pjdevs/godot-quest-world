namespace QuestWorld.Tests.GameSessionTests;

using System.Collections.Generic;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.GameSession;
using QuestWorld.Tests.GameSessionTests.Fixtures;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Network")]
public sealed class GameSessionTravelNetworkTest
{
    private const string NestedWorldPath =
        "res://addons/game_session/tests/fixtures/NestedSpawnerWorld.tscn";

    private const string ReplicatedPlayerStatePath =
        "res://addons/game_session/tests/fixtures/ReplicatedTestPlayerState.tscn";

    [TestCase]
    public async Task ClientDisconnectDuringTravelClearsRuntimeSession()
    {
        GameSessionTestFixtures.NetworkFixture fixture = await GameSessionTestFixtures.Connect();
        try
        {
            PackedScene firstWorld = GD.Load<PackedScene>(
                "res://addons/game_session/tests/fixtures/WorldA.tscn"
            );
            AssertThat(fixture.Server.GameSession.Travel(firstWorld)).IsTrue();
            await WaitForActive(fixture);
            AssertThat(fixture.Client.GameSession.CurrentWorld).IsNotNull();

            PackedScene secondWorld = GD.Load<PackedScene>(
                "res://addons/game_session/tests/fixtures/WorldB.tscn"
            );
            AssertThat(fixture.Server.GameSession.Travel(secondWorld)).IsTrue();
            fixture.Client.GameSession.BeginTravel(
                fixture.Server.GameSession.CurrentTravelId,
                secondWorld.ResourcePath
            );
            AssertThat(fixture.Client.GameSession.State).IsEqual(GameSessionState.Traveling);

            fixture.Client.Network.Stop();
            await fixture.Pump(4);

            AssertThat(fixture.Client.GameSession.State).IsEqual(GameSessionState.Idle);
            AssertThat(fixture.Client.GameSession.PlayerStates.Count).IsEqual(0);
            AssertThat(fixture.Client.GameSession.CurrentWorld).IsNull();
        }
        finally
        {
            fixture.Close();
        }
    }

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

            GameSessionTestFixtures.LatePeer late = await fixture.JoinLate(
                beforeStart: lateSession =>
                    lateSession.GameSession.WorldLoaded += (_, _) => lateEvents.Add("late-loaded")
            );
            await fixture.Pump(24);

            AssertThat(fixture.Server.GameSession.PlayerStates.Count).IsEqual(3);
            AssertThat(late.GameSession.PlayerStates.Count).IsEqual(3);
            AssertThat(late.GameSession.State).IsEqual(GameSessionState.Active);
            AssertThat(late.GameSession.CurrentWorldPath).IsEqual(world.ResourcePath);
            AssertThat(late.GameSession.CurrentWorld is not null).IsTrue();
            AssertThat(existingClientCompletions).IsEqual(0);
            AssertThat(lateEvents).IsEqual(new List<string> { "late-loaded", "server-ready" });
        }
        finally
        {
            fixture.Close();
        }
    }

    [TestCase]
    public async Task LateJoinReconstructsCurrentWorldBeforeNestedSpawnHistory()
    {
        GameSessionTestFixtures.NetworkFixture fixture = await GameSessionTestFixtures.Connect();
        try
        {
            PackedScene nestedWorld = GD.Load<PackedScene>(NestedWorldPath);
            AssertThat(fixture.Server.GameSession.Travel(nestedWorld)).IsTrue();
            await WaitForActive(fixture);

            NestedSpawnerWorld serverWorld = (NestedSpawnerWorld)
                fixture.Server.GameSession.CurrentWorld!;
            AssertThat(serverWorld.SpawnReplicatedNode("ExistingNestedActor") is not null).IsTrue();
            await fixture.Pump(8);

            GameSessionTestFixtures.LatePeer late = await fixture.JoinLate();
            await fixture.Pump(24);

            AssertThat(late.GameSession.CurrentWorld is NestedSpawnerWorld).IsTrue();
            AssertThat(
                    late.GameSession.CurrentWorld!.GetNode("NestedRoot")
                        .HasNode("ExistingNestedActor")
                )
                .IsTrue();
        }
        finally
        {
            fixture.Close();
        }
    }

    [TestCase]
    public async Task GlobalTravelSupersedesPendingLateJoinReadiness()
    {
        GameSessionTestFixtures.NetworkFixture fixture = await GameSessionTestFixtures.Connect();
        try
        {
            PackedScene firstWorld = GD.Load<PackedScene>(
                "res://addons/game_session/tests/fixtures/WorldA.tscn"
            );
            PackedScene secondWorld = GD.Load<PackedScene>(
                "res://addons/game_session/tests/fixtures/WorldB.tscn"
            );
            AssertThat(fixture.Server.GameSession.Travel(firstWorld)).IsTrue();
            await WaitForActive(fixture);

            long latePeerId = 0;
            int lateReadyCount = 0;
            bool secondTravelStarted = false;
            fixture.Server.GameSession.PlayerJoined += playerState =>
            {
                if (
                    playerState.PeerId is not 1
                    && playerState.PeerId != fixture.ClientApi.GetUniqueId()
                )
                {
                    latePeerId = playerState.PeerId;
                }
            };
            fixture.Server.GameSession.PlayerWorldReady += playerState =>
            {
                if (playerState.PeerId == latePeerId)
                {
                    lateReadyCount++;
                }
            };

            GameSessionTestFixtures.LatePeer late = await fixture.JoinLate(
                beforeStart: lateSession =>
                    lateSession.GameSession.WorldLoaded += (_, _) =>
                    {
                        if (!secondTravelStarted)
                        {
                            secondTravelStarted = fixture.Server.GameSession.Travel(secondWorld);
                        }
                    }
            );
            for (int frame = 0; frame < 240; frame++)
            {
                await fixture.Pump();
                if (
                    fixture.Server.GameSession.State == GameSessionState.Active
                    && late.GameSession.State == GameSessionState.Active
                    && fixture.Server.GameSession.CurrentTravelId == 2
                )
                {
                    break;
                }
            }

            AssertThat(secondTravelStarted).IsTrue();
            AssertThat(latePeerId).IsGreater(1L);
            AssertThat(fixture.Server.GameSession.State).IsEqual(GameSessionState.Active);
            AssertThat(late.GameSession.State).IsEqual(GameSessionState.Active);
            AssertThat(lateReadyCount).IsEqual(1);

            late.GameSession.RpcId(
                1,
                nameof(GameSession.WorldReady),
                fixture.Server.GameSession.CurrentTravelId
            );
            await fixture.Pump(12);

            AssertThat(lateReadyCount).IsEqual(1);
        }
        finally
        {
            fixture.Close();
        }
    }

    [TestCase]
    public async Task DerivedPlayerStatePropertySurvivesTwoWorldTravels()
    {
        PackedScene playerStateScene = GD.Load<PackedScene>(ReplicatedPlayerStatePath);
        GameSessionTestFixtures.NetworkFixture fixture = await GameSessionTestFixtures.Connect(
            playerStateScene: playerStateScene
        );
        try
        {
            AssertThat(
                    fixture.Server.GameSession.TryGetPlayerStateByPeerId(
                        1,
                        out PlayerState? serverPlayer
                    )
                )
                .IsTrue();
            ReplicatedTestPlayerState state = (ReplicatedTestPlayerState)serverPlayer!;
            ulong instanceId = state.GetInstanceId();
            state.SelectionId = "red";

            for (int frame = 0; frame < 60; frame++)
            {
                await fixture.Pump();
                if (
                    fixture.Client.GameSession.TryGetPlayerStateByPeerId(
                        1,
                        out PlayerState? remotePlayer
                    )
                    && remotePlayer is ReplicatedTestPlayerState remoteState
                    && remoteState.SelectionId == "red"
                )
                {
                    break;
                }
            }

            PackedScene firstWorld = GD.Load<PackedScene>(
                "res://addons/game_session/tests/fixtures/WorldA.tscn"
            );
            AssertThat(fixture.Server.GameSession.Travel(firstWorld)).IsTrue();
            await WaitForActive(fixture);
            PackedScene secondWorld = GD.Load<PackedScene>(
                "res://addons/game_session/tests/fixtures/WorldB.tscn"
            );
            AssertThat(fixture.Server.GameSession.Travel(secondWorld)).IsTrue();
            await WaitForActive(fixture);

            AssertThat(
                    fixture.Server.GameSession.TryGetPlayerStateByPeerId(
                        1,
                        out PlayerState? afterPlayer
                    )
                )
                .IsTrue();
            ReplicatedTestPlayerState after = (ReplicatedTestPlayerState)afterPlayer!;
            AssertThat(after.GetInstanceId()).IsEqual(instanceId);
            AssertThat(after.SelectionId).IsEqual("red");

            AssertThat(
                    fixture.Client.GameSession.TryGetPlayerStateByPeerId(
                        1,
                        out PlayerState? remoteAfterPlayer
                    )
                )
                .IsTrue();
            ReplicatedTestPlayerState remoteAfter = (ReplicatedTestPlayerState)remoteAfterPlayer!;
            AssertThat(remoteAfter.SelectionId).IsEqual("red");
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
            int hostReadyCount = 0;
            fixture.Server.GameSession.PlayerWorldReady += playerState =>
            {
                if (playerState.PeerId == disconnectedPeerId)
                {
                    disconnectedParticipantBecameReady = true;
                }
                else if (playerState.PeerId == 1)
                {
                    hostReadyCount++;
                }
            };
            PackedScene world = GD.Load<PackedScene>(NestedWorldPath);

            AssertThat(fixture.Server.GameSession.Travel(world)).IsTrue();
            fixture.Client.Network.Stop();
            await fixture.Pump(24);

            AssertThat(fixture.Server.GameSession.State).IsEqual(GameSessionState.Active);
            AssertThat(fixture.Server.GameSession.PlayerStates.Count).IsEqual(1);
            AssertThat(disconnectedParticipantBecameReady).IsFalse();
            AssertThat(hostReadyCount).IsEqual(1);
            AssertThat(fixture.Server.GameSession.WorldContainer!.GetChildCount()).IsEqual(1);
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

            fixture.Client.GameSession.RpcId(1, nameof(GameSession.WorldReady), travelId);
            fixture.Client.GameSession.RpcId(1, nameof(GameSession.WorldReady), travelId - 1);
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
        for (int frame = 0; frame < 240; frame++)
        {
            await fixture.Pump();
            if (
                fixture.Server.GameSession.State == GameSessionState.Active
                && fixture.Client.GameSession.State == GameSessionState.Active
            )
            {
                break;
            }
        }

        if (
            fixture.Server.GameSession.State != GameSessionState.Active
            || fixture.Client.GameSession.State != GameSessionState.Active
        )
        {
            string diagnostic =
                $"server state={fixture.Server.GameSession.State}, "
                + $"ready={fixture.Server.GameSession.IsLocalWorldReady}, "
                + $"travel={fixture.Server.GameSession.CurrentTravelId}, "
                + $"players={fixture.Server.GameSession.PlayerStates.Count}; "
                + $"client state={fixture.Client.GameSession.State}, "
                + $"ready={fixture.Client.GameSession.IsLocalWorldReady}, "
                + $"travel={fixture.Client.GameSession.CurrentTravelId}, "
                + $"world={fixture.Client.GameSession.CurrentWorldPath}, "
                + $"peers={fixture.ServerApi.GetPeers().Length}";
            throw new System.InvalidOperationException($"Travel timed out: {diagnostic}");
        }

        AssertThat(fixture.Server.GameSession.State).IsEqual(GameSessionState.Active);
        AssertThat(fixture.Client.GameSession.State).IsEqual(GameSessionState.Active);
    }
}
