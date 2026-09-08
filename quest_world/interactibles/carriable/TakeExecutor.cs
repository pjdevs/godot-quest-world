using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Actions;
using Godot;

[GlobalClass]
public partial class TakeExecutor : GameplayActionExecutor
{
    [Export]
    public CarriableItemDefinition? Item { get; set; }

    public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
    {
        ICarrier? carrier = context.GetInstigator<ICarrier>();
        Node3D? carriableObject = context.GetHost<Node3D>();
        if (carriableObject is null || carrier is null || Item is null || Item.Id.IsEmpty)
        {
            return new GameplayActionExecutionFailed("Carriable pickup context is incomplete.");
        }

        if (!carrier.TryTake(Item.Id, carriableObject))
        {
            return new GameplayActionExecutionFailed("Cannot carry item");
        }

        return new GameplayActionExecutionCompleted();
    }
}
