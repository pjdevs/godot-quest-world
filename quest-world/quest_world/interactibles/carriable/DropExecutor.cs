using System.Threading.Tasks;
using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Actions;
using Godot;

[GlobalClass]
public partial class DropExecutor : GameplayActionExecutor
{
    private Task<bool>? _currentTakeTask = null;

    public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
    {
        ICarrier? carrier = context.GetHost<ICarrier>();
        if (carrier is null || !carrier.IsCarrying)
        {
            return new GameplayActionExecutionFailed("Carriable pickup context is incomplete.");
        }

        _currentTakeTask = WaitForDropCompletion(context, carrier);

        return new GameplayActionExecutionRunning();
    }

    private async Task<bool> WaitForDropCompletion(GameplayActionContext context, ICarrier carrier)
    {
        bool result = await carrier.TryDropAsync(() => context.ReleaseRequesterDependency());

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
