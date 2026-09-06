namespace QuestWorld.Tests;

using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Execution;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Runtime.Actions;

/// <summary>Executor whose single outcome and duration a test writes before the command runs.</summary>
/// <remarks>
/// The acknowledgement tests are about what the authority reports, not about what a gameplay script
/// does, so the executor is reduced to the one decision that changes the protocol: which of the four
/// results it returns.
/// </remarks>
public sealed partial class TestScriptedExecutor : InteractionActionExecutor
{
    private readonly TimedExecution _timedExecution = new();

    public GameplayActionExecutionResult Result { get; set; } =
        new GameplayActionExecutionCompleted();

    public float? Duration { get; set; }

    public ulong LastExecutionId { get; private set; }

    public int ExecuteCount { get; private set; }

    public override GameplayActionExecutionResult Execute(in InteractionExecutionContext context)
    {
        ExecuteCount++;
        LastExecutionId = context.ExecutionId;

        // A deadline reaches the core only through the query below, so a scripted running result is
        // turned into one here — which is what any executor with an authored length now does.
        if (Result is not GameplayActionExecutionRunning || !Duration.HasValue)
        {
            return Result;
        }

        return
            _timedExecution.Start(
                context.Interactive.ActionComponent!,
                context.ExecutionId,
                Duration.Value
            ) == TimedExecutionStartResult.Started
            ? Running()
            : new GameplayActionExecutionFailed("The scripted timer could not start.");
    }

    internal override GameplayActionProgressSample? GetInteractionPredictionSample(
        in InteractionContext context
    ) => Duration.HasValue ? TimedExecution.BuildPredictionSample(Duration.Value) : null;

    protected internal override void OnExecutionCompleted(in InteractionExecutionContext context)
    {
        _timedExecution.Stop(context.ExecutionId);
    }

    protected internal override void OnExecutionCancelled(
        in InteractionExecutionContext context,
        string reason
    )
    {
        _timedExecution.Stop(context.ExecutionId);
    }

    protected internal override void OnExecutionFailed(
        in InteractionExecutionContext context,
        string reason
    )
    {
        _timedExecution.Stop(context.ExecutionId);
    }
}
