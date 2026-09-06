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
using QuestWorld.Interaction.Integration.Stateful;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.State;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Network")]
public sealed partial class InteractionNetworkLateJoinTest : InteractionNetworkTestBase
{
    [TestCase]
    public async Task AnInteractionReplicatesItsStateToEveryPeerWhileTheAckStaysWithItsRequester()
    {
        // The whole point of the two channels, in one scenario: what is true of the world reaches
        // everybody, what is true only of one player's request reaches only that player.
        Session session = await Connect();
        try
        {
            StateLog onA = session.WatchState(session.ClientA);
            StateLog onB = session.WatchState(session.ClientB);
            session.Focus();

            session.ClientA.InteractorA.TryStartInteractionInput(SwitchInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.ClientB.Stateful.State.ToString()).IsEqual("activating");
            AssertThat(onB.Presentation).IsEqual(new List<string> { "activating" });
            AssertThat(onA.Presentation).IsEqual(new List<string> { "activating" });

            AssertThat(session.KindsA()).IsEqual(new List<string> { "started", "completed" });
            AssertThat(session.KindsB()).IsEmpty();

            // And B presents the action as busy from the state alone, having received no interaction
            // event of any kind.
            GameplayActionPresentation switchOnB = PresentedAction(
                session.ClientB.Interactive.GetPresentation(
                    session.ClientB.InteractorB,
                    isFocused: true
                ),
                "switch"
            );
            AssertThat(switchOnB.Availability is GameplayActionBlocked).IsTrue();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task ALateJoinerArrivesAtTheCurrentStateWithoutTheStatesItMissed()
    {
        Session session = await Connect();
        try
        {
            session.Server.Stateful.SetState(ActivatingState);
            await session.Pump(RoundTripFrames);
            session.Server.Stateful.SetState(ActivatedState);
            await session.Pump(RoundTripFrames);

            LatePeer late = await session.JoinLate("ClientC");

            AssertThat(late.Scene.Stateful.State.ToString()).IsEqual("activated");
            // The intermediate state is gone: replication carries the current value, so a peer that
            // was not there never learns the world passed through `activating`.
            AssertThat(late.Log.Changed).IsEqual(new List<string> { "activated" });
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task ALateJoinerReceivesItsArrivalAsASynchronizedTransition()
    {
        // The retained contract. The component applies the initial state in _Ready, then the first
        // replicated value arrives through the very same setter as any later one, so it is dispatched as
        // `idle > activated`: that is what makes a door found already open play its opening and land on
        // the right pose and collision. What tells the two apart is the flag, so a presentation can
        // apply the pose in both cases and keep its one-shots for a change its player actually lived.
        Session session = await Connect();
        try
        {
            session.Server.Stateful.SetState(ActivatedState);
            await session.Pump(RoundTripFrames);

            LatePeer late = await session.JoinLate("ClientC");

            AssertThat(late.Log.Transitions).IsEqual(new List<string> { "idle>activated" });
            AssertThat(late.Log.Presentation).IsEqual(new List<string> { "activated" });
            AssertThat(late.Log.Authority).IsEmpty();
            AssertThat(late.Log.Synchronizations).IsEqual(new List<bool> { true });
            AssertThat(late.Log.PresentationSynchronizations).IsEqual(new List<bool> { true });
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task AJoinerOnAnUntouchedTargetLivesItsFirstTransition()
    {
        // The other half of the contract, and the one that makes the flag trustworthy: joining an object
        // nobody touched replicates a value equal to the initial state, which dispatches nothing at all.
        // The arrival is still spent, so the first real transition afterwards is reported as lived — a
        // chest opened under the eyes of a player who joined before it did gets its confetti.
        Session session = await Connect();
        try
        {
            LatePeer late = await session.JoinLate("ClientC");

            AssertThat(late.Log.Transitions).IsEmpty();

            session.Server.Stateful.SetState(ActivatedState);
            await session.Pump(RoundTripFrames);

            AssertThat(late.Log.Transitions).IsEqual(new List<string> { "idle>activated" });
            AssertThat(late.Log.Synchronizations).IsEqual(new List<bool> { false });
            AssertThat(late.Log.PresentationSynchronizations).IsEqual(new List<bool> { false });
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task ALateJoinerPresentsAnActionAlreadyTakenAsBusy()
    {
        // The half of the late join that does hold: the pose and the availability come from the
        // current state, so a peer arriving mid-action reads the world correctly.
        Session session = await Connect();
        try
        {
            session.Focus();
            session.ClientA.InteractorA.TryStartInteractionInput(SwitchInput);
            await session.Pump(RoundTripFrames);

            LatePeer late = await session.JoinLate("ClientC");

            AssertThat(late.Scene.Stateful.State.ToString()).IsEqual("activating");
            GameplayActionPresentation onLate = PresentedAction(
                late.Scene.Interactive.GetPresentation(late.Scene.InteractorA, isFocused: true),
                "switch"
            );
            AssertThat(onLate.Availability is GameplayActionBlocked).IsTrue();
            // And the acknowledgement of somebody else's request never reached it.
            AssertThat(session.KindsB()).IsEmpty();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task ReplicatedExecutionReachesRequesterObserverLateJoinerAndThenDisappears()
    {
        Session session = await Connect();
        try
        {
            session.SetExecutionVisibility(GameplayActionExecutionVisibility.Replicated);
            session.Arm(new GameplayActionExecutionRunning(), 3600.0f);
            session.Focus();

            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(
                    session.ClientA.Interactive.TryGetExecutionPresentation(
                        ActivateAction,
                        out GameplayActionExecutionPresentation requester
                    )
                )
                .IsTrue();
            AssertThat(
                    session.ClientB.Interactive.TryGetExecutionPresentation(
                        ActivateAction,
                        out GameplayActionExecutionPresentation observer
                    )
                )
                .IsTrue();
            AssertThat(observer.ExecutionId).IsEqual(requester.ExecutionId);
            AssertThat(requester.Relation)
                .IsEqual(GameplayActionExecutionRelation.RequestedLocally);
            AssertThat(observer.Relation).IsEqual(GameplayActionExecutionRelation.Observed);

            LatePeer late = await session.JoinLate("ClientC");
            AssertThat(
                    late.Scene.Interactive.TryGetExecutionPresentation(
                        ActivateAction,
                        out GameplayActionExecutionPresentation joined
                    )
                )
                .IsTrue();
            AssertThat(joined.ExecutionId).IsEqual(requester.ExecutionId);
            AssertThat(joined.Relation).IsEqual(GameplayActionExecutionRelation.Observed);

            AssertThat(session.Server.Interactive.CompleteExecution(requester.ExecutionId))
                .IsTrue();
            await session.Pump(RoundTripFrames);

            AssertThat(
                    session.ClientA.Interactive.TryGetExecutionPresentation(ActivateAction, out _)
                )
                .IsFalse();
            AssertThat(
                    session.ClientB.Interactive.TryGetExecutionPresentation(ActivateAction, out _)
                )
                .IsFalse();
            AssertThat(late.Scene.Interactive.TryGetExecutionPresentation(ActivateAction, out _))
                .IsFalse();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task AuthorityOnlyAcknowledgesLifecycleWithoutLeakingExecutionPresentation()
    {
        Session session = await Connect();
        try
        {
            session.SetExecutionVisibility(GameplayActionExecutionVisibility.AuthorityOnly);
            session.Arm(new GameplayActionExecutionRunning(), 3600.0f);
            session.Focus();

            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.KindsA()).IsEqual(new List<string> { "started" });
            AssertThat(
                    session.Server.Interactive.TryGetExecutionPresentation(ActivateAction, out _)
                )
                .IsTrue();
            AssertThat(
                    session.ClientA.Interactive.TryGetExecutionPresentation(ActivateAction, out _)
                )
                .IsFalse();
            AssertThat(
                    session.ClientB.Interactive.TryGetExecutionPresentation(ActivateAction, out _)
                )
                .IsFalse();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task NativeSynchronizerVisibilityHidesThenRevealsTheCurrentSnapshot()
    {
        Session session = await Connect();
        try
        {
            session.SetExecutionVisibility(GameplayActionExecutionVisibility.Replicated);
            session.Arm(new GameplayActionExecutionRunning(), 3600.0f);
            session.Focus();
            int observerPeerId = session.Server.InteractorB.Runner!.OwnerPeerId;
            AssertThat(observerPeerId).IsEqual(session.ClientB.Root.Multiplayer.GetUniqueId());
            session.Server.ExecutionSynchronizer.PublicVisibility = false;
            session.Server.ExecutionSynchronizer.SetVisibilityFor(
                session.Server.InteractorA.Runner!.OwnerPeerId,
                true
            );
            session.Server.ExecutionSynchronizer.UpdateVisibility();
            AssertThat(session.Server.ExecutionSynchronizer.GetVisibilityFor(observerPeerId))
                .IsFalse();
            AssertThat(
                    session.ClientB.Interactive.TryGetExecutionPresentation(ActivateAction, out _)
                )
                .IsFalse();

            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(
                    session.ClientA.Interactive.TryGetExecutionPresentation(ActivateAction, out _)
                )
                .IsTrue();
            AssertThat(
                    session.ClientB.Interactive.TryGetExecutionPresentation(ActivateAction, out _)
                )
                .IsFalse();

            session.Server.ExecutionSynchronizer.SetVisibilityFor(observerPeerId, true);
            session.Server.ExecutionSynchronizer.UpdateVisibility(observerPeerId);
            await session.Pump(RoundTripFrames);

            AssertThat(
                    session.ClientB.Interactive.TryGetExecutionPresentation(ActivateAction, out _)
                )
                .IsTrue();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task ADroppedPeerReleasesItsExecutionOnTheAuthority()
    {
        // The node departure is not the only way out. Here nobody despawns the dropped player, so only
        // the interactor's own subscription to the session frees the target — without it the
        // reservation lasted forever and locked the target for everybody else.
        Session session = await Connect();
        try
        {
            session.Arm(new GameplayActionExecutionRunning(), duration: 5.0f);
            session.Focus();
            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);
            ulong reserved = session.Server.Executor.LastExecutionId;

            session.Peers[1].Close();
            await session.Pump(60);
            session.ClientB.InteractorB.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.Server.Interactive.IsExecutionActive(reserved)).IsFalse();
            AssertThat(session.KindsB()).IsEqual(new List<string> { "started" });
            AssertThat(session.Server.Executor.ExecuteCount).IsEqual(2);
        }
        finally
        {
            session.Close();
        }
    }
}
