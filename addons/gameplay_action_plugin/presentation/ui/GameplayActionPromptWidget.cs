using System.Linq;
using Godot;
using QuestWorld.GameplayActions;

namespace QuestWorld.GameplayActions.Presentation.UI;

/// <summary>Default prompt showing an action input and its allowed or blocked state.</summary>
public partial class GameplayActionPromptWidget : PanelContainer, IGameplayActionWidget
{
    [Export]
    public Label? ActionNameLabel { get; set; }

    [Export]
    public Label? ActionKeyLabel { get; set; }

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
