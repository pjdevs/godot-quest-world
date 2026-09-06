namespace QuestWorld.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using QuestWorld.State;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
[TestCategory("Runtime")]
public sealed partial class StatefulBehaviorTest
{
    [TestCase]
    public void CoreTransitionMutatesWithoutDispatchingSignals()
    {
        StatefulComponent stateful = CreateStateful(initialState: "closed");
        StatefulSignalCounts counts = Observe(stateful);

        try
        {
            StateTransition? transition = stateful.ApplyStateCore(new StringName("open"));

            AssertThat(transition.HasValue).IsTrue();
            AssertThat(transition?.OldState.ToString()).IsEqual(string.Empty);
            AssertThat(transition?.NewState.ToString()).IsEqual("open");
            AssertThat(stateful.State.ToString()).IsEqual("open");
            AssertThat(counts.Universal).IsEqual(0);
        }
        finally
        {
            stateful.Free();
        }
    }

    [TestCase]
    public async Task CoreTransitionReturnsNothingWhenValueIsAlreadyApplied()
    {
        StatefulComponent stateful = CreateStateful(initialState: "closed");
        await StartAsync(stateful);

        StateTransition? transition = stateful.ApplyStateCore(new StringName("closed"));

        AssertThat(transition.HasValue).IsFalse();
        AssertThat(stateful.State.ToString()).IsEqual("closed");
    }

    [TestCase]
    public async Task DispatchEmitsEachScopedSignalExactlyOnce()
    {
        StatefulComponent stateful = CreateStateful(initialState: "closed");
        await StartAsync(stateful);
        StatefulSignalCounts counts = Observe(stateful);
        StateTransition? transition = stateful.ApplyStateCore(new StringName("open"));

        stateful.DispatchStateTransition(transition!.Value);

        AssertThat(counts.Universal).IsEqual(1);
        AssertThat(counts.Authority).IsEqual(1);
        AssertThat(counts.Presentation).IsEqual(1);
    }

    [TestCase]
    public async Task ReadyAppliesInitialStateWithoutDispatchingSignals()
    {
        StatefulComponent stateful = CreateStateful(initialState: "closed");
        StatefulSignalCounts counts = Observe(stateful);

        await StartAsync(stateful);

        AssertThat(stateful.State.ToString()).IsEqual("closed");
        AssertThat(counts.Universal).IsEqual(0);
    }

    [TestCase]
    public async Task SetStateAppliesAuthoritativeValueAndNotifiesOnce()
    {
        StatefulComponent stateful = CreateStateful(initialState: "closed");
        await StartAsync(stateful);
        StatefulSignalCounts counts = Observe(stateful);

        AssertThat(stateful.SetState(new StringName("open"))).IsTrue();

        AssertThat(stateful.State.ToString()).IsEqual("open");
        AssertThat(counts.Universal).IsEqual(1);
        AssertThat(counts.Authority).IsEqual(1);
        AssertThat(counts.Presentation).IsEqual(1);
        // A lived change is never a synchronization, whichever channel observes it.
        AssertThat(counts.UniversalSynchronizations).IsEqual(new List<bool> { false });
        AssertThat(counts.AuthoritySynchronizations).IsEqual(new List<bool> { false });
        AssertThat(counts.PresentationSynchronizations).IsEqual(new List<bool> { false });
    }

    [TestCase]
    public void SetStateAppliesOfflineWithoutAMultiplayerPeer()
    {
        // Outside any tree, Multiplayer is null, which is the peerless game: asking the API for an id
        // it does not have would push an error and answer that nobody is the server, so the only
        // authoritative path of this component would refuse itself.
        StatefulComponent stateful = CreateStateful(initialState: "closed");

        try
        {
            AssertThat(stateful.SetState(new StringName("open"))).IsTrue();

            AssertThat(stateful.State.ToString()).IsEqual("open");
        }
        finally
        {
            stateful.Free();
        }
    }

    [TestCase]
    public async Task SetStateReportsNoChangeWhenValueIsAlreadyApplied()
    {
        StatefulComponent stateful = CreateStateful(initialState: "closed");
        await StartAsync(stateful);
        StatefulSignalCounts counts = Observe(stateful);

        AssertThat(stateful.SetState(new StringName("closed"))).IsFalse();

        AssertThat(counts.Universal).IsEqual(0);
    }

    [TestCase]
    public async Task SetStateAcceptsAnyValueWhenNoSchemaIsAssigned()
    {
        StatefulComponent stateful = CreateStateful(initialState: "dry");
        await StartAsync(stateful);

        AssertThat(stateful.SetState(new StringName("flooded"))).IsTrue();

        AssertThat(stateful.State.ToString()).IsEqual("flooded");
    }

    [TestCase]
    public async Task SetStateRejectsValueOutsideSchemaWithoutMutatingOrNotifying()
    {
        StatefulComponent stateful = CreateStateful(
            initialState: "closed",
            schema: CreateSchema("closed", "open")
        );
        await StartAsync(stateful);
        StatefulSignalCounts counts = Observe(stateful);

        AssertThat(stateful.SetState(new StringName("melted"))).IsFalse();

        AssertThat(stateful.State.ToString()).IsEqual("closed");
        AssertThat(counts.Universal).IsEqual(0);
    }

    [TestCase]
    public async Task InitialStateOutsideSchemaRemainsTheAppliedValue()
    {
        StatefulComponent stateful = CreateStateful(
            initialState: "melted",
            schema: CreateSchema("closed", "open")
        );

        await StartAsync(stateful);

        AssertThat(stateful.State.ToString()).IsEqual("melted");
    }

