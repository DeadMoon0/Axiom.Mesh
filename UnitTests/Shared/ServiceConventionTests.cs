using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Axiom.Mesh.Tests.Shared;

/// <summary>
/// The rule every service under <c>Services/</c> keeps (S-D13, DESIGN.md §10.5): a service owns no user interface. It is the
/// driver behind an app and offers one surface, its <c>axiom-mesh-&lt;name&gt;/1</c> namespace, typed by its <c>.Client</c>
/// library; it references no UI framework and not the command line, writes to the console only in <c>Program.cs</c> (the
/// runtime's <c>ServiceShell</c> prints <c>version</c> and the usage), and renders nothing for a person in its public surface.
/// Derived by the suite of every service from S1 on.
/// </summary>
/// <typeparam name="TAnchor">Any public type of the service's executable.</typeparam>
public abstract class ServiceConventionTests<TAnchor>
{
    private static readonly string[] UserInterfaceAssemblies = ["Avalonia", "Terminal.Gui", "Spectre.Console", "Axiom.Mesh.Cli"];

    private static readonly Type[] RenderingTypes = [typeof(TextWriter), typeof(StringBuilder), typeof(IFormatProvider), typeof(Console)];

    private static Assembly Executable => typeof(TAnchor).Assembly;

    /// <summary>Any public type of the service's <c>.Client</c> library.</summary>
    protected abstract Type ClientAnchor { get; }

    /// <summary>The Client library's table of method names: a static class of <c>public const string</c> fields.</summary>
    protected abstract Type Methods { get; }

    /// <summary>The Client library's table of event names: a static class of <c>public const string</c> fields.</summary>
    protected abstract Type Events { get; }

    // No UI framework and not the command line, anywhere in the reference closure of the executable.
    [Fact]
    public void NoServiceOwnsAUserInterface()
    {
        var offenders = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<(Assembly Assembly, string Path)>();
        pending.Push((Executable, Executable.GetName().Name!));
        while (pending.Count > 0)
        {
            (Assembly assembly, string path) = pending.Pop();
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                string name = reference.Name!;
                string via = path + " -> " + name;
                if (UserInterfaceAssemblies.Any(ui => name == ui || name.StartsWith(ui + ".", StringComparison.Ordinal)))
                    offenders.Add(via);
                else if (name.StartsWith("Axiom.", StringComparison.Ordinal) && seen.Add(name))
                    pending.Push((Assembly.Load(reference), via));
            }
        }

        string folder = Repository.ProjectFolder(Executable.GetName().Name!);
        offenders.AddRange(Repository.SourceFiles(folder, "*.cs")
            .Where(file => Path.GetFileName(file) != "Program.cs")
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"\bConsole\s*\."))
            .Select(file => Repository.Relative(file) + " uses System.Console"));

        offenders.AddRange(Executable.GetExportedTypes()
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .SelectMany(member => SignatureTypes(member).Where(used => RenderingTypes.Contains(used)).Select(used => $"{type.FullName}.{member.Name} renders with {used.Name}"))));

        Assert.True(offenders.Count == 0, "A service owns a user interface: " + string.Join("; ", offenders.Distinct()));
    }

    // Every method and event name of the namespace has its records in the Client library, so every app speaks it typed.
    [Fact]
    public void EveryServiceMethodAndEventIsTypedInItsClientLibrary()
    {
        Assembly client = ClientAnchor.Assembly;
        HashSet<string> types = [.. client.GetExportedTypes().Select(t => t.Name)];
        var missing = new List<string>();
        foreach (string method in NamesOf(Methods))
        {
            missing.AddRange(new[] { RequestTypeOf(method), ReplyTypeOf(method) }.Where(t => !types.Contains(t)).Select(t => $"method '{method}' has no {t}"));
        }

        foreach (string evt in NamesOf(Events))
        {
            if (!types.Contains(EventTypeOf(evt)))
                missing.Add($"event '{evt}' has no {EventTypeOf(evt)}");
        }

        Assert.True(Methods.Assembly == client && Events.Assembly == client, "The method and event tables belong in the Client library.");
        Assert.True(missing.Count == 0, "Names of the namespace without their records in the Client library: " + string.Join("; ", missing));
    }

    /// <summary>The record a method's request is: <c>about</c> → <c>AboutRequest</c>.</summary>
    /// <param name="method">The method's name.</param>
    /// <returns>The type's name.</returns>
    protected virtual string RequestTypeOf(string method) => Pascal(method) + "Request";

    /// <summary>The record a method's reply is: <c>about</c> → <c>AboutReply</c>.</summary>
    /// <param name="method">The method's name.</param>
    /// <returns>The type's name.</returns>
    protected virtual string ReplyTypeOf(string method) => Pascal(method) + "Reply";

    /// <summary>The record an event's payload is: <c>message</c> → <c>MessageEvent</c>.</summary>
    /// <param name="evt">The event's name.</param>
    /// <returns>The type's name.</returns>
    protected virtual string EventTypeOf(string evt) => Pascal(evt) + "Event";

    private static IEnumerable<string> NamesOf(Type table) =>
        table.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

    private static string Pascal(string name) =>
        string.Concat(name.Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static IEnumerable<Type> SignatureTypes(MemberInfo member) => member switch
    {
        MethodInfo m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType),
        ConstructorInfo c => c.GetParameters().Select(p => p.ParameterType),
        PropertyInfo p => [p.PropertyType],
        FieldInfo f => [f.FieldType],
        _ => [],
    };
}
