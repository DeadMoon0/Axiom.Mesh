namespace Axiom.Mesh.Local;

/// <summary>The role a client asks for in <c>Hello</c> and is granted in <c>HelloAck</c> (LOCAL-SPEC.md §4.2, §14.2).</summary>
/// <remarks>Any value other than <see cref="User"/> and <see cref="Service"/> in a <c>Hello</c> is a violation (§11.3).</remarks>
public enum Role : byte
{
    /// <summary>No role; never valid on the wire.</summary>
    None = 0,

    /// <summary>The user role: the app, the command line, a script (§6).</summary>
    User = 1,

    /// <summary>The service role: a program that defines a Package kind (§9).</summary>
    Service = 2,
}
