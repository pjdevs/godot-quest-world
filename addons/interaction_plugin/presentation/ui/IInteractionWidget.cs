namespace QuestWorld.Interaction.Presentation.UI;

/// <summary>Contract implemented by target-level controls such as prompt containers and indications.</summary>
public interface IInteractionWidget
{
    /// <summary>Refreshes the local widget from the latest target presentation.</summary>
    /// <param name="presentation">Immutable snapshot produced for the local interactor.</param>
    void Bind(in InteractionTargetPresentation presentation);
}
