using System.Threading.Tasks;
using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Actions;
using Godot;

[GlobalClass]
public partial class TakeExecutor : GameplayActionExecutor
{
    [Export]
    public CarriableItemDefinition? Item { get; set; }
    private Task<bool>? _currentTakeTask = null;

    public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
    {
        ICarrier? carrier = context.GetHost<ICarrier>();
        Node3D? carriableObject = context.GetTarget<Node3D>();
        if (carriableObject is null || carrier is null || Item is null || Item.Id.IsEmpty)
        {
            return new GameplayActionExecutionFailed("Carriable pickup context is incomplete.");
        }

        _currentTakeTask = WaitForTakeCompletion(context, carrier, Item.Id, carriableObject);

        return new GameplayActionExecutionRunning();
    }

    private async Task<bool> WaitForTakeCompletion(
        GameplayActionContext context,
        ICarrier carrier,
        StringName itemId,
        Node3D carriableObject
    )
    {
        bool result = await carrier.TryTakeAsync(
            itemId,
            carriableObject,
            () => context.ReleaseRequesterDependency()
        );

        if (result)
        {
            context.CompleteExecution();
        }
        else
        {
            context.FailExecution("Could not take carriable object.");
        }

        _currentTakeTask = null;

        return result;
    }
}
