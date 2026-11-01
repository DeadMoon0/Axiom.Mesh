using System.Reflection;
using Axiom.Mesh.Service;

namespace Axiom.Mesh.Chat;

/// <summary>
/// Who the chat service is (LOCAL-SPEC.md §4.2, §9.1, CHAT-SERVICE.md §2): its name, its version and its kinds, as the Host's
/// default configuration lists them (<c>services.chat</c>: kinds [2], entryKinds [2, 3]).
/// </summary>
public static class ChatProgram
{
    /// <summary>The service's version: the assembly's informational version.</summary>
    public static string Version { get; } =
        typeof(ChatProgram).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ChatProgram).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <summary>The definition: <c>chat</c>, the Channel kind with the Message and Redaction Entry kinds.</summary>
    public static ServiceDefinition Definition { get; } = new()
    {
        Name = ChatProtocol.ServiceName,
        Version = Version,
        Kinds = [new ServiceKind { Kind = PackageKind.Channel, EntryKinds = [EntryKind.Message, EntryKind.Redaction] }],
    };
}
