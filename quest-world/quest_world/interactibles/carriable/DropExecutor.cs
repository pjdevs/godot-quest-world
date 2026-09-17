using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Actions;
using Godot;

[GlobalClass]
public partial class DropExecutor : GameplayActionExecutor
{
    public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
    {
        ICarrier? carrier = context.GetHost<ICarrier>();
        if (carrier is null || carrier.CarryComponent is null)
        {
            return new GameplayActionExecutionFailed("Carriable drop context is incomplete.");
        }

        CarryOperation? operation = carrier.CarryComponent.TryStartDrop();
        if (operation is null)
        {
            return new GameplayActionExecutionFailed("TryStartDrop failed.");
        }

        GameplayActionContext executionContext = context;

        void Cleanup()
        {
            operation.Finished -= OnDropOperationFinished;
            operation.Failed -= OnDropOperationFailed;
            operation.Cancelled -= OnDropOperationCancelled;
        }

        void OnDropOperationFinished()
        {
            executionContext.CompleteExecution();
            Cleanup();
        }

        void OnDropOperationFailed(string reason)
        {
            executionContext.FailExecution(reason);
            Cleanup();
        }

        void OnDropOperationCancelled() => Cleanup();

        operation.Finished += OnDropOperationFinished;
        operation.Failed += OnDropOperationFailed;
        operation.Cancelled += OnDropOperationCancelled;

        return new GameplayActionExecutionRunning();
    }

    protected internal override void OnExecutionCancelled(
        in GameplayActionContext context,
        string reason
    )
    {
        context.GetHost<ICarrier>()?.CarryComponent?.CancelCurrentOperation();
    }
}
