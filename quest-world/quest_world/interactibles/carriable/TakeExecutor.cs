using System;
using System.Threading.Tasks;
using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Actions;
using Godot;

[GlobalClass]
public partial class TakeExecutor : GameplayActionExecutor
{
    // TODO Move this on interactible object with ICarriable interface providing Item
    [Export]
    public CarriableItemDefinition? Item { get; set; }

    private GameplayActionContext? _context = null;
    private ICarrier? _carrier = null;

    public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
    {
        ICarrier? carrier = context.GetHost<ICarrier>();
        Node3D? carriableItemObject = context.GetTarget<Node3D>();
        if (
            carriableItemObject is null
            || carrier is null
            || carrier.CarryComponent is null
            || Item is null
            || Item.Id.IsEmpty
        )
        {
            return new GameplayActionExecutionFailed("Carriable pickup context is incomplete.");
        }

        carrier.CarryComponent.TakeOperationCommitted += OnTakeOperationCommited;
        carrier.CarryComponent.TakeOperationFailed += OnTakeOperationFailed;
        carrier.CarryComponent.TakeOperationFinished += OnTakeOperationFinished;

        if (!carrier.CarryComponent.TryStartTake(Item.Id, carriableItemObject))
        {
            return new GameplayActionExecutionFailed("TryStartTake failed.");
        }

        _context = context;
        _carrier = carrier;

        return new GameplayActionExecutionRunning();
    }

    private void OnTakeOperationFinished()
    {
        _context?.CompleteExecution();
    }

    private void OnTakeOperationFailed(string reason)
    {
        _context?.FailExecution(reason);
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
    }
}
