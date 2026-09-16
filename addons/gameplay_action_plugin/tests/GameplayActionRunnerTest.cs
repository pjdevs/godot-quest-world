namespace QuestWorld.Tests.GameplayActions;

using System;
using System.Threading.Tasks;
using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Access;
using GameplayActionPlugin.Runtime.Actions;
using GameplayActionPlugin.Runtime.Bindings;
using GameplayActionPlugin.Runtime.Runner;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Runtime")]
public sealed partial class GameplayActionRunnerTest
{
    [TestCase]
    public void RequesterConcurrencyIsOptIn()
    {
        GameplayAction action = AutoFree(new GameplayAction());

        AssertThat(action.GetRequesterConcurrencyGroup().IsEmpty).IsTrue();
    }

    [TestCase]
    public void RunningActionBlocksTheSameRequesterGroupAcrossComponents()
    {
        TestGameplayActionExecutor firstExecutor = new()
        {
            Result = new GameplayActionExecutionRunning(),
        };
        GameplayActionRunner runner = CreateRunnerWithOwnedActions(
            out GameplayActionComponent owned,
            ("first", firstExecutor)
        );
        owned.ResolveAction("first")!.RequesterConcurrencyGroup = "shared";
        GameplayActionComponent external = AutoFree(new GameplayActionComponent());
        TestGameplayActionExecutor secondExecutor = new()
        {
            Result = new GameplayActionExecutionRunning(),
        };
        GameplayAction secondAction = AddExternalAction(external, "second", secondExecutor);
        secondAction.RequesterConcurrencyGroup = "shared";
        runner.RegisterAccessProvider(
            AccessControlledAction.ProviderId,
            new TestAccessProvider { Allowed = true }
        );

        GameplayActionBinding firstBinding = runner.BindAction(
            owned,
            "first",
            AutoFree(new Node()),
            Config("first", GameplayActionActivationMode.Press)
        )!;
        GameplayActionBinding secondBinding = runner.BindAction(
            external,
            "second",
            AutoFree(new Node()),
            Config("second", GameplayActionActivationMode.Press)
        )!;

        AssertThat(runner.TryStartActionInput(firstBinding.InputActionName)).IsTrue();
        AssertThat(
                runner.GetBindingAvailability(secondBinding.Id) is GameplayActionBlocked blocked
                    && blocked.Reason == GameplayActionAvailabilityExtensions.UnavailableReason
            )
            .IsTrue();
        AssertThat(runner.TryStartActionInput(secondBinding.InputActionName)).IsFalse();
        AssertThat(secondExecutor.ExecuteCount).IsEqual(0);
    }

    [TestCase]
    public void RequesterConcurrencyIsLocalToEachRunner()
    {
        TestGameplayActionExecutor firstExecutor = new()
        {
            Result = new GameplayActionExecutionRunning(),
        };
        GameplayActionRunner firstRunner = CreateRunnerWithOwnedActions(
            out GameplayActionComponent firstComponent,
            ("first", firstExecutor)
        );
        TestGameplayActionExecutor secondExecutor = new()
        {
            Result = new GameplayActionExecutionRunning(),
        };
        GameplayActionRunner secondRunner = CreateRunnerWithOwnedActions(
            out GameplayActionComponent secondComponent,
            ("second", secondExecutor)
        );
        firstComponent.ResolveAction("first")!.RequesterConcurrencyGroup = "shared";
        secondComponent.ResolveAction("second")!.RequesterConcurrencyGroup = "shared";

        GameplayActionBinding firstBinding = firstRunner.BindAction(
            firstComponent,
            "first",
            AutoFree(new Node()),
            Config("first", GameplayActionActivationMode.Press)
        )!;
        GameplayActionBinding secondBinding = secondRunner.BindAction(
            secondComponent,
            "second",
            AutoFree(new Node()),
            Config("second", GameplayActionActivationMode.Press)
        )!;

        AssertThat(firstRunner.TryStartActionInput(firstBinding.InputActionName)).IsTrue();
        AssertThat(secondRunner.GetBindingAvailability(secondBinding.Id) is GameplayActionAllowed)
            .IsTrue();
        AssertThat(secondRunner.TryStartActionInput(secondBinding.InputActionName)).IsTrue();
    }

    [TestCase]
    public void DifferentRequesterGroupsCanRunConcurrently()
    {
        TestGameplayActionExecutor firstExecutor = new()
        {
            Result = new GameplayActionExecutionRunning(),
        };
        GameplayActionRunner runner = CreateRunnerWithOwnedActions(
            out GameplayActionComponent firstComponent,
            ("first", firstExecutor)
        );
        firstComponent.ResolveAction("first")!.RequesterConcurrencyGroup = "hands";
        GameplayActionComponent secondComponent = AutoFree(new GameplayActionComponent());
        TestGameplayActionExecutor secondExecutor = new()
        {
            Result = new GameplayActionExecutionRunning(),
        };
        GameplayAction secondAction = AddExternalAction(secondComponent, "second", secondExecutor);
        secondAction.RequesterConcurrencyGroup = "dialogue";
        runner.RegisterAccessProvider(
            AccessControlledAction.ProviderId,
            new TestAccessProvider { Allowed = true }
        );

        GameplayActionBinding firstBinding = runner.BindAction(
            firstComponent,
            "first",
            AutoFree(new Node()),
            Config("first", GameplayActionActivationMode.Press)
        )!;
        GameplayActionBinding secondBinding = runner.BindAction(
            secondComponent,
            "second",
            AutoFree(new Node()),
            Config("second", GameplayActionActivationMode.Press)
        )!;

        AssertThat(runner.TryStartActionInput(firstBinding.InputActionName)).IsTrue();
        AssertThat(runner.GetBindingAvailability(secondBinding.Id) is GameplayActionAllowed)
            .IsTrue();
        AssertThat(runner.TryStartActionInput(secondBinding.InputActionName)).IsTrue();
        AssertThat(secondExecutor.ExecuteCount).IsEqual(1);
    }

