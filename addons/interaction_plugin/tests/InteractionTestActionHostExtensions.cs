namespace QuestWorld.Tests;

using System;
using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Actions;
using GameplayActionPlugin.Runtime.Bindings;
using GameplayActionPlugin.Runtime.Execution;
using GameplayActionPlugin.Runtime.Runner;
using Godot;
using InteractionPlugin.Runtime.Interactive;
using InteractionPlugin.Runtime.Interactor;
using InteractionPlugin.Runtime.Offers;

internal static class InteractionTestActionHostExtensions
{
    /// <summary>
    /// Gives one interactive the generic host the final architecture expects, laid out exactly like
    /// the authored scenes: the host sits beside the interactive and owns every action as a direct
    /// child, so each relative path a rule or an executor spells out from its action keeps the depth
    /// it had before the extraction. Call it once the interactive is already parented, or pass the
    /// node the host belongs under when the interactive is not in its tree yet — an interactive
    /// enters the tree subscribed to the host it was authored with, so a live tree needs the host
    /// assigned first.
    /// </summary>
    public static GameplayActionComponent ConfigureActionHost(
        this InteractiveComponent interactive,
        Node? beside = null
    )
    {
        if (interactive.ActionComponent is not null)
        {
            return interactive.ActionComponent;
        }

        GameplayActionComponent component = new() { Name = "GameplayActions" };
        (beside ?? interactive.GetParent() ?? (Node)interactive).AddChild(component);
        interactive.ActionComponent = component;
        return component;
    }

    /// <summary>Declares one action on the host of an interactive, creating that host when needed.</summary>
    /// <remarks>
    /// An interactive offers what its host declares, so a test builds its actions the way a scene
    /// does: as direct children of the host, in declaration order. A host already inside the tree has
    /// run its authored registration, so an action added afterwards goes through the runtime entry
    /// point exactly like production.
    /// </remarks>
    public static void AddAction(
        this InteractiveComponent interactive,
        GameplayAction action,
        Node? beside = null
    )
    {
        GameplayActionComponent component = interactive.ConfigureActionHost(beside);
        action.ConfiguredAccessProviderId = InteractionOffer.InteractionAccessProviderId;
        AdoptAction(component, action);

        if (component.IsInsideTree())
        {
            component.AddAction(action);
        }
        else if (!component.Actions.Contains(action))
        {
            component.Actions.Add(action);
        }

        if (
            action.Definition is not null
            && action is InputGameplayAction inputAction
            && inputAction.DefaultBindingConfig is GameplayActionBindingConfig binding
        )
        {
            interactive.Offers.Add(
                new InteractionOffer
                {
                    ActionSource = InteractionOfferSource.Target,
                    ActionId = action.Definition.Id,
                    BindingConfig = binding,
                }
            );
        }
    }

    /// <summary>Gets one action of an interactive by its position in the declared order.</summary>
    public static GameplayAction ActionAt(this InteractiveComponent interactive, int index) =>
        interactive.ActionComponent!.Actions[index];

    public static InteractionOffer OfferAt(this InteractiveComponent interactive, int index) =>
        interactive.Offers[index];

    /// <summary>Evaluates the offer that resolves to a target-owned action.</summary>
    /// <remarks>
    /// Production callers evaluate authored offers directly. This overload keeps the older test
    /// worlds readable while they are migrated to the same offer-backed model.
    /// </remarks>
    public static GameplayActionAvailability EvaluateAvailability(
        this InteractiveComponent interactive,
        InteractionInteractor interactor,
        GameplayAction action
    )
    {
        GameplayActionComponent? component = action.Component ?? interactive.ActionComponent;
        if (
            component is null
            || action.Definition is null
            || !interactive.TryResolveOfferForEndpoint(
                interactor,
                component,
                action,
                out InteractionOffer? offer
            )
            || offer is null
        )
        {
            return new GameplayActionBlocked("Interaction is not configured.");
        }

        return interactive.EvaluateAvailability(interactor, offer);
    }

    private static void AdoptAction(GameplayActionComponent component, GameplayAction action)
    {
        Node? parent = action.GetParent();
        if (parent == component)
        {
            return;
        }

        if (parent is null)
        {
            component.AddChild(action);
            return;
        }

        if (action.IsInsideTree() && component.IsInsideTree())
        {
            action.Reparent(component);
            return;
        }

        parent.RemoveChild(action);
        component.AddChild(action);
    }

    public static void ConfigureActionRunner(
        this InteractionInteractor interactor,
        int ownerPeerId = 1,
        int serverPeerId = 1
    )
    {
        if (interactor.Runner is not null)
            return;

        GameplayActionComponent owned = new() { Name = "OwnedGameplayActions" };
        GameplayActionRunner runner = new()
        {
            Name = "GameplayActionRunner",
            OwnedActionComponent = owned,
            Instigator = interactor,
            OwnerPeerId = ownerPeerId,
            ServerPeerId = serverPeerId,
        };
        interactor.AddChild(owned);
        interactor.AddChild(runner);
        interactor.Runner = runner;
    }

