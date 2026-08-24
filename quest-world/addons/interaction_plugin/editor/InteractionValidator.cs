#if TOOLS

using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction.Examples.Interactive;
using QuestWorld.Interaction.Presentation.UI;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Interaction.Runtime.State;

namespace InteractionPlugin.Editor;

public static class InteractionValidator
{
    private enum InspectableType
    {
        None,
        InteractiveComponent,
        InteractionInteractor,
        InteractionStateful,
        InteractionPresenter,
        InteractiveActor,
    }

    public static bool CanHandle(GodotObject obj) => ResolveType(obj) != InspectableType.None;

    public static IEnumerable<string> Validate(GodotObject obj)
    {
        switch (ResolveType(obj))
        {
            case InspectableType.InteractiveComponent:
                if (GetObject(obj, "InteractionArea") is null)
                    yield return "InteractionArea must be assigned.";

                if (GetObject(obj, "InteractionAnchor") is null)
                    yield return "InteractionAnchor must be assigned.";

                break;
            case InspectableType.InteractionInteractor:
                if (GetObject(obj, "ViewOrigin") is null)
                    yield return "ViewOrigin must be assigned.";

                break;
            case InspectableType.InteractionPresenter:
                if (GetObject(obj, "Interactor") is null)
                    yield return "Interactor must be assigned.";

                if (GetObject(obj, "Camera") is null)
                    yield return "Camera must be assigned.";

                break;
            case InspectableType.InteractiveActor:
                if (GetObject(obj, "Interactive") is null)
                    yield return "Interactive must be assigned.";

                if (GetObject(obj, "Stateful") is null)
                    yield return "Stateful must be assigned.";

                break;
        }
    }

    private static InspectableType ResolveType(GodotObject obj)
    {
        InspectableType managedType = obj switch
        {
            InteractiveComponent => InspectableType.InteractiveComponent,
            InteractionInteractor => InspectableType.InteractionInteractor,
            InteractionStateful => InspectableType.InteractionStateful,
            InteractionPresenter => InspectableType.InteractionPresenter,
            InteractiveActor => InspectableType.InteractiveActor,
            _ => InspectableType.None,
        };
        if (managedType != InspectableType.None)
        {
            return managedType;
        }

        Script? script = GetAttachedScript(obj);
        string globalName = script?.GetGlobalName().ToString() ?? string.Empty;
        return globalName switch
        {
            nameof(InteractiveComponent) => InspectableType.InteractiveComponent,
            nameof(InteractionInteractor) => InspectableType.InteractionInteractor,
            nameof(InteractionStateful) => InspectableType.InteractionStateful,
            nameof(InteractionPresenter) => InspectableType.InteractionPresenter,
            nameof(InteractiveActor) => InspectableType.InteractiveActor,
            _ => ResolveTypeFromPath(script?.ResourcePath),
        };
    }

    private static InspectableType ResolveTypeFromPath(string? path)
    {
        return path switch
        {
            "res://addons/interaction_plugin/runtime/interactive/InteractiveComponent.cs" =>
                InspectableType.InteractiveComponent,
            "res://addons/interaction_plugin/runtime/interactor/InteractionInteractor.cs" =>
                InspectableType.InteractionInteractor,
            "res://addons/interaction_plugin/runtime/state/InteractionStateful.cs" =>
                InspectableType.InteractionStateful,
            "res://addons/interaction_plugin/presentation/ui/InteractionPresenter.cs" =>
                InspectableType.InteractionPresenter,
            "res://addons/interaction_plugin/examples/interactive/InteractiveActor.cs" =>
                InspectableType.InteractiveActor,
            _ => InspectableType.None,
        };
    }

    private static GodotObject? GetObject(GodotObject obj, StringName propertyName) =>
        obj.Get(propertyName).AsGodotObject();

    private static Script? GetAttachedScript(GodotObject obj) =>
        obj.GetScript().AsGodotObject() as Script;
}

#endif
