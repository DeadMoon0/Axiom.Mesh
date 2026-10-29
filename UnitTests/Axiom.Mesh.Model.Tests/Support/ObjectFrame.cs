using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Tests.Support;

/// <summary>
/// One object per frame, so ProtoStream's harness can drive the object decoder: decoding copies the frame, checks its
/// hash and parses it, and verifies a self-verifying signed object, exactly as intake would.
/// </summary>
internal sealed class ObjectFrame : IWireMessage<ObjectFrame>
{
    public required ParsedObject Object { get; init; }

    public static bool TryRead(ref WireReader reader, out ObjectFrame message)
    {
        byte[] bytes = reader.Bytes(reader.Remaining);
        message = null!;
        if (!Objects.TryParse(HashedObject.Of(bytes), out ParsedObject? parsed, out _))
            return reader.Fail();

        _ = parsed switch
        {
            UnverifiedManifest manifest => VerifiedManifest.TryVerify(manifest, out _),
            UnverifiedRoster roster => VerifiedRoster.TryVerify(roster, out _),
            _ => true,
        };
        message = new ObjectFrame { Object = parsed };
        return reader.Ok;
    }

    public void Write(ref WireWriter writer) => writer.Bytes(Object.Bytes.Span);
}
