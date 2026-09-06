namespace QuestWorld.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Integration.Stateful;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Bindings;
using QuestWorld.GameplayActions.Runtime.Execution;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Examples.Rules;
using QuestWorld.Interaction.Integration.Stateful;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Interaction.Runtime.Rules;
using QuestWorld.State;
using QuestWorld.Tests.GameplayActions;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Runtime")]
public sealed partial class InteractionInputTest : InteractionTestBase
{
    [TestCase]
    public void OfflineInteractorKeepsLocalControlWithoutMultiplayerPeer()
    {
        InteractionInteractor interactor = new();

        try
        {
            AssertThat(interactor.IsLocallyControlled).IsTrue();
        }
        finally
        {
            interactor.Free();
        }
    }

    [TestCase]
    public async Task OfflineInputUsesAuthoritativeStartAndEndPath()
    {
        TestWorld testWorld = BuildWorld();
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();
        AssertThat(testWorld.Owner.StartCount).IsEqual(1);
        AssertThat(testWorld.Stateful.State).IsEqual(ActivatingState);

        AssertThat(testWorld.Interactor.TryEndInteractionInput(InteractInput)).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(1);
        AssertThat(testWorld.Interactive.ActiveInteractor == null).IsTrue();
    }

    [TestCase]
    public async Task BlockedAvailabilityStopsRequestBeforeAuthoritativeDispatch()
    {
        TestWorld testWorld = BuildWorld();
        testWorld.Interactive.TargetRules.Add(
            new AlwaysBlockedInteractionRule { Reason = "Locked" }
        );
        bool requestEmitted = false;
        testWorld.Interactor.InteractionRequested += (_, _) => requestEmitted = true;
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsFalse();
        AssertThat(requestEmitted).IsFalse();
        AssertThat(testWorld.Owner.StartCount).IsEqual(0);
    }

    [TestCase]
    public async Task OneInputResolvesToTheActionAllowedByTheCurrentWorldState()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        door.Detect(door.Interactive);
        List<string> startedActions = new();
        door.Interactive.InteractionActionStarted += (_, action) =>
            startedActions.Add(action.Definition!.Id.ToString());

