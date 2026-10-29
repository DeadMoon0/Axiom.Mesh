using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Axiom.Mesh.Tests.Shared;

/// <summary>
/// Rules about a whole assembly, which no member-level modifier can state. Every package's suite derives this with a
/// public type of its own package: a rule only one suite enforces is a rule only one package follows.
/// </summary>
/// <typeparam name="TAnchor">Any public type of the package under test.</typeparam>
public abstract class ConventionTests<TAnchor>
{
    private const string FamilyPrefix = "Axiom.";
    private const string NewtonsoftJson = "Newtonsoft.Json";
    private const string SystemTextJson = "System.Text.Json";

    private const string Model = "Axiom.Mesh.Model";
    private const string Protocol = "Axiom.Mesh.Protocol";
    private const string Local = "Axiom.Mesh.Local";
    private const string Discovery = "Axiom.Mesh.Discovery";
    private const string Core = "Axiom.Mesh.Core";
    private const string Host = "Axiom.Mesh.Host";
    private const string Testing = "Axiom.Mesh.Testing";
    private const string Cli = "Axiom.Mesh.Cli";
    private const string ProtoStream = "Axiom.ProtoStream";

    // Outside the family, a few assemblies are allowed to one layer only: the Host alone wraps keys with DPAPI (plan
    // decision D3) and alone runs the generic host and reads JSON configuration files.
    private static readonly string[] GuardedForeignAssemblies =
        ["System.Security.Cryptography.ProtectedData", "Microsoft.Extensions.Hosting", "Microsoft.Extensions.Configuration.Json"];

    // Assemblies whose types must never appear in a public signature: a test framework, a JSON library or a DI
    // container in a signature ties every consumer to it.
    private static readonly string[] ForeignSurfaceAssemblies =
        ["xunit", "xunit.core", "xunit.assert", "xunit.abstractions", NewtonsoftJson, SystemTextJson, "Microsoft.Extensions.DependencyInjection.Abstractions"];

    private static Assembly Package => typeof(TAnchor).Assembly;

    private static string PackageName => Package.GetName().Name!;

    /// <summary>Whether this package needs JSON. A package that does not must not reference a JSON library at all.</summary>
    protected abstract bool UsesJson { get; }

    /// <summary>
    /// The assemblies of the family (and the guarded foreign ones) each package may reference: the dependency graph of
    /// the fundamentals plan §1, stated once for every package so that no suite can widen its own.
    /// </summary>
    /// <param name="package">An assembly name.</param>
    /// <returns>What it may reference, or <see langword="null"/> for an assembly the table does not know.</returns>
    internal static IReadOnlyCollection<string>? AllowedReferences(string package)
    {
        // Axiom.LDB.Primitives ships inside the Axiom.LDB.Core package (the generated entity code reads rows through it).
        string[] core = [Model, Protocol, Local, Discovery, ProtoStream, "Axiom.LWSE.Core", "Axiom.LDB.Entity", "Axiom.LDB.Core", "Axiom.LDB.Primitives", "Axiom.LDB.Query"];
        string[] host = [.. core, Core, "System.Security.Cryptography.ProtectedData", "Microsoft.Extensions.Hosting", "Microsoft.Extensions.Configuration.Json"];
        return package switch
        {
            Model => [ProtoStream],
            Protocol => [Model, ProtoStream],
            Local => [Model, ProtoStream],
            Discovery => [Model],
            Core => core,
            Host => host,
            Testing => [.. host, Host, "Axiom.LWSE.Testing", "Axiom.ProtoStream.Testing"],
            Cli => [Local, Model, ProtoStream],
            _ => null,
        };
    }

    // A grant between packages is a surface somebody needed and nobody added. The only exempt reader is the package's
    // own test assembly, derived from the assembly name, never matched by a name pattern.
    [Fact]
    public void InternalsAreOpenToTheOwnTestAssemblyOnly()
    {
        string ownTests = PackageName + ".Tests";
        var offenders = Package.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(a => a.AssemblyName.Split(',')[0].Trim())
            .Where(name => name != ownTests)
            .ToList();

        Assert.True(offenders.Count == 0, "Internals opened to other assemblies: " + string.Join(", ", offenders));
    }

    // One JSON library, the platform's: Newtonsoft.Json is not AOT-safe and would bring a second set of attributes.
    // Checked against the compiled references, because a stray using is invisible in a diff.
    [Fact]
    public void ReferencesNewtonsoftJsonNowhere()
    {
        Assert.DoesNotContain(Package.GetReferencedAssemblies(), r => r.Name == NewtonsoftJson);
    }

