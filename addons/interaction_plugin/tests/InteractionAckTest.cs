namespace QuestWorld.Tests;

using System.Collections.Generic;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using static GdUnit4.Assertions;

/// <summary>Covers the authoritative acknowledgement the requesting peer receives.</summary>
/// <remarks>
/// Every world here owns its interactor, which is the listen host and the offline game at once. That
/// is deliberate: the acknowledgement must reach a local owner by a direct call rather than by an
/// RPC, so these tests are what proves the host is not silently skipped. The remote half is the RPC
/// declaration itself, which no in-process test can exercise without a real peer.
/// </remarks>
[TestSuite]
[RequireGodotRuntime]
public sealed partial class InteractionAckTest
{
    private static readonly StringName InteractInput = new("interact");

    [TestCase]
    public async Task AnInstantActionAcknowledgesStartedThenCompleted()
    {
        AckWorld world = BuildWorld();
        await world.Runner.SimulateFrames(1);
        world.Focus();

        world.Interactor.TryStartInteractionInput(InteractInput);

        AssertThat(world.Kinds()).IsEqual(new List<string> { "started", "completed" });
    }

    [TestCase]
    public async Task AnAcknowledgementCarriesTheTargetAndTheActionItCorrelatesWith()
    {
        AckWorld world = BuildWorld();
        await world.Runner.SimulateFrames(1);
        world.Focus();

        world.Interactor.TryStartInteractionInput(InteractInput);

        Ack started = world.Acks[0];
        AssertThat(started.Target == world.Interactive).IsTrue();
        AssertThat(started.ActionId.ToString()).IsEqual("activate");
        AssertThat(started.ExecutionId).IsGreater(0ul);
    }

    [TestCase]
    public async Task ARunningActionAcknowledgesWithoutExposingDuration()
    {
        AckWorld world = BuildWorld();
        world.Executor.Duration = 2.0f;
        world.Executor.Result = new InteractionExecutionRunning();
        await world.Runner.SimulateFrames(1);
        world.Focus();

        world.Interactor.TryStartInteractionInput(InteractInput);

        AssertThat(world.Kinds()).IsEqual(new List<string> { "started" });
        AssertThat(
                world.Interactive.TryGetExecutionPresentation(
                    world.Definition.Id,
                    out InteractionExecutionPresentation presentation
                )
            )
            .IsTrue();
        AssertThat(presentation.Progress.HasValue).IsTrue();
    }

    [TestCase]
    public async Task ACompletedRunningActionAcknowledgesCompletedExactlyOnce()
    {
        AckWorld world = BuildWorld();
        world.Executor.Duration = 2.0f;
        world.Executor.Result = new InteractionExecutionRunning();
        await world.Runner.SimulateFrames(1);
        world.Focus();
        world.Interactor.TryStartInteractionInput(InteractInput);

        world.Interactive.CompleteExecution(world.Executor.LastExecutionId);

        AssertThat(world.Kinds()).IsEqual(new List<string> { "started", "completed" });
    }

    [TestCase]
    public async Task ACancelledRunningActionAcknowledgesItsReason()
    {
        AckWorld world = BuildWorld();
        world.Executor.Duration = 2.0f;
        world.Executor.Result = new InteractionExecutionRunning();
        await world.Runner.SimulateFrames(1);
        world.Focus();
        world.Interactor.TryStartInteractionInput(InteractInput);

        world.Interactive.CancelExecution(world.Executor.LastExecutionId, "The reactor tripped.");

        AssertThat(world.Kinds()).IsEqual(new List<string> { "started", "cancelled" });
        AssertThat(world.Acks[1].Reason).IsEqual("The reactor tripped.");
    }

