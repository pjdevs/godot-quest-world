namespace QuestWorld.Interaction;

public interface IInteractionHandler
{
    InteractionStatus EvaluateCustomInteractionStatus(in InteractionContext context);

    void OnStartInteractionInput(in InteractionContext context);

    void OnEndInteractionInput(in InteractionContext context);
}

public interface IInteractionStateHandler
{
    void OnInteractionStateChangedAuthority(InteractionState oldState, InteractionState newState);

    void OnInteractionStateChangedPresentation(InteractionState oldState, InteractionState newState);
}
