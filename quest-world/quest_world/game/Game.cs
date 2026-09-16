using System.Collections.Generic;
using GameSessionPlugin;
using Godot;
using NetworkPlugin;

namespace QuestWorld.Game;

[GlobalClass]
public partial class Game : Node3D
{
    [Export]
    public NetworkSession? NetworkSession { get; set; }

    [Export]
    public GameSession? GameSession { get; set; }

    [Export]
    public PackedScene? InitialWorld { get; set; }

    private bool _initialized;

    public bool Initialize()
    {
        if (_initialized)
        {
            return true;
        }

        if (!ValidateConfiguration())
        {
            return false;
        }

        List<string> commandLineArguments = [.. OS.GetCmdlineArgs(), .. OS.GetCmdlineUserArgs()];
        if (
            !NetworkLaunchOptions.TryParse(
                commandLineArguments,
                out NetworkLaunchOptions? launchOptions,
                out string parseError
            )
        )
        {
            GD.PushError($"{GetPath()}: {parseError}");
            GetTree().Quit(2);
            return false;
        }

        if (!GameSession!.Initialize())
        {
            return false;
        }

        if (!NetworkSession!.Start(launchOptions!))
        {
            GameSession.Reset();
            return false;
        }

        _initialized = true;

        GameSession.WorldLoaded += OnWorldLoaded;
        GameSession.TravelCompleted += OnTravelCompleted;

        if (InitialWorld is not null && NetworkSession.IsServer && GameSession.CurrentWorld is null)
        {
            GameSession.Travel(InitialWorld);
        }

        return true;
    }

    public override void _Ready()
    {
        Initialize();
    }

    public override void _ExitTree()
    {
        if (_initialized)
        {
            GameSession?.WorldLoaded -= OnWorldLoaded;
            GameSession?.TravelCompleted -= OnTravelCompleted;
            GameSession?.Reset();
            NetworkSession?.Stop();
        }

        _initialized = false;
    }

    private bool ValidateConfiguration()
    {
        if (NetworkSession is null)
        {
            return FailConfiguration("NetworkSession is required.");
        }

        if (GameSession is null)
        {
            return FailConfiguration("GameSession is required.");
        }

        return true;
    }

    private bool FailConfiguration(string reason)
    {
        GD.PushError($"{GetPath()}: {reason}");
        return false;
    }

    private void OnWorldLoaded(long travelId, Node world)
    {
        if (world is World currentWorld)
        {
            currentWorld.AttachGameSession(GameSession!);
        }
    }

    private void OnTravelCompleted(long travelId, Node world)
    {
        if (NetworkSession?.IsServer == true && world is World currentWorld)
        {
            currentWorld.InitializeAuthority();
        }
    }
}