    [TestCase]
    public async Task AFailedActionAcknowledgesStartedThenFailedInsteadOfRejected()
    {
        // The authority accepted the command and only then discovered the error, so reporting a
        // rejection would tell the owner that nothing ever ran.
        AckWorld world = BuildWorld();
        world.Executor.Result = new InteractionExecutionFailed("The socket is welded shut.");
        await world.Runner.SimulateFrames(1);
        world.Focus();

        world.Interactor.TryStartInteractionInput(InteractInput);

        AssertThat(world.Kinds()).IsEqual(new List<string> { "started", "failed" });
        AssertThat(world.Acks[1].Reason).IsEqual("The socket is welded shut.");
    }

    [TestCase]
    public async Task AnActionRefusedAtTheExecutionBoundaryIsRejectedAndNeverStarted()
    {
        AckWorld world = BuildWorld();
        world.Executor.Result = new InteractionExecutionRejected("The till is closed.");
        await world.Runner.SimulateFrames(1);
        world.Focus();

        world.Interactor.TryStartInteractionInput(InteractInput);

        AssertThat(world.Kinds()).IsEqual(new List<string> { "rejected" });
        AssertThat(world.Acks[0].Reason).IsEqual("The till is closed.");
    }

    [TestCase]
    public async Task AHostRefusedByItsOwnAuthorityIsAcknowledgedExactlyOnce()
    {
        // The host requests like any other peer: it must not be the one player who learns nothing
        // when the authoritative half of its own process refuses.
        AckWorld world = BuildWorld();
        world.Executor.Result = new InteractionExecutionRejected("The till is closed.");
        await world.Runner.SimulateFrames(1);
        world.Focus();

        world.Interactor.TryStartInteractionInput(InteractInput);

        AssertThat(world.Kinds()).HasSize(1);
    }

    [TestCase]
    public async Task ARejectionClearsThePredictionOfTheRequestItRefuses()
    {
        AckWorld world = BuildWorld();
        world.Executor.Duration = 2.0f;
        world.Executor.Result = new InteractionExecutionRejected("The till is closed.");
        await world.Runner.SimulateFrames(1);
        world.Focus();

        world.Interactor.TryStartInteractionInput(InteractInput);

        AssertThat(world.Interactive.TryGetExecutionPresentation(world.Definition.Id, out _))
            .IsFalse();
    }

    [TestCase]
    public async Task ARejectionClearsSustainedExecutionButReleaseStillConsumesThePress()
    {
        // A refused request leaves nothing running, so release sends no server end for an execution
        // that never existed. The local press stays consumed until that release, however, and the
        // input boundary therefore reports that it handled the completed press cycle.
        AckWorld world = BuildWorld();
        world.Definition.CancelOnInputReleased = true;
        world.Executor.Duration = 2.0f;
        world.Executor.Result = new InteractionExecutionRunning();
        await world.Runner.SimulateFrames(1);
        world.Focus();
        world.Interactor.TryStartInteractionInput(InteractInput);
        world.Interactor.ClientInteractionRejected(
            world.Interactive.GetPath(),
            world.Definition.Id,
            "The till is closed."
        );

        bool reported = world.Interactor.TryEndInteractionInput(InteractInput);

        AssertThat(reported).IsTrue();
    }

    [TestCase]
    public async Task ARejectionLeavesTheBarOfARunningExecutionAlone()
    {
        // The bar belongs to an acknowledged execution and not to the last request, so a refusal on
        // the very action already running — a player pressing again mid-hack — must not erase it. The
        // rejection is played on the same identifier on purpose: that is the pair that used to match.
        AckWorld world = BuildWorld();
        world.Executor.Duration = 2.0f;
        world.Executor.Result = new InteractionExecutionRunning();
        await world.Runner.SimulateFrames(1);
        world.Focus();
        world.Interactor.TryStartInteractionInput(InteractInput);

        world.Interactor.ClientInteractionRejected(
            world.Interactive.GetPath(),
            world.Definition.Id,
            "This is already in use."
        );

        AssertThat(world.Interactive.TryGetExecutionPresentation(world.Definition.Id, out _))
            .IsTrue();
    }