        AssertThat(door.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();
        AssertThat(door.Interactor.TryEndInteractionInput(InteractInput)).IsTrue();
        door.State.SetState(new StringName("open"));
        AssertThat(door.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        AssertThat(string.Join(",", startedActions)).IsEqual("open,close");
    }

    [TestCase]
    public async Task InputResolutionPrefersAllowedThenPriorityThenIdentifier()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        door.State.SetState(new StringName("locked"));
        InteractionAction zulu = CreateAction("zulu");
        InteractionAction alpha = CreateAction("alpha");
        InteractionAction blocked = CreateAction(
            "blocked",
            new AlwaysBlockedInteractionRule { Reason = "Locked" }
        );
        blocked.DefaultBindingConfig!.Priority = 10;
        door.Interactive.AddAction(zulu);
        door.Interactive.AddAction(alpha);
        door.Interactive.AddAction(blocked);
        door.Detect(door.Interactive);

        AssertThat(door.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();
        AssertThat(ExecutorOf(alpha).ExecuteCount).IsEqual(1);
        AssertThat(door.Interactor.TryEndInteractionInput(InteractInput)).IsTrue();

        zulu.DefaultBindingConfig!.Priority = 5;
        door.Interactor.RefreshFocusedBindings(door.Interactive);

        AssertThat(door.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();
        AssertThat(ExecutorOf(zulu).ExecuteCount).IsEqual(1);
        AssertThat(door.Interactor.TryEndInteractionInput(InteractInput)).IsTrue();

        zulu.Rules.Add(new AlwaysBlockedInteractionRule { Reason = "Locked" });
        alpha.Rules.Add(new AlwaysBlockedInteractionRule { Reason = "Locked" });

        AssertThat(door.Interactor.TryStartInteractionInput(InteractInput)).IsFalse();
    }

    [TestCase]
    public async Task OwnedAndFocusedInteractionBindingsCompeteByPriority()
    {
        TestWorld testWorld = BuildWorld();
        InputGameplayAction ownedAction = new()
        {
            Name = "OwnedAction",
            Definition = new GameplayActionDefinition
            {
                Id = new StringName("owned"),
                Label = "Owned",
            },
            DefaultBindingConfig = new GameplayActionBindingConfig
            {
                InputActionName = InteractInput,
                ActivationMode = GameplayActionActivationMode.Press,
                Priority = 10,
            },
        };
        TestGameplayActionExecutor ownedExecutor = new() { Name = "OwnedExecutor" };
        ownedAction.AddChild(ownedExecutor);
        ownedAction.Executor = ownedExecutor;
        testWorld.Interactor.Runner!.OwnedActionComponent!.AddAction(ownedAction);
        testWorld.Action.DefaultBindingConfig!.Priority = 20;

        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();
        AssertThat(testWorld.Owner.StartCount).IsEqual(1);
        AssertThat(ownedExecutor.ExecuteCount).IsEqual(0);
    }

    [TestCase]
    public async Task AnInputWithoutAnyMatchingActionRequestsNothing()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        door.Detect(door.Interactive);
        int startedCount = 0;
        door.Interactive.InteractionActionStarted += (_, _) => startedCount++;

        AssertThat(door.Interactor.TryStartInteractionInput(new StringName("inspect"))).IsFalse();
        AssertThat(startedCount).IsEqual(0);
        AssertThat(ExecutorOf(door.Open).ExecuteCount).IsEqual(0);
    }

    [TestCase]
    public async Task ReleasingAnotherInputKeepsTheActiveInteraction()
    {
        TestWorld testWorld = BuildWorld();
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        AssertThat(testWorld.Interactor.TryEndInteractionInput(new StringName("inspect")))
            .IsFalse();

        AssertThat(testWorld.Interactive.ActiveInteractor == testWorld.Interactor).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(0);
        AssertThat(testWorld.Interactor.TryEndInteractionInput(InteractInput)).IsTrue();
        AssertThat(testWorld.Interactive.ActiveInteractor == null).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(1);
    }

    [TestCase]
    public async Task ReleaseEndsTheStartedExecutionWithoutReResolvingTheInput()
    {
        TestWorld testWorld = BuildWorld();
        InteractionAction alternative = CreateAction("alternative");
        // A group of its own, so the running execution leaves it available and a fresh resolution
        // really would pick it. Sharing the default group would block it like everything else.
        alternative.HostConcurrencyGroup = new StringName("inspection");
        testWorld.Interactive.AddAction(alternative);
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();
        AssertThat(testWorld.Stateful.State).IsEqual(ActivatingState);
        AssertThat(
                testWorld.Interactive.EvaluateAvailability(testWorld.Interactor, alternative)
                    is GameplayActionAllowed
            )
            .IsTrue();

        AssertThat(testWorld.Interactor.TryEndInteractionInput(InteractInput)).IsTrue();

        AssertThat(testWorld.Interactive.ActiveInteractor == null).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(1);
    }

    [TestCase]
    public async Task AutomaticActionStartsOnFocusAndStaysOutOfPrompts()
    {
        TestWorld testWorld = BuildWorld();
        testWorld.Action.DefaultBindingConfig!.ActivationMode =
            GameplayActionActivationMode.Automatic;
        testWorld.Action.DefaultBindingConfig.InputActionName = new StringName();
        testWorld.Action.DefaultBindingConfig.InputRequirement =
            GameplayActionInputRequirement.None;
        await testWorld.Runner.SimulateFrames(1);

        testWorld.Detect(testWorld.Interactive);

        AssertThat(testWorld.Owner.StartCount).IsEqual(1);
        AssertThat(testWorld.Stateful.State).IsEqual(ActivatingState);
        InteractionTargetPresentation presentation = testWorld.Interactive.GetPresentation(
            testWorld.Interactor,
            true
        );
        AssertThat(presentation.Actions.Count).IsEqual(1);
        AssertThat(presentation.Actions[0].IsAutomatic).IsTrue();
        AssertThat(presentation.HasPromptableAction).IsFalse();
    }

    [TestCase]
    public async Task AutomaticActionDoesNotAnswerAPlayerInput()
    {
        TestWorld testWorld = BuildWorld();
        InteractionAction automatic = CreateAction("automatic");
        automatic.DefaultBindingConfig!.ActivationMode = GameplayActionActivationMode.Automatic;
        testWorld.Interactive.AddAction(automatic);
        await testWorld.Runner.SimulateFrames(1);

        testWorld.Detect(testWorld.Interactive);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();
        AssertThat(testWorld.Owner.StartCount).IsEqual(1);
    }

    [TestCase]
    public async Task TheInteractorReportsWhichInputsAreWorthSampling()
    {
        TestWorld testWorld = BuildWorld();
        InteractionAction inspect = CreateAction("inspect");
        inspect.DefaultBindingConfig!.InputActionName = new StringName("inspect");
        inspect.HostConcurrencyGroup = new StringName("inspection");
        InteractionAction pickup = CreateAction("pickup");
        pickup.DefaultBindingConfig!.ActivationMode = GameplayActionActivationMode.Automatic;
        testWorld.Interactive.AddAction(inspect);
        testWorld.Interactive.AddAction(pickup);
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);

        List<StringName> focused = new(testWorld.Interactor.Runner!.GetRelevantInputs());

        // Both bound inputs of the focused target, and only once each. The automatic action shares
        // the interact input but is not what puts it there: no key requests an automatic action.
        AssertThat(focused.Count).IsEqual(2);
        AssertThat(focused.Contains(InteractInput)).IsTrue();
        AssertThat(focused.Contains(new StringName("inspect"))).IsTrue();

        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();
        testWorld.Undetect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Detect(testWorld.Interactive);

        // Nothing is focused any more, yet the input this interactor believes it is sustaining stays
        // reportable, so a release is still forwarded instead of being silently dropped.
        List<StringName> sustained = new(testWorld.Interactor.Runner!.GetRelevantInputs());

        AssertThat(sustained.Contains(InteractInput)).IsTrue();
    }

