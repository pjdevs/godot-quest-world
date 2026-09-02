using System.Collections.Generic;
using Godot;

namespace QuestWorld.GameplayActions.Runtime.Runner;

public partial class GameplayActionRunner
{
    /// <summary>
    /// Returns inputs whose press cycle is still owned by the runner, even if their original binding
    /// disappeared. Integrations use this to keep forwarding the matching release without duplicating
    /// gesture state outside the runner.
    /// </summary>
    public IReadOnlyList<StringName> GetConsumedInputs() => _gestures.GetConsumedInputs();
}