    [TestCase]
    public void EmptyRequesterGroupOptsOutOfRequesterConcurrency()
    {
        TestGameplayActionExecutor firstExecutor = new()
        {
            Result = new GameplayActionExecutionRunning(),
        };
        GameplayActionRunner runner = CreateRunnerWithOwnedActions(
            out GameplayActionComponent firstComponent,
            ("first", firstExecutor)
        );
        firstComponent.ResolveAction("first")!.RequesterConcurrencyGroup = "hands";
        GameplayActionComponent secondComponent = AutoFree(new GameplayActionComponent());
        TestGameplayActionExecutor secondExecutor = new()
        {
            Result = new GameplayActionExecutionRunning(),
        };
        GameplayAction secondAction = AddExternalAction(secondComponent, "second", secondExecutor);
        secondAction.RequesterConcurrencyGroup = new StringName();
        runner.RegisterAccessProvider(
            AccessControlledAction.ProviderId,
            new TestAccessProvider { Allowed = true }
        );

        GameplayActionBinding firstBinding = runner.BindAction(
            firstComponent,
            "first",
            AutoFree(new Node()),
            Config("first", GameplayActionActivationMode.Press)
        )!;
        GameplayActionBinding secondBinding = runner.BindAction(
            secondComponent,
            "second",
            AutoFree(new Node()),
            Config("second", GameplayActionActivationMode.Press)
        )!;

        AssertThat(runner.TryStartActionInput(firstBinding.InputActionName)).IsTrue();
        AssertThat(runner.GetBindingAvailability(secondBinding.Id) is GameplayActionAllowed)
            .IsTrue();
        AssertThat(runner.TryStartActionInput(secondBinding.InputActionName)).IsTrue();
        AssertThat(secondExecutor.ExecuteCount).IsEqual(1);
    }

    [TestCase]
    public void HiddenRequesterBusyActionsAreRemovedFromCandidates()
    {
        TestGameplayActionExecutor firstExecutor = new()
        {
            Result = new GameplayActionExecutionRunning(),
        };
        GameplayActionRunner runner = CreateRunnerWithOwnedActions(
            out GameplayActionComponent firstComponent,
            ("first", firstExecutor)
        );
        firstComponent.ResolveAction("first")!.RequesterConcurrencyGroup = "shared";
        GameplayActionComponent secondComponent = AutoFree(new GameplayActionComponent());
        TestGameplayActionExecutor secondExecutor = new()
        {
            Result = new GameplayActionExecutionRunning(),
        };
        GameplayAction secondAction = AddExternalAction(secondComponent, "second", secondExecutor);
        secondAction.WhenRequesterBusy = GameplayActionUnavailableKind.Hidden;
        secondAction.RequesterConcurrencyGroup = "shared";
        runner.RegisterAccessProvider(
            AccessControlledAction.ProviderId,
            new TestAccessProvider { Allowed = true }
        );

        GameplayActionBinding firstBinding = runner.BindAction(
            firstComponent,
            "first",
            AutoFree(new Node()),
            Config("first", GameplayActionActivationMode.Press)
        )!;
        GameplayActionBinding secondBinding = runner.BindAction(
            secondComponent,
            "second",
            AutoFree(new Node()),
            Config("second", GameplayActionActivationMode.Press)
        )!;

        AssertThat(runner.TryStartActionInput(firstBinding.InputActionName)).IsTrue();
        AssertThat(runner.GetBindingAvailability(secondBinding.Id) is GameplayActionHidden)
            .IsTrue();
        AssertThat(runner.TryStartActionInput(secondBinding.InputActionName)).IsFalse();
        AssertThat(secondExecutor.ExecuteCount).IsEqual(0);
    }

    [TestCase]
    public void RequesterConcurrencyUsesConfiguredGroupReason()
    {
        RequesterConcurrencyFixture fixture = CreateRequesterConcurrencyFixture(
            new GameplayActionExecutionRunning(),
            new GameplayActionExecutionRunning()
        );
        fixture.Runner.RequesterConcurrencyReasons["shared"] = "Your hands are not free.";

        AssertThat(fixture.Runner.TryStartActionInput(fixture.FirstBinding.InputActionName))
            .IsTrue();
        AssertThat(
                fixture.Runner.GetBindingAvailability(fixture.SecondBinding.Id)
                    is GameplayActionBlocked blocked
                    && blocked.Reason == "Your hands are not free."
            )
            .IsTrue();
    }