    [TestCase]
    public async Task ADelayedAcknowledgementExtendsTheRemainingTimeWithoutVisibleRewind()
    {
        // The authority starts its clock half a round trip after the press and its completion needs the
        // other half to come back, so a bar that ran from the press would finish a full trip early. The
        // acknowledgement is replayed here after half a second to stand for a slow link: the prediction
        // has been counting since the press, and that is exactly the delay to add.
        AckWorld world = BuildWorld();
        world.Executor.Duration = 1.0f;
        world.Executor.Result = new InteractionExecutionRunning();
        await world.Runner.SimulateFrames(1);
        world.Focus();
        world.Interactor.TryStartInteractionInput(InteractInput);
        ulong authoritativeExecutionId = world.Executor.LastExecutionId;
        AssertThat(
                world.Interactive.RemoveRequesterExecution(
                    world.Definition.Id,
                    authoritativeExecutionId
                )
            )
            .IsTrue();
        world.Interactive.AddPredictedExecution(
            world.Definition.Id,
            new InteractionProgressSample(0.0f, 1.0f, 0L)
        );
        await world.Runner.SimulateFrames(30);
        AssertThat(
                world.Interactive.TryGetExecutionPresentation(
                    world.Definition.Id,
                    out InteractionExecutionPresentation beforeAck
                )
            )
            .IsTrue();
        float uncompensated = beforeAck.Progress!.Value;

        world.Interactor.ClientInteractionStarted(
            world.Interactive.GetPath(),
            world.Definition.Id,
            world.Executor.LastExecutionId + 1ul,
            true,
            0.0f,
            1.0f,
            1L
        );

        AssertThat(
                world.Interactive.TryGetExecutionPresentation(
                    world.Definition.Id,
                    out InteractionExecutionPresentation afterAck
                )
            )
            .IsTrue();
        float compensated = afterAck.Progress!.Value;
        // No frame ran between the two reads. Reconciliation changes the remaining rate, not the value
        // the player already saw, so a lower value here would be a visible rewind at ACK.
        AssertThat(compensated).IsEqualApprox(uncompensated, 0.0005f);
    }

    [TestCase]
    public async Task AnExecutionEndedBeforeItsDeadlineTakesItsBarWithIt()
    {
        // Nothing local ends here: the input was never sustained, so only the terminal acknowledgement
        // can say the execution stopped short. Without it the bar would draw down to a deadline that
        // no longer exists.
        AckWorld world = BuildWorld();
        world.Executor.Duration = 3600.0f;
        world.Executor.Result = new InteractionExecutionRunning();
        await world.Runner.SimulateFrames(1);
        world.Focus();
        world.Interactor.TryStartInteractionInput(InteractInput);
        AssertThat(world.Interactive.TryGetExecutionPresentation(world.Definition.Id, out _))
            .IsTrue();

        world.Interactive.CancelExecution(world.Executor.LastExecutionId, "The reactor tripped.");

        AssertThat(world.Interactive.TryGetExecutionPresentation(world.Definition.Id, out _))
            .IsFalse();
    }

    [TestCase]
    public async Task AReleasedInputClearsThePredictionWithoutWaitingForItsAcknowledgement()
    {
        // The bar belongs to the local prediction and disappears with no round trip; the terminal
        // acknowledgement is what everything else closes on, and arrives afterwards.
        AckWorld world = BuildWorld();
        world.Definition.CancelOnInputReleased = true;
        world.Executor.Duration = 2.0f;
        world.Executor.Result = new InteractionExecutionRunning();
        await world.Runner.SimulateFrames(1);
        world.Focus();
        world.Interactor.TryStartInteractionInput(InteractInput);

        world.Interactor.TryEndInteractionInput(InteractInput);

        AssertThat(world.Interactive.TryGetExecutionPresentation(world.Definition.Id, out _))
            .IsFalse();
        AssertThat(world.Kinds()).IsEqual(new List<string> { "started", "cancelled" });
    }

