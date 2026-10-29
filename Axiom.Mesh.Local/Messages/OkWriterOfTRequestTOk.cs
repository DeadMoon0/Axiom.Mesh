using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>The one <see cref="BodyWriter"/> per request class that writes its OK body.</summary>
/// <typeparam name="TRequest">The request.</typeparam>
/// <typeparam name="TOk">Its OK body.</typeparam>
internal static class OkWriter<TRequest, TOk>
    where TRequest : LocalRequest, IRequest<TRequest, TOk>
{
    /// <summary>Writes a boxed <typeparamref name="TOk"/>.</summary>
    public static readonly BodyWriter Write = Box;

    private static void Box(object body, ref WireWriter writer) => TRequest.WriteOk(ref writer, (TOk)body);
}
