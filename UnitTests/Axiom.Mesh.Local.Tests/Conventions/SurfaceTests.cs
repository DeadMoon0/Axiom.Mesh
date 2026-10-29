using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Axiom.Mesh.Local.Tests.Conventions;

/// <summary>
/// What Axiom.Mesh.Local exposes: every app and service references it, so nothing of the engine may leak through it
/// (plan decision D1, ARCHITECTURE.md rule 2), and its public types reach only the BCL, Model and ProtoStream.
/// </summary>
public sealed class SurfaceTests
{
    private static readonly string[] Allowed = ["Axiom.Mesh.Local", "Axiom.Mesh.Model", "Axiom.ProtoStream"];

    [Fact]
    public void NoCoreTypeIsReachableFromLocalsPublicSurface()
    {
        var offenders = new List<string>();
        foreach (Type type in typeof(LocalProtocol).Assembly.GetExportedTypes())
        {
            foreach (Type used in SurfaceOf(type))
            {
                string assembly = used.Assembly.GetName().Name!;
                bool framework = assembly.StartsWith("System", StringComparison.Ordinal) || assembly is "mscorlib" or "netstandard";
                if (!framework && !Allowed.Contains(assembly, StringComparer.Ordinal))
                    offenders.Add($"{type.FullName} exposes {used.FullName} from {assembly}");
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<Type> SurfaceOf(Type type)
    {
        IEnumerable<Type> declared = type.GetInterfaces().Append(type.BaseType ?? typeof(object));
        foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            declared = declared.Concat(member switch
            {
                MethodInfo m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType),
                ConstructorInfo c => c.GetParameters().Select(p => p.ParameterType),
                PropertyInfo p => [p.PropertyType],
                FieldInfo f => [f.FieldType],
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
