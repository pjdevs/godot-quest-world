using System.Collections.Generic;
using System.Linq;
using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Presentation.UI;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Runner;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Runtime.Actions;

[GlobalClass]
public partial class ActionPresenter : CanvasLayer
{
    [Export]
    public GameplayActionRunner? ActionRunner { get; set; } = null;

    [Export]
    public Control? ActionContainer { get; set; } = null;

    [Export]
    public PackedScene? ActionScene { get; set; } = null;

    private Dictionary<StringName, Control> _presentedActions = new();

    public override void _Process(double delta)
    {
        if (ActionRunner is null)
        {
            return;
        }

        IEnumerable<GameplayAction> relevantActions = ActionRunner
            .GetBindings()
            .Where(binding =>
                ActionRunner.GetBindingAvailability(binding.Id) is not GameplayActionHidden
            )
            .Select(binding => binding.Component.ResolveAction(binding.ActionId))
            .OfType<GameplayAction>()
            .Where(action => action is not InteractionAction)
            .Where(action => action.Definition is not null);
        HashSet<StringName> relevantActionIds = [];
        foreach (var action in relevantActions)
        {
            if (!_presentedActions.ContainsKey(action.Definition!.Id))
            {
                if (ActionScene is null || ActionContainer is null)
                {
                    continue;
                }

                Control? actionControl = ActionScene.Instantiate<Control>();
                if (actionControl is null)
                {
                    continue;
                }

                if (actionControl is IGameplayActionWidget actionWidget)
                {
                    // TODO: Bind the presentation of the action
                    // (not available for simple GameplayAction for now, only InteractionAction)
                    var binding = ActionRunner
                        .GetBindings()
                        .FirstOrDefault(b => b.ActionId == action.Definition.Id);
                    actionWidget.Bind(
                        new GameplayActionPresentation(
                            action.Definition.Id,
                            action.Definition.Label,
                            action.Definition.Description,
                            binding?.InputActionName ?? "interact",
                            new GameplayActionAllowed(),
                            binding?.ActivationMode ?? GameplayActionActivationMode.Press
                        ),
                        null
                    );
                }

                ActionContainer.AddChild(actionControl);
                _presentedActions[action.Definition.Id] = actionControl;
            }

            relevantActionIds.Add(action.Definition.Id);
        }

        foreach (var actionId in _presentedActions.Keys)
        {
            if (!relevantActionIds.Contains(actionId))
            {
                Node actionControl = _presentedActions[actionId];
                ActionContainer?.RemoveChild(actionControl);
                actionControl.QueueFree();
                _presentedActions.Remove(actionId);
            }
        }
    }
}
