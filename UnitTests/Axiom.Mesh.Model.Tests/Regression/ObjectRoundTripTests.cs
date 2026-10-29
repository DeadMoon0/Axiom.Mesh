using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Axiom.Mesh.Tests.Support;
using Axiom.ProtoStream.Testing;

namespace Axiom.Mesh.Tests.Regression;

/// <summary>Objects through ProtoStream's harnesses: every byte split, and mutations that must never throw.</summary>
public sealed class ObjectRoundTripTests
{
    // Damage: a parser that reads past a field, keeps state between objects or depends on how the bytes arrived would
    // decode one split differently from another; the harness splits the stream of every sample at every position.
    [Fact]
    public void EveryObjectRoundTripsAtEveryByteSplit()
    {
        ObjectFrame[] frames = [.. ObjectProtocol.Samples().Select(o => new ObjectFrame { Object = o })];

        CodecHarness.VerifyRoundTrip(ObjectProtocol.Definition, frames, (sent, received) =>
            sent.Object.Hash == received.Object.Hash
            && sent.Object.Tag == received.Object.Tag
            && sent.Object.Bytes.Span.SequenceEqual(received.Object.Bytes.Span));
    }

    // Damage: an exception from the object decoder on hostile bytes would surface in the engine's intake as a crash
    // instead of a malformed object; SPEC.md §12.2 makes a malformed object a value, never a failure of the node.
    [Fact]
    public void MutatedObjectsDecodeOrAreRefusedButNeverThrow_Seed42()
    {
        byte[][] samples = [.. ObjectProtocol.Samples().Select(o => Frame(o.Bytes.Span))];

        FuzzReport report = CodecHarness.Fuzz(ObjectProtocol.Definition, samples, seed: 42, iterations: 20_000);

        Assert.Equal(20_000, report.Iterations);
        Assert.True(report.Invalid > 0, "No mutation was refused; the fuzzer did not reach the parsers.");
        Assert.True(report.Messages > 0, "No mutation parsed; the fuzzer did not reach the well-formed paths.");
    }

    // Damage: the harness mutates whole frames; a parser fault that only shows on a direct call with a bit flipped,
    // the object cut short or a length field rewritten, with the signature paths behind it, would go unseen.
    [Fact]
    public void BitFlipsTruncationsAndForgedLengthsNeverThrow_Seed42()
    {
        var random = new Random(42);
        IReadOnlyList<ParsedObject> samples = ObjectProtocol.Samples();
        VerifiedManifest manifest = Samples.Channel();
        for (int i = 0; i < 20_000; i++)
        {
            byte[] bytes = samples[random.Next(samples.Count)].Bytes.ToArray();
            switch (random.Next(3))
            {
                case 0:
                    bytes[random.Next(bytes.Length)] ^= (byte)(1 << random.Next(8));
                    break;
                case 1:
                    bytes = bytes[..random.Next(bytes.Length)];
                    break;
                default:
                    int at = random.Next(Math.Max(1, bytes.Length - 4));
                    bytes[at] = 0xFF;
                    bytes[Math.Min(at + 1, bytes.Length - 1)] = 0xFF;
                    break;
            }

            if (!Objects.TryParse(HashedObject.Of(bytes), out ParsedObject? parsed, out _))
                continue;

            _ = parsed switch
            {
                UnverifiedEntry entry when entry.Content.Device == TestKeys.AliceLaptop.PublicKey.AsDeviceId() => VerifiedEntry.TryVerify(entry, TestKeys.AliceLaptop.PublicKey, out _),
                UnverifiedGrant grant when grant.Content.Package == manifest.Content.Package => VerifiedGrant.TryVerify(grant, manifest, out _),
                UnverifiedManifest m => VerifiedManifest.TryVerify(m, out _),
                UnverifiedRoster r => VerifiedRoster.TryVerify(r, out _),
                _ => true,
            };
        }
    }

    private static byte[] Frame(ReadOnlySpan<byte> objectBytes)
    {
        var output = new ArrayBufferWriter<byte>();
        Span<byte> header = stackalloc byte[5];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header, (uint)(objectBytes.Length + 1));
        header[4] = 1;
        output.Write(header);
        output.Write(objectBytes);
        return output.WrittenSpan.ToArray();
    }
}
