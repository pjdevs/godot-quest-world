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
    public async Task ARunningActionAcknowledgesTheDurationTheExecutorDeclared()
    {
        AckWorld world = BuildWorld();
        world.Executor.Duration = 2.0f;
        world.Executor.Result = new InteractionExecutionRunning();
        await world.Runner.SimulateFrames(1);
        world.Focus();

        world.Interactor.TryStartInteractionInput(InteractInput);

        AssertThat(world.Kinds()).IsEqual(new List<string> { "started" });
        AssertThat(world.Acks[0].Duration).IsEqual(2.0f);
    }

    [TestCase]
    public async Task ARunningActionAcknowledgesTheClockItsExecutorTookOver()
    {
        // The executor knows the length of the clip it just played, so the owner must be told that
        // value rather than the estimate the reservation was built with.
        AckWorld world = BuildWorld();
        world.Executor.Duration = 2.0f;
        world.Executor.Result = new InteractionExecutionRunning(5.0f);
        await world.Runner.SimulateFrames(1);
        world.Focus();

        world.Interactor.TryStartInteractionInput(InteractInput);

        AssertThat(world.Acks[0].Duration).IsEqual(5.0f);
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

        AssertThat(world.Interactor.TryGetExecutionProgress(out _, out _)).IsFalse();
    }

    [TestCase]
    public async Task ARejectionClearsTheSustainedInputItsRequestCreated()
    {
        // A refused request left nothing running, so the release that follows must find nothing to
        // report rather than send the server an end for an execution that never existed.
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

        AssertThat(reported).IsFalse();
    }

    [TestCase]
    public async Task ARejectionLeavesThePredictionOfAnotherActionAlone()
    {
        AckWorld world = BuildWorld();
        world.Executor.Duration = 2.0f;
        world.Executor.Result = new InteractionExecutionRunning();
        await world.Runner.SimulateFrames(1);
        world.Focus();
        world.Interactor.TryStartInteractionInput(InteractInput);

        world.Interactor.ClientInteractionRejected(
            world.Interactive.GetPath(),
            new StringName("somethingElse"),
            "The till is closed."
        );

        AssertThat(world.Interactor.TryGetExecutionProgress(out _, out _)).IsTrue();
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

        AssertThat(world.Interactor.TryGetExecutionProgress(out _, out _)).IsFalse();
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
        float Duration,
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
            Interactor.InteractionStarted += (target, actionId, executionId, duration) =>
                Acks.Add(new Ack("started", target, actionId, executionId, duration, string.Empty));
            Interactor.InteractionCompleted += (target, actionId) =>
                Acks.Add(new Ack("completed", target, actionId, 0ul, 0.0f, string.Empty));
            Interactor.InteractionCancelled += (target, actionId, reason) =>
                Acks.Add(new Ack("cancelled", target, actionId, 0ul, 0.0f, reason));
            Interactor.InteractionFailed += (target, actionId, reason) =>
                Acks.Add(new Ack("failed", target, actionId, 0ul, 0.0f, reason));
            Interactor.InteractionRejected += (target, actionId, reason) =>
                Acks.Add(new Ack("rejected", target, actionId, 0ul, 0.0f, reason));
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
            interactor.InteractionStarted += (_, _, _, _) => Open();
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
