#if TOOLS

using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Examples.Interactive;
using QuestWorld.Interaction.Presentation.UI;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Interaction.Runtime.State;

namespace InteractionPlugin.Editor;

public static class InteractionValidator
{
    public static IEnumerable<string> Validate(GodotObject obj)
    {
        if (obj is InteractiveComponent interactive)
        {
            if (interactive.InteractionArea is null)
                yield return "InteractionArea must be assigned.";

            if (interactive.InteractionOwner is null)
                yield return "InteractionOwner must be assigned.";
            else if (interactive.InteractionOwner is not IInteractionHandler)
                yield return "InteractionOwner must implement IInteractionHandler.";
        }

        if (obj is InteractionInteractor interactor)
        {
            if (interactor.ViewOrigin is null)
                yield return "ViewOrigin must be assigned.";
        }

        if (obj is InteractionStateful stateful)
        {
            if (stateful.Interactive is null)
                yield return "Interactive must be assigned.";
        }

        if (obj is InteractionPresenter presenter)
        {
            if (presenter.Interactor is null)
                yield return "Interactor must be assigned.";

            if (presenter.Camera is null)
                yield return "Camera must be assigned.";
        }

        if (obj is InteractiveActor actor)
        {
            if (actor.Interactive is null)
                yield return "Interactive must be assigned.";

            if (actor.Stateful is null)
                yield return "Stateful must be assigned.";
        }
    }
}

#endif
