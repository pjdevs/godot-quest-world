namespace QuestWorld.Tests;

using System;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Examples.Rules;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Interaction.Runtime.Rules;
using QuestWorld.Interaction.Runtime.State;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class InteractionBehaviorTest
{
    [TestCase]
    public void ReplicatedStateIsAvailableToGodotButHiddenFromTheEditor()
    {
        InteractionStateful stateful = new();
        try
        {
            bool propertyFound = false;
            bool propertyIsVisibleInEditor = false;
            foreach (Godot.Collections.Dictionary property in stateful.GetPropertyList())
            {
                if (property["name"].AsString() != "ReplicatedState")
                {
                    continue;
                }

                propertyFound = true;
                PropertyUsageFlags usage = property["usage"].As<PropertyUsageFlags>();
                propertyIsVisibleInEditor = usage.HasFlag(PropertyUsageFlags.Editor);
                break;
            }

            AssertThat(propertyFound).IsTrue();
            AssertThat(propertyIsVisibleInEditor).IsFalse();
        }
        finally
        {
            stateful.Free();
        }
    }

    [TestCase]
    public void StatusUsesExhaustiveAllowedAndBlockedCases()
    {
        InteractionStatus allowed = new InteractionAllowed();
        InteractionStatus blocked = new InteractionBlocked("Needs a key");

        AssertThat(Describe(allowed)).IsEqual("allowed");
        AssertThat(Describe(blocked)).IsEqual("Needs a key");
    }

    [TestCase]
    public void OfflineInteractorKeepsLocalControlWithoutMultiplayerPeer()
    {
        InteractionInteractor interactor = new() { OwnerPeerId = 1 };

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
    public async Task RulesStopAtFirstBlock()
    {
        TestInteractionOwner owner = new();
        Area3D area = new() { Name = "InteractionArea" };
        InteractiveComponent interactive = new()
        {
            InteractionArea = area,
            InteractionOwner = owner,
            InteractionRules = new Godot.Collections.Array<InteractionRule>
            {
                new AlwaysBlockedInteractionRule { Reason = "First reason" },
                new AlwaysBlockedInteractionRule { Reason = "Second reason" },
            },
        };
        owner.AddChild(area);
        owner.AddChild(interactive);
        InteractionInteractor interactor = new();
        Node3D view = new() { Name = "ViewOrigin" };
        interactor.ViewOrigin = view;
        interactor.AddChild(view);
        Node3D world = new();
        world.AddChild(owner);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world);
        await runner.SimulateFrames(1);

        InteractionStatus status = interactive.EvaluateStatus(interactor);

        AssertThat(status is InteractionBlocked blocked && blocked.Reason == "First reason")
            .IsTrue();
    }

    [TestCase]
    public async Task CustomRuleCanEvaluateOwnerGameplayState()
    {
        TestInteractionOwner owner = new() { GameplayBlocked = true };
        Area3D area = new() { Name = "InteractionArea" };
        InteractiveComponent interactive = new()
        {
            InteractionArea = area,
            InteractionOwner = owner,
            InteractionRules = new Godot.Collections.Array<InteractionRule>
            {
                new OwnerGameplayRule(),
            },
        };
        owner.AddChild(area);
        owner.AddChild(interactive);
        InteractionInteractor interactor = new();
        Node3D world = new();
        world.AddChild(owner);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world);
        await runner.SimulateFrames(1);

        InteractionStatus blockedStatus = interactive.EvaluateStatus(interactor);
        owner.GameplayBlocked = false;
        InteractionStatus allowedStatus = interactive.EvaluateStatus(interactor);

        AssertThat(
                blockedStatus is InteractionBlocked blocked
                    && blocked.Reason == "Gameplay condition is blocked."
            )
            .IsTrue();
        AssertThat(allowedStatus is InteractionAllowed).IsTrue();
    }

    [TestCase]
    public async Task FocusUsesViewAlignmentAndDistanceAndEmitsStatus()
    {
        TestWorld testWorld = BuildWorld();
        bool focusChanged = false;
        testWorld.Interactor.FocusedInteractiveChanged += _ => focusChanged = true;

        testWorld.Interactor.AddInteractive(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactor.FocusedInteractive == testWorld.Interactive).IsTrue();
        AssertThat(focusChanged).IsTrue();
        AssertThat(testWorld.Interactor.GetInteractionPresentation()?.IsAllowed == true).IsTrue();
    }

    [TestCase]
    public async Task LongPhaseReservesObjectAndRejectsACompetitorRelease()
    {
        TestWorld testWorld = BuildWorld();
        Node3D secondView = new() { Name = "ViewOrigin" };
        InteractionInteractor secondInteractor = new() { ViewOrigin = secondView };
        secondInteractor.AddChild(secondView);
        testWorld.World.AddChild(secondInteractor);
        await testWorld.Runner.SimulateFrames(1);

        testWorld.Interactor.AddInteractive(testWorld.Interactive);
        secondInteractor.AddInteractive(testWorld.Interactive);
        AssertThat(testWorld.Interactive.StartInteraction(testWorld.Interactor)).IsTrue();
        AssertThat(testWorld.Stateful.State).IsEqual(InteractionState.Activating);
        AssertThat(testWorld.Interactive.StartInteraction(secondInteractor)).IsFalse();

        AssertThat(testWorld.Interactive.ReleaseInteractionInput(secondInteractor)).IsFalse();
        AssertThat(testWorld.Stateful.State).IsEqual(InteractionState.Activating);
        AssertThat(testWorld.Interactive.ActiveInteractor == testWorld.Interactor).IsTrue();
        AssertThat(testWorld.Interactive.EndInteractionPhase(InteractionState.Activated)).IsTrue();

        AssertThat(testWorld.Interactive.ActiveInteractor == null).IsTrue();
        AssertThat(testWorld.Stateful.State).IsEqual(InteractionState.Activated);
        AssertThat(testWorld.Owner.StartCount).IsEqual(1);
        AssertThat(testWorld.Owner.EndCount).IsEqual(0);
    }

    [TestCase]
    public async Task StatefulBlockReasonsAreConfigurable()
    {
        TestWorld testWorld = BuildWorld();
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Interactive.BusyReason = "Talking...";
        testWorld.Interactive.ActivatedReason = "Already used.";
        testWorld.Stateful.SetState(InteractionState.Activating);

        InteractionStatus busyStatus = testWorld.Interactive.EvaluateStatus(testWorld.Interactor);
        testWorld.Stateful.SetState(InteractionState.Activated);

        InteractionStatus activatedStatus = testWorld.Interactive.EvaluateStatus(
            testWorld.Interactor
        );

        AssertThat(busyStatus is InteractionBlocked busy && busy.Reason == "Talking...").IsTrue();
        AssertThat(
                activatedStatus is InteractionBlocked activated
                    && activated.Reason == "Already used."
            )
            .IsTrue();
    }

    [TestCase]
    public async Task SnapshotRestoreUsesCommonStateApplicationAndRejectsUnknownVersion()
    {
        TestWorld testWorld = BuildWorld();
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Stateful.SetState(InteractionState.Activated);
        InteractionSavedState saved = testWorld.Stateful.SaveState();
        testWorld.Stateful.SetState(InteractionState.Idle);

        testWorld.Stateful.LoadState(saved);

        AssertThat(testWorld.Stateful.State).IsEqual(InteractionState.Activated);
        AssertThat(testWorld.Owner.PresentationStateChanges).IsGreater(0);
        bool rejectedUnknownVersion = false;
        try
        {
            testWorld.Stateful.LoadState(new InteractionSavedState(999, InteractionState.Idle));
        }
        catch (ArgumentOutOfRangeException)
        {
            rejectedUnknownVersion = true;
        }

        AssertThat(rejectedUnknownVersion).IsTrue();
    }

    [TestCase]
    public async Task SnapshotRestoreReappliesSignalsWhenStateIsUnchanged()
    {
        TestWorld testWorld = BuildWorld();
        await testWorld.Runner.SimulateFrames(1);
        InteractionSavedState saved = testWorld.Stateful.SaveState();

        testWorld.Stateful.LoadState(saved);

        AssertThat(testWorld.Stateful.State).IsEqual(InteractionState.Idle);
        AssertThat(testWorld.Owner.AuthorityStateChanges).IsEqual(1);
        AssertThat(testWorld.Owner.PresentationStateChanges).IsEqual(1);
    }

    [TestCase]
    public async Task StandaloneStatefulChangesSignalsReplicatesAndRestoresState()
    {
        TestInteractionOwner owner = new();
        InteractionStateful stateful = new() { Name = "Stateful" };
        owner.AddChild(stateful);
        ISceneRunner runner = ISceneRunner.Load(owner);
        await runner.SimulateFrames(1);
        int signalCount = 0;
        stateful.InteractionStateChanged += (_, _) => signalCount++;
        stateful.InteractionStateChangedAuthority += owner.OnInteractionStateChangedAuthority;
        stateful.InteractionStateChangedPresentation += owner.OnInteractionStateChangedPresentation;

        AssertThat(stateful.SetState(InteractionState.Activated)).IsTrue();
        InteractionSavedState saved = stateful.SaveState();
        stateful.Set("ReplicatedState", (int)InteractionState.Idle);
        stateful.LoadState(saved);

        AssertThat(stateful.State).IsEqual(InteractionState.Activated);
        AssertThat(signalCount).IsEqual(3);
        AssertThat(owner.AuthorityStateChanges).IsEqual(3);
        AssertThat(owner.PresentationStateChanges).IsEqual(3);
    }

    [TestCase]
    public async Task InteractiveWithoutStatefulSupportsInstantInteractionOnly()
    {
        TestInteractionOwner owner = new();
        Area3D area = new() { Name = "InteractionArea" };
        InteractiveComponent interactive = new()
        {
            InteractionArea = area,
            InteractionOwner = owner,
        };
        owner.AddChild(area);
        owner.AddChild(interactive);
        interactive.InteractionInputStarted += owner.OnInteractionInputStarted;
        owner.Interactive = interactive;
        InteractionInteractor interactor = new();
        Node3D world = new();
        world.AddChild(owner);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world);
        await runner.SimulateFrames(1);

        AssertThat(interactive.EvaluateStatus(interactor) is InteractionAllowed).IsTrue();
        AssertThat(interactive.StartInteraction(interactor)).IsTrue();
        AssertThat(interactive.StartInteractionPhase(interactor)).IsFalse();
        AssertThat(owner.StartCount).IsEqual(1);
    }

    [TestCase]
    public async Task ExternalAndReplicatedStateChangesNotifyConfiguredInteractive()
    {
        TestWorld testWorld = BuildWorld();
        await testWorld.Runner.SimulateFrames(1);
        int notificationCount = 0;
        testWorld.Interactive.InteractiveStatusChanged += () => notificationCount++;

        testWorld.Stateful.SetState(InteractionState.Activated);
        testWorld.Stateful.Set("ReplicatedState", (int)InteractionState.Idle);

        AssertThat(notificationCount).IsEqual(2);
    }

    [TestCase]
    public async Task OfflineInputUsesAuthoritativeStartAndEndPath()
    {
        TestWorld testWorld = BuildWorld();
        testWorld.Interactor.AddInteractive(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactor.TryStartInteractionInput()).IsTrue();
        AssertThat(testWorld.Owner.StartCount).IsEqual(1);
        AssertThat(testWorld.Stateful.State).IsEqual(InteractionState.Activating);

        AssertThat(testWorld.Interactor.TryEndInteractionInput()).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(1);
        AssertThat(testWorld.Interactive.ActiveInteractor == null).IsTrue();
    }

    [TestCase]
    public async Task BlockedStatusStopsRequestBeforeAuthoritativeDispatch()
    {
        TestWorld testWorld = BuildWorld();
        testWorld.Interactive.InteractionRules.Add(
            new AlwaysBlockedInteractionRule { Reason = "Locked" }
        );
        bool requestEmitted = false;
        testWorld.Interactor.InteractionRequested += _ => requestEmitted = true;
        testWorld.Interactor.AddInteractive(testWorld.Interactive);
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactor.TryStartInteractionInput()).IsFalse();
        AssertThat(requestEmitted).IsFalse();
        AssertThat(testWorld.Owner.StartCount).IsEqual(0);
    }

    [TestCase]
    public async Task ServerReleasesRemoteOwnerInteractionWhenCandidateLeavesRange()
    {
        TestWorld testWorld = BuildWorld(ownerPeerId: 2);
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Interactor.AddInteractive(testWorld.Interactive);
        AssertThat(testWorld.Interactor.TryStartInteractionInput()).IsTrue();

        testWorld.Interactor.RemoveInteractive(testWorld.Interactive);

        AssertThat(testWorld.Interactive.ActiveInteractor == null).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(1);
    }

    [TestCase]
    public async Task ServerReleasesInteractionWhenRemoteInteractorExitsTree()
    {
        TestWorld testWorld = BuildWorld(ownerPeerId: 2);
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Interactor.AddInteractive(testWorld.Interactive);
        AssertThat(testWorld.Interactor.TryStartInteractionInput()).IsTrue();

        testWorld.Interactor.QueueFree();
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactive.ActiveInteractor == null).IsTrue();
        AssertThat(testWorld.Owner.EndCount).IsEqual(1);
    }

    [TestCase]
    public async Task InteractorNetworkAuthorityRemainsOnServerForRemoteOwner()
    {
        TestWorld testWorld = BuildWorld(ownerPeerId: 2);
        await testWorld.Runner.SimulateFrames(1);

        AssertThat(testWorld.Interactor.GetMultiplayerAuthority())
            .IsEqual(testWorld.Interactor.ServerPeerId);
    }

    private static string Describe(InteractionStatus status) =>
        status switch
        {
            InteractionAllowed => "allowed",
            InteractionBlocked blocked => blocked.Reason,
        };

    private static TestWorld BuildWorld(int ownerPeerId = 1)
    {
        Node3D world = new();
        TestInteractionOwner owner = new()
        {
            Name = "InteractiveOwner",
            Position = new Vector3(0, 0, -2),
        };
        Area3D area = new() { Name = "InteractionArea" };
        area.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 3.0f } });
        InteractionStateful stateful = new() { Name = "Stateful" };
        InteractiveComponent interactive = new()
        {
            Name = "Interactive",
            InteractionArea = area,
            Stateful = stateful,
            InteractionOwner = owner,
        };
        owner.AddChild(area);
        owner.AddChild(stateful);
        owner.AddChild(interactive);
        interactive.InteractionInputStarted += owner.OnInteractionInputStarted;
        interactive.InteractionInputEnded += owner.OnInteractionInputEnded;
        stateful.InteractionStateChangedAuthority += owner.OnInteractionStateChangedAuthority;
        stateful.InteractionStateChangedPresentation += owner.OnInteractionStateChangedPresentation;
        owner.Interactive = interactive;

        Node3D view = new() { Name = "ViewOrigin" };
        InteractionInteractor interactor = new()
        {
            Name = "Interactor",
            ViewOrigin = view,
            OwnerPeerId = ownerPeerId,
        };
        interactor.AddChild(view);
        world.AddChild(owner);
        world.AddChild(interactor);
        ISceneRunner runner = ISceneRunner.Load(world);
        return new TestWorld(world, runner, owner, stateful, interactive, interactor);
    }

    private sealed record TestWorld(
        Node3D World,
        ISceneRunner Runner,
        TestInteractionOwner Owner,
        InteractionStateful Stateful,
        InteractiveComponent Interactive,
        InteractionInteractor Interactor
    );

    private sealed partial class TestInteractionOwner : Node3D
    {
        public InteractiveComponent? Interactive { get; set; }

        public bool GameplayBlocked { get; set; }

        public int StartCount { get; private set; }
        public int EndCount { get; private set; }
        public int AuthorityStateChanges { get; private set; }
        public int PresentationStateChanges { get; private set; }

        public void OnInteractionInputStarted(InteractionInteractor interactor)
        {
            StartCount++;
            Interactive?.StartInteractionPhase(interactor);
        }

        public void OnInteractionInputEnded(InteractionInteractor interactor) => EndCount++;

        public void OnInteractionStateChangedAuthority(int oldState, int newState)
        {
            AuthorityStateChanges++;
        }

        public void OnInteractionStateChangedPresentation(int oldState, int newState)
        {
            PresentationStateChanges++;
        }
    }

    private sealed partial class OwnerGameplayRule : InteractionRule
    {
        public override InteractionStatus Evaluate(in InteractionContext context)
        {
            return context.InteractionOwner is TestInteractionOwner { GameplayBlocked: true }
                ? new InteractionBlocked("Gameplay condition is blocked.")
                : new InteractionAllowed();
        }
    }
}
