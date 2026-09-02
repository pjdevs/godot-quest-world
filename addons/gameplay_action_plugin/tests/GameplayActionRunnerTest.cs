namespace QuestWorld.Tests.GameplayActions;

using GdUnit4;
using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Access;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Bindings;
using QuestWorld.GameplayActions.Runtime.Runner;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class GameplayActionRunnerTest
{
    [TestCase]
    public void PressReleaseHoldAndAutomaticUseTheirOwnActivationEdges()
    {
        GameplayActionRunner runner = CreateRunnerWithOwnedActions(
            out GameplayActionComponent component,
            ("press", new TestGameplayActionExecutor()),
            ("release", new TestGameplayActionExecutor()),
            ("hold", new TestGameplayActionExecutor()),
            ("automatic", new TestGameplayActionExecutor())
        );
        Node source = AutoFree(new Node());
        TestGameplayActionExecutor press = Executor(component, "press");
        TestGameplayActionExecutor release = Executor(component, "release");
        TestGameplayActionExecutor hold = Executor(component, "hold");
        TestGameplayActionExecutor automatic = Executor(component, "automatic");
        runner.BindAction(
            component,
            "press",
            source,
            Config("press", GameplayActionActivationMode.Press)
        );
        runner.BindAction(
            component,
            "release",
            source,
            Config("release", GameplayActionActivationMode.Release)
        );
        runner.BindAction(
            component,
            "hold",
            source,
            Config("hold", GameplayActionActivationMode.Hold, holdDuration: 0.5f)
        );
        runner.BindAction(
            component,
            "automatic",
            source,
            Config(string.Empty, GameplayActionActivationMode.Automatic)
        );

        AssertThat(runner.TryStartActionInput("press")).IsTrue();
        AssertThat(runner.TryStartActionInput("release")).IsTrue();
        AssertThat(release.ExecuteCount).IsEqual(0);
        AssertThat(runner.TryEndActionInput("release")).IsTrue();
        AssertThat(runner.TryStartActionInput("hold")).IsTrue();
        runner.AdvanceGestures(0.49f);
        AssertThat(hold.ExecuteCount).IsEqual(0);
        runner.AdvanceGestures(0.01f);

        AssertThat(press.ExecuteCount).IsEqual(1);
        AssertThat(release.ExecuteCount).IsEqual(1);
        AssertThat(hold.ExecuteCount).IsEqual(1);
        AssertThat(automatic.ExecuteCount).IsEqual(1);
    }

    [TestCase]
    public void HoldDefersPressAndCapturesCandidatesUntilTheGestureEnds()
    {
        GameplayActionRunner runner = CreateRunnerWithOwnedActions(
            out GameplayActionComponent component,
            ("tap", new TestGameplayActionExecutor()),
            ("hold", new TestGameplayActionExecutor()),
            ("late", new TestGameplayActionExecutor())
        );
        Node source = AutoFree(new Node());
        runner.BindAction(
            component,
            "tap",
            source,
            Config("use", GameplayActionActivationMode.Press)
        );
        GameplayActionBinding holdBinding = runner.BindAction(
            component,
            "hold",
            source,
            Config("use", GameplayActionActivationMode.Hold, holdDuration: 1.0f)
        )!;

        runner.TryStartActionInput("use");
        runner.BindAction(
            component,
            "late",
            source,
            Config("use", GameplayActionActivationMode.Hold, holdDuration: 0.1f, priority: 100)
        );
        runner.AdvanceGestures(0.2f);

        AssertThat(Executor(component, "tap").ExecuteCount).IsEqual(0);
        AssertThat(Executor(component, "hold").ExecuteCount).IsEqual(0);
        AssertThat(Executor(component, "late").ExecuteCount).IsEqual(0);

        runner.TryEndActionInput("use");
        AssertThat(Executor(component, "tap").ExecuteCount).IsEqual(1);

        runner.TryStartActionInput("use");
        runner.UnbindAction(holdBinding.Id);
        runner.AdvanceGestures(1.0f);
        runner.TryEndActionInput("use");

        AssertThat(Executor(component, "hold").ExecuteCount).IsEqual(0);
        AssertThat(Executor(component, "late").ExecuteCount).IsEqual(1);
    }

    [TestCase]
    public void ConflictResolutionPrefersAllowedThenPriorityThenStableHostAndActionIdentity()
    {
        TestGameplayActionExecutor allowedLow = new();
        TestGameplayActionExecutor allowedHighB = new();
        TestGameplayActionExecutor allowedHighA = new();
        TestGameplayActionExecutor blocked = new();
        GameplayActionRunner runner = CreateRunnerWithOwnedActions(
            out GameplayActionComponent component,
            ("low", allowedLow),
            ("b", allowedHighB),
            ("a", allowedHighA),
            ("blocked", blocked)
        );
        component
            .ResolveAction("blocked")!
            .Rules.Add(new MutableRule(new GameplayActionBlocked("No charge.")));
        Node source = AutoFree(new Node());
        runner.BindAction(
            component,
            "blocked",
            source,
            Config("use", GameplayActionActivationMode.Press, priority: 100)
        );
        runner.BindAction(
            component,
            "low",
            source,
            Config("use", GameplayActionActivationMode.Press, priority: 1)
        );
        runner.BindAction(
            component,
            "b",
            source,
            Config("use", GameplayActionActivationMode.Press, priority: 10)
        );
        runner.BindAction(
            component,
            "a",
            source,
            Config("use", GameplayActionActivationMode.Press, priority: 10)
        );

        AssertThat(runner.TryStartActionInput("use")).IsTrue();

        AssertThat(allowedHighA.ExecuteCount).IsEqual(1);
        AssertThat(allowedHighB.ExecuteCount).IsEqual(0);
        AssertThat(allowedLow.ExecuteCount).IsEqual(0);
        AssertThat(blocked.ExecuteCount).IsEqual(0);
    }

    [TestCase]
    public void PressedRequirementCancelsTheAcceptedExecutionOnReleaseAfterBindingLoss()
    {
        TestGameplayActionExecutor executor = new()
        {
            Result = new GameplayActionExecutionRunning(),
        };
        GameplayActionRunner runner = CreateRunnerWithOwnedActions(
            out GameplayActionComponent component,
            ("charge", executor)
        );
        Node source = AutoFree(new Node());
        GameplayActionBinding binding = runner.BindAction(
            component,
            "charge",
            source,
            Config(
                "charge",
                GameplayActionActivationMode.Press,
                inputRequirement: GameplayActionInputRequirement.Pressed
            )
        )!;

        runner.TryStartActionInput("charge");
        runner.UnbindAction(binding.Id);
        runner.TryEndActionInput("charge");

        AssertThat(executor.CancelledCount).IsEqual(1);
        AssertThat(component.IsActionExecuting("charge")).IsFalse();
    }

    [TestCase]
    public void ExternalActionRequiresItsAuthoritativeTypedAccessProvider()
    {
        TestGameplayActionExecutor executor = new();
        GameplayActionComponent owned = AutoFree(new GameplayActionComponent());
        GameplayActionComponent external = AutoFree(new GameplayActionComponent());
        AccessControlledAction action = AutoFree(
            new AccessControlledAction
            {
                Definition = new GameplayActionDefinition { Id = "open" },
                Executor = executor,
            }
        );
        action.AddChild(executor);
        external.AddAction(action);
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = owned }
        );
        Node source = AutoFree(new Node());
        GameplayActionBinding binding = runner.BindAction(
            external,
            "open",
            source,
            Config("use", GameplayActionActivationMode.Press)
        )!;

        AssertThat(runner.TryStartActionInput("use")).IsFalse();
        AssertThat(executor.ExecuteCount).IsEqual(0);

        TestAccessProvider provider = new() { Allowed = true };
        runner.RegisterAccessProvider(AccessControlledAction.ProviderId, provider);
        runner.InvalidateBinding(binding.Id);

        AssertThat(runner.TryStartActionInput("use")).IsTrue();
        AssertThat(executor.ExecuteCount).IsEqual(1);
        AssertThat(provider.RequestChecks).IsEqual(2);
    }

    [TestCase]
    public void SustainedExternalAccessIsCheckedByTheAuthorityWhileTheExecutionRuns()
    {
        TestGameplayActionExecutor executor = new()
        {
            Result = new GameplayActionExecutionRunning(),
        };
        GameplayActionComponent owned = AutoFree(new GameplayActionComponent());
        GameplayActionComponent external = AutoFree(new GameplayActionComponent());
        AccessControlledAction action = AutoFree(
            new AccessControlledAction
            {
                Definition = new GameplayActionDefinition { Id = "channel" },
                Executor = executor,
            }
        );
        action.AddChild(executor);
        external.AddAction(action);
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = owned }
        );
        TestAccessProvider provider = new() { Allowed = true };
        runner.RegisterAccessProvider(AccessControlledAction.ProviderId, provider);
        Node source = AutoFree(new Node());
        runner.BindAction(
            external,
            "channel",
            source,
            Config("use", GameplayActionActivationMode.Press)
        );

        runner.TryStartActionInput("use");
        provider.Allowed = false;
        runner.ValidateSustainedExecutions();

        AssertThat(executor.CancelledCount).IsEqual(1);
        AssertThat(external.IsActionExecuting("channel")).IsFalse();
        AssertThat(provider.RequestChecks).IsEqual(3);
    }

    [TestCase]
    public async System.Threading.Tasks.Task StaleTerminalAcknowledgementCannotEndAgain()
    {
        Node world = new() { Name = "World" };
        GameplayActionComponent component = new() { Name = "Actions" };
        TestGameplayActionExecutor executor = new()
        {
            Result = new GameplayActionExecutionRunning(),
        };
        GameplayAction action = new()
        {
            Name = "Charge",
            Definition = new GameplayActionDefinition { Id = "charge" },
            Executor = executor,
        };
        action.AddChild(executor);
        component.AddAction(action);
        GameplayActionRunner runner = new() { Name = "Runner", OwnedActionComponent = component };
        world.AddChild(component);
        world.AddChild(runner);
        ISceneRunner scene = ISceneRunner.Load(world);
        await scene.SimulateFrames(1);
        runner.BindAction(
            component,
            "charge",
            component,
            Config("charge", GameplayActionActivationMode.Press)
        );
        long executionId = 0;
        int completions = 0;
        runner.GameplayActionStarted += (_, _, startedId) => executionId = startedId;
        runner.GameplayActionCompleted += (_, _, _) => completions++;

        runner.TryStartActionInput("charge");
        component.CompleteExecution((ulong)executionId);
        NodePath componentPath = world.GetTree().Root.GetPathTo(component);
        runner.ClientActionCompleted(componentPath, "charge", executionId);

        AssertThat(executionId).IsGreater(0L);
        AssertThat(completions).IsEqual(1);
    }

    [TestCase]
    public void RequesterTeardownCancelsPresenceOwnedButLeavesWorldOwnedExecutionRunning()
    {
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        PresencePolicyExecutor presenceOwned = AutoFree(
            new PresencePolicyExecutor(requiresRequesterPresence: true)
        );
        PresencePolicyExecutor worldOwned = AutoFree(
            new PresencePolicyExecutor(requiresRequesterPresence: false)
        );
        GameplayAction presenceAction = AutoFree(
            new GameplayAction
            {
                Definition = new GameplayActionDefinition { Id = "presence" },
                Executor = presenceOwned,
                HostConcurrencyGroup = "presence",
            }
        );
        GameplayAction worldAction = AutoFree(
            new GameplayAction
            {
                Definition = new GameplayActionDefinition { Id = "world" },
                Executor = worldOwned,
                HostConcurrencyGroup = "world",
            }
        );
        presenceAction.AddChild(presenceOwned);
        worldAction.AddChild(worldOwned);
        component.AddAction(presenceAction);
        component.AddAction(worldAction);
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = component }
        );
        Node source = AutoFree(new Node());
        runner.BindAction(
            component,
            "presence",
            source,
            Config("presence", GameplayActionActivationMode.Press)
        );
        runner.BindAction(
            component,
            "world",
            source,
            Config("world", GameplayActionActivationMode.Press)
        );

        runner.TryStartActionInput("presence");
        runner.TryStartActionInput("world");
        runner._ExitTree();

        AssertThat(presenceOwned.CancelledCount).IsEqual(1);
        AssertThat(worldOwned.CancelledCount).IsEqual(0);
        AssertThat(component.IsActionExecuting("presence")).IsFalse();
        AssertThat(component.IsActionExecuting("world")).IsTrue();
    }

    private static GameplayActionRunner CreateRunnerWithOwnedActions(
        out GameplayActionComponent component,
        params (string Id, TestGameplayActionExecutor Executor)[] actions
    )
    {
        component = AutoFree(new GameplayActionComponent { Name = "Actions" });
        foreach ((string id, TestGameplayActionExecutor executor) in actions)
        {
            GameplayAction action = AutoFree(
                new GameplayAction
                {
                    Name = id,
                    Definition = new GameplayActionDefinition { Id = new StringName(id) },
                    Executor = executor,
                }
            );
            action.AddChild(executor);
            component.AddAction(action);
        }

        return AutoFree(new GameplayActionRunner { OwnedActionComponent = component });
    }

    private static GameplayActionBindingConfig Config(
        string input,
        GameplayActionActivationMode mode,
        float holdDuration = 0.0f,
        int priority = 0,
        GameplayActionInputRequirement inputRequirement = GameplayActionInputRequirement.None
    ) =>
        new()
        {
            InputActionName = new StringName(input),
            ActivationMode = mode,
            HoldDuration = holdDuration,
            Priority = priority,
            InputRequirement = inputRequirement,
        };

    private static TestGameplayActionExecutor Executor(
        GameplayActionComponent component,
        string id
    ) => (TestGameplayActionExecutor)component.ResolveAction(new StringName(id))!.Executor!;

    private sealed partial class MutableRule(GameplayActionAvailability result)
        : QuestWorld.GameplayActions.Runtime.Rules.GameplayActionRule
    {
        public GameplayActionAvailability Result { get; set; } = result;

        public override GameplayActionAvailability Evaluate(in GameplayActionContext context) =>
            Result;
    }

    private sealed partial class AccessControlledAction : GameplayAction
    {
        public static readonly StringName ProviderId = new("test-access");

        public override StringName AccessProviderId => ProviderId;
    }

    private sealed class TestAccessProvider : IGameplayActionAccessProvider
    {
        public bool Allowed { get; set; }

        public int RequestChecks { get; private set; }

        public bool CanRequest(in GameplayActionAccessContext context)
        {
            RequestChecks++;
            return Allowed;
        }
    }

    private sealed partial class PresencePolicyExecutor(bool requiresRequesterPresence)
        : GameplayActionExecutor
    {
        public int CancelledCount { get; private set; }

        public override bool RequiresRequesterPresence => requiresRequesterPresence;

        public override GameplayActionExecutionResult Execute(
            in GameplayActionExecutionContext context
        ) => new GameplayActionExecutionRunning();

        protected internal override void OnExecutionCancelled(
            in GameplayActionExecutionContext context,
            string reason
        ) => CancelledCount++;
    }
}