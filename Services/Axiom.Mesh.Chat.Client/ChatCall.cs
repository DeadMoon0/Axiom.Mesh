using System;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Chat;

/// <summary>
/// Calls a method of <c>axiom-mesh-chat/1</c> over <c>ServiceCall</c> (LOCAL-SPEC.md §6.7): encodes the request, sends it
/// to the service <c>chat</c> as a user client, and decodes the reply the method's type names.
/// </summary>
public static class ChatCall
{
    /// <summary>Calls one method.</summary>
    /// <typeparam name="TRequest">The method's request record.</typeparam>
    /// <typeparam name="TReply">Its reply record.</typeparam>
    /// <param name="user">A connection in the user role.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Stops waiting for the answer.</param>
    /// <returns>
    /// The reply, or the status the call failed with: a status of LOCAL-SPEC.md §11.1 (<c>ServiceUnavailable</c> when the
    /// service is not connected, <c>ServiceTimeout</c>, <c>Busy</c>, the service's own use of them) or a
    /// <see cref="ChatStatus"/> as a <see cref="Status"/>, with the service's <see cref="Error"/>. A reply that does not
    /// parse as <typeparamref name="TReply"/> (a service speaking another version of the namespace) is
    /// <c>ServiceUnavailable</c>.
    /// </returns>
    /// <exception cref="ArgumentException">A field of the request is outside its bounds.</exception>
    public static async Task<LocalResult<TReply>> SendAsync<TRequest, TReply>(LocalClient user, IChatMethod<TRequest, TReply> request, CancellationToken cancellationToken)
        where TRequest : class, IChatMethod<TRequest, TReply>
        where TReply : class, IWireRecord<TReply>
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);
        var call = new ServiceCall { Service = ChatProtocol.ServiceName, Method = TRequest.Method, Payload = ChatCodec.Encode((TRequest)request) };
        LocalResult<ReadOnlyMemory<byte>> answered = await user.SendAsync(call, cancellationToken).ConfigureAwait(false);
        if (!answered.IsOk)
            return LocalResult<TReply>.Failure(answered.Status, answered.Error ?? new Error());
        return ChatCodec.TryDecode(answered.Value, out TReply? reply)
            ? LocalResult<TReply>.Ok(reply)
            : LocalResult<TReply>.Failure(Status.ServiceUnavailable, new Error
            {
                Message = $"The chat service answered '{TRequest.Method}' in a layout this app cannot read; update the app or the service.",
                Detail = ChatProtocol.Namespace,
            });
    }
}
