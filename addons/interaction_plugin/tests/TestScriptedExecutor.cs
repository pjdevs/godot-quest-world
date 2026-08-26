namespace QuestWorld.Tests;

using Godot;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Runtime.Actions;

/// <summary>Executor whose single outcome and duration a test writes before the command runs.</summary>
/// <remarks>
/// The acknowledgement tests are about what the authority reports, not about what a gameplay script
/// does, so the executor is reduced to the one decision that changes the protocol: which of the four
/// results it returns.
/// </remarks>
internal sealed partial class TestScriptedExecutor : InteractionActionExecutor
{
    public InteractionExecutionResult Result { get; set; } = new InteractionExecutionCompleted();

    public float Duration { get; set; }

    public ulong LastExecutionId { get; private set; }

    public int ExecuteCount { get; private set; }

    public override float ExpectedDuration => Duration;

    public override InteractionExecutionResult Execute(in InteractionExecutionContext context)
    {
        ExecuteCount++;
        LastExecutionId = context.ExecutionId;
        return Result;
    }
}
