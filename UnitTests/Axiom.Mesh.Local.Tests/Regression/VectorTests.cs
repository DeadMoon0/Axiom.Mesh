using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Axiom.Mesh.Local.Tests.Support;
using Axiom.Mesh.Tests.Shared;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local.Tests.Regression;

/// <summary>
/// The published test vectors of the local protocol (LOCAL-SPEC.md §15): <c>Documentation/vectors/local.txt</c> holds one
/// frame per sample message, length prefix included, as hex, one line each (<c>client|host &lt;sample name&gt; &lt;hex&gt;</c>, the hex last).
/// Another implementation decodes each and re-encodes it to the same bytes. The file is written only with
/// <c>AXIOM_MESH_WRITE_VECTORS=1</c>; the local protocol has no signatures, so the bytes are the same on every run.
/// </summary>
public sealed class VectorTests
{
    private const string WriteVariable = "AXIOM_MESH_WRITE_VECTORS";

    private static string File => Path.Combine(Repository.Root, "Documentation", "vectors", "local.txt");

    // Damage: a codec change that moves one byte of any frame breaks every other implementation that followed the
    // published vectors; the file pins every sample, both directions, and decodes back to the same bytes.
    [Fact]
    public void EveryPublishedVectorDecodesAndReencodesToTheSameBytes()
    {
        IReadOnlyList<string> expected = Lines();
        if (Environment.GetEnvironmentVariable(WriteVariable) == "1")
            System.IO.File.WriteAllLines(File, ["# axiom-mesh-local/1 test vectors (LOCAL-SPEC.md §15): <side> <sample> <frame as hex, length prefix included>", .. expected]);

        string[] published = [.. System.IO.File.ReadAllLines(File).Where(l => l.Length > 0 && !l.StartsWith('#'))];
        Assert.Equal(expected, published);
        foreach (string line in published)
        {
            string[] parts = line.Split(' ');
            byte[] frame = Convert.FromHexString(parts[^1]);
            byte[] again = parts[0] == "client"
                ? Frames.FromClient(Message(Frames.AtHost(frame), line))
                : Frames.FromHost(Message(Frames.AtClient(frame), line));
            Assert.True(frame.AsSpan().SequenceEqual(again), "re-encoding differs: " + string.Join(' ', parts[1..^1]));
        }
    }

    private static IReadOnlyList<string> Lines() =>
    [
        .. MessageSamples.FromClient.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => $"client {s.Key} {Convert.ToHexString(Frames.FromClient(s.Value)).ToLowerInvariant()}"),
        .. MessageSamples.FromHost.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => $"host {s.Key} {Convert.ToHexString(Frames.FromHost(s.Value)).ToLowerInvariant()}"),
    ];

    private static T Message<T>(DecodeOutcome<T> outcome, string line)
        where T : class =>
        outcome.Message ?? throw new InvalidOperationException($"the vector does not decode ({outcome.Status}): {line[..Math.Min(line.Length, 80)]}");
}
