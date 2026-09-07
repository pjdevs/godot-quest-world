using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;

[GlobalClass]
public partial class DropExecutor : GameplayActionExecutor
{
    [Export]
    public CarriableItemDefinition? Item { get; set; }

    public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
    {
        ICarrier? carrier = context.GetInstigator<ICarrier>();
        if (carrier is null || Item is null || Item.Id.IsEmpty)
        {
            return new GameplayActionExecutionFailed("Carriable drop context is incomplete.");
        }

        if (!carrier.TryDrop(Item.Id))
        {
            return new GameplayActionExecutionFailed("The carriable could not be spawned.");
        }

        return new GameplayActionExecutionCompleted();
    }
}
