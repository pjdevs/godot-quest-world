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
public sealed partial class InteractionNetworkStateTest : InteractionNetworkTestBase
{
    [TestCase]
    public async Task TwoClientsOnTwoTargetsBothStartWithoutHearingEachOther()
    {
        Session session = await Connect();
        try
        {
            session.Arm(new GameplayActionExecutionRunning(), duration: 5.0f);
            session.FocusA(session.Server.Interactive, session.ClientA.Interactive);
            session.FocusB(session.Server.SecondInteractive, session.ClientB.SecondInteractive);

            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            session.ClientB.InteractorB.TryStartInteractionInput(InteractInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.KindsA()).IsEqual(new List<string> { "started" });
            AssertThat(session.KindsB()).IsEqual(new List<string> { "started" });
            AssertThat(session.AcksA[0].Target == session.ClientA.Interactive).IsTrue();
            AssertThat(session.AcksB[0].Target == session.ClientB.SecondInteractive).IsTrue();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task TwoClientsOnDistinctConcurrencyGroupsOfOneTargetBothStart()
    {
        // Exclusivity is a property of the concurrency group, not of the target: two commands that
        // do not exclude each other may be held at once by two different players.
        Session session = await Connect();
        try
        {
            session.Arm(new GameplayActionExecutionRunning(), duration: 5.0f);
            session.Focus();

            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            session.ClientB.InteractorB.TryStartInteractionInput(TuneInput);
            await session.Pump(RoundTripFrames);

            AssertThat(session.KindsA()).IsEqual(new List<string> { "started" });
            AssertThat(session.KindsB()).IsEqual(new List<string> { "started" });
            AssertThat(session.AcksA[0].ActionId.ToString()).IsEqual("activate");
            AssertThat(session.AcksB[0].ActionId.ToString()).IsEqual("tune");
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task ALongActionCompletedByItsOwnClockIsAcknowledgedToItsRequesterAlone()
    {
        Session session = await Connect();
        try
        {
            session.Arm(new GameplayActionExecutionRunning(), duration: 0.2f);
            session.Focus();

            session.ClientA.InteractorA.TryStartInteractionInput(InteractInput);
            await session.Pump(30, millisecondsPerFrame: 20);

            AssertThat(session.KindsA()).IsEqual(new List<string> { "started", "completed" });
            AssertThat(session.KindsB()).IsEmpty();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task AReplicatedTransitionPlaysTheFeedbackOfEachPeerExactlyOnce()
    {
        Session session = await Connect();
        try
        {
            StateLog onServer = session.WatchState(session.Server);
            StateLog onA = session.WatchState(session.ClientA);
            StateLog onB = session.WatchState(session.ClientB);

            session.Server.Stateful.SetState(ActivatingState);
            await session.Pump(RoundTripFrames);
            foreach (PeerScene scene in new[] { session.Server, session.ClientA, session.ClientB })
            {
                AssertThat(scene.Stateful.State.ToString()).IsEqual("activating");
            }

            foreach (StateLog log in new[] { onServer, onA, onB })
            {
                AssertThat(log.Changed).IsEqual(new List<string> { "activating" });
                AssertThat(log.Presentation).IsEqual(new List<string> { "activating" });
                // Lived by everybody: the clients were already connected, so their arrival was spent on
                // the full sync of an untouched object. Nobody here is catching up.
                AssertThat(log.Synchronizations).IsEqual(new List<bool> { false });
            }

            // The server of this harness is a listen host: it is the only peer with authority, and
            // it plays its presentation like anybody else.
            AssertThat(onServer.Authority).IsEqual(new List<string> { "activating" });
            AssertThat(onA.Authority).IsEmpty();
            AssertThat(onB.Authority).IsEmpty();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task AStateSetTwiceToTheSameValueReplicatesNothing()
    {
        Session session = await Connect();
        try
        {
            session.Server.Stateful.SetState(ActivatingState);
            await session.Pump(RoundTripFrames);
            StateLog onServer = session.WatchState(session.Server);
            StateLog onA = session.WatchState(session.ClientA);

            session.Server.Stateful.SetState(ActivatingState);
            await session.Pump(RoundTripFrames);

            AssertThat(onServer.Changed).IsEmpty();
            AssertThat(onA.Changed).IsEmpty();
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task TwoTransitionsSeparatedByAFrameArriveInOrderAndPlayOnce()
    {
        Session session = await Connect();
        try
        {
            StateLog onA = session.WatchState(session.ClientA);
            StateLog onB = session.WatchState(session.ClientB);

            session.Server.Stateful.SetState(ActivatingState);
            await session.Pump(RoundTripFrames);
            session.Server.Stateful.SetState(ActivatedState);
            await session.Pump(RoundTripFrames);

            List<string> expected = new() { "activating", "activated" };
            AssertThat(onA.Presentation).IsEqual(expected);
            AssertThat(onB.Presentation).IsEqual(expected);
        }
        finally
        {
            session.Close();
        }
    }

    [TestCase]
    public async Task TwoTransitionsInsideOneFrameReachClientsAsTheLastValueOnly()
    {
        // A replicated property carries a value, not a history. A one-shot feedback keyed on a
        // transition a client never receives simply never plays, which is why a pose is applied from
        // the current state and only sounds and effects are left to transitions.
        Session session = await Connect();
        try
        {
            StateLog onA = session.WatchState(session.ClientA);

            session.Server.Stateful.SetState(ActivatingState);
            session.Server.Stateful.SetState(ActivatedState);
            await session.Pump(RoundTripFrames);

            AssertThat(session.ClientA.Stateful.State.ToString()).IsEqual("activated");
            AssertThat(onA.Presentation).IsEqual(new List<string> { "activated" });
        }
        finally
        {
            session.Close();
        }
    }
}
