using System.Threading.Tasks;
using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Actions;
using Godot;

[GlobalClass]
public partial class DropExecutor : GameplayActionExecutor
{
    private GameplayActionContext? _context = null;
    private ICarrier? _carrier = null;

    public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
    {
        ICarrier? carrier = context.GetHost<ICarrier>();
        if (carrier is null || carrier.CarryComponent is null)
        {
            return new GameplayActionExecutionFailed("Carriable drop context is incomplete.");
        }

        carrier.CarryComponent.DropOperationFailed += OnDropOperationFailed;
        carrier.CarryComponent.DropOperationFinished += OnDropOperationFinished;

        if (!carrier.CarryComponent.TryStartDrop())
        {
            return new GameplayActionExecutionFailed("TryStartTake failed.");
        }

        _context = context;
        _carrier = carrier;

        return new GameplayActionExecutionRunning();
    }

    private void OnDropOperationFinished()
    {
        _context?.CompleteExecution();
    }

    private void OnDropOperationFailed(string reason)
    {
        _context?.FailExecution(reason);
    }

    protected internal override void OnExecutionCancelled(
        in GameplayActionContext context,
        string reason
    )
    {
        _carrier?.CarryComponent?.CancelCurrentOperation();
    }
}
