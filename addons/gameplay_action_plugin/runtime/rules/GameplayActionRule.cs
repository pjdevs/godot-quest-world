using Godot;

namespace QuestWorld.GameplayActions.Runtime.Rules;

[GlobalClass]
public abstract partial class GameplayActionRule : Resource
{
    public abstract GameplayActionAvailability Evaluate(in GameplayActionContext context);
}