    [TestCase]
    public async Task AnAutomaticActionRetriesWhenItBecomesAllowedWithoutRefocusing()
    {
        TestWorld testWorld = BuildWorld();
        testWorld.Owner.GameplayBlocked = true;
        testWorld.Interactive.TargetRules.Add(new InteractiveParentGameplayRule());
        testWorld.Action.DefaultBindingConfig!.ActivationMode =
            GameplayActionActivationMode.Automatic;
        testWorld.Action.DefaultBindingConfig.InputActionName = new StringName();
        testWorld.Action.DefaultBindingConfig.InputRequirement =
            GameplayActionInputRequirement.None;
        int rejectedCount = 0;
        testWorld.Interactor.InteractionRejected += (_, _, _) => rejectedCount++;
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(3);

        AssertThat(testWorld.Owner.StartCount).IsEqual(0);
        AssertThat(rejectedCount).IsEqual(0);

        // Focus never moves: only the rule flips, and the action must still start by itself.
        testWorld.Owner.GameplayBlocked = false;
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Owner.StartCount).IsEqual(1);
        AssertThat(testWorld.Stateful.State).IsEqual(ActivatingState);

        await testWorld.Runner.SimulateFrames(3);

        AssertThat(testWorld.Owner.StartCount).IsEqual(1);
    }

    [TestCase]
    public async Task HoldingOneInputSelectsTheActionThatAsksForTheHold()
    {
        TestWorld testWorld = BuildWorld();
        InteractionAction force = CreateAction("force");
        force.DefaultBindingConfig!.ActivationMode = GameplayActionActivationMode.Hold;
        force.DefaultBindingConfig!.HoldDuration = 0.05f;
        testWorld.Interactive.AddAction(force);
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        // Pressing only started the hold: nothing is selected while the threshold is not reached.
        AssertThat(testWorld.Owner.StartCount).IsEqual(0);
        AssertThat(ExecutorOf(force).ExecuteCount).IsEqual(0);
        GameplayActionPresentation forcePresentation = PresentedAction(
            testWorld.Interactor.GetInteractionPresentation()!.Value,
            "force"
        );
        AssertThat(forcePresentation.HoldProgress.HasValue).IsTrue();
        AssertThat(forcePresentation.HoldElapsed.HasValue).IsTrue();

        for (int frame = 0; frame < 300 && ExecutorOf(force).ExecuteCount == 0; frame++)
        {
            await testWorld.Runner.SimulateFrames(1);
        }

        AssertThat(ExecutorOf(force).ExecuteCount).IsEqual(1);
        AssertThat(testWorld.Owner.StartCount).IsEqual(0);
        AssertThat(
                PresentedAction(
                    testWorld.Interactor.GetInteractionPresentation()!.Value,
                    "force"
                ).HoldProgress.HasValue
            )
            .IsFalse();
    }

    [TestCase]
    public async Task AConsumedUnlockHoldCannotOpenTheDoorBeforeRelease()
    {
        DoorWorld door = BuildDoorWorld();
        AssertThat(door.State.SetState(new StringName("locked"))).IsTrue();
        InteractionAction unlock = CreateAction("unlock", DoorStateRule("locked"));
        unlock.DefaultBindingConfig!.ActivationMode = GameplayActionActivationMode.Hold;
        unlock.DefaultBindingConfig!.HoldDuration = 0.001f;
        BindSetStateExecutor(unlock, door.State, "closed");
        door.Interactive.AddAction(unlock);
        await door.Runner.SimulateFrames(1);
        door.Detect(door.Interactive);

        AssertThat(door.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();
        for (int frame = 0; frame < 10 && door.State.State.ToString() != "closed"; frame++)
        {
            await door.Runner.SimulateFrames(1);
        }

        AssertThat(door.State.State.ToString()).IsEqual("closed");
        door.Detector.ClearDetection(door.Interactive);
        door.Interactor.RecalculateFocus();
        AssertThat(
                new List<StringName>(door.Interactor.Runner!.GetRelevantInputs()).Contains(
                    InteractInput
                )
            )
            .IsTrue();
        door.Detect(door.Interactive);

        AssertThat(door.Interactor.TryStartInteractionInput(InteractInput)).IsFalse();
        AssertThat(ExecutorOf(door.Open).ExecuteCount).IsEqual(0);

        AssertThat(door.Interactor.TryEndInteractionInput(InteractInput)).IsTrue();
        AssertThat(door.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();
        AssertThat(ExecutorOf(door.Open).ExecuteCount).IsEqual(1);
    }

    [TestCase]
    public async Task ReleasingBeforeTheThresholdSelectsTheActionThatAsksForNoHold()
    {
        TestWorld testWorld = BuildWorld();
        InteractionAction force = CreateAction("force");
        force.DefaultBindingConfig!.ActivationMode = GameplayActionActivationMode.Hold;
        force.DefaultBindingConfig!.HoldDuration = 3600.0f;
        testWorld.Interactive.AddAction(force);
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        AssertThat(testWorld.Interactor.TryEndInteractionInput(InteractInput)).IsTrue();

        AssertThat(testWorld.Owner.StartCount).IsEqual(1);
        AssertThat(testWorld.Stateful.State).IsEqual(ActivatingState);
        AssertThat(ExecutorOf(force).ExecuteCount).IsEqual(0);
    }

    [TestCase]
    public async Task EveryHeldActionFillsOnItsOwnThreshold()
    {
        TestWorld testWorld = BuildWorld();
        InteractionAction force = CreateAction("force");
        force.DefaultBindingConfig!.ActivationMode = GameplayActionActivationMode.Hold;
        force.DefaultBindingConfig!.HoldDuration = 3600.0f;
        InteractionAction pry = CreateAction("pry");
        pry.DefaultBindingConfig!.ActivationMode = GameplayActionActivationMode.Hold;
        pry.DefaultBindingConfig!.HoldDuration = 0.001f;
        testWorld.Interactive.AddAction(force);
        testWorld.Interactive.AddAction(pry);
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        await testWorld.Runner.SimulateFrames(2);
        InteractionTargetPresentation presentation = testWorld.Interactive.GetPresentation(
            testWorld.Interactor,
            true
        );

        // Normalised on the threshold of each action and not on the longest one of the input: a bar
        // drawn around the key reaches one when the action it belongs to becomes selectable, which the
        // shorter of two actions sharing an input would otherwise never do.
        AssertThat(PresentedAction(presentation, "pry").HoldProgress!.Value).IsEqual(1.0f);
        AssertThat(PresentedAction(presentation, "force").HoldProgress!.Value > 0.0f).IsTrue();
        AssertThat(PresentedAction(presentation, "force").HoldProgress!.Value < 1.0f).IsTrue();

        // The raw seconds come along because a widget cannot rebuild them from the ratio: the
        // threshold it would multiply by is not part of the presentation.
        AssertThat(PresentedAction(presentation, "pry").HoldElapsed!.Value)
            .IsEqual(PresentedAction(presentation, "force").HoldElapsed!.Value);
        AssertThat(PresentedAction(presentation, "pry").HoldElapsed!.Value > 0.0f).IsTrue();

        // The hold is a selection between the actions sharing an input, so the one asking for no
        // threshold reports nothing: its bar would promise a hold that selects it, and none does.
        AssertThat(PresentedAction(presentation, "activate").HoldProgress.HasValue).IsFalse();
        AssertThat(PresentedAction(presentation, "activate").HoldElapsed.HasValue).IsFalse();
    }
}
