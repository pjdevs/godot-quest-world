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
public sealed partial class InteractionNetworkTest : InteractionNetworkTestBase
{
    [TestCase]
    public async Task AStartedActionIsAcknowledgedToTheRequesterAlone()
    {
        Session session = await Connect();
        try
        {
            session.Arm(new GameplayActionExecutionRunning(), duration: 2.0f);
            session.Focus();

            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.KindsA()).IsEqual(new List<string> { "started" });
            AssertThat(session.KindsB()).IsEmpty();
            Ack started = session.AcksA[0];
            AssertThat(started.ExecutionId).IsGreater(0ul);
            // Each peer resolves the path in its own branch, so the requester is handed its own copy.
            AssertThat(started.Target == session.ClientA.Interactive).IsTrue();
            // The command ran once, on the authority, and nowhere else.
            AssertThat(session.Server.Executor.LastExecutionId).IsGreater(0ul);
            AssertThat(session.ClientA.Executor.LastExecutionId).IsEqual(0ul);
            AssertThat(session.ClientB.Executor.LastExecutionId).IsEqual(0ul);
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task ACompletionIsAcknowledgedToTheRequesterAlone()
    {
        Session session = await Connect();
        try
        {
            session.Arm(new GameplayActionExecutionRunning(), duration: 2.0f);
            session.Focus();
            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            session.Server.Interactive.CompleteExecution(session.Server.Executor.LastExecutionId);
            await session.Pump(RoundTripFrames);

            AssertThat(session.KindsA()).IsEqual(new List<string> { "started", "completed" });
            AssertThat(session.KindsB()).IsEmpty();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task ASecondClientIsRefusedWhileTheFirstHoldsTheAction()
    {
        Session session = await Connect();
        try
        {
            session.Arm(new GameplayActionExecutionRunning(), duration: 2.0f);
            session.Focus();
            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            session.ClientB.InteractorB.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            // One execution started, and the peer that lost learns it without the winner hearing a
            // thing about a request that was never its own.
            AssertThat(session.KindsA()).IsEqual(new List<string> { "started" });
            AssertThat(session.KindsB()).IsEqual(new List<string> { "rejected" });
            AssertThat(
                    session.Server.Interactive.IsExecutionActive(
                        session.Server.Executor.LastExecutionId
                    )
                )
                .IsTrue();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task AFailureCrossesTheNetworkAsStartedThenFailed()
    {
        Session session = await Connect();
        try
        {
            session.Arm(new GameplayActionExecutionFailed("The socket is welded shut."), 0.0f);
            session.Focus();

            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.KindsA()).IsEqual(new List<string> { "started", "failed" });
            AssertThat(session.AcksA[1].Reason).IsEqual("The socket is welded shut.");
            AssertThat(session.KindsB()).IsEmpty();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task ARefusalCrossesTheNetworkWithoutEverStarting()
    {
        Session session = await Connect();
        try
        {
            session.Arm(new GameplayActionExecutionRejected("The till is closed."), 0.0f);
            session.Focus();

            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.KindsA()).IsEqual(new List<string> { "rejected" });
            AssertThat(session.AcksA[0].Reason).IsEqual("The till is closed.");
            AssertThat(
                    session.ClientA.Interactive.TryGetExecutionPresentation(ActivateAction, out _)
                )
                .IsFalse();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task TwoClientsRequestingTheSameActionInOneFrameStartASingleExecution()
    {
        // The race the doc calls out: both peers ask before anything replicated, so both believe
        // they may. Exactly one command runs, and the loser learns it alone.
        Session session = await Connect();
        try
        {
            session.Arm(new GameplayActionExecutionRunning(), duration: 5.0f);
            session.Focus();

            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            session.ClientB.InteractorB.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.Server.Executor.ExecuteCount).IsEqual(1);
            List<string> a = session.KindsA();
            List<string> b = session.KindsB();
            AssertThat(a.Count + b.Count).IsEqual(2);
            AssertThat(a.Contains("started") ^ b.Contains("started")).IsTrue();
            AssertThat(a.Contains("rejected") ^ b.Contains("rejected")).IsTrue();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task ARemoteClientDrawsItsBarBeforeTheAuthorityAnswers()
    {
        // The duration is a query every peer may run, so the requester runs it on its own copy of the
        // executor and draws at once. Interacting has no "starting" state: the player pressed, the bar
        // is there, and the acknowledgement only has the last word on its length.
        Session session = await Connect();
        try
        {
            session.Arm(new GameplayActionExecutionRunning(), duration: 5.0f);
            session.Focus();

            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);

            AssertThat(
                    session.ClientA.Interactive.TryGetExecutionPresentation(
                        ActivateAction,
                        out GameplayActionExecutionPresentation presentation
                    )
                )
                .IsTrue();
            AssertThat(presentation.ActionId.ToString()).IsEqual("activate");
            AssertThat(presentation.Progress.HasValue).IsTrue();
            AssertThat(presentation.Progress!.Value < 1.0f).IsTrue();

            await session.Pump(RoundTripFrames);

            AssertThat(
                    session.ClientA.Interactive.TryGetExecutionPresentation(ActivateAction, out _)
                )
                .IsTrue();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task TheAcknowledgementArmsABarTheClientCouldNotPredict()
    {
        // The two copies of the executor answer differently, which is what reading state a client does
        // not have looks like from here: this one declines to draw, and the authority hands it a
        // deadline one round trip later.
        Session session = await Connect();
        try
        {
            session.Arm(new GameplayActionExecutionRunning(), duration: 5.0f);
            session.ClientA.Executor.Duration = null;
            session.Focus();

            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);

            AssertThat(
                    session.ClientA.Interactive.TryGetExecutionPresentation(ActivateAction, out _)
                )
                .IsFalse();

            await session.Pump(RoundTripFrames);

            AssertThat(
                    session.ClientA.Interactive.TryGetExecutionPresentation(ActivateAction, out _)
                )
                .IsTrue();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task TheAcknowledgementClearsABarTheClientInvented()
    {
        // The mirror case, and the one that proves who has the last word: the client predicts an hour
        // where the authority reserved no deadline at all. Nothing completes here, so only the started
        // acknowledgement can take that bar away.
        Session session = await Connect();
        try
        {
            session.Arm(new GameplayActionExecutionRunning(), duration: 0.0f);
            session.Server.Executor.Duration = null;
            session.ClientA.Executor.Duration = 3600.0f;
            session.Focus();

            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);

            AssertThat(
                    session.ClientA.Interactive.TryGetExecutionPresentation(ActivateAction, out _)
                )
                .IsTrue();

            await session.Pump(RoundTripFrames);

            AssertThat(session.KindsA()).IsEqual(new List<string> { "started" });
            AssertThat(
                    session.ClientA.Interactive.TryGetExecutionPresentation(
                        ActivateAction,
                        out GameplayActionExecutionPresentation presentation
                    )
                )
                .IsTrue();
            AssertThat(presentation.Progress.HasValue).IsFalse();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task TheClientThatLostTheRaceClearsItsPredictionAtOnce()
    {
        Session session = await Connect();
        try
        {
            session.Arm(new GameplayActionExecutionRunning(), duration: 5.0f);
            session.Focus();
            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            session.ClientB.InteractorB.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.KindsB()).IsEqual(new List<string> { "rejected" });
            // The loser drew a bar at its own press, like the winner did, and the refusal takes it
            // away: an unacknowledged prediction is exactly what a refusal invalidates.
            AssertThat(
                    session.ClientB.Interactive.TryGetExecutionPresentation(ActivateAction, out _)
                )
                .IsFalse();
            // The winner keeps drawing its own bar.
            AssertThat(
                    session.ClientA.Interactive.TryGetExecutionPresentation(ActivateAction, out _)
                )
                .IsTrue();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task AReleasedLongActionFreesTheTargetForTheOtherClient()
    {
        Session session = await Connect();
        try
        {
            session.Arm(new GameplayActionExecutionRunning(), duration: 5.0f);
            session.Focus();
            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            session.ClientA.InteractorA.TryEndInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);
            session.ClientB.InteractorB.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.KindsA()).IsEqual(new List<string> { "started", "cancelled" });
            AssertThat(session.KindsB()).IsEqual(new List<string> { "started" });
            AssertThat(session.Server.Executor.ExecuteCount).IsEqual(2);
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task AnInteractorLeavingTheTreeFreesTheTargetForTheOtherClient()
    {
        // A disconnection reaches the plugin as the departure of the interactor node, which is what
        // the project spawn layer does when a player leaves. Nothing else releases the reservation.
        Session session = await Connect();
        try
        {
            session.Arm(new GameplayActionExecutionRunning(), duration: 5.0f);
            session.Focus();
            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            session.Server.InteractorA.GetParent().RemoveChild(session.Server.InteractorA);
            await session.Pump(RoundTripFrames);
            session.ClientB.InteractorB.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.KindsB()).IsEqual(new List<string> { "started" });
            AssertThat(session.Server.Executor.ExecuteCount).IsEqual(2);
        }
        finally
        {
            session.Server.InteractorA.Free();
            session.Close();
        }
    }

    [TestCase]
    public async Task AClientLeavingTheAuthoritativeWindowLosesItsExecution()
    {
        Session session = await Connect();
        try
        {
            session.Arm(new GameplayActionExecutionRunning(), duration: 5.0f);
            session.Focus();
            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            session.UnfocusAOnAuthority();
            await session.Pump(RoundTripFrames);
            session.ClientB.InteractorB.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.KindsA()).IsEqual(new List<string> { "started", "cancelled" });
            AssertThat(session.KindsB()).IsEqual(new List<string> { "started" });
        }
        finally
        {
            session.Close();
        }
    }
}
