namespace QuestWorld.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Actions;
using GameplayActionPlugin.Runtime.Bindings;
using GameplayActionPlugin.Runtime.Execution;
using GdUnit4;
using Godot;
using InteractionPlugin;
using InteractionPlugin.Runtime.Interactive;
using InteractionPlugin.Runtime.Interactor;
using InteractionPlugin.Runtime.Offers;
using InteractionPlugin.Runtime.Rules;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Runtime")]
public sealed partial class InteractionOfferTest : InteractionTestBase
{
    [TestCase]
    public void OffersResolveTargetAndInstigatorActionOwners()
    {
        InteractiveComponent interactive = AutoFree(new InteractiveComponent());
        GameplayActionComponent targetActions = AutoFree(new GameplayActionComponent());
        GameplayAction targetAction = NewGenericAction("open");
        targetActions.AddAction(targetAction);
        interactive.ActionComponent = targetActions;

        InteractionInteractor interactor = AutoFree(new InteractionInteractor());
        interactor.ConfigureActionRunner();
        GameplayActionComponent ownedActions = interactor.Runner!.OwnedActionComponent!;
        GameplayAction ownedAction = NewGenericAction("take");
        ownedActions.AddAction(ownedAction);

        InteractionOffer targetOffer = new()
        {
            ActionSource = InteractionOfferSource.Target,
            ActionId = new StringName("open"),
        };
        InteractionOffer instigatorOffer = new()
        {
            ActionSource = InteractionOfferSource.Instigator,
            ActionId = new StringName("take"),
        };

        AssertThat(interactive.TryResolveOffer(interactor, targetOffer, out var targetResolution))
            .IsTrue();
        AssertThat(targetResolution.Component == targetActions).IsTrue();
        AssertThat(targetResolution.Action == targetAction).IsTrue();
        AssertThat(
                interactive.TryResolveOffer(
                    interactor,
                    instigatorOffer,
                    out var instigatorResolution
                )
            )
            .IsTrue();
        AssertThat(instigatorResolution.Component == ownedActions).IsTrue();
        AssertThat(instigatorResolution.Action == ownedAction).IsTrue();
    }

    [TestCase]
    public async Task FocusProjectsAnInstigatorOwnedOfferIntoATargetedBinding()
    {
        Node3D world = new();
        TestInteractiveActor owner = new() { Name = "Battery", Position = new Vector3(0, 0, -2) };
        Area3D area = new() { Name = "InteractionArea" };
        InteractiveComponent interactive = new()
        {
            Name = "Interactive",
            InteractionArea = area,
            InteractionAnchor = owner,
            Offers =
            {
                new InteractionOffer
                {
                    ActionSource = InteractionOfferSource.Instigator,
                    ActionId = new StringName("take"),
                    BindingConfig = Press("interact"),
                },
            },
        };
        owner.AddChild(area);
        owner.AddChild(interactive);

        InteractionInteractor interactor = new() { Name = "Interactor" };
        Node3D view = new() { Name = "ViewOrigin" };
        interactor.AddChild(view);
        TestInteractionDetector detector = AttachDetector(interactor, view);
        world.AddChild(owner);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world, autoFree: true);
        await runner.SimulateFrames(1);
        GameplayAction ownedAction = NewGenericAction("take");
        interactor.Runner!.OwnedActionComponent!.AddAction(ownedAction);

        detector.SetDetection(interactive, InteractionDetectionKind.Interactible);
        interactor.RecalculateFocus();

