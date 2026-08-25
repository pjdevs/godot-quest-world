#if TOOLS

using System.Collections.Generic;
using Godot;
using QuestWorld.State;

namespace StatefulPlugin.Editor;

public static class StatefulValidator
{
    private enum InspectableType
    {
        None,
        StatefulComponent,
        StateSchema,
    }

    public static bool CanHandle(GodotObject obj) => ResolveType(obj) != InspectableType.None;

    public static IEnumerable<string> Validate(GodotObject obj)
    {
        switch (ResolveType(obj))
        {
            case InspectableType.StatefulComponent:
                string initialState = GetStringName(obj, "InitialState");
                if (initialState.Length == 0)
                    yield return "InitialState must be assigned.";

                GodotObject? schema = GetObject(obj, "Schema");
                if (schema is not null && !GetStates(schema).Contains(initialState))
                    yield return "InitialState must be declared by the assigned Schema.";

                break;
            case InspectableType.StateSchema:
                List<string> states = GetStates(obj);
                if (states.Count == 0)
                    yield return "States must declare at least one state.";

                if (states.Contains(string.Empty))
                    yield return "States must not declare an empty state.";

                if (states.Count != new HashSet<string>(states).Count)
                    yield return "States must not declare the same state twice.";

                break;
        }
    }

    private static InspectableType ResolveType(GodotObject obj)
    {
        InspectableType managedType = obj switch
        {
            StatefulComponent => InspectableType.StatefulComponent,
            StateSchema => InspectableType.StateSchema,
            _ => InspectableType.None,
        };
        if (managedType != InspectableType.None)
        {
            return managedType;
        }

        Script? script = GetAttachedScript(obj);
        string globalName = script?.GetGlobalName().ToString() ?? string.Empty;
        return globalName switch
        {
            nameof(StatefulComponent) => InspectableType.StatefulComponent,
            nameof(StateSchema) => InspectableType.StateSchema,
            _ => ResolveTypeFromPath(script?.ResourcePath),
        };
    }

    private static InspectableType ResolveTypeFromPath(string? path)
    {
        return path switch
        {
            "res://addons/stateful_plugin/runtime/StatefulComponent.cs" =>
                InspectableType.StatefulComponent,
            "res://addons/stateful_plugin/runtime/StateSchema.cs" => InspectableType.StateSchema,
            _ => InspectableType.None,
        };
    }

    private static List<string> GetStates(GodotObject schema)
    {
        List<string> states = new();

        foreach (Variant state in schema.Get("States").AsGodotArray())
        {
            states.Add(state.AsStringName().ToString());
        }

        return states;
    }

    private static string GetStringName(GodotObject obj, StringName propertyName) =>
        obj.Get(propertyName).AsStringName().ToString();

    private static GodotObject? GetObject(GodotObject obj, StringName propertyName) =>
        obj.Get(propertyName).AsGodotObject();

    private static Script? GetAttachedScript(GodotObject obj) =>
        obj.GetScript().AsGodotObject() as Script;
}

#endif
