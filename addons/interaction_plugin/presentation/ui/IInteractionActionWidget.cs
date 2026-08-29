namespace QuestWorld.Interaction.Presentation.UI;

/// <summary>Contract implemented by controls presenting a single action of the focused target.</summary>
/// <remarks>
/// One instance exists per presented action. The widget shows the allowed or blocked state of that
/// action alone and never summarizes the target.
/// </remarks>
public interface IInteractionActionWidget
{
    /// <summary>Refreshes the widget from its action and optional execution snapshots.</summary>
    /// <param name="action">Immutable snapshot of the single action shown by this widget.</param>
    /// <param name="execution">Matching execution on the target, or null when none is visible.</param>
    void Bind(in InteractionActionPresentation action, InteractionExecutionPresentation? execution);
}