    [TestCase]
    public void RequesterConcurrencyIsReleasedWhenExecutionCompletes()
    {
        TestGameplayActionExecutor firstExecutor = new()
        {
            Result = new GameplayActionExecutionRunning(),
        };
        GameplayActionRunner runner = CreateRunnerWithOwnedActions(
            out GameplayActionComponent firstComponent,
            ("first", firstExecutor)
        );
        firstComponent.ResolveAction("first")!.RequesterConcurrencyGroup = "shared";
        GameplayActionComponent secondComponent = AutoFree(new GameplayActionComponent());
        TestGameplayActionExecutor secondExecutor = new()
        {
            Result = new GameplayActionExecutionRunning(),
        };
        GameplayAction secondAction = AddExternalAction(secondComponent, "second", secondExecutor);
        secondAction.RequesterConcurrencyGroup = "shared";
        runner.RegisterAccessProvider(
            AccessControlledAction.ProviderId,
            new TestAccessProvider { Allowed = true }
        );

        GameplayActionBinding firstBinding = runner.BindAction(
            firstComponent,
            "first",
            AutoFree(new Node()),
            Config("first", GameplayActionActivationMode.Press)
        )!;
        GameplayActionBinding secondBinding = runner.BindAction(
            secondComponent,
            "second",
            AutoFree(new Node()),
            Config("second", GameplayActionActivationMode.Press)
        )!;

        AssertThat(runner.TryStartActionInput(firstBinding.InputActionName)).IsTrue();
        AssertThat(runner.GetBindingAvailability(secondBinding.Id) is GameplayActionBlocked)
            .IsTrue();
        AssertThat(firstComponent.CompleteExecution(1)).IsTrue();
        AssertThat(runner.GetBindingAvailability(secondBinding.Id) is GameplayActionAllowed)
            .IsTrue();
        AssertThat(runner.TryStartActionInput(secondBinding.InputActionName)).IsTrue();
    }

    [TestCase]
    public void RequesterConcurrencyIsReleasedWhenExecutionIsCancelled()
    {
        RequesterConcurrencyFixture fixture = CreateRequesterConcurrencyFixture(
            new GameplayActionExecutionRunning(),
            new GameplayActionExecutionRunning()
        );

        AssertThat(fixture.Runner.TryStartActionInput(fixture.FirstBinding.InputActionName))
            .IsTrue();
        AssertThat(
                fixture.Runner.GetBindingAvailability(fixture.SecondBinding.Id)
                    is GameplayActionBlocked
            )
            .IsTrue();
        AssertThat(fixture.Runner.TryEndActionInput(fixture.FirstBinding.InputActionName)).IsTrue();
        AssertThat(
                fixture.Runner.GetBindingAvailability(fixture.SecondBinding.Id)
                    is GameplayActionAllowed
            )
            .IsTrue();
    }

    [TestCase]
    public void RequesterConcurrencyIsReleasedWhenExecutionFails()
    {
        RequesterConcurrencyFixture fixture = CreateRequesterConcurrencyFixture(
            new GameplayActionExecutionRunning(),
            new GameplayActionExecutionRunning()
        );

        AssertThat(fixture.Runner.TryStartActionInput(fixture.FirstBinding.InputActionName))
            .IsTrue();
        AssertThat(fixture.FirstComponent.FailExecution(1, "failed")).IsTrue();
        AssertThat(
                fixture.Runner.GetBindingAvailability(fixture.SecondBinding.Id)
                    is GameplayActionAllowed
            )
            .IsTrue();
    }

    [TestCase]
    public void RejectedExecutionDoesNotLeaveRequesterConcurrencyOccupied()
    {
        RequesterConcurrencyFixture fixture = CreateRequesterConcurrencyFixture(
            new GameplayActionExecutionRejected("not now"),
            new GameplayActionExecutionRunning()
        );

        AssertThat(fixture.Runner.TryStartActionInput(fixture.FirstBinding.InputActionName))
            .IsFalse();
        AssertThat(
                fixture.Runner.GetBindingAvailability(fixture.SecondBinding.Id)
                    is GameplayActionAllowed
            )
            .IsTrue();
    }