        GameplayActionBinding binding = interactor.Runner.GetBindings().Single();
        AssertThat(binding.Component == interactor.Runner.OwnedActionComponent).IsTrue();
        AssertThat(binding.ActionId == new StringName("take")).IsTrue();
        AssertThat(binding.Source == interactive).IsTrue();
        AssertThat(binding.Target == interactive).IsTrue();
    }

    [TestCase]
    public async Task TargetOwnedOfferUsesTheInteractionAccessProvider()
    {
        Node3D world = new();
        TestInteractiveActor owner = new() { Name = "Door", Position = new Vector3(0, 0, -2) };
        Area3D area = new() { Name = "InteractionArea" };
        GameplayActionComponent targetActions = new() { Name = "GameplayActions" };
        CompletingExecutor executor = new() { Name = "OpenExecutor" };
        GameplayAction action = new()
        {
            Name = "OpenAction",
            ConfiguredAccessProviderId = InteractionOffer.InteractionAccessProviderId,
            Definition = new GameplayActionDefinition
            {
                Id = new StringName("open"),
                Label = "Open",
            },
            Executor = executor,
        };
        action.AddChild(executor);
        targetActions.AddChild(action);
        targetActions.Actions.Add(action);
        InteractiveComponent interactive = new()
        {
            Name = "Interactive",
            InteractionArea = area,
            InteractionAnchor = owner,
            ActionComponent = targetActions,
            Offers =
            {
                new InteractionOffer
                {
                    ActionSource = InteractionOfferSource.Target,
                    ActionId = new StringName("open"),
                    BindingConfig = Press("interact"),
                },
            },
        };
        owner.AddChild(area);
        owner.AddChild(targetActions);
        owner.AddChild(interactive);

        InteractionInteractor interactor = new() { Name = "Interactor" };
        Node3D view = new() { Name = "ViewOrigin" };
        interactor.AddChild(view);
        TestInteractionDetector detector = AttachDetector(interactor, view);
        world.AddChild(owner);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world, autoFree: true);
        await runner.SimulateFrames(1);

        detector.SetDetection(interactive, InteractionDetectionKind.Interactible);
        interactor.RecalculateFocus();

        AssertThat(interactor.Runner!.TryStartActionInput(new StringName("interact"))).IsTrue();
        AssertThat(executor.ExecuteCount).IsEqual(1);
    }

    [TestCase]
    public async Task TargetReservationBlocksOtherInteractorsUntilTheExecutionEnds()
    {
        Node3D world = new();
        TestInteractiveActor owner = new() { Name = "Door", Position = new Vector3(0, 0, -2) };
        Area3D area = new() { Name = "InteractionArea" };
        GameplayActionComponent targetActions = new() { Name = "GameplayActions" };
        CompletingExecutor executor = new()
        {
            Name = "OpenExecutor",
            Result = new GameplayActionExecutionRunning(),
        };
        GameplayAction action = new()
        {
            Name = "OpenAction",
            ConfiguredAccessProviderId = InteractionOffer.InteractionAccessProviderId,
            Definition = new GameplayActionDefinition { Id = "open", Label = "Open" },
            Executor = executor,
        };
        action.AddChild(executor);
        targetActions.AddAction(action);

        InteractiveComponent interactive = new()
        {
            Name = "Interactive",
            InteractionArea = area,
            InteractionAnchor = owner,
            ActionComponent = targetActions,
            Offers =
            {
                new InteractionOffer
                {
                    ActionSource = InteractionOfferSource.Target,
                    ActionId = new StringName("open"),
                    BindingConfig = Press("interact"),
                    TargetConcurrencyGroup = new StringName("door_operation"),
                },
            },
        };
        owner.AddChild(area);
        owner.AddChild(targetActions);
        owner.AddChild(interactive);

        InteractionInteractor first = new() { Name = "FirstInteractor" };
        Node3D firstView = new() { Name = "ViewOrigin" };
        first.AddChild(firstView);
        TestInteractionDetector firstDetector = AttachDetector(first, firstView);
        InteractionInteractor second = new() { Name = "SecondInteractor" };
        Node3D secondView = new() { Name = "ViewOrigin" };
        second.AddChild(secondView);
        TestInteractionDetector secondDetector = AttachDetector(second, secondView);
        world.AddChild(owner);
        world.AddChild(first);
        world.AddChild(second);
        ISceneRunner runner = ISceneRunner.Load(world, autoFree: true);
        await runner.SimulateFrames(1);

        firstDetector.SetDetection(interactive, InteractionDetectionKind.Interactible);
        secondDetector.SetDetection(interactive, InteractionDetectionKind.Interactible);
        first.RecalculateFocus();
        GameplayActionBinding firstBinding = first.Runner!.GetBindings().Single();

        ulong executionId = 0;
        first.Runner.GameplayActionStarted += (_, _, startedId) => executionId = (ulong)startedId;
        AssertThat(first.Runner.TryStartActionInput("interact")).IsTrue();
        AssertThat(executor.ExecuteCount).IsEqual(1);

        GameplayAction forceAction = NewGenericAction("force");
        second.Runner!.OwnedActionComponent!.AddAction(forceAction);
        InteractionOffer forceOffer = new()
        {
            ActionSource = InteractionOfferSource.Instigator,
            ActionId = new StringName("force"),
            BindingConfig = Press("force"),
            TargetConcurrencyGroup = new StringName("door_operation"),
        };
        interactive.Offers.Add(forceOffer);
        GameplayActionAvailability otherAvailability = interactive.EvaluateAvailability(
            second,
            interactive.Offers[0]
        );
        GameplayActionAvailability selfAvailability = interactive.EvaluateAvailability(
            first,
            interactive.Offers[0]
        );
        AssertThat(otherAvailability is GameplayActionBlocked).IsTrue();
        AssertThat(selfAvailability is GameplayActionBlocked).IsTrue();
        string otherReason = otherAvailability is GameplayActionBlocked otherBlocked
            ? otherBlocked.Reason
            : string.Empty;
        string selfReason = selfAvailability is GameplayActionBlocked selfBlocked
            ? selfBlocked.Reason
            : string.Empty;
        AssertThat(otherReason).IsEqual("Someone else is using this target.");
        AssertThat(selfReason).IsEqual("This target is already in use.");
        AssertThat(
                interactive.EvaluateAvailability(second, forceOffer)
                    is GameplayActionBlocked blockedForce
                    && blockedForce.Reason == "Someone else is using this target."
            )
            .IsTrue();

        GameplayAction independentAction = NewGenericAction("inspect");
        second.Runner.OwnedActionComponent!.AddAction(independentAction);
        InteractionOffer independentOffer = new()
        {
            ActionSource = InteractionOfferSource.Instigator,
            ActionId = new StringName("inspect"),
            TargetConcurrencyGroup = new StringName("inspection"),
        };
        interactive.Offers.Add(independentOffer);
        AssertThat(
                interactive.EvaluateAvailability(second, independentOffer) is GameplayActionAllowed
            )
            .IsTrue();

        AssertThat(targetActions.CompleteExecution(executionId)).IsTrue();
        AssertThat(
                interactive.EvaluateAvailability(second, interactive.Offers[0])
                    is GameplayActionAllowed
            )
            .IsTrue();
        AssertThat(interactive.EvaluateAvailability(second, forceOffer) is GameplayActionAllowed)
            .IsTrue();
        AssertThat(firstBinding.Target).IsEqual(interactive);

        AssertThat(first.Runner.TryEndActionInput("interact")).IsTrue();
        executor.Result = new GameplayActionExecutionRunning();
        ulong destroyedTargetExecutionId = 0;
        first.Runner.GameplayActionStarted += (_, _, startedId) =>
            destroyedTargetExecutionId = (ulong)startedId;
        AssertThat(first.Runner.TryStartActionInput("interact")).IsTrue();
        AssertThat(destroyedTargetExecutionId > 0).IsTrue();

        interactive.QueueFree();
        await runner.SimulateFrames(1);
        AssertThat(targetActions.IsActionExecuting("open")).IsFalse();
        AssertThat(targetActions.CompleteExecution(destroyedTargetExecutionId)).IsFalse();
    }

    [TestCase]
    public async Task TargetAndOfferRulesSeeTheOfferWithoutMutatingTheOwnedAction()
    {
        Node3D world = new();
        TestInteractiveActor owner = new() { Name = "Battery", Position = new Vector3(0, 0, -2) };
        Area3D area = new() { Name = "InteractionArea" };
        RecordingInteractionRule targetRule = new();
        RecordingInteractionRule offerRule = new();
        GameplayAction ownedAction = NewGenericAction("take");
        InteractiveComponent interactive = new()
        {
            Name = "Interactive",
            InteractionArea = area,
            InteractionAnchor = owner,
            TargetRules = { targetRule },
            Offers =
            {
                new InteractionOffer
                {
                    ActionSource = InteractionOfferSource.Instigator,
                    ActionId = new StringName("take"),
                    BindingConfig = Press("interact"),
                    Rules = { offerRule },
                },
            },
        };
        owner.AddChild(area);
        owner.AddChild(interactive);
        InteractionInteractor interactor = new() { Name = "Interactor" };
        Node3D view = new() { Name = "ViewOrigin" };
        interactor.AddChild(view);
        TestInteractionDetector detector = AttachDetector(interactor, view);
        world.AddChild(owner);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world, autoFree: true);
        await runner.SimulateFrames(1);
        interactor.Runner!.OwnedActionComponent!.AddAction(ownedAction);

        detector.SetDetection(interactive, InteractionDetectionKind.Interactible);

        GameplayActionAvailability availability = interactive.EvaluateAvailability(
            interactor,
            interactive.Offers[0]
        );
        AssertThat(availability is GameplayActionAllowed).IsTrue();
        AssertThat(targetRule.LastOffer == interactive.Offers[0]).IsTrue();
        AssertThat(offerRule.LastOffer == interactive.Offers[0]).IsTrue();
        AssertThat(ownedAction.Rules.Count).IsEqual(0);
    }

    private static GameplayAction NewGenericAction(string id)
    {
        GameplayAction action = new()
        {
            Name = $"{id}Action",
            Definition = new GameplayActionDefinition { Id = new StringName(id), Label = id },
        };
        CompletingExecutor executor = new() { Name = $"{id}Executor" };
        action.AddChild(executor);
        action.Executor = executor;
        return action;
    }

    private static GameplayActionBindingConfig Press(string input) =>
        new()
        {
            InputActionName = new StringName(input),
            ActivationMode = GameplayActionActivationMode.Press,
        };

    private sealed partial class CompletingExecutor : GameplayActionExecutor
    {
        public int ExecuteCount { get; private set; }

        public GameplayActionExecutionResult Result { get; set; } =
            new GameplayActionExecutionCompleted();

        public override GameplayActionExecutionResult Execute(in GameplayActionContext context) =>
            ExecuteAndReturnResult();

        private GameplayActionExecutionResult ExecuteAndReturnResult()
        {
            ExecuteCount++;
            return Result;
        }
    }

    private sealed partial class RecordingInteractionRule : InteractionRule
    {
        public InteractionOffer? LastOffer { get; private set; }

        public override GameplayActionAvailability Evaluate(in InteractionContext context)
        {
            LastOffer = context.Offer;
            return new GameplayActionAllowed();
        }
    }
}