    [TestCase]
    public async Task AVendorWindowOpensAndClosesOnTheAcknowledgementAlone()
    {
        // The case the acknowledgement exists for: a non-blocking window needs no replicated state
        // and no downstream network session, only the authoritative lifecycle of its own request.
        AckWorld world = BuildWorld();
        world.Executor.Duration = 0.0f;
        world.Executor.Result = new InteractionExecutionRunning();
        VendorWindow window = new();
        window.Listen(world.Interactor);
        await world.Runner.SimulateFrames(1);
        world.Focus();

        world.Interactor.TryStartInteractionInput(InteractInput);
        AssertThat(window.IsOpen).IsTrue();
        AssertThat(window.OpenCount).IsEqual(1);

        world.Interactive.CompleteExecution(world.Executor.LastExecutionId);
        AssertThat(window.IsOpen).IsFalse();
        AssertThat(window.CloseCount).IsEqual(1);
    }

    [TestCase]
    public async Task AVendorWindowNeverOpensOnARefusedRequest()
    {
        AckWorld world = BuildWorld();
        world.Executor.Result = new InteractionExecutionRejected("The till is closed.");
        VendorWindow window = new();
        window.Listen(world.Interactor);
        await world.Runner.SimulateFrames(1);
        world.Focus();

        world.Interactor.TryStartInteractionInput(InteractInput);

        AssertThat(window.OpenCount).IsEqual(0);
        AssertThat(window.IsOpen).IsFalse();
    }

    [TestCase]
    public async Task AVendorWindowOpenedByAnAcknowledgementClosesOnAFailure()
    {
        AckWorld world = BuildWorld();
        world.Executor.Result = new InteractionExecutionFailed("The socket is welded shut.");
        VendorWindow window = new();
        window.Listen(world.Interactor);
        await world.Runner.SimulateFrames(1);
        world.Focus();

        world.Interactor.TryStartInteractionInput(InteractInput);

        AssertThat(window.OpenCount).IsEqual(1);
        AssertThat(window.IsOpen).IsFalse();
    }

    [TestCase]
    public async Task AnAutomaticActionRefusedIsNotRequestedAgainWhileNothingChanges()
    {
        // Forgetting the automatic request on a refusal without remembering the refused pair would
        // re-send the very same request on the very next frame, turning one refusal into a flood.
        AckWorld world = BuildWorld();
        world.Action.Automatic = true;
        world.Executor.Result = new InteractionExecutionRejected("The till is closed.");
        await world.Runner.SimulateFrames(1);

        world.Focus();
        world.Interactor.RecalculateFocus();
        world.Interactor.RecalculateFocus();

        AssertThat(world.Executor.ExecuteCount).IsEqual(1);
        AssertThat(world.Kinds()).IsEqual(new List<string> { "rejected" });
    }

    [TestCase]
    public async Task AnAutomaticActionRefusedTriesAgainOnceGameplayInvalidatesItsTarget()
    {
        // The refusal is a backoff, not a permanent ban: the moment the situation it was decided
        // against changes, the automatic action is allowed to ask again.
        AckWorld world = BuildWorld();
        world.Action.Automatic = true;
        world.Executor.Result = new InteractionExecutionRejected("The till is closed.");
        await world.Runner.SimulateFrames(1);
        world.Focus();

        world.Interactive.NotifyStatusChanged();

        AssertThat(world.Executor.ExecuteCount).IsEqual(2);
    }

    [TestCase]
    public async Task AnAutomaticActionRefusedTriesAgainAfterTheFocusMoved()
    {
        AckWorld world = BuildWorld();
        world.Action.Automatic = true;
        world.Executor.Result = new InteractionExecutionRejected("The till is closed.");
        await world.Runner.SimulateFrames(1);
        world.Focus();

        world.Unfocus();
        world.Focus();

        AssertThat(world.Executor.ExecuteCount).IsEqual(2);
    }

    private static AckWorld BuildWorld()
    {
        Node3D world = new() { Name = "World" };
        Node3D actor = new() { Name = "Actor", Position = new Vector3(0, 0, -2) };
        Area3D area = new() { Name = "InteractionArea" };
        area.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 3.0f } });
        InteractiveComponent interactive = new()
        {
            Name = "Interactive",
            InteractionArea = area,
            InteractionAnchor = actor,
        };
        InteractionActionDefinition definition = new()
        {
            Id = new StringName("activate"),
            Label = "Activate",
            InputActionName = InteractInput,
        };
        InteractionAction action = new() { Name = "ActivateAction", Definition = definition };
        TestScriptedExecutor executor = new() { Name = "ActivateExecutor" };
        action.AddChild(executor);
        action.Executor = executor;
        interactive.Actions.Add(action);
        actor.AddChild(area);
        actor.AddChild(interactive);
        interactive.AddChild(action);

        Node3D view = new() { Name = "ViewOrigin" };
        InteractionInteractor interactor = new() { Name = "Interactor", OwnerPeerId = 1 };
        interactor.AddChild(view);
        TestInteractionDetector detector = new() { Name = "Detector", ViewOrigin = view };
        interactor.AddChild(detector);
        interactor.Detector = detector;
        world.AddChild(actor);
        world.AddChild(interactor);

        ISceneRunner runner = ISceneRunner.Load(world);
        AckWorld built = new(
            runner,
            interactive,
            interactor,
            detector,
            definition,
            action,
            executor,
            new List<Ack>()
        );
        built.Listen();
        return built;
    }

    private sealed record Ack(
        string Kind,
        Node? Target,
        StringName ActionId,
        ulong ExecutionId,
        string Reason
    );

    private sealed record AckWorld(
        ISceneRunner Runner,
        InteractiveComponent Interactive,
        InteractionInteractor Interactor,
        TestInteractionDetector Detector,
        InteractionActionDefinition Definition,
        InteractionAction Action,
        TestScriptedExecutor Executor,
        List<Ack> Acks
    )
    {
        public void Listen()
        {
            Interactor.InteractionStarted += (target, actionId, executionId) =>
                Acks.Add(new Ack("started", target, actionId, executionId, string.Empty));
            Interactor.InteractionCompleted += (target, actionId) =>
                Acks.Add(new Ack("completed", target, actionId, 0ul, string.Empty));
            Interactor.InteractionCancelled += (target, actionId, reason) =>
                Acks.Add(new Ack("cancelled", target, actionId, 0ul, reason));
            Interactor.InteractionFailed += (target, actionId, reason) =>
                Acks.Add(new Ack("failed", target, actionId, 0ul, reason));
            Interactor.InteractionRejected += (target, actionId, reason) =>
                Acks.Add(new Ack("rejected", target, actionId, 0ul, reason));
        }

        /// <summary>Detects the target as interactible and runs the pipeline once, like a frame would.</summary>
        public void Focus()
        {
            Detector.SetDetection(Interactive, InteractionDetectionKind.Interactible);
            Interactor.RecalculateFocus();
        }

        /// <summary>Stops detecting the target and runs the pipeline once.</summary>
        public void Unfocus()
        {
            Detector.ClearDetection(Interactive);
            Interactor.RecalculateFocus();
        }

        public List<string> Kinds()
        {
            List<string> kinds = new();
            foreach (Ack ack in Acks)
            {
                kinds.Add(ack.Kind);
            }

            return kinds;
        }
    }

    /// <summary>Local window a non-blocking vendor opens, driven by the acknowledgement only.</summary>
    private sealed class VendorWindow
    {
        public bool IsOpen { get; private set; }

        public int OpenCount { get; private set; }

        public int CloseCount { get; private set; }

        public void Listen(InteractionInteractor interactor)
        {
            interactor.InteractionStarted += (_, _, _) => Open();
            interactor.InteractionCompleted += (_, _) => Close();
            interactor.InteractionCancelled += (_, _, _) => Close();
            interactor.InteractionFailed += (_, _, _) => Close();
        }

        private void Open()
        {
            IsOpen = true;
            OpenCount++;
        }

        private void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            CloseCount++;
        }
    }
}