    [TestCase]
    public void ReleasingRequesterDependencyDoesNotReleaseRequesterConcurrency()
    {
        GameplayActionComponent firstComponent = AutoFree(new GameplayActionComponent());
        ReleaseDuringExecuteExecutor firstExecutor = new();
        AccessControlledAction firstAction = AutoFree(
            new AccessControlledAction
            {
                Definition = new GameplayActionDefinition { Id = "first" },
                Executor = firstExecutor,
            }
        );
        firstAction.AddChild(firstExecutor);
        firstComponent.AddAction(firstAction);

        GameplayActionComponent secondComponent = AutoFree(new GameplayActionComponent());
        TestGameplayActionExecutor secondExecutor = new()
        {
            Result = new GameplayActionExecutionRunning(),
        };
        AddExternalAction(secondComponent, "second", secondExecutor);
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = firstComponent }
        );
        firstComponent.ResolveAction("first")!.RequesterConcurrencyGroup = "shared";
        secondComponent.ResolveAction("second")!.RequesterConcurrencyGroup = "shared";
        TrackingReservation reservation = new();
        runner.RegisterAccessProvider(
            AccessControlledAction.ProviderId,
            new TestAccessProvider { Allowed = true, Reservation = reservation }
        );

        GameplayActionBinding firstBinding = runner.BindAction(
            firstComponent,
            "first",
            AutoFree(new Node()),
            Config("first", GameplayActionActivationMode.Press)
        )!;
        GameplayActionBinding secondBinding = runner.BindAction(
            secondComponent,
            "second",
            AutoFree(new Node()),
            Config("second", GameplayActionActivationMode.Press)
        )!;

        AssertThat(runner.TryStartActionInput(firstBinding.InputActionName)).IsTrue();
        AssertThat(firstExecutor.RequesterDependencyReleased).IsTrue();
        AssertThat(firstExecutor.AccessReservationReleased).IsTrue();
        AssertThat(reservation.ReleaseCount).IsEqual(1);
        AssertThat(runner.GetBindingAvailability(secondBinding.Id) is GameplayActionBlocked)
            .IsTrue();
    }

    [TestCase]
    public void ProgrammaticExecutionDoesNotOccupyRequesterConcurrency()
    {
        TestGameplayActionExecutor firstExecutor = new()
        {
            Result = new GameplayActionExecutionRunning(),
        };
        GameplayActionRunner runner = CreateRunnerWithOwnedActions(
            out GameplayActionComponent firstComponent,
            ("first", firstExecutor)
        );
        firstComponent.ResolveAction("first")!.RequesterConcurrencyGroup = "shared";
        GameplayActionComponent secondComponent = AutoFree(new GameplayActionComponent());
        TestGameplayActionExecutor secondExecutor = new()
        {
            Result = new GameplayActionExecutionRunning(),
        };
        GameplayAction secondAction = AddExternalAction(secondComponent, "second", secondExecutor);
        secondAction.RequesterConcurrencyGroup = "shared";
        runner.RegisterAccessProvider(
            AccessControlledAction.ProviderId,
            new TestAccessProvider { Allowed = true }
        );
        GameplayActionBinding secondBinding = runner.BindAction(
            secondComponent,
            "second",
            AutoFree(new Node()),
            Config("second", GameplayActionActivationMode.Press)
        )!;

        AssertThat(firstComponent.ExecuteAction("first", out _) is GameplayActionExecutionRunning)
            .IsTrue();
        AssertThat(runner.GetBindingAvailability(secondBinding.Id) is GameplayActionAllowed)
            .IsTrue();
        AssertThat(runner.TryStartActionInput(secondBinding.InputActionName)).IsTrue();
    }

    [TestCase]
    public void PendingRequesterRequestOccupiesItsGroupBeforeExecutorReturns()
    {
        ReentrantRequesterExecutor firstExecutor = new();
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        GameplayAction firstAction = AutoFree(
            new GameplayAction
            {
                Definition = new GameplayActionDefinition { Id = "first" },
                Executor = firstExecutor,
                HostConcurrencyGroup = "first-host",
                RequesterConcurrencyGroup = "shared",
            }
        );
        TestGameplayActionExecutor secondExecutor = new()
        {
            Result = new GameplayActionExecutionRunning(),
        };
        GameplayAction secondAction = AutoFree(
            new GameplayAction
            {
                Definition = new GameplayActionDefinition { Id = "second" },
                Executor = secondExecutor,
                HostConcurrencyGroup = "second-host",
                RequesterConcurrencyGroup = "shared",
            }
        );
        firstAction.AddChild(firstExecutor);
        secondAction.AddChild(secondExecutor);
        component.AddAction(firstAction);
        component.AddAction(secondAction);
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = component }
        );
        firstExecutor.Runner = runner;
        GameplayActionBinding firstBinding = runner.BindAction(
            component,
            "first",
            AutoFree(new Node()),
            Config("first", GameplayActionActivationMode.Press)
        )!;
        GameplayActionBinding secondBinding = runner.BindAction(
            component,
            "second",
            AutoFree(new Node()),
            Config("second", GameplayActionActivationMode.Press)
        )!;

        AssertThat(runner.TryStartActionInput(firstBinding.InputActionName)).IsTrue();
        AssertThat(firstExecutor.SecondRequestResult).IsFalse();
        AssertThat(runner.GetBindingAvailability(secondBinding.Id) is GameplayActionBlocked)
            .IsTrue();
        AssertThat(secondExecutor.ExecuteCount).IsEqual(0);
    }

    [TestCase]
    public async Task RunnerClaimsServerAuthorityWhenInheritedFromPlayerRoot()
    {
        Node player = new() { Name = "Player_1883133663" };
        GameplayActionRunner runner = new() { ServerPeerId = 1 };
        player.AddChild(runner);
        player.SetMultiplayerAuthority(1883133663);

        ISceneRunner scene = ISceneRunner.Load(player, autoFree: true);
        await scene.SimulateFrames(1);

        AssertThat(runner.GetMultiplayerAuthority()).IsEqual(runner.ServerPeerId);
    }

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
    public void HoldPresentationUsesEachCapturedBindingThresholdAndInput()
    {
        GameplayActionRunner runner = CreateRunnerWithOwnedActions(
            out GameplayActionComponent component,
            ("short", new TestGameplayActionExecutor()),
            ("long", new TestGameplayActionExecutor()),
            ("other", new TestGameplayActionExecutor())
        );
        Node source = AutoFree(new Node());
        GameplayActionBinding shortBinding = runner.BindAction(
            component,
            "short",
            source,
            Config("use", GameplayActionActivationMode.Hold, holdDuration: 0.5f)
        )!;
        GameplayActionBinding longBinding = runner.BindAction(
            component,
            "long",
            source,
            Config("use", GameplayActionActivationMode.Hold, holdDuration: 1.0f)
        )!;
        GameplayActionBinding otherBinding = runner.BindAction(
            component,
            "other",
            source,
            Config("other", GameplayActionActivationMode.Hold, holdDuration: 0.75f)
        )!;

        AssertThat(
                runner.TryGetBinding(component, "short", source, out GameplayActionBinding? found)
            )
            .IsTrue();
        AssertThat(found!.Id).IsEqual(shortBinding.Id);
        AssertThat(runner.TryStartActionInput("use")).IsTrue();
        AssertThat(runner.TryStartActionInput("other")).IsTrue();
        runner.AdvanceGestures(0.25f);

        AssertHoldProgress(runner, shortBinding.Id, 0.5f, 0.25f);
        AssertHoldProgress(runner, longBinding.Id, 0.25f, 0.25f);
        AssertHoldProgress(runner, otherBinding.Id, 1.0f / 3.0f, 0.25f);
    }

    [TestCase]
    public void HoldPresentationOnlyReportsBindingsCapturedAtPress()
    {
        GameplayActionRunner runner = CreateRunnerWithOwnedActions(
            out GameplayActionComponent component,
            ("hold", new TestGameplayActionExecutor()),
            ("late", new TestGameplayActionExecutor()),
            ("press", new TestGameplayActionExecutor()),
            ("release", new TestGameplayActionExecutor())
        );
        Node source = AutoFree(new Node());
        GameplayActionBinding holdBinding = runner.BindAction(
            component,
            "hold",
            source,
            Config("use", GameplayActionActivationMode.Hold, holdDuration: 1.0f)
        )!;
        GameplayActionBinding pressBinding = runner.BindAction(
            component,
            "press",
            source,
            Config("press", GameplayActionActivationMode.Press)
        )!;
        GameplayActionBinding releaseBinding = runner.BindAction(
            component,
            "release",
            source,
            Config("release", GameplayActionActivationMode.Release)
        )!;

        AssertThat(runner.TryStartActionInput("use")).IsTrue();
        GameplayActionBinding lateBinding = runner.BindAction(
            component,
            "late",
            source,
            Config("use", GameplayActionActivationMode.Hold, holdDuration: 0.1f)
        )!;
        runner.AdvanceGestures(0.05f);

        AssertHoldProgress(runner, holdBinding.Id, 0.05f, 0.05f);
        AssertThat(runner.TryGetBindingHoldProgress(lateBinding.Id, out _, out _)).IsFalse();
        AssertThat(runner.TryGetBindingHoldProgress(pressBinding.Id, out _, out _)).IsFalse();
        AssertThat(runner.TryGetBindingHoldProgress(releaseBinding.Id, out _, out _)).IsFalse();

        runner.UnbindAction(holdBinding.Id);
        AssertThat(runner.TryGetBindingHoldProgress(holdBinding.Id, out _, out _)).IsFalse();
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
    public void OwnedActionWithAnAccessProviderStillAsksThatProvider()
    {
        TestGameplayActionExecutor executor = new();
        GameplayActionComponent owned = AutoFree(new GameplayActionComponent());
        AccessControlledAction action = AutoFree(
            new AccessControlledAction
            {
                Definition = new GameplayActionDefinition { Id = "open" },
                Executor = executor,
            }
        );
        action.AddChild(executor);
        owned.AddAction(action);
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = owned }
        );
        TestAccessProvider provider = new() { Allowed = false };
        runner.RegisterAccessProvider(AccessControlledAction.ProviderId, provider);
        Node source = AutoFree(new Node());
        Node target = AutoFree(new Node { Name = "Target" });
        GameplayActionBinding binding = runner.BindAction(
            owned,
            "open",
            source,
            Config("use", GameplayActionActivationMode.Press),
            target: target
        )!;

        AssertThat(runner.GetBindingAvailability(binding.Id) is GameplayActionHidden).IsTrue();
        AssertThat(runner.TryStartActionInput("use")).IsFalse();
        AssertThat(provider.RequestChecks).IsEqual(1);
        AssertThat(provider.LastTarget).IsEqual(target);

        provider.Allowed = true;
        runner.InvalidateBinding(binding.Id);

        AssertThat(runner.TryStartActionInput("use")).IsTrue();
        AssertThat(executor.ExecuteCount).IsEqual(1);
        AssertThat(provider.RequestChecks).IsEqual(3);
    }

    [TestCase]
    public void ExternalActionWithoutAnAccessProviderIsNotAccessible()
    {
        TestGameplayActionExecutor executor = new();
        GameplayActionComponent owned = AutoFree(new GameplayActionComponent());
        GameplayActionComponent external = AutoFree(new GameplayActionComponent());
        GameplayAction action = AutoFree(
            new GameplayAction
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

        GameplayActionBinding binding = runner.BindAction(
            external,
            "open",
            AutoFree(new Node()),
            Config("use", GameplayActionActivationMode.Press)
        )!;

        AssertThat(runner.GetBindingAvailability(binding.Id) is GameplayActionHidden).IsTrue();
        AssertThat(runner.TryStartActionInput("use")).IsFalse();
        AssertThat(executor.ExecuteCount).IsEqual(0);
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
        Node accessSource = AutoFree(new Node { Name = "AccessSource" });
        Node target = AutoFree(new Node { Name = "Target" });
        runner.BindAction(
            external,
            "channel",
            source,
            Config(
                "use",
                GameplayActionActivationMode.Press,
                inputRequirement: GameplayActionInputRequirement.Pressed
            ),
            target: target,
            accessSource: accessSource
        );

        runner.TryStartActionInput("use");
        AssertThat(provider.LastAccessSource).IsEqual(accessSource);
        AssertThat(provider.LastTarget).IsEqual(target);
        AssertThat(provider.LastReservationAccessSource).IsEqual(accessSource);
        AssertThat(provider.LastReservationTarget).IsEqual(target);
        provider.Allowed = false;
        runner.ValidateSustainedExecutions();

        AssertThat(executor.CancelledCount).IsEqual(1);
        AssertThat(external.IsActionExecuting("channel")).IsFalse();
        AssertThat(provider.RequestChecks).IsEqual(3);
        AssertThat(provider.LastSustainedAccessSource).IsEqual(accessSource);
        AssertThat(provider.LastSustainedTarget).IsEqual(target);
    }

    [TestCase]
    public void SustainedAccessLossBeforeCommitCancelsTheRequestedExecution()
    {
        CommitAwareExecutor executor = new();
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        AccessControlledAction action = AutoFree(
            new AccessControlledAction
            {
                Definition = new GameplayActionDefinition { Id = "take" },
                Executor = executor,
            }
        );
        action.AddChild(executor);
        component.AddAction(action);
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = component }
        );
        TargetValidityAccessProvider provider = new();
        TrackingReservation reservation = new();
        provider.Reservation = reservation;
        runner.RegisterAccessProvider(AccessControlledAction.ProviderId, provider);
        Node target = AutoFree(new Node { Name = "Target" });
        runner.BindAction(
            component,
            "take",
            AutoFree(new Node()),
            Config(
                "use",
                GameplayActionActivationMode.Press,
                inputRequirement: GameplayActionInputRequirement.Pressed
            ),
            target: target
        );

        AssertThat(runner.TryStartActionInput("use")).IsTrue();
        target.Free();
        runner.ValidateSustainedExecutions();

        AssertThat(executor.CancelledCount).IsEqual(1);
        AssertThat(component.IsActionExecuting("take")).IsFalse();
        AssertThat(reservation.ReleaseCount).IsEqual(1);
    }

    [TestCase]
    public void CommitReleasesSustainedAccessBeforeTheTargetDisappears()
    {
        CommitAwareExecutor executor = new();
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        AccessControlledAction action = AutoFree(
            new AccessControlledAction
            {
                Definition = new GameplayActionDefinition { Id = "take" },
                Executor = executor,
            }
        );
        action.AddChild(executor);
        component.AddAction(action);
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = component }
        );
        TargetValidityAccessProvider provider = new();
        TrackingReservation reservation = new();
        provider.Reservation = reservation;
        runner.RegisterAccessProvider(AccessControlledAction.ProviderId, provider);
        Node target = AutoFree(new Node { Name = "Target" });
        runner.BindAction(
            component,
            "take",
            AutoFree(new Node()),
            Config(
                "use",
                GameplayActionActivationMode.Press,
                inputRequirement: GameplayActionInputRequirement.Pressed
            ),
            target: target
        );

        AssertThat(runner.TryStartActionInput("use")).IsTrue();
        AssertThat(reservation.BindCount).IsEqual(1);
        AssertThat(reservation.ReleaseCount).IsEqual(0);
        AssertThat(executor.Commit()).IsTrue();
        target.Free();
        runner.ValidateSustainedExecutions();

        AssertThat(executor.CancelledCount).IsEqual(0);
        AssertThat(component.IsActionExecuting("take")).IsTrue();
        AssertThat(reservation.ReleaseCount).IsEqual(0);
        AssertThat(component.CompleteExecution(1)).IsTrue();
        AssertThat(reservation.ReleaseCount).IsEqual(1);
    }

    [TestCase]
    public void ReleaseDuringExecuteDetachesTheRunningExecutionBeforeValidation()
    {
        ReleaseDuringExecuteExecutor executor = new();
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        AccessControlledAction action = AutoFree(
            new AccessControlledAction
            {
                Definition = new GameplayActionDefinition { Id = "take" },
                Executor = executor,
                HostConcurrencyGroup = new StringName("take-group"),
            }
        );
        action.AddChild(executor);
        component.AddAction(action);
        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = component }
        );
        TrackingReservation reservation = new();
        TestAccessProvider provider = new() { Allowed = true, Reservation = reservation };
        runner.RegisterAccessProvider(AccessControlledAction.ProviderId, provider);
        Node target = AutoFree(new Node { Name = "Target" });
        runner.BindAction(
            component,
            "take",
            AutoFree(new Node()),
            Config(
                "use",
                GameplayActionActivationMode.Press,
                inputRequirement: GameplayActionInputRequirement.Pressed
            ),
            target: target
        );

        AssertThat(runner.TryStartActionInput("use")).IsTrue();
        AssertThat(executor.RequesterDependencyReleased).IsTrue();
        AssertThat(executor.AccessReservationReleased).IsTrue();
        AssertThat(reservation.BindCount).IsEqual(0);
        AssertThat(reservation.ReleaseCount).IsEqual(1);
        AssertThat(component.IsActionExecuting("take")).IsTrue();
        AssertThat(component.IsConcurrencyGroupExecuting("take-group")).IsTrue();

        provider.Allowed = false;
        runner.ValidateSustainedExecutions();

        AssertThat(executor.CancelledCount).IsEqual(0);
        AssertThat(component.IsActionExecuting("take")).IsTrue();
        AssertThat(component.CompleteExecution(1)).IsTrue();
        AssertThat(reservation.ReleaseCount).IsEqual(1);
    }

    [TestCase]
    public void RequestReservationLeaseLivesThroughRunningExecutionAndReleasesOnTerminal()
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
        TrackingReservation reservation = new();
        TestAccessProvider provider = new() { Allowed = true, Reservation = reservation };
        runner.RegisterAccessProvider(AccessControlledAction.ProviderId, provider);
        runner.BindAction(
            external,
            "channel",
            AutoFree(new Node()),
            Config("use", GameplayActionActivationMode.Press)
        );

        AssertThat(runner.TryStartActionInput("use")).IsTrue();
        AssertThat(reservation.BindCount).IsEqual(1);
        AssertThat(reservation.ReleaseCount).IsEqual(0);

        AssertThat(external.CompleteExecution(1)).IsTrue();
        AssertThat(reservation.ReleaseCount).IsEqual(1);
    }

    [TestCase]
    public void SynchronousRequestReservationLeaseReleasesForEveryTerminalOutcome()
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
        TestAccessProvider provider = new() { Allowed = true };
        runner.RegisterAccessProvider(AccessControlledAction.ProviderId, provider);
        runner.BindAction(
            external,
            "open",
            AutoFree(new Node()),
            Config("use", GameplayActionActivationMode.Press)
        );

        TrackingReservation completed = new();
        provider.Reservation = completed;
        executor.Result = new GameplayActionExecutionCompleted();
        AssertThat(runner.TryStartActionInput("use")).IsTrue();
        AssertThat(completed.ReleaseCount).IsEqual(1);
        AssertThat(runner.TryEndActionInput("use")).IsTrue();

        TrackingReservation failed = new();
        provider.Reservation = failed;
        executor.Result = new GameplayActionExecutionFailed("not now");
        AssertThat(runner.TryStartActionInput("use")).IsFalse();
        AssertThat(failed.ReleaseCount).IsEqual(1);

        TrackingReservation rejected = new();
        provider.Reservation = rejected;
        executor.Result = new GameplayActionExecutionRejected("not now");
        AssertThat(runner.TryStartActionInput("use")).IsFalse();
        AssertThat(rejected.ReleaseCount).IsEqual(1);
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
        ISceneRunner scene = ISceneRunner.Load(world, autoFree: true);
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
                RequesterConcurrencyGroup = "presence",
            }
        );
        GameplayAction worldAction = AutoFree(
            new GameplayAction
            {
                Definition = new GameplayActionDefinition { Id = "world" },
                Executor = worldOwned,
                HostConcurrencyGroup = "world",
                RequesterConcurrencyGroup = "world",
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

    [TestCase]
    public void ExitRemovesDetachedExecutionsBeforeReentrantReservationRelease()
    {
        GameplayActionComponent component = AutoFree(new GameplayActionComponent());
        CommitAwareExecutor firstExecutor = new();
        CommitAwareExecutor secondExecutor = new();
        GameplayAction firstAction = AutoFree(
            new AccessControlledAction
            {
                Name = "FirstAction",
                Definition = new GameplayActionDefinition { Id = "first" },
                Executor = firstExecutor,
                HostConcurrencyGroup = "first",
                RequesterConcurrencyGroup = "first",
            }
        );
        GameplayAction secondAction = AutoFree(
            new AccessControlledAction
            {
                Name = "SecondAction",
                Definition = new GameplayActionDefinition { Id = "second" },
                Executor = secondExecutor,
                HostConcurrencyGroup = "second",
                RequesterConcurrencyGroup = "second",
            }
        );
        firstAction.AddChild(firstExecutor);
        secondAction.AddChild(secondExecutor);
        component.AddAction(firstAction);
        component.AddAction(secondAction);

        GameplayActionRunner runner = AutoFree(
            new GameplayActionRunner { OwnedActionComponent = component }
        );
        ReentrantReleaseReservation firstReservation = new();
        ReentrantReleaseReservation secondReservation = new();
        TestAccessProvider provider = new() { Allowed = true, Reservation = firstReservation };
        runner.RegisterAccessProvider(AccessControlledAction.ProviderId, provider);
        runner.BindAction(
            component,
            "first",
            AutoFree(new Node()),
            Config("first", GameplayActionActivationMode.Press)
        );
        runner.BindAction(
            component,
            "second",
            AutoFree(new Node()),
            Config("second", GameplayActionActivationMode.Press)
        );

        AssertThat(runner.TryStartActionInput("first")).IsTrue();
        provider.Reservation = secondReservation;
        AssertThat(runner.TryStartActionInput("second")).IsTrue();
        AssertThat(firstExecutor.Commit()).IsTrue();
        AssertThat(secondExecutor.Commit()).IsTrue();

        bool reentrantCompletionSucceeded = false;
        secondReservation.OnRelease = () =>
        {
            reentrantCompletionSucceeded = component.CompleteExecution(secondExecutor.ExecutionId);
        };

        runner._ExitTree();

        AssertThat(reentrantCompletionSucceeded).IsTrue();
        AssertThat(firstReservation.ReleaseCount).IsEqual(1);
        AssertThat(secondReservation.ReleaseCount).IsEqual(1);
        AssertThat(component.IsActionExecuting("first")).IsTrue();
        AssertThat(component.IsActionExecuting("second")).IsFalse();
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

    private static GameplayAction AddExternalAction(
        GameplayActionComponent component,
        string id,
        TestGameplayActionExecutor executor
    )
    {
        AccessControlledAction action = AutoFree(
            new AccessControlledAction
            {
                Name = id,
                Definition = new GameplayActionDefinition { Id = new StringName(id) },
                Executor = executor,
            }
        );
        action.AddChild(executor);
        component.AddAction(action);
        return action;
    }

    private static RequesterConcurrencyFixture CreateRequesterConcurrencyFixture(
        GameplayActionExecutionResult firstResult,
        GameplayActionExecutionResult secondResult,
        GameplayActionUnavailableKind busyKind = GameplayActionUnavailableKind.Blocked
    )
    {
        TestGameplayActionExecutor firstExecutor = new() { Result = firstResult };
        GameplayActionRunner runner = CreateRunnerWithOwnedActions(
            out GameplayActionComponent firstComponent,
            ("first", firstExecutor)
        );
        GameplayActionComponent secondComponent = AutoFree(new GameplayActionComponent());
        TestGameplayActionExecutor secondExecutor = new() { Result = secondResult };
        GameplayAction secondAction = AddExternalAction(secondComponent, "second", secondExecutor);
        secondAction.WhenRequesterBusy = busyKind;
        secondAction.RequesterConcurrencyGroup = "shared";
        runner.OwnedActionComponent!.ResolveAction("first")!.RequesterConcurrencyGroup = "shared";
        runner.RegisterAccessProvider(
            AccessControlledAction.ProviderId,
            new TestAccessProvider { Allowed = true }
        );

        GameplayActionBinding firstBinding = runner.BindAction(
            firstComponent,
            "first",
            AutoFree(new Node()),
            Config(
                "first",
                GameplayActionActivationMode.Press,
                inputRequirement: GameplayActionInputRequirement.Pressed
            )
        )!;
        GameplayActionBinding secondBinding = runner.BindAction(
            secondComponent,
            "second",
            AutoFree(new Node()),
            Config("second", GameplayActionActivationMode.Press)
        )!;
        return new RequesterConcurrencyFixture(
            runner,
            firstComponent,
            firstBinding,
            secondBinding,
            firstExecutor,
            secondExecutor
        );
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

    private static void AssertHoldProgress(
        GameplayActionRunner runner,
        ulong bindingId,
        float expectedProgress,
        float expectedElapsed
    )
    {
        AssertThat(
                runner.TryGetBindingHoldProgress(bindingId, out float progress, out float elapsed)
            )
            .IsTrue();
        AssertThat(progress).IsEqualApprox(expectedProgress, 0.001f);
        AssertThat(elapsed).IsEqualApprox(expectedElapsed, 0.001f);
    }

    private sealed record RequesterConcurrencyFixture(
        GameplayActionRunner Runner,
        GameplayActionComponent FirstComponent,
        GameplayActionBinding FirstBinding,
        GameplayActionBinding SecondBinding,
        TestGameplayActionExecutor FirstExecutor,
        TestGameplayActionExecutor SecondExecutor
    );

    private sealed partial class MutableRule(GameplayActionAvailability result)
        : GameplayActionPlugin.Runtime.Rules.GameplayActionRule
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

        public IGameplayActionRequestReservation? Reservation { get; set; }

        public int RequestChecks { get; private set; }

        public Node? LastTarget { get; private set; }

        public Node? LastAccessSource { get; private set; }

        public Node? LastSustainedTarget { get; private set; }

        public Node? LastSustainedAccessSource { get; private set; }

        public Node? LastReservationTarget { get; private set; }

        public Node? LastReservationAccessSource { get; private set; }

        public GameplayActionAccessPolicy ResolveAccess(in GameplayActionAccessContext context)
        {
            RequestChecks++;
            LastAccessSource = context.AccessSource;
            LastTarget = context.Target;
            if (context.Sustained)
            {
                LastSustainedAccessSource = context.AccessSource;
                LastSustainedTarget = context.Target;
            }
            return new GameplayActionAccessPolicy(Allowed);
        }

        public bool TryAcquireRequestReservation(
            in GameplayActionAccessContext context,
            out IGameplayActionRequestReservation? reservation
        )
        {
            LastReservationAccessSource = context.AccessSource;
            LastReservationTarget = context.Target;
            reservation = Reservation;
            return Allowed;
        }
    }

    private sealed class TargetValidityAccessProvider : IGameplayActionAccessProvider
    {
        public IGameplayActionRequestReservation? Reservation { get; set; }

        public GameplayActionAccessPolicy ResolveAccess(in GameplayActionAccessContext context) =>
            new(context.Target is not null && GodotObject.IsInstanceValid(context.Target));

        public bool TryAcquireRequestReservation(
            in GameplayActionAccessContext context,
            out IGameplayActionRequestReservation? reservation
        )
        {
            reservation = Reservation;
            return ResolveAccess(context).Allowed;
        }
    }

    private sealed class TrackingReservation : IGameplayActionRequestReservation
    {
        public int BindCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public void BindExecution(ulong executionId) => BindCount++;

        public void Release() => ReleaseCount++;
    }

    private sealed class ReentrantReleaseReservation : IGameplayActionRequestReservation
    {
        public int ReleaseCount { get; private set; }

        public Action? OnRelease { get; set; }

        public void BindExecution(ulong executionId) { }

        public void Release()
        {
            ReleaseCount++;
            OnRelease?.Invoke();
        }
    }

    private sealed partial class PresencePolicyExecutor(bool requiresRequesterPresence)
        : GameplayActionExecutor
    {
        public int CancelledCount { get; private set; }

        public override bool RequiresRequesterPresence => requiresRequesterPresence;

        public override GameplayActionExecutionResult Execute(in GameplayActionContext context) =>
            new GameplayActionExecutionRunning();

        protected internal override void OnExecutionCancelled(
            in GameplayActionContext context,
            string reason
        ) => CancelledCount++;
    }

    private sealed partial class CommitAwareExecutor : GameplayActionExecutor
    {
        private GameplayActionContext? _context;

        public int CancelledCount { get; private set; }

        public ulong ExecutionId { get; private set; }

        public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
        {
            _context = context;
            ExecutionId = context.ExecutionId;
            return new GameplayActionExecutionRunning();
        }

        public bool Commit() =>
            _context is GameplayActionContext context && context.ReleaseRequesterDependency();

        protected internal override void OnExecutionCancelled(
            in GameplayActionContext context,
            string reason
        ) => CancelledCount++;
    }

    private sealed partial class ReleaseDuringExecuteExecutor : GameplayActionExecutor
    {
        public bool RequesterDependencyReleased { get; private set; }

        public bool AccessReservationReleased { get; private set; }

        public int CancelledCount { get; private set; }

        public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
        {
            RequesterDependencyReleased = context.ReleaseRequesterDependency();
            AccessReservationReleased = context.ReleaseAccessReservation();
            return new GameplayActionExecutionRunning();
        }

        protected internal override void OnExecutionCancelled(
            in GameplayActionContext context,
            string reason
        ) => CancelledCount++;
    }

    private sealed partial class ReentrantRequesterExecutor : GameplayActionExecutor
    {
        public GameplayActionRunner? Runner { get; set; }

        public bool SecondRequestResult { get; private set; }

        public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
        {
            SecondRequestResult = Runner?.TryStartActionInput("second") == true;
            return new GameplayActionExecutionRunning();
        }
    }
}
