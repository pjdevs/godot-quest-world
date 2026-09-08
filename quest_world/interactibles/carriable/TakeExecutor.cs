using System.Threading.Tasks;
using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Actions;
using Godot;

[GlobalClass]
public partial class TakeExecutor : GameplayActionExecutor
{
    [Export]
    public CarriableItemDefinition? Item { get; set; }

    private ulong _executionId = 0;
    private GameplayActionComponent? _component = null;
    private Task<bool>? _currentTakeTask = null;

    public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
    {
        ICarrier? carrier = context.GetInstigator<ICarrier>();
        Node3D? carriableObject = context.GetHost<Node3D>();
        if (carriableObject is null || carrier is null || Item is null || Item.Id.IsEmpty)
        {
            return new GameplayActionExecutionFailed("Carriable pickup context is incomplete.");
        }

        _executionId = context.ExecutionId;
        _component = context.Component;
        _currentTakeTask = WaitForTakeCompletion(carrier, Item.Id, carriableObject);

        return new GameplayActionExecutionRunning();
    }

    private async Task<bool> WaitForTakeCompletion(
        ICarrier carrier,
        StringName itemId,
        Node3D carriableObject
    )
    {
        bool result = await carrier.TryTakeAsync(itemId, carriableObject);

        if (result)
        {
            _component?.CompleteExecution(_executionId);
        }
        else
        {
            _component?.FailExecution(_executionId, "Could not take carriable object.");
        }

        _executionId = 0;
        _component = null;
        _currentTakeTask = null;

        return result;
    }
}
