using Godot;

namespace QuestWorld.GameplayActions.Runtime.Rules;

/// <summary>Pure ordered availability rule evaluated before an action is reserved.</summary>
[GlobalClass]
public abstract partial class GameplayActionRule : Resource
{
    /// <summary>Evaluates current availability from the supplied action context.</summary>
    /// <remarks>Rules should query gameplay state without mutating it.</remarks>
    public abstract GameplayActionAvailability Evaluate(in GameplayActionContext context);
}
