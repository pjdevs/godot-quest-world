using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Actions;
using Godot;

[GlobalClass]
public partial class TakeExecutor : GameplayActionExecutor
{
    public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
    {
        ICarrier? carrier = context.GetHost<ICarrier>();
        Carriable? carriableItemObject = context.GetTarget<Carriable>();
        if (
            carriableItemObject is null
            || carrier is null
            || carrier.CarryComponent is null
            || carriableItemObject.Item is null
            || carriableItemObject.Item.Id.IsEmpty
        )
        {
            return new GameplayActionExecutionFailed("Carriable pickup context is incomplete.");
        }

        CarryOperation? operation = carrier.CarryComponent.TryStartTake(
            carriableItemObject.Item.Id,
            carriableItemObject
        );
        if (operation is null)
        {
            return new GameplayActionExecutionFailed("TryStartTake failed.");
        }

        GameplayActionContext executionContext = context;

        void Cleanup()
        {
            operation.Committed -= OnTakeOperationCommitted;
            operation.Finished -= OnTakeOperationFinished;
            operation.Failed -= OnTakeOperationFailed;
            operation.Cancelled -= OnTakeOperationCancelled;
        }

        void OnTakeOperationCommitted() => executionContext.ReleaseRequesterDependency();

        void OnTakeOperationFinished()
        {
            executionContext.CompleteExecution();
            Cleanup();
        }

        void OnTakeOperationFailed(string reason)
        {
            executionContext.FailExecution(reason);
            Cleanup();
        }

        void OnTakeOperationCancelled() => Cleanup();

        operation.Committed += OnTakeOperationCommitted;
        operation.Finished += OnTakeOperationFinished;
        operation.Failed += OnTakeOperationFailed;
        operation.Cancelled += OnTakeOperationCancelled;

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
