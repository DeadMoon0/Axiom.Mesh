using System;
using System.Diagnostics.CodeAnalysis;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local;

/// <summary>
/// One row of <see cref="RequestTable"/>: what LOCAL-SPEC.md states about a request (its id, name, section and roles)
/// and how to read its body, taken from the request class itself.
/// </summary>
public sealed class RequestDescriptor
{
    private readonly RequestBodyReader reader;

    private RequestDescriptor(ushort id, string name, string section, Roles allowedRoles, Type requestType, Type okType, RequestBodyReader reader)
    {
        Id = id;
        Name = name;
        Section = section;
        AllowedRoles = allowedRoles;
        RequestType = requestType;
        OkType = okType;
        this.reader = reader;
    }

    /// <summary>The message id (§14.1).</summary>
    public ushort Id { get; }

    /// <summary>The request's name as §14.1 writes it, for logs and telemetry keys.</summary>
    public string Name { get; }

    /// <summary>The section of LOCAL-SPEC.md that specifies it, such as <c>6.1</c>.</summary>
    public string Section { get; }

    /// <summary>The roles that may send it; any other is answered <c>NotPermitted</c> (§3.4).</summary>
    public Roles AllowedRoles { get; }

    /// <summary>The request class.</summary>
    public Type RequestType { get; }

    /// <summary>The type of its OK body.</summary>
    public Type OkType { get; }

    /// <summary>Whether a connection in <paramref name="role"/> may send it.</summary>
    /// <param name="role">The connection's role, fixed at <c>HelloAck</c>.</param>
    /// <returns><see langword="true"/> when the role is one of <see cref="AllowedRoles"/>.</returns>
    public bool Allows(Role role) => role is Role.User or Role.Service && (AllowedRoles & (Roles)(byte)role) != 0;

    /// <summary>Reads a body of this request, which must end exactly where the request ends.</summary>
    /// <param name="body">The bytes after the correlation.</param>
    /// <param name="request">The request.</param>
    /// <returns><see langword="false"/> when a field is outside its bounds or bytes are left over: a violation (§11.3).</returns>
    public bool TryRead(ReadOnlyMemory<byte> body, [NotNullWhen(true)] out LocalRequest? request)
    {
        var wire = new WireReader(body);
        return TryRead(ref wire, out request);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({Id}, §{Section})";

    internal bool TryRead(ref WireReader wire, [NotNullWhen(true)] out LocalRequest? request)
    {
        request = reader(ref wire, out LocalRequest? read) && wire.IsAtEnd ? read : null;
        return request is not null;
    }

    internal static RequestDescriptor Of<TRequest, TOk>(string section)
        where TRequest : LocalRequest, IRequest<TRequest, TOk> =>
        new(TRequest.Id, TRequest.RequestName, section, TRequest.AllowedRoles, typeof(TRequest), typeof(TOk), Read<TRequest>);

    private static bool Read<TRequest>(ref WireReader wire, out LocalRequest? request)
        where TRequest : LocalRequest, IWireRecord<TRequest>
    {
        bool read = TRequest.TryRead(ref wire, out TRequest? typed);
        request = typed;
        return read;
    }
}
