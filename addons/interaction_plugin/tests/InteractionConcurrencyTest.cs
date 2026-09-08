namespace QuestWorld.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GameplayActionPlugin;
using GameplayActionPlugin.Integration.Stateful;
using GameplayActionPlugin.Runtime.Actions;
using GameplayActionPlugin.Runtime.Bindings;
using GameplayActionPlugin.Runtime.Execution;
using GdUnit4;
using Godot;
using InteractionPlugin;
using InteractionPlugin.Examples.Rules;
using InteractionPlugin.Integration.Stateful;
using InteractionPlugin.Runtime.Actions;
using InteractionPlugin.Runtime.Interactive;
using InteractionPlugin.Runtime.Interactor;
using InteractionPlugin.Runtime.Rules;
using QuestWorld.Tests.GameplayActions;
using StatefulPlugin;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Runtime")]
public sealed partial class InteractionConcurrencyTest : InteractionTestBase
{
    [TestCase]
    public async Task DialogueLikeConcurrencyHidesTheRequesterButBlocksObservers()
    {
        TestWorld testWorld = BuildWorld();
        InteractionInteractor other = AddOtherInteractor(testWorld);
        testWorld.Action.WhenExecutingBySelf = GameplayActionUnavailableKind.Hidden;
        testWorld.Action.WhenExecutingByOther = GameplayActionUnavailableKind.Blocked;
        ActivationExecutorOf(testWorld.Action).Duration = 3600.0f;
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Action.Rules.Clear();

        testWorld.Interactive.ExecuteAction(testWorld.Interactor, testWorld.Action);

        AssertThat(
                testWorld.Interactive.EvaluateAvailability(testWorld.Interactor, testWorld.Action)
                    is GameplayActionHidden
            )
            .IsTrue();
        AssertThat(
                testWorld.Interactive.EvaluateAvailability(other, testWorld.Action)
                    is GameplayActionBlocked blocked
                    && blocked.Reason == "Someone else is using this."
            )
            .IsTrue();
    }

    [TestCase]
    public async Task FullyHiddenConcurrencyHidesTheActionForEveryone()
    {
        TestWorld testWorld = BuildWorld();
        InteractionInteractor other = AddOtherInteractor(testWorld);
        testWorld.Action.WhenExecutingBySelf = GameplayActionUnavailableKind.Hidden;
        testWorld.Action.WhenExecutingByOther = GameplayActionUnavailableKind.Hidden;
        ActivationExecutorOf(testWorld.Action).Duration = 3600.0f;
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Action.Rules.Clear();

        testWorld.Interactive.ExecuteAction(testWorld.Interactor, testWorld.Action);

        AssertThat(
                testWorld.Interactive.EvaluateAvailability(testWorld.Interactor, testWorld.Action)
                    is GameplayActionHidden
            )
            .IsTrue();
        AssertThat(
                testWorld.Interactive.EvaluateAvailability(other, testWorld.Action)
                    is GameplayActionHidden
            )
            .IsTrue();
    }

    [TestCase]
    public async Task InverseConcurrencyHidesObserversButBlocksTheRequester()
    {
        TestWorld testWorld = BuildWorld();
        InteractionInteractor other = AddOtherInteractor(testWorld);
        testWorld.Action.WhenExecutingBySelf = GameplayActionUnavailableKind.Blocked;
        testWorld.Action.WhenExecutingByOther = GameplayActionUnavailableKind.Hidden;
        ActivationExecutorOf(testWorld.Action).Duration = 3600.0f;
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Action.Rules.Clear();

        testWorld.Interactive.ExecuteAction(testWorld.Interactor, testWorld.Action);

        AssertThat(
                testWorld.Interactive.EvaluateAvailability(testWorld.Interactor, testWorld.Action)
                    is GameplayActionBlocked blocked
                    && blocked.Reason == "This is already in use."
            )
            .IsTrue();
        AssertThat(
                testWorld.Interactive.EvaluateAvailability(other, testWorld.Action)
                    is GameplayActionHidden
            )
            .IsTrue();
    }

    [TestCase]
    public async Task ConcurrencyPolicyUsesTheRunningSiblingGroup()
    {
        TestWorld testWorld = BuildWorld();
        InteractionAction sibling = CreateAction("sibling");
        sibling.HostConcurrencyGroup = testWorld.Action.GetHostConcurrencyGroup();
        sibling.WhenExecutingBySelf = GameplayActionUnavailableKind.Hidden;
        sibling.WhenExecutingByOther = GameplayActionUnavailableKind.Blocked;
        testWorld.Interactive.AddAction(sibling);
        ActivationExecutorOf(testWorld.Action).Duration = 3600.0f;
        await testWorld.Runner.SimulateFrames(1);
        testWorld.Action.Rules.Clear();

        testWorld.Interactive.ExecuteAction(testWorld.Interactor, testWorld.Action);

        AssertThat(
                testWorld.Interactive.EvaluateAvailability(testWorld.Interactor, sibling)
                    is GameplayActionHidden
            )
            .IsTrue();
    }
}
