using System;
using System.Text;
using Axiom.Mesh.Testing;
using BenchmarkDotNet.Attributes;

namespace Axiom.Mesh.Benchmarks.Micro;

/// <summary>
/// Signing and verifying an Entry (plan §5: at least 10 000 signed and 5 000 verified per second per core). Signing is
/// the producer as the node runs it: encode, sign, parse back and verify against the own key (<see cref="EntryDraft.Sign"/>).
/// Verifying is the check every Entry from a peer gets.
/// </summary>
[MemoryDiagnoser]
public class EntrySignatureBenchmarks
{
    private static readonly PackageId Package = new(Hash32.Of("benchmark package"u8));
    private readonly EntryDraft draft = new()
    {
        Package = Package,
        Clock = 41,
        Parents = EntryParents.Of(ObjectHash.Of("parent"u8)),
        Body = new EntryBody(EntryKind.Message, Encoding.UTF8.GetBytes("a message of a typical length, some forty bytes"), EntryReferences.None, default),
    };

    private UnverifiedEntry signed = null!;

    /// <summary>Signs one Entry to verify.</summary>
    [GlobalSetup]
    public void Setup()
    {
        VerifiedEntry entry = draft.Sign(TestKeys.AliceLaptop);
        if (!Objects.TryParse(HashedObject.Of(entry.Bytes), out ParsedObject? parsed, out _) || parsed is not UnverifiedEntry unverified)
            throw new InvalidOperationException("A signed Entry did not parse back.");
        signed = unverified;
    }

    /// <summary>Encode, sign, parse back and verify one Entry.</summary>
    /// <returns>The signed Entry.</returns>
    [Benchmark]
    public VerifiedEntry SignEntry() => draft.Sign(TestKeys.AliceLaptop);

    /// <summary>Verify one Entry's signature against its device key.</summary>
    /// <returns>Whether it verified.</returns>
    [Benchmark]
    public bool VerifyEntry() => VerifiedEntry.TryVerify(signed, TestKeys.AliceLaptop.PublicKey, out _);
}
