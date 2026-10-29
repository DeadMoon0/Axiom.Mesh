using System.IO;
using System.Linq;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Core.Tests.Conventions;

/// <summary>
/// ARCHITECTURE.md rule 4, where the compiler cannot hold it: an <c>Object</c> made from bytes, not from a
/// <c>StorableObject</c>, may only carry what the node's own store holds, so only the session's store send path makes one.
/// </summary>
public sealed class SendPathTests
{
    // ObjectMessage.FromStored trusts its caller that the bytes come from the store; a second caller anywhere in Core
    // could forward unverified bytes, so there is exactly one.
    [Fact]
    public void OnlyTheStoreSendPathMakesObjectMessagesFromStoredBytes()
    {
        string[] callers = [.. Repository.SourceFiles(Repository.ProjectFolder("Axiom.Mesh.Core"), "*.cs")
            .Where(file => File.ReadAllText(file).Contains("ObjectMessage.FromStored(", System.StringComparison.Ordinal))
            .Select(Repository.Relative)];

        Assert.Equal(["Axiom.Mesh.Core/Replication/ReplicationSession.cs"], callers);
    }
}
