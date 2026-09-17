using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Rules;
using Godot;

[GlobalClass]
public partial class CarryItemRule : GameplayActionRule
{
    public override GameplayActionAvailability Evaluate(in GameplayActionContext context)
    {
        if (
            context.GetHost<ICarrier>() is ICarrier carrier
            && carrier.CarryComponent?.IsCarrying is true
        )
        {
            return new GameplayActionAllowed();
        }

        return new GameplayActionHidden();
    }
}
