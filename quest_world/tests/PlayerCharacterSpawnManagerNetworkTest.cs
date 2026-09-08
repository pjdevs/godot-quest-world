namespace QuestWorld.Tests.Game;

using System.Threading.Tasks;
using GameplayActionPlugin.Runtime.Runner;
using GameSessionPlugin;
using GdUnit4;
using Godot;
using QuestWorld.Tests.GameSessionTests;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Network")]
public sealed class PlayerCharacterSpawnManagerNetworkTest
{
    [TestCase]
    public async Task ClientOwnedCharacterKeepsServerSubsystemAuthoritiesAfterSpawn()
    {
        GameSessionTestFixtures.NetworkFixture fixture = await GameSessionTestFixtures.Connect();
        try
        {
            fixture.Server.GameSession.WorldLoaded += (_, world) =>
            {
                World currentWorld = (World)world;
                currentWorld.AttachGameSession(fixture.Server.GameSession);
            };
            fixture.Client.GameSession.WorldLoaded += (_, world) =>
            {
                World currentWorld = (World)world;
                currentWorld.AttachGameSession(fixture.Client.GameSession);
            };

            PackedScene worldScene = GD.Load<PackedScene>(
                "res://quest_world/levels/test_world.tscn"
            );
            AssertThat(fixture.Server.GameSession.Travel(worldScene)).IsTrue();
            await WaitForActive(fixture);

            long clientPeerId = fixture.ClientApi.GetUniqueId();
            string playerPath = $"Players/Player_{clientPeerId}";
            await WaitForCharacters(fixture, playerPath);

            QuestWorldCharacter serverCharacter = (QuestWorldCharacter)
                fixture.Server.GameSession.CurrentWorld!.GetNode(playerPath);
            QuestWorldCharacter clientCharacter = (QuestWorldCharacter)
                fixture.Client.GameSession.CurrentWorld!.GetNode(playerPath);

            AssertAuthorityLayout(serverCharacter, clientPeerId);
            AssertAuthorityLayout(clientCharacter, clientPeerId);
        }
        finally
        {
            fixture.Close();
        }
    }

    private static void AssertAuthorityLayout(QuestWorldCharacter character, long clientPeerId)
    {
        AssertThat(character.OwnerPeerId).IsEqual((int)clientPeerId);
        AssertThat(character.GetMultiplayerAuthority()).IsEqual((int)clientPeerId);
        AssertThat(
                character
                    .GetNode<GameplayActionRunner>("GameplayActionRunner")
                    .GetMultiplayerAuthority()
            )
            .IsEqual(1);
        AssertThat(character.GetNode<CarryComponent>("CarryComponent").GetMultiplayerAuthority())
            .IsEqual(1);
        AssertThat(
                character
                    .GetNode<MultiplayerSynchronizer>("CarryComponent/MultiplayerSynchronizer")
                    .GetMultiplayerAuthority()
            )
            .IsEqual(1);
        AssertThat(
                character
                    .GetNode<MultiplayerSynchronizer>(
                        "InventoryComponent/InventoryReplicationSynchronizer"
                    )
                    .GetMultiplayerAuthority()
            )
            .IsEqual(1);
    }

    private static async Task WaitForCharacters(
        GameSessionTestFixtures.NetworkFixture fixture,
        string playerPath
    )
    {
        for (int frame = 0; frame < 120; frame++)
        {
            await fixture.Pump();
            if (
                fixture.Server.GameSession.CurrentWorld?.HasNode(playerPath) == true
                && fixture.Client.GameSession.CurrentWorld?.HasNode(playerPath) == true
            )
            {
                return;
            }
        }

        throw new System.InvalidOperationException(
            $"Player '{playerPath}' was not spawned on both peers."
        );
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
                return;
            }
        }

        throw new System.InvalidOperationException("World travel did not become active.");
    }
}
