namespace Axiom.Mesh.Cli;

/// <summary>Which part of the operating system starts the Host at logon (release design, fundamentals plan §6e).</summary>
internal enum AutostartSystem
{
    /// <summary>None: the platform has no supported autostart yet.</summary>
    Unknown = 0,

    /// <summary>Windows: a per-user scheduled task with a logon trigger.</summary>
    ScheduledTask = 1,

    /// <summary>Linux: a systemd user unit, enabled for <c>default.target</c>.</summary>
    SystemdUser = 2,
}
