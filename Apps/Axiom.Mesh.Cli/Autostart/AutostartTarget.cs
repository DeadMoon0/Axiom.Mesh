namespace Axiom.Mesh.Cli;

/// <summary>
/// Everything an autostart registration is planned from, read by the shell before planning: which daemon, which home,
/// whose logon, and where the files go. The plan is a function of this record alone.
/// </summary>
internal sealed record AutostartTarget
{
    /// <summary>The daemon's executable, absolute: <c>axiom-meshd</c> next to the command line, or <c>--daemon</c>.</summary>
    public required string Daemon { get; init; }

    /// <summary>The home the daemon is started with (<c>--home</c>), from <c>AXIOM_MESH_HOME</c>; <see langword="null"/> for the platform's default.</summary>
    public string? Home { get; init; }

    /// <summary>Windows: the account whose logon starts the task, <c>DOMAIN\user</c>.</summary>
    public string Account { get; init; } = string.Empty;

    /// <summary>Linux: the systemd user unit directory, <c>$XDG_CONFIG_HOME/systemd/user</c> or <c>~/.config/systemd/user</c>.</summary>
    public string UnitDirectory { get; init; } = string.Empty;

    /// <summary>The value of <c>AXIOM_MESH_PIPE</c> in the installing shell, which the registered daemon will not see.</summary>
    public string? PipeVariable { get; init; }

    /// <summary>Windows: where the task definition is written for <c>schtasks</c> and deleted again.</summary>
    public string TemporaryDirectory { get; init; } = string.Empty;
}
