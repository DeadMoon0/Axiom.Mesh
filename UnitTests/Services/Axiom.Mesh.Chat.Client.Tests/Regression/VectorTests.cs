using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Axiom.Mesh.Chat.Client.Tests.Support;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Chat.Client.Tests.Regression;

/// <summary>
/// The published test vectors of <c>axiom-mesh-chat/1</c> (CHAT-SERVICE.md §13): <c>Documentation/vectors/chat/records.txt</c>
/// holds the payload of every sample record as hex, one line each (<c>&lt;record&gt; &lt;hex&gt;</c>). Another implementation
/// decodes each and re-encodes it to the same bytes. The file is written only with <c>AXIOM_MESH_WRITE_VECTORS=1</c>.
/// </summary>
public sealed class VectorTests
{
    private const string WriteVariable = "AXIOM_MESH_WRITE_VECTORS";

    private static string File => Path.Combine(Repository.Root, "Documentation", "vectors", "chat", "records.txt");

    // Damage: a codec change that moves one byte of any record breaks every app and service that followed the published
    // vectors; the file pins every sample and decodes back to the same bytes.
    [Fact]
    public void EveryPublishedVectorDecodesAndReencodesToTheSameBytes()
    {
        IReadOnlyList<string> expected = [.. ChatSamples.Types().Select(t => $"{t.Name} {Convert.ToHexString(ChatSamples.Encode(ChatSamples.Of(t))).ToLowerInvariant()}")];
        if (Environment.GetEnvironmentVariable(WriteVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.WriteAllLines(File, ["# axiom-mesh-chat/1 test vectors (CHAT-SERVICE.md §13): <record> <payload as hex>", .. expected]);
        }

        string[] published = [.. System.IO.File.ReadAllLines(File).Where(l => l.Length > 0 && !l.StartsWith('#'))];
        Assert.Equal(expected, published);
        foreach (string line in published)
        {
            string[] parts = line.Split(' ');
            Type type = ChatSamples.Types().Single(t => t.Name == parts[0]);
            byte[] payload = Convert.FromHexString(parts[1]);
            Assert.True(ChatSamples.TryDecode(type, payload, out object? read), "does not decode: " + parts[0]);
            Assert.Equal(payload, ChatSamples.Encode(read!));
        }
    }
}
