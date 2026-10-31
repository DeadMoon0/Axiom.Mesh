using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Axiom.Mesh.Core;
using Axiom.Mesh.Host;
using Axiom.Mesh.Payloads;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.ReadmeSamples.Tests.Conventions;

/// <summary>
/// The Host is content-blind (DESIGN.md §11, plan §6f): connections, routing, basic management and security, and no
/// payload parsed (SPEC.md §8.5, LOCAL-SPEC.md decision 5). The payload layouts live in <c>Axiom.Mesh.Payloads</c> for
/// services and the command line; the compiler cannot keep Core and Host from using them, so this test does.
/// </summary>
public sealed class ContentBlindHostTests
{
    private const string PayloadsNamespace = "Axiom.Mesh.Payloads";

    // No source file of Core or Host names the namespace or one of its types, and no member of any of their types, public
    // or not, has one in its signature.
    [Fact]
    public void NoPayloadTypeIsReferencedByCoreOrHost()
    {
        string[] payloadTypes = [.. typeof(EntryPayload).Assembly.GetExportedTypes().Where(t => t.Namespace == PayloadsNamespace).Select(t => t.Name)];
        Assert.Contains(nameof(VersionPayload), payloadTypes);

        string[] sources = [.. new[] { "Axiom.Mesh.Core", "Axiom.Mesh.Host" }
            .SelectMany(project => Repository.SourceFiles(Repository.ProjectFolder(project), "*.cs"))
            .Where(file => Names(File.ReadAllText(file), payloadTypes))
            .Select(Repository.Relative)];
        Assert.True(sources.Length == 0, "Core and Host sources that name a payload type: " + string.Join(", ", sources));

        string[] signatures = [.. new[] { typeof(MeshNode).Assembly, typeof(HostProgram).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => Signature(type).Where(IsPayloadType).Select(used => $"{type.FullName} uses {used.FullName}"))
            .Distinct()];
        Assert.True(signatures.Length == 0, "Core and Host members whose signature has a payload type: " + string.Join("; ", signatures));
    }

    private static bool Names(string source, string[] payloadTypes) =>
        source.Contains(PayloadsNamespace, StringComparison.Ordinal)
        || payloadTypes.Any(name => System.Text.RegularExpressions.Regex.IsMatch(source, $@"\b{name}\b"));

    private static bool IsPayloadType(Type type) => type.Namespace == PayloadsNamespace;

    private static IEnumerable<Type> Signature(Type type)
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        IEnumerable<Type> declared = type.GetInterfaces().Append(type.BaseType ?? typeof(object));
        foreach (MemberInfo member in type.GetMembers(All))
        {
            declared = declared.Concat(member switch
            {
                MethodInfo m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType),
                ConstructorInfo c => c.GetParameters().Select(p => p.ParameterType),
                PropertyInfo p => [p.PropertyType],
                FieldInfo f => [f.FieldType],
                EventInfo e => e.EventHandlerType is { } handler ? [handler] : [],
                _ => [],
            });
        }

        return declared.SelectMany(Expand);
    }

    private static IEnumerable<Type> Expand(Type type)
    {
        if (type.HasElementType)
            return Expand(type.GetElementType()!);
        if (type.IsGenericType)
            return type.GetGenericArguments().SelectMany(Expand).Append(type.GetGenericTypeDefinition());
        return type.IsGenericParameter ? [] : [type];
    }
}
