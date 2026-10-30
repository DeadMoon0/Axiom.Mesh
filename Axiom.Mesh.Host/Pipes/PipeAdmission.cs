using System;
using System.Collections.Generic;
using Axiom.Mesh.Core;

namespace Axiom.Mesh.Host;

/// <summary>
/// Who the pipe admits (LOCAL-SPEC.md §2.2): the operating-system user the daemon serves, and, for a daemon that runs as
/// a system service, the users its allow list names; nobody else. The access list on the pipe is the first fence; this
/// decision on the process identity read for every connection is the second, made before <c>Hello</c> is answered.
/// </summary>
internal static class PipeAdmission
{
    /// <summary>Decides one connection.</summary>
    /// <param name="identity">The connection's process identity, as the listener read it.</param>
    /// <param name="hostUser">The user the daemon runs as, named as <see cref="ProcessIdentity.User"/> names users.</param>
    /// <param name="allowUsers">The configured allow list (<c>pipe.allowUsers</c>).</param>
    /// <param name="runningAsService">Whether the daemon runs as a system service, the only case the allow list applies.</param>
    /// <returns>Admit, or refuse with the reason for the log.</returns>
    public static PipeAdmissionVerdict Decide(ProcessIdentity identity, string hostUser, IReadOnlyList<string> allowUsers, bool runningAsService)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(hostUser);
        ArgumentNullException.ThrowIfNull(allowUsers);
        if (identity.Kind != ProcessKind.Pipe || identity.User.Length == 0)
            return PipeAdmissionVerdict.Refuse("the connection's operating-system user could not be read");
        if (SameUser(identity.User, hostUser))
            return PipeAdmissionVerdict.Admit;
        if (runningAsService)
        {
            foreach (string allowed in allowUsers)
            {
                if (SameUser(identity.User, allowed))
                    return PipeAdmissionVerdict.Admit;
            }

            return PipeAdmissionVerdict.Refuse($"user \"{identity.User}\" is not on the allow list of this system service (pipe.allowUsers)");
        }

        return PipeAdmissionVerdict.Refuse($"user \"{identity.User}\" is not \"{hostUser}\", the user this daemon serves");
    }

    // Windows account names compare without case; uids are digits, where case changes nothing.
    private static bool SameUser(string a, string b) => a.Length != 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
