namespace Axiom.Mesh.Core;

/// <summary>Which side of a TLS handshake a port's <see cref="MeshTlsLayer"/> runs. The protocol above is symmetric (SPEC.md §6.1).</summary>
internal enum TlsRole
{
    /// <summary>No role: the value of an uninitialised field, refused when a port is built.</summary>
    None = 0,

    /// <summary>The accepting side: the mesh port, for connections a peer dialled.</summary>
    Server = 1,

    /// <summary>The dialling side: the outbound port, for connections this node dialled.</summary>
    Client = 2,
}
