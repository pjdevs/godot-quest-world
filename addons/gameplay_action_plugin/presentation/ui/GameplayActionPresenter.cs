using System.Collections.Generic;
using Godot;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Bindings;
using QuestWorld.GameplayActions.Runtime.Runner;

namespace QuestWorld.GameplayActions.Presentation.UI;

/// <summary>Presents the locally owned, manually triggered actions of one gameplay runner.</summary>
/// <remarks>
/// Bindings are the identity of the view: two bindings of one action get two controls, and a binding
/// replacement cannot update a stale control. The component ownership check keeps external actions in
/// the presenter that owns their gameplay context instead of classifying integrations by their type.
/// </remarks>
[GlobalClass]
public partial class GameplayActionPresenter : CanvasLayer
{
    /// <summary>Gets or sets the locally controlled runner whose owned bindings are presented.</summary>
    [Export]
    public GameplayActionRunner? ActionRunner { get; set; }

    /// <summary>Gets or sets the container receiving one control per visible owned binding.</summary>
    [Export]
    public Control? ActionContainer { get; set; }

    /// <summary>Gets or sets the scene instantiated for each presented binding.</summary>
    [Export]
    public PackedScene? ActionScene { get; set; }

    private readonly Dictionary<ulong, Control> _presentedActions = new();
    private readonly HashSet<ulong> _relevantBindingIds = new();
    private readonly List<ulong> _staleBindingIds = new();

    /// <summary>Reconciles the binding-driven view with current local availability each frame.</summary>
    public override void _Process(double delta)
    {
        if (
            ActionRunner is null
            || ActionContainer is null
            || ActionScene is null
            || !ActionRunner.IsLocallyControlled
        )
        {
            ClearPresentation();
            return;
        }

        _relevantBindingIds.Clear();
        foreach (GameplayActionBinding binding in ActionRunner.GetBindings())
        {
            if (
                binding.Component != ActionRunner.OwnedActionComponent
                || binding.ActivationMode == GameplayActionActivationMode.Automatic
                || ActionRunner.GetBindingAvailability(binding.Id) is GameplayActionHidden
            )
            {
                continue;
            }

            GameplayAction? action = binding.Component.ResolveAction(binding.ActionId);
            if (action?.Definition is null)
            {
                continue;
            }

            _relevantBindingIds.Add(binding.Id);
            if (!_presentedActions.TryGetValue(binding.Id, out Control? actionControl))
            {
                actionControl = ActionScene.Instantiate<Control>();
                if (actionControl is null)
                {
                    continue;
                }

                ActionContainer.AddChild(actionControl);
                _presentedActions.Add(binding.Id, actionControl);
            }

            BindAction(binding, action);
        }

        _staleBindingIds.Clear();
        foreach (ulong bindingId in _presentedActions.Keys)
        {
            if (!_relevantBindingIds.Contains(bindingId))
            {
                _staleBindingIds.Add(bindingId);
            }
        }

        foreach (ulong bindingId in _staleBindingIds)
        {
            RemoveAction(bindingId);
        }
    }

    private void BindAction(GameplayActionBinding binding, GameplayAction action)
    {
        if (_presentedActions[binding.Id] is not IGameplayActionWidget widget)
        {
            return;
        }

        GameplayActionAvailability availability = ActionRunner!.GetBindingAvailability(binding.Id);
        float? holdProgress = null;
        float? holdElapsed = null;
        if (
            ActionRunner.TryGetBindingHoldProgress(
                binding.Id,
                out float currentProgress,
                out float currentElapsed
            )
        )
        {
            holdProgress = currentProgress;
            holdElapsed = currentElapsed;
        }

        GameplayActionPresentation presentation = new(
            action.Definition!.Id,
            action.Definition.Label,
            action.Definition.Description,
            binding.InputActionName,
            availability,
            binding.ActivationMode,
            holdProgress,
            holdElapsed
        );
        GameplayActionExecutionPresentation? execution =
            binding.Component.TryGetExecutionPresentation(
                binding.ActionId,
                out GameplayActionExecutionPresentation currentExecution
            )
                ? currentExecution
                : null;
        widget.Bind(presentation, execution);
    }

    private void ClearPresentation()
    {
        _staleBindingIds.Clear();
        _staleBindingIds.AddRange(_presentedActions.Keys);
        foreach (ulong bindingId in _staleBindingIds)
        {
            RemoveAction(bindingId);
        }
    }

    private void RemoveAction(ulong bindingId)
    {
        if (!_presentedActions.Remove(bindingId, out Control? actionControl))
        {
            return;
        }

        if (actionControl.GetParent() is Node parent)
        {
            parent.RemoveChild(actionControl);
        }

        actionControl.QueueFree();
    }
}
