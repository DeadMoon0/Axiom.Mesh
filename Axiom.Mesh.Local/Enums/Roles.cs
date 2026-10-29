using System;

namespace Axiom.Mesh.Local;

/// <summary>The roles that may send a request (LOCAL-SPEC.md §6, §9: <b>U</b> and <b>S</b>); the bits equal the values of <see cref="Role"/>.</summary>
[Flags]
public enum Roles : byte
{
    /// <summary>No role may send it.</summary>
    None = 0,

    /// <summary>The user role (U).</summary>
    User = 1,

    /// <summary>The service role (S).</summary>
    Service = 2,
}
