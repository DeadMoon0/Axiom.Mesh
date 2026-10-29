using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Axiom.Mesh.Tests.Shared;

namespace Axiom.Mesh.Tests.Support;

/// <summary>
/// The published test vectors of SPEC.md §15 in <c>Documentation/vectors/</c>: one hex file per object type and a
/// <c>vectors.json</c> manifest naming each vector's tag, hash, signer and the key it verifies against, plus the test
/// identities' keys. Written only when <c>AXIOM_MESH_WRITE_VECTORS=1</c>; signatures are not deterministic (BCL ECDSA is
/// not RFC 6979), so regenerating changes the signed vectors' bytes and hashes.
/// </summary>
internal static class VectorFiles
{
    public const string WriteVariable = "AXIOM_MESH_WRITE_VECTORS";

    public static string Folder => Path.Combine(Repository.Root, "Documentation", "vectors");

    /// <summary>The unsigned vectors, rebuilt the same way every time.</summary>
    public static (Chunk Chunk, ChunkList List, Tree Tree) Unsigned()
    {
        Chunk chunk = ObjectBuilders.Chunk("Hello, Axiom Mesh!\n"u8);
        ChunkList list = ObjectBuilders.ChunkList(19, [chunk.Hash]);
        if (!ObjectBuilders.TryTree([new TreeEntry("hello.txt", TreeEntryMode.File, list.Hash, 19)], out Tree? tree, out string? problem))
            throw new InvalidOperationException(problem);
        return (chunk, list, tree);
    }

    /// <summary>Writes every vector and the manifest.</summary>
    public static void Write()
    {
        (Chunk chunk, ChunkList list, Tree tree) = Unsigned();
        VerifiedManifest manifest = new ManifestDraft
        {
            Nonce = Samples.Nonce(1),
            Kind = PackageKind.Channel,
            Seq = 1,
            Name = "Family",
            Created = 1_767_225_600_000,
        }.Sign(TestKeys.Alice);
        VerifiedRoster roster = new RosterDraft
        {
            Seq = 1,
            Devices =
            [
                new RosterDevice(TestKeys.AliceLaptop.PublicKey, "alice-laptop", 1_767_225_600_000, removed: false),
                new RosterDevice(TestKeys.AlicePhone.PublicKey, "alice-phone", 1_767_225_660_000, removed: false),
            ],
        }.Sign(TestKeys.Alice);
        VerifiedGrant grant = new GrantDraft { Grantee = TestKeys.Bob.PublicKey, Rights = GrantRights.Write, Seq = 1 }.Sign(TestKeys.Alice, manifest);
        VerifiedEntry entry = new EntryDraft
        {
            Package = manifest.Content.Package,
            Clock = 0,
            Parents = EntryParents.None,
            Payload = new MessagePayload { Text = "Hello, Bob.", Attachments = [new Attachment("hello.txt", list.Hash, 19)], ReplyTo = default },
        }.Sign(TestKeys.AliceLaptop);

        (string File, ObjectTag Tag, ReadOnlyMemory<byte> Bytes, ObjectHash Hash, string? Signer, string? VerifiedWith)[] vectors =
        [
            ("chunk.hex", ObjectTag.Chunk, chunk.Bytes, chunk.Hash, null, null),
            ("chunklist.hex", ObjectTag.ChunkList, list.Bytes, list.Hash, null, null),
            ("tree.hex", ObjectTag.Tree, tree.Bytes, tree.Hash, null, null),
            ("entry.hex", ObjectTag.Entry, entry.Bytes, entry.Hash, "alice-laptop", "alice-laptop"),
            ("manifest.hex", ObjectTag.Manifest, manifest.Bytes, manifest.Hash, "alice", "ownerKey"),
            ("roster.hex", ObjectTag.Roster, roster.Bytes, roster.Hash, "alice", "userKey"),
            ("grant.hex", ObjectTag.Grant, grant.Bytes, grant.Hash, "alice", "manifest.hex"),
        ];

        Directory.CreateDirectory(Folder);
        foreach (var vector in vectors)
            File.WriteAllText(Path.Combine(Folder, vector.File), Wrap(Convert.ToHexString(vector.Bytes.Span).ToLowerInvariant()));

        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            json.WriteString("specification", "AMP/1, SPEC.md draft 3, §15");
            json.WriteString("about", "Each .hex file is one object (SPEC.md §8), lowercase hex, 64 characters per line. 'hash' is H(object). Signed vectors verify against the key 'verifiedWith' names (a key below, the object's own key field, or another vector's owner key); their signatures are not deterministic, so compare them by verifying, never byte for byte. The keys are published test keys (PKCS#8 DER, hex): never use them for anything else.");
            json.WriteString("regenerate", $"Set {WriteVariable}=1 and run the Model.Tests suite (VectorTests).");
            json.WriteStartArray("keys");
            foreach ((string name, string role, string pkcs8, EcdsaSigner signer) in TestKeys.All)
            {
                json.WriteStartObject();
                json.WriteString("name", name);
                json.WriteString("role", role);
                json.WriteString("id", signer.PublicKey.AsUserId().ToString());
                json.WriteString("publicKey", signer.PublicKey.ToString());
                json.WriteString("pkcs8", pkcs8);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("vectors");
            foreach (var vector in vectors)
            {
                json.WriteStartObject();
                json.WriteString("file", vector.File);
                json.WriteNumber("tag", (byte)vector.Tag);
                json.WriteString("object", vector.Tag.ToString());
                json.WriteString("hash", vector.Hash.ToString());
                if (vector.Signer is null)
                    json.WriteNull("signedBy");
                else
                    json.WriteString("signedBy", vector.Signer);
                if (vector.VerifiedWith is null)
                    json.WriteNull("verifiedWith");
                else
                    json.WriteString("verifiedWith", vector.VerifiedWith);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        string text = Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        File.WriteAllText(Path.Combine(Folder, "vectors.json"), text);
    }

    /// <summary>The bytes of a hex file, whitespace ignored.</summary>
    public static byte[] Read(string file) =>
        Convert.FromHexString(string.Concat(File.ReadAllText(Path.Combine(Folder, file)).Where(c => !char.IsWhiteSpace(c))));

    private static string Wrap(string hex) =>
        string.Concat(Enumerable.Range(0, (hex.Length + 63) / 64).Select(i => hex.Substring(i * 64, Math.Min(64, hex.Length - (i * 64))) + "\n"));
}
