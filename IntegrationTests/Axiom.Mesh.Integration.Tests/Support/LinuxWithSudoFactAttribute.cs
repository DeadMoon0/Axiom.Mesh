using System;
using System.ComponentModel;
using System.Diagnostics;

namespace Axiom.Mesh.Integration.Tests.Support;

/// <summary>
/// A fact that needs Linux and a <c>sudo</c> that runs commands as another user without a password, as GitHub's Ubuntu
/// runners have; skipped, saying why, everywhere else.
/// </summary>
public sealed class LinuxWithSudoFactAttribute : FactAttribute
{
    /// <summary>Decides at discovery whether the fact can run here.</summary>
    public LinuxWithSudoFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "Runs on Linux only: it connects to the Host's Unix socket as another operating-system user.";
        else if (!PasswordlessSudo())
            Skip = "Needs 'sudo -n -u nobody' without a password (GitHub's ubuntu-latest has it); this machine asks for one.";
    }

    private static bool PasswordlessSudo()
    {
        try
        {
            var start = new ProcessStartInfo("sudo") { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "-n", "-u", "nobody", "true" })
                start.ArgumentList.Add(argument);
            using Process probe = Process.Start(start)!;
            return probe.WaitForExit(10_000) && probe.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
