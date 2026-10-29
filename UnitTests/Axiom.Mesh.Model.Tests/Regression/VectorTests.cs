using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Axiom.Mesh.Tests.Support;

namespace Axiom.Mesh.Tests.Regression;

/// <summary>The published test vectors of SPEC.md §15 (<c>Documentation/vectors/</c>).</summary>
public sealed class VectorTests
{
    // Damage: a change to hashing, parsing or verification that this implementation agrees with itself on, but that
    // breaks agreement with every implementation built against the published vectors, would pass every round trip.
    [Fact]
    public void EveryPublishedVectorParsesHashesAndVerifies()
    {
        if (Environment.GetEnvironmentVariable(VectorFiles.WriteVariable) == "1")
            VectorFiles.Write();

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(VectorFiles.Folder, "vectors.json")));
        JsonElement root = manifest.RootElement;

        foreach (JsonElement key in root.GetProperty("keys").EnumerateArray())
        {
            using EcdsaSigner signer = EcdsaSigner.FromPkcs8(Convert.FromHexString(key.GetProperty("pkcs8").GetString()!));
            Assert.Equal(key.GetProperty("publicKey").GetString(), signer.PublicKey.ToString());
            Assert.Equal(key.GetProperty("id").GetString(), signer.PublicKey.AsUserId().ToString());
        }

        JsonElement[] vectors = [.. root.GetProperty("vectors").EnumerateArray()];
        Assert.Equal(Enum.GetValues<ObjectTag>().Length - 1, vectors.Length);
        foreach (JsonElement vector in vectors)
        {
            string file = vector.GetProperty("file").GetString()!;
            byte[] bytes = VectorFiles.Read(file);
            Assert.True(ObjectHash.TryParseHex(vector.GetProperty("hash").GetString(), out ObjectHash hash), file);
            Assert.True(HashedObject.TryVerify(bytes, hash, out HashedObject hashed), file + ": the bytes do not hash to the published hash");
            Assert.True(Objects.TryParse(hashed, out ParsedObject? parsed, out ObjectFault fault), file + ": " + fault);
            Assert.Equal(vector.GetProperty("tag").GetByte(), (byte)parsed.Tag);
            Assert.True(Verifies(parsed, vector, root), file + ": the signature does not verify against " + vector.GetProperty("verifiedWith"));
        }

        // The unsigned vectors are deterministic: the builders reproduce them byte for byte.
        (Chunk chunk, ChunkList list, Tree tree) = VectorFiles.Unsigned();
        Assert.Equal(VectorFiles.Read("chunk.hex"), chunk.Bytes.ToArray());
        Assert.Equal(VectorFiles.Read("chunklist.hex"), list.Bytes.ToArray());
        Assert.Equal(VectorFiles.Read("tree.hex"), tree.Bytes.ToArray());
    }

    private static bool Verifies(ParsedObject parsed, JsonElement vector, JsonElement root) => parsed switch
    {
        StorableObject => vector.GetProperty("verifiedWith").ValueKind == JsonValueKind.Null,
        UnverifiedManifest manifest => VerifiedManifest.TryVerify(manifest, out _),
        UnverifiedRoster roster => VerifiedRoster.TryVerify(roster, out _),
        UnverifiedGrant grant => VerifiedGrant.TryVerify(grant, ManifestVector(vector.GetProperty("verifiedWith").GetString()!), out _),
        UnverifiedEntry entry => VerifiedEntry.TryVerify(entry, Key(root, vector.GetProperty("verifiedWith").GetString()!), out _),
        _ => false,
    };

    private static VerifiedManifest ManifestVector(string file)
    {
        Assert.True(VerifiedManifest.TryVerify(Samples.Parse<UnverifiedManifest>(VectorFiles.Read(file)), out VerifiedManifest? manifest));
        return manifest;
    }

    private static PublicKey Key(JsonElement root, string name)
    {
        string hex = root.GetProperty("keys").EnumerateArray().Single(k => k.GetProperty("name").GetString() == name).GetProperty("publicKey").GetString()!;
        Assert.True(PublicKey.TryParse(Convert.FromHexString(hex), out PublicKey? key));
        return key;
    }
}
