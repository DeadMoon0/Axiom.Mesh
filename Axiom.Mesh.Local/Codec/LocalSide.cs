namespace Axiom.Mesh.Local;

/// <summary>Which end of the pipe a codec serves.</summary>
internal enum LocalSide : byte
{
    /// <summary>The Host: reads what clients send, writes what the Host sends.</summary>
    Host = 0,

    /// <summary>A client: reads what the Host sends, writes what clients send.</summary>
    Client = 1,
}
