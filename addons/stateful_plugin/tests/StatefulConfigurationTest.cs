namespace QuestWorld.Tests;

using System.Linq;
using GdUnit4;
using Godot;
using QuestWorld.State;
using StatefulPlugin.Editor;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class StatefulConfigurationTest
{
    [TestCase]
    public void StatefulComponentRequiresAnInitialState()
    {
        StatefulComponent stateful = AutoFree(new StatefulComponent());

        string[] warnings = StatefulValidator.Validate(stateful).ToArray();

        AssertThat(warnings.Contains("InitialState must be assigned.")).IsTrue();
    }

    [TestCase]
    public void StatefulComponentRequiresAnInitialStateDeclaredByItsSchema()
    {
        StatefulComponent stateful = AutoFree(
            new StatefulComponent
            {
                InitialState = new StringName("melted"),
                Schema = CreateSchema("closed", "open"),
            }
        );

        string[] warnings = StatefulValidator.Validate(stateful).ToArray();

        AssertThat(warnings.Contains("InitialState must be declared by the assigned Schema."))
            .IsTrue();
    }

    [TestCase]
    public void StatefulComponentAcceptsAnInitialStateDeclaredByItsSchema()
    {
        StatefulComponent stateful = AutoFree(
            new StatefulComponent
            {
                InitialState = new StringName("closed"),
                Schema = CreateSchema("closed", "open"),
            }
        );

        string[] warnings = StatefulValidator.Validate(stateful).ToArray();

        AssertThat(warnings.Length).IsEqual(0);
    }

    [TestCase]
    public void StatefulComponentAcceptsAFreeInitialStateWithoutSchema()
    {
        StatefulComponent stateful = AutoFree(
            new StatefulComponent { InitialState = new StringName("flooded") }
        );

        string[] warnings = StatefulValidator.Validate(stateful).ToArray();

        AssertThat(warnings.Length).IsEqual(0);
    }

    [TestCase]
    public void SchemaRequiresAtLeastOneState()
    {
        StateSchema schema = new();

        string[] warnings = StatefulValidator.Validate(schema).ToArray();

        AssertThat(warnings.Contains("States must declare at least one state.")).IsTrue();
    }

    [TestCase]
    public void SchemaRejectsDuplicatedStates()
    {
        StateSchema schema = CreateSchema("closed", "open", "closed");

        string[] warnings = StatefulValidator.Validate(schema).ToArray();

        AssertThat(warnings.Contains("States must not declare the same state twice.")).IsTrue();
    }

    [TestCase]
    public void SchemaRejectsEmptyStates()
    {
        StateSchema schema = CreateSchema("closed", string.Empty);

        string[] warnings = StatefulValidator.Validate(schema).ToArray();

        AssertThat(warnings.Contains("States must not declare an empty state.")).IsTrue();
    }

    [TestCase]
    public void ValidatorIgnoresUnrelatedObjects()
    {
        Node3D unrelated = new();

        try
        {
            AssertThat(StatefulValidator.CanHandle(unrelated)).IsFalse();
        }
        finally
        {
            unrelated.Free();
        }
    }

    [TestCase]
    public void ValidatorHandlesStatefulTypes()
    {
        StatefulComponent stateful = new();

        try
        {
            AssertThat(StatefulValidator.CanHandle(stateful)).IsTrue();
            AssertThat(StatefulValidator.CanHandle(new StateSchema())).IsTrue();
        }
        finally
        {
            stateful.Free();
        }
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
}
