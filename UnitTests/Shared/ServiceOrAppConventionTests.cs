using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Axiom.Mesh.Tests.Shared;

/// <summary>
/// The rule every service and every app keeps: it is a client of the local protocol and reaches nothing of the engine
/// or the daemon, directly or through another package. Derived by the suite of every service and app.
/// </summary>
/// <typeparam name="TAnchor">Any public type of the service or app under test.</typeparam>
public abstract class ServiceOrAppConventionTests<TAnchor>
{
    private static readonly string[] Forbidden = ["Axiom.Mesh.Core", "Axiom.Mesh.Host"];

    // Checked over the whole reference closure: a package in between that started referencing Core would otherwise
    // carry the engine into every app unseen.
    [Fact]
    public void NoServiceOrAppReferencesCoreOrHost()
    {
        var offenders = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<(Assembly Assembly, string Path)>();
        Assembly start = typeof(TAnchor).Assembly;
        pending.Push((start, start.GetName().Name!));

        while (pending.Count > 0)
        {
            (Assembly assembly, string path) = pending.Pop();
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies().Where(r => r.Name!.StartsWith("Axiom.", StringComparison.Ordinal)))
            {
                string via = path + " -> " + reference.Name;
                if (Forbidden.Contains(reference.Name, StringComparer.Ordinal))
                    offenders.Add(via);
                else if (seen.Add(reference.Name!))
                    pending.Push((Assembly.Load(reference), via));
            }
        }

        Assert.True(offenders.Count == 0, "A service or app reaches the engine or the daemon: " + string.Join("; ", offenders));
    }
}