    public static GameplayActionExecutionResult ExecuteAction(
        this InteractiveComponent interactive,
        InteractionInteractor interactor,
        GameplayAction action
    ) => interactive.ExecuteAction(interactor, action, out _);

    public static GameplayActionExecutionResult ExecuteAction(
        this InteractiveComponent interactive,
        InteractionInteractor interactor,
        GameplayAction action,
        out ulong executionId
    )
    {
        if (interactive.ActionComponent is null || action.Definition is null)
        {
            executionId = 0;
            return new GameplayActionExecutionRejected("Interaction is not configured.");
        }

        // A programmatic execution names no requester: nobody asked for it over the wire, so nobody
        // is waiting to be acknowledged. The interactor it is attributed to is its instigator.
        return interactive.ActionComponent.ExecuteAction(
            action.Definition.Id,
            out executionId,
            interactor,
            interactive.ResolveInvocationTarget()
        );
    }

    public static bool CompleteExecution(
        this InteractiveComponent interactive,
        ulong executionId
    ) => interactive.ActionComponent?.CompleteExecution(executionId) == true;

    public static bool CancelExecution(
        this InteractiveComponent interactive,
        ulong executionId,
        string reason = ""
    ) => interactive.ActionComponent?.CancelExecution(executionId, reason) == true;

    public static bool FailExecution(
        this InteractiveComponent interactive,
        ulong executionId,
        string reason
    ) => interactive.ActionComponent?.FailExecution(executionId, reason) == true;

    public static bool IsExecutionActive(
        this InteractiveComponent interactive,
        ulong executionId
    ) => interactive.ActionComponent?.IsExecutionActive(executionId) == true;

    public static bool ReportExecutionProgress(
        this InteractiveComponent interactive,
        ulong executionId,
        float? progress
    ) => interactive.ActionComponent?.ReportExecutionProgress(executionId, progress) == true;

    public static bool SetExecutionProgressSource(
        this InteractiveComponent interactive,
        ulong executionId,
        Callable source
    ) => interactive.ActionComponent?.SetExecutionProgressSource(executionId, source) == true;

    public static bool ClearExecutionProgressSource(
        this InteractiveComponent interactive,
        ulong executionId
    ) => interactive.ActionComponent?.ClearExecutionProgressSource(executionId) == true;

    public static bool RemoveRequesterExecution(
        this InteractiveComponent interactive,
        StringName actionId,
        ulong executionId
    ) => interactive.ActionComponent?.RemoveRequesterExecution(actionId, executionId) == true;

    // Interaction no longer owns a request transport: the generic runner carries every request and
    // acknowledgement. These bridges keep the spatial vocabulary of the behaviour tests while
    // driving exactly the RPC entry points the final architecture uses.
    public static void ServerTryStartInteraction(
        this InteractionInteractor interactor,
        NodePath targetPath,
        StringName actionId
    )
    {
        GameplayActionComponent? component = ResolveActionComponent(interactor, targetPath);
        InteractiveComponent? interactive = interactor.GetNodeOrNull<InteractiveComponent>(
            targetPath
        );
        if (interactor.Runner is null || component is null || interactive is null)
            return;

        interactor.Runner.ServerTryStartAction(
            interactor.GetTree().Root.GetPathTo(component),
            actionId,
            interactor.GetTree().Root.GetPathTo(interactive),
            interactor.GetTree().Root.GetPathTo(interactive.ResolveInvocationTarget())
        );
    }

    public static void ServerTryEndInteraction(
        this InteractionInteractor interactor,
        StringName inputActionName
    ) => interactor.Runner?.TryEndActionInput(inputActionName);

    public static void ClientInteractionRejected(
        this InteractionInteractor interactor,
        NodePath targetPath,
        StringName actionId,
        string reason
    )
    {
        GameplayActionComponent? component = ResolveActionComponent(interactor, targetPath);
        if (interactor.Runner is null || component is null)
            return;

        interactor.Runner.ClientActionRejected(component.GetPath(), actionId, reason);
    }

    public static void ClientGameplayActionStarted(
        this InteractionInteractor interactor,
        NodePath targetPath,
        StringName actionId,
        ulong executionId,
        GameplayActionExecutionVisibility visibility,
        bool hasProgress,
        float progressBase,
        float progressPerSecond,
        long revision
    )
    {
        GameplayActionComponent? component = ResolveActionComponent(interactor, targetPath);
        if (interactor.Runner is null || component is null)
            return;

        interactor.Runner.ClientActionStarted(
            component.GetPath(),
            actionId,
            (long)executionId,
            (int)visibility,
            hasProgress,
            progressBase,
            progressPerSecond,
            revision
        );
    }

    private static GameplayActionComponent? ResolveActionComponent(
        InteractionInteractor interactor,
        NodePath targetPath
    ) =>
        interactor.IsInsideTree()
            ? (interactor.GetNodeOrNull(targetPath) as InteractiveComponent)?.ActionComponent
            : null;

    public static bool AddPendingExecutionPresentation(
        this InteractiveComponent interactive,
        StringName actionId,
        GameplayActionProgressSample sample
    ) => interactive.ActionComponent?.AddPendingExecutionPresentation(actionId, sample) == true;
}
