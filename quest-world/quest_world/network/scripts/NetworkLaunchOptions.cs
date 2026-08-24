using System;
using System.Collections.Generic;
using System.Globalization;

public enum NetworkLaunchMode
{
    Offline,
    Server,
    Host,
    Client,
}

public sealed class NetworkLaunchOptions
{
    public const string DefaultAddress = "127.0.0.1";
    public const int DefaultPort = 7000;
    public const int DefaultMaxPlayers = 8;

    private NetworkLaunchOptions(NetworkLaunchMode mode, string address, int port, int maxPlayers)
    {
        Mode = mode;
        Address = address;
        Port = port;
        MaxPlayers = maxPlayers;
    }

    public NetworkLaunchMode Mode { get; }

    public string Address { get; }

    public int Port { get; }

    public int MaxPlayers { get; }

    public bool IsNetworked => Mode != NetworkLaunchMode.Offline;

    public static bool TryParse(
        IReadOnlyList<string> arguments,
        out NetworkLaunchOptions options,
        out string error
    )
    {
        NetworkLaunchMode? requestedMode = null;
        string address = DefaultAddress;
        int port = DefaultPort;
        int maxPlayers = DefaultMaxPlayers;

        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (argument == "--")
            {
                continue;
            }

            switch (argument)
            {
                case "--offline":
                    if (!TrySetMode(NetworkLaunchMode.Offline, ref requestedMode, out error))
                    {
                        options = null!;
                        return false;
                    }
                    break;
                case "--server":
                    if (!TrySetMode(NetworkLaunchMode.Server, ref requestedMode, out error))
                    {
                        options = null!;
                        return false;
                    }
                    break;
                case "--host":
                    if (!TrySetMode(NetworkLaunchMode.Host, ref requestedMode, out error))
                    {
                        options = null!;
                        return false;
                    }
                    break;
                case "--client":
                    if (!TrySetMode(NetworkLaunchMode.Client, ref requestedMode, out error))
                    {
                        options = null!;
                        return false;
                    }
                    break;
                default:
                    if (
                        TryReadValue(
                            argument,
                            "--connect",
                            arguments,
                            ref index,
                            out string? addressValue
                        )
                    )
                    {
                        if (string.IsNullOrWhiteSpace(addressValue))
                        {
                            options = null!;
                            error = "The --connect address cannot be empty.";
                            return false;
                        }

                        address = addressValue;
                    }
                    else if (
                        TryReadValue(
                            argument,
                            "--port",
                            arguments,
                            ref index,
                            out string? portValue
                        )
                    )
                    {
                        if (
                            !int.TryParse(
                                portValue,
                                NumberStyles.None,
                                CultureInfo.InvariantCulture,
                                out port
                            ) || port is < 1 or > 65535
                        )
                        {
                            options = null!;
                            error = $"The --port value '{portValue}' must be between 1 and 65535.";
                            return false;
                        }
                    }
                    else if (
                        TryReadValue(
                            argument,
                            "--max-players",
                            arguments,
                            ref index,
                            out string? maxPlayersValue
                        )
                    )
                    {
                        if (
                            !int.TryParse(
                                maxPlayersValue,
                                NumberStyles.None,
                                CultureInfo.InvariantCulture,
                                out maxPlayers
                            ) || maxPlayers is < 1 or > 64
                        )
                        {
                            options = null!;
                            error =
                                $"The --max-players value '{maxPlayersValue}' must be between 1 and 64.";
                            return false;
                        }
                    }
                    break;
            }
        }

        options = new NetworkLaunchOptions(
            requestedMode ?? NetworkLaunchMode.Offline,
            address,
            port,
            maxPlayers
        );
        error = string.Empty;
        return true;
    }

    private static bool TrySetMode(
        NetworkLaunchMode mode,
        ref NetworkLaunchMode? requestedMode,
        out string error
    )
    {
        if (requestedMode.HasValue && requestedMode.Value != mode)
        {
            error =
                $"Only one network launch mode can be selected; found both '{requestedMode}' and '{mode}'.";
            return false;
        }

        requestedMode = mode;
        error = string.Empty;
        return true;
    }

    private static bool TryReadValue(
        string argument,
        string option,
        IReadOnlyList<string> arguments,
        ref int index,
        out string? value
    )
    {
        string prefix = option + "=";
        if (argument.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = argument[prefix.Length..];
            return true;
        }

        if (argument == option)
        {
            if (index + 1 < arguments.Count)
            {
                value = arguments[++index];
                return true;
            }

            value = null;
            return true;
        }

        value = null;
        return false;
    }
}
