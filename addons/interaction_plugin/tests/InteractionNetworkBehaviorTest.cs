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
[TestCategory("Network")]
public sealed partial class InteractionNetworkBehaviorTest : InteractionTestBase
{
    [TestCase]
    public async Task ServerReleasesRemoteOwnerInteractionWhenCandidateLeavesRange()
    {
        TestWorld testWorld = BuildWorld(ownerPeerId: 2);
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Detect(testWorld.Interactive);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        testWorld.Undetect(testWorld.Interactive);
        // Presence is now validated by the authoritative frame instead of by an overlap callback, so
        // the release happens on the next one rather than inside the detection change itself.
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactive.ActiveInteractor == null).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(1);
    }

    [TestCase]
    public async Task ServerReleasesInteractionWhenRemoteInteractorExitsTree()
    {
        TestWorld testWorld = BuildWorld(ownerPeerId: 2);
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Detect(testWorld.Interactive);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        testWorld.Interactor.QueueFree();
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactive.ActiveInteractor == null).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(1);
    }

    [TestCase]
    public async Task AWorldOwnedExecutionOutlivesTheInteractorLeavingItsWindow()
    {
        TestWorld testWorld = BuildWorld();
        // A machine that was switched on, not a channel: nobody holds a key for it and the world owns
        // the transition from the moment it started.
        testWorld.Action.DefaultBindingConfig!.InputRequirement =
            GameplayActionInputRequirement.None;
        ActivationExecutorOf(testWorld.Action).RequiresPresence = false;
        ActivationExecutorOf(testWorld.Action).Duration = 3600.0f;
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Detect(testWorld.Interactive);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        testWorld.Undetect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(2);

        AssertThat(testWorld.Interactive.ActiveInteractor == testWorld.Interactor).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(0);
    }

    [TestCase]
    public async Task AWorldOwnedExecutionOutlivesTheInteractorLeavingTheTree()
    {
        TestWorld testWorld = BuildWorld();
        testWorld.Action.DefaultBindingConfig!.InputRequirement =
            GameplayActionInputRequirement.None;
        ActivationExecutorOf(testWorld.Action).RequiresPresence = false;
        ActivationExecutorOf(testWorld.Action).Duration = 3600.0f;
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Detect(testWorld.Interactive);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        testWorld.Interactor.QueueFree();
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactive.ActiveInteractor != null).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(0);
    }

    [TestCase]
    public async Task GameplayActionRunnerNetworkAuthorityRemainsOnServerForRemoteOwner()
    {
        TestWorld testWorld = BuildWorld(ownerPeerId: 2, inheritedAuthority: true);
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactor.Runner!.GetMultiplayerAuthority())
            .IsEqual(testWorld.Interactor.Runner.ServerPeerId);
    }

    [TestCase]
    public async Task ServerRejectsAnActionTheClientBelievesIsAllowed()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        door.Detect(door.Interactive);
        door.Open.Rules.Insert(
            0,
            new AlwaysBlockedInteractionRule { Reason = "Requires a keycard." }
        );
        int startedCount = 0;
        door.Interactive.InteractionActionStarted += (_, _) => startedCount++;
        string rejectedActionId = string.Empty;
        string rejectedReason = string.Empty;
        door.Interactor.InteractionRejected += (_, actionId, reason) =>
        {
            rejectedActionId = actionId.ToString();
            rejectedReason = reason;
        };

        door.Interactor.ServerTryStartInteraction(
            door.Interactive.GetPath(),
            new StringName("open")
        );

        AssertThat(startedCount).IsEqual(0);
        AssertThat(ExecutorOf(door.Open).ExecuteCount).IsEqual(0);
        AssertThat(rejectedActionId).IsEqual("open");
        AssertThat(rejectedReason).IsEqual("Requires a keycard.");
    }

    [TestCase]
    public async Task ServerRejectsAnActionHiddenByItsOwnWorldState()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        door.Detect(door.Interactive);
        int startedCount = 0;
        door.Interactive.InteractionActionStarted += (_, _) => startedCount++;
        string rejectedReason = string.Empty;
        door.Interactor.InteractionRejected += (_, _, reason) => rejectedReason = reason;

        door.Interactor.ServerTryStartInteraction(
            door.Interactive.GetPath(),
            new StringName("close")
        );

        AssertThat(startedCount).IsEqual(0);
        AssertThat(ExecutorOf(door.Close).ExecuteCount).IsEqual(0);
        AssertThat(rejectedReason).IsEqual("Interaction unavailable.");
    }

    [TestCase]
    public async Task ServerRejectsAnActionIdentifierItsOwnTargetDoesNotDeclare()
    {
        DoorWorld door = BuildDoorWorld();
        await door.Runner.SimulateFrames(1);
        door.Detect(door.Interactive);
        int startedCount = 0;
        int rejectedCount = 0;
        door.Interactive.InteractionActionStarted += (_, _) => startedCount++;
        door.Interactor.InteractionRejected += (_, _, _) => rejectedCount++;

        door.Interactor.ServerTryStartInteraction(
            door.Interactive.GetPath(),
            new StringName("teleport")
        );
        door.Interactor.ServerTryStartInteraction(
            door.Interactive.GetPath(),
            new StringName(string.Empty)
        );

        AssertThat(startedCount).IsEqual(0);
        AssertThat(rejectedCount).IsEqual(2);
    }

    [TestCase]
    public async Task ServerKeepsAReservationWhenAnotherInputIsReleased()
    {
        TestWorld testWorld = BuildWorld();
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        testWorld.Interactor.ServerTryEndInteraction(new StringName("inspect"));

        AssertThat(testWorld.Interactive.ActiveInteractor == testWorld.Interactor).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(0);

        testWorld.Interactor.ServerTryEndInteraction(InteractInput);

        AssertThat(testWorld.Interactive.ActiveInteractor == null).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(1);
    }

    [TestCase]
    public async Task ReplicatedSnapshotAppliesCurrentProgressRejectsStaleStateAndRemovesAbsence()
    {
        TestWorld authority = BuildWorld();
        authority.Action.ExecutionVisibility = GameplayActionExecutionVisibility.Replicated;
        InteractiveComponent receiver = AddPresentationReceiver(
            authority.World,
            authority.Action.Definition!.Id,
            GameplayActionExecutionVisibility.Replicated
        );
        await authority.Runner.SimulateFrames(1);
        authority.Interactive.ExecuteAction(
            authority.Interactor,
            authority.Action,
            out ulong executionId
        );
        GameplayActionExecutionSynchronizer source = AutoFree(
            new GameplayActionExecutionSynchronizer
            {
                Component = authority.Interactive.ActionComponent,
            }
        );
        GameplayActionExecutionSynchronizer destination = AutoFree(
            new GameplayActionExecutionSynchronizer { Component = receiver.ActionComponent }
        );

        Godot.Collections.Dictionary started = source.CaptureSnapshot();
        AssertThat(destination.ApplySnapshot(started)).IsTrue();
        AssertThat(
                receiver.TryGetExecutionPresentation(
                    authority.Action.Definition.Id,
                    out GameplayActionExecutionPresentation initial
                )
            )
            .IsTrue();
        AssertThat(initial.ExecutionId).IsEqual(executionId);

        AssertThat(authority.Interactive.ReportExecutionProgress(executionId, 0.66f)).IsTrue();
        Godot.Collections.Dictionary progressed = source.CaptureSnapshot();
        AssertThat(destination.ApplySnapshot(progressed)).IsTrue();
        AssertThat(destination.ApplySnapshot(started)).IsFalse();
        AssertThat(
                receiver.TryGetExecutionPresentation(
                    authority.Action.Definition.Id,
                    out GameplayActionExecutionPresentation current
                )
            )
            .IsTrue();
        AssertThat(current.Progress!.Value).IsEqualApprox(0.66f, 0.001f);

        AssertThat(authority.Interactive.CompleteExecution(executionId)).IsTrue();
        AssertThat(destination.ApplySnapshot(source.CaptureSnapshot())).IsTrue();
        AssertThat(receiver.TryGetExecutionPresentation(authority.Action.Definition.Id, out _))
            .IsFalse();
    }

    [TestCase]
    public async Task TheRequestingPeerDrawsItsProgressWithoutAnyReplication()
    {
        TestWorld testWorld = BuildWorld();
        ActivationExecutorOf(testWorld.Action).Duration = 3600.0f;
        testWorld.Detect(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);
        AssertThat(testWorld.Interactor.TryStartInteractionInput(InteractInput)).IsTrue();

        await testWorld.Runner.SimulateFrames(2);

        AssertThat(
                testWorld.Interactive.TryGetExecutionPresentation(
                    testWorld.Action.Definition!.Id,
                    out GameplayActionExecutionPresentation presentation
                )
            )
            .IsTrue();
        AssertThat(presentation.ActionId).IsEqual(new StringName("activate"));
        AssertThat(presentation.Progress.HasValue).IsTrue();
        AssertThat(presentation.Progress!.Value > 0.0f).IsTrue();
        AssertThat(presentation.Progress!.Value < 1.0f).IsTrue();

        AssertThat(testWorld.Interactor.TryEndInteractionInput(InteractInput)).IsTrue();

        AssertThat(
                testWorld.Interactive.TryGetExecutionPresentation(
                    testWorld.Action.Definition!.Id,
                    out _
                )
            )
            .IsFalse();
    }
}
