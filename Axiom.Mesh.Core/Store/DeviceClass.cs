namespace Axiom.Mesh.Core;

/// <summary>The device classes of SPEC.md §11.2, whose retention defaults differ.</summary>
internal enum DeviceClass
{
    /// <summary>Keeps everything by default.</summary>
    Desktop = 0,

    /// <summary>Keeps the history and the latest by default; the rest on demand.</summary>
    Phone = 1,
}
