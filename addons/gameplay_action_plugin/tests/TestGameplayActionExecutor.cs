namespace QuestWorld.Tests.GameplayActions;

using System;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;

public sealed partial class TestGameplayActionExecutor : GameplayActionExecutor
{
    public GameplayActionExecutionResult Result { get; set; } =
        new GameplayActionExecutionCompleted();

    public Exception? ExceptionToThrow { get; set; }

    public int ExecuteCount { get; private set; }

    public int CompletedCount { get; private set; }

    public int CancelledCount { get; private set; }

    public int FailedCount { get; private set; }

    public bool WasReservedWhenExecuted { get; private set; }

    public override GameplayActionExecutionResult Execute(in GameplayActionExecutionContext context)
    {
        ExecuteCount++;
        WasReservedWhenExecuted = context.Component.IsActionExecuting(
            context.Action.Definition!.Id
        );
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Result;
    }

    protected internal override void OnExecutionCompleted(
        in GameplayActionExecutionContext context
    ) => CompletedCount++;

    protected internal override void OnExecutionCancelled(
        in GameplayActionExecutionContext context,
        string reason
    ) => CancelledCount++;

    protected internal override void OnExecutionFailed(
        in GameplayActionExecutionContext context,
        string reason
    ) => FailedCount++;
}