    [TestCase]
    public async Task ReplicatedValueIsAppliedAndNotifiedWithoutSchemaValidation()
    {
        StatefulComponent stateful = CreateStateful(
            initialState: "closed",
            schema: CreateSchema("closed", "open")
        );
        await StartAsync(stateful);
        StatefulSignalCounts counts = Observe(stateful);

        stateful.Set("ReplicatedState", new StringName("melted"));

        AssertThat(stateful.State.ToString()).IsEqual("melted");
        AssertThat(counts.Universal).IsEqual(1);
    }

    [TestCase]
    public async Task SnapshotRestoresTheAuthoritativeValue()
    {
        StatefulComponent stateful = CreateStateful(initialState: "closed");
        await StartAsync(stateful);
        AssertThat(stateful.SetState(new StringName("open"))).IsTrue();
        StatefulSavedState saved = stateful.SaveState();
        AssertThat(stateful.SetState(new StringName("closed"))).IsTrue();

        stateful.LoadState(saved);

        AssertThat(saved.Version).IsEqual(StatefulComponent.CurrentSaveVersion);
        AssertThat(stateful.State.ToString()).IsEqual("open");
    }

    [TestCase]
    public async Task SnapshotRestoreReappliesSignalsWhenValueIsUnchanged()
    {
        StatefulComponent stateful = CreateStateful(initialState: "closed");
        await StartAsync(stateful);
        StatefulSavedState saved = stateful.SaveState();
        StatefulSignalCounts counts = Observe(stateful);

        stateful.LoadState(saved);

        AssertThat(stateful.State.ToString()).IsEqual("closed");
        AssertThat(counts.Universal).IsEqual(1);
        AssertThat(counts.Authority).IsEqual(1);
        AssertThat(counts.Presentation).IsEqual(1);
        // A restoration is a catch-up on all three channels: the world already was in this state, so a
        // presentation that plays confetti on a lived change must be able to stay silent here.
        AssertThat(counts.UniversalSynchronizations).IsEqual(new List<bool> { true });
        AssertThat(counts.AuthoritySynchronizations).IsEqual(new List<bool> { true });
        AssertThat(counts.PresentationSynchronizations).IsEqual(new List<bool> { true });
    }

    [TestCase]
    public async Task SnapshotRestoreRejectsUnsupportedVersion()
    {
        StatefulComponent stateful = CreateStateful(initialState: "closed");
        await StartAsync(stateful);
        StatefulSavedState saved = new(StatefulComponent.CurrentSaveVersion + 1, "open");
        bool rejected = false;

        try
        {
            stateful.LoadState(saved);
        }
        catch (ArgumentOutOfRangeException)
        {
            rejected = true;
        }

        AssertThat(rejected).IsTrue();
        AssertThat(stateful.State.ToString()).IsEqual("closed");
    }

    [TestCase]
    public async Task SnapshotRestoreRejectsValueOutsideSchema()
    {
        StatefulComponent stateful = CreateStateful(
            initialState: "closed",
            schema: CreateSchema("closed", "open")
        );
        await StartAsync(stateful);
        StatefulSavedState saved = new(StatefulComponent.CurrentSaveVersion, "melted");
        bool rejected = false;

        try
        {
            stateful.LoadState(saved);
        }
        catch (ArgumentOutOfRangeException)
        {
            rejected = true;
        }

        AssertThat(rejected).IsTrue();
        AssertThat(stateful.State.ToString()).IsEqual("closed");
    }

    [TestCase]
    public void SchemaAllowsOnlyDeclaredStates()
    {
        StateSchema schema = CreateSchema("closed", "open");

        AssertThat(schema.Contains(new StringName("open"))).IsTrue();
        AssertThat(schema.Contains(new StringName("melted"))).IsFalse();
    }

    private static StateSchema CreateSchema(params string[] states)
    {
        StateSchema schema = new();

        foreach (string state in states)
        {
            schema.States.Add(new StringName(state));
        }

        return schema;
    }

    private static StatefulComponent CreateStateful(string initialState, StateSchema? schema = null)
    {
        return new StatefulComponent
        {
            Name = "Stateful",
            Schema = schema,
            InitialState = new StringName(initialState),
        };
    }

    private static async Task<ISceneRunner> StartAsync(StatefulComponent stateful)
    {
        Node root = new() { Name = "StatefulRoot" };
        root.AddChild(stateful);
        ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        await runner.SimulateFrames(1);

        return runner;
    }

    private static StatefulSignalCounts Observe(StatefulComponent stateful)
    {
        StatefulSignalCounts counts = new();
        stateful.StateChanged += (_, _, isSynchronization) =>
        {
            counts.Universal++;
            counts.UniversalSynchronizations.Add(isSynchronization);
        };
        stateful.StateChangedAuthority += (_, _, isSynchronization) =>
        {
            counts.Authority++;
            counts.AuthoritySynchronizations.Add(isSynchronization);
        };
        stateful.StateChangedPresentation += (_, _, isSynchronization) =>
        {
            counts.Presentation++;
            counts.PresentationSynchronizations.Add(isSynchronization);
        };

        return counts;
    }

    private sealed class StatefulSignalCounts
    {
        public int Universal { get; set; }
        public int Authority { get; set; }
        public int Presentation { get; set; }
        public List<bool> UniversalSynchronizations { get; } = new();
        public List<bool> AuthoritySynchronizations { get; } = new();
        public List<bool> PresentationSynchronizations { get; } = new();
    }
}
