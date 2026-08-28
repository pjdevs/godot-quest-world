using System.Linq;
using Godot;

namespace QuestWorld.Interaction.Presentation.UI;

/// <summary>Default prompt of one action, showing its input when allowed or its reason when blocked.</summary>
public partial class InteractionActionPromptWidget : PanelContainer, IInteractionActionWidget
{
    [Export]
    public Label? ActionNameLabel { get; set; }

    [Export]
    public Label? ActionKeyLabel { get; set; }

    [Export]
    public ProgressBar? ActionProgress { get; set; }

    public override void _Ready() { }

    /// <inheritdoc />
    public void Bind(in InteractionActionPresentation presentation)
    {
        string actionKey =
            InputMap.ActionGetEvents(presentation.InputActionName).FirstOrDefault()?.AsText()
            ?? "???";
        ActionKeyLabel?.SetText($"{actionKey}");

        if (presentation.IsAllowed)
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