    // A dependency belongs to the package that needs it; a package without JSON carries no JSON library.
    [Fact]
    public void ReferencesNoJsonLibraryWhenItNeedsNone()
    {
        if (UsesJson)
            return;

        var offenders = Package.GetReferencedAssemblies()
            .Select(r => r.Name!)
            .Where(name => name is NewtonsoftJson or SystemTextJson)
            .ToList();

        Assert.True(offenders.Count == 0, $"{PackageName} references {string.Join(", ", offenders)} although it states it needs no JSON.");
    }

    // Packages talk only along the stated graph; dependencies point downwards.
    [Fact]
    public void ReferencesOnlyTheStatedFamilyPackages()
    {
        IReadOnlyCollection<string>? allowed = AllowedReferences(PackageName);
        Assert.True(allowed is not null, $"{PackageName} is missing from the reference table; add its row, as the plan's layering states it.");

        var offenders = Package.GetReferencedAssemblies()
            .Select(r => r.Name!)
            .Where(name => name.StartsWith(FamilyPrefix, StringComparison.Ordinal) || GuardedForeignAssemblies.Contains(name, StringComparer.Ordinal))
            .Where(name => !allowed!.Contains(name))
            .ToList();

        Assert.True(offenders.Count == 0, $"{PackageName} references outside its layer: " + string.Join(", ", offenders));
    }

    // A static field holds state for the whole process and every node in it; an in-memory mesh of three nodes in one
    // process would share it. It must be readonly and must not hold a mutable collection.
    [Fact]
    public void HoldsNoMutableStaticState()
    {
        var offenders = Package.GetTypes()
            .Where(t => !IsCompilerGenerated(t))
            .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(f => !f.IsLiteral && !IsCompilerGenerated(f))
            .Where(f => !f.IsInitOnly || IsMutableContainer(f.FieldType))
            .Select(f => $"{f.DeclaringType!.FullName}.{f.Name}")
            .ToList();

        Assert.True(offenders.Count == 0, "Mutable static state: " + string.Join(", ", offenders));
    }

    [Fact]
    public void PublicSignaturesUseNoForeignFrameworkTypes()
    {
        var offenders = new List<string>();
        foreach (Type type in Package.GetExportedTypes())
        {
            foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                foreach (Type used in SignatureTypes(member))
                {
                    string assembly = used.Assembly.GetName().Name!;
                    if (ForeignSurfaceAssemblies.Contains(assembly, StringComparer.Ordinal))
                        offenders.Add($"{type.FullName}.{member.Name} uses {used.FullName} from {assembly}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Foreign framework types in public signatures: " + string.Join("; ", offenders));
    }

    // The default of an enum is what an uninitialised field, a zeroed buffer or an unknown wire value turns into; it
    // must be a member somebody named and documented, never a value no member stands for.
    [Fact]
    public void EveryEnumHasAnExplicitZeroMember()
    {
        var offenders = Package.GetTypes()
            .Where(t => t.IsEnum && !IsCompilerGenerated(t))
            .Where(t => !Enum.IsDefined(t, Enum.ToObject(t, 0)))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(offenders.Count == 0, "Enums without a member of value 0: " + string.Join(", ", offenders));
    }

    private static IEnumerable<Type> SignatureTypes(MemberInfo member)
    {
        IEnumerable<Type> direct = member switch
        {
            MethodInfo m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType),
            ConstructorInfo c => c.GetParameters().Select(p => p.ParameterType),
            PropertyInfo p => [p.PropertyType],
            FieldInfo f => [f.FieldType],
            EventInfo e when e.EventHandlerType is not null => [e.EventHandlerType],
            _ => [],
        };
        return direct.SelectMany(Expand);
    }

    private static IEnumerable<Type> Expand(Type type)
    {
        if (type.HasElementType && type.GetElementType() is { } element)
            return Expand(element);
        if (type.IsGenericType)
            return type.GetGenericArguments().SelectMany(Expand).Append(type);
        return [type];
    }

    private static bool IsMutableContainer(Type type) =>
        type.IsArray
        || (type.IsGenericType && type.GetGenericTypeDefinition() is var d
            && (d == typeof(List<>) || d == typeof(Dictionary<,>) || d == typeof(HashSet<>) || d == typeof(Queue<>) || d == typeof(Stack<>)))
        || typeof(ArrayList).IsAssignableFrom(type) || typeof(Hashtable).IsAssignableFrom(type);

    private static bool IsCompilerGenerated(MemberInfo member) =>
        member.Name.StartsWith('<') || member.GetCustomAttribute<CompilerGeneratedAttribute>() is not null;
}
