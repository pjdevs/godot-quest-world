using System.Linq;
using Godot;
using QuestWorld.GameplayActions;

namespace QuestWorld.GameplayActions.Presentation.UI;

/// <summary>Default prompt showing an action input and its allowed or blocked state.</summary>
public partial class GameplayActionPromptWidget : PanelContainer, IGameplayActionWidget
{
    /// <summary>Gets or sets the label displaying the action name and optional refusal reason.</summary>
    [Export]
    public Label? ActionNameLabel { get; set; }

    /// <summary>Gets or sets the label displaying the mapped input event.</summary>
    [Export]
    public Label? ActionKeyLabel { get; set; }

    /// <summary>Gets or sets the optional progress bar used for hold-selection progress.</summary>
    [Export]
    public ProgressBar? ActionProgress { get; set; }

    public override void _Ready() { }

    /// <inheritdoc />
    public void Bind(
        in GameplayActionPresentation presentation,
        GameplayActionExecutionPresentation? execution
    )
    {
        string actionKey =
            InputMap.ActionGetEvents(presentation.InputActionName).FirstOrDefault()?.AsText()
            ?? "???";
        ActionKeyLabel?.SetText($"{actionKey}");

        bool requestedLocally =
            execution?.Relation == GameplayActionExecutionRelation.RequestedLocally;
        if (presentation.IsAllowed || requestedLocally)
        {
            ActionNameLabel?.SetText($"{presentation.Label}");
            ActionNameLabel?.RemoveThemeColorOverride("font_color");
            ActionKeyLabel?.RemoveThemeColorOverride("font_color");
        }
        else
        {
            ActionNameLabel?.SetText($"{presentation.Label}: {presentation.BlockReason}");
            ActionNameLabel?.AddThemeColorOverride("font_color", Colors.Red);
            ActionKeyLabel?.AddThemeColorOverride("font_color", Colors.Red);
        }

        if (ActionProgress is not null)
        {
            ActionProgress.Visible = presentation.IsHoldable;
            ActionProgress.SetValue(presentation.HoldProgress ?? 0.0f);
        }
    }
}
