#if !GODOT_WINDOWS

namespace QuestWorld.Tests.GameplayActions;

using System.Threading.Tasks;
using GameplayActionPlugin.Runtime.Runner;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

public sealed partial class GameplayActionRunnerNetworkTest
{
    [TestCase]
    public async Task InvalidTargetPathIsRejectedByTheAuthority()
    {
        Session session = await Connect(serverAllowsAccess: true);
        try
        {
            int rejections = 0;
            session.Client.Runner.GameplayActionRejected += (_, actionId, _) =>
            {
                AssertThat(actionId).IsEqual(OpenAction);
                rejections++;
            };

            session.Client.Runner.RpcId(
                1,
                nameof(GameplayActionRunner.ServerTryStartAction),
                new NodePath("Door/Actions"),
                OpenAction,
                new NodePath("Door/AccessSource"),
                new NodePath("Door/UnknownTarget")
            );
            await session.Pump(RoundTripFrames);

            AssertThat(session.Server.Executor.ExecuteCount).IsEqual(0);
            AssertThat(rejections).IsEqual(1);
        }
        finally
        {
            session.Close();
        }
    }
}

#endif
