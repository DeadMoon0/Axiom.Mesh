namespace Axiom.Mesh.Cli;

/// <summary>A positional argument of a command: its name in the usage line, and whether it must be given.</summary>
/// <param name="Name">The name, as the usage shows it: <c>&lt;package&gt;</c>.</param>
/// <param name="Required">Whether the command needs it.</param>
/// <param name="Variadic">Whether it takes every remaining word; only the last argument may.</param>
internal sealed record ArgumentSpec(string Name, bool Required, bool Variadic)
{
    /// <summary>A required argument.</summary>
    /// <param name="name">Its name, without brackets.</param>
    /// <returns>The spec.</returns>
    public static ArgumentSpec Of(string name) => new(name, Required: true, Variadic: false);

    /// <summary>An argument that may be left out.</summary>
    /// <param name="name">Its name, without brackets.</param>
    /// <returns>The spec.</returns>
    public static ArgumentSpec Optional(string name) => new(name, Required: false, Variadic: false);

    /// <summary>The usage form: <c>&lt;name&gt;</c>, <c>[&lt;name&gt;]</c> or <c>[&lt;name&gt;...]</c>.</summary>
    public string Usage => Required ? $"<{Name}>" : Variadic ? $"[<{Name}>...]" : $"[<{Name}>]";
}
