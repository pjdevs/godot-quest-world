using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Actions;
using Godot;

[GlobalClass]
public partial class TakeExecutor : GameplayActionExecutor
{
    private GameplayActionContext? _context = null;
    private ICarrier? _carrier = null;

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

        carrier.CarryComponent.TakeOperationCommitted += OnTakeOperationCommited;
        carrier.CarryComponent.TakeOperationFailed += OnTakeOperationFailed;
        carrier.CarryComponent.TakeOperationFinished += OnTakeOperationFinished;

        if (!carrier.CarryComponent.TryStartTake(carriableItemObject.Item.Id, carriableItemObject))
        {
            Cleanup();
            return new GameplayActionExecutionFailed("TryStartTake failed.");
        }

        _context = context;
        _carrier = carrier;

        return new GameplayActionExecutionRunning();
    }

    private void OnTakeOperationFinished()
    {
        _context?.CompleteExecution();
        Cleanup();
    }

    private void OnTakeOperationFailed(string reason)
    {
        _context?.FailExecution(reason);
        Cleanup();
    }

    private void OnTakeOperationCommited()
    {
        _context?.ReleaseRequesterDependency();
    }

    protected internal override void OnExecutionCancelled(
        in GameplayActionContext context,
        string reason
    )
    {
        _carrier?.CarryComponent?.CancelCurrentOperation();
        Cleanup();
    }

    private void Cleanup()
    {
        if (_carrier?.CarryComponent is not null)
        {
            _carrier.CarryComponent.TakeOperationCommitted -= OnTakeOperationCommited;
            _carrier.CarryComponent.TakeOperationFailed -= OnTakeOperationFailed;
            _carrier.CarryComponent.TakeOperationFinished -= OnTakeOperationFinished;
        }

        _context = null;
        _carrier = null;
    }
}
