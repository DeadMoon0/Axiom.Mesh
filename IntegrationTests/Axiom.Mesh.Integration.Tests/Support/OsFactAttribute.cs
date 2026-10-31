using System;

namespace Axiom.Mesh.Integration.Tests.Support;

/// <summary>A fact that runs on one operating system and is skipped, saying so, on the others.</summary>
public sealed class OsFactAttribute : FactAttribute
{
    /// <summary>Runs on <paramref name="os"/> only: <c>windows</c> or <c>linux</c>.</summary>
    /// <param name="os">The operating system.</param>
    public OsFactAttribute(string os)
    {
        bool runs = os switch
        {
            "windows" => OperatingSystem.IsWindows(),
            "linux" => OperatingSystem.IsLinux(),
            _ => false,
        };
        if (!runs)
            Skip = $"Runs on {os} only: it tests what only that operating system provides.";
    }
}
