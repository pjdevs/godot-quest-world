using System.Collections.Generic;
using Godot;

namespace QuestWorld.GameplayActions.Runtime.Runner;

internal sealed class GameplayActionGesturePlan(
    StringName input,
    IReadOnlyList<ulong> candidateIds,
    float longestHoldDuration
)
{
    public StringName Input { get; } = input;

    public IReadOnlyList<ulong> CandidateIds { get; } = candidateIds;

    public float LongestHoldDuration { get; } = longestHoldDuration;

    public float Elapsed { get; set; }
}
