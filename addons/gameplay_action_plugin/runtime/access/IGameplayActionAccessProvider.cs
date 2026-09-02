using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Runner;

namespace QuestWorld.GameplayActions.Runtime.Access;

public readonly record struct GameplayActionAccessContext(
    GameplayActionRunner Runner,
    GameplayActionComponent Component,
    GameplayAction Action
);

public interface IGameplayActionAccessProvider
{
    bool CanRequest(in GameplayActionAccessContext context);

    bool HasSustainedAccess(in GameplayActionAccessContext context) => CanRequest(context);
}
