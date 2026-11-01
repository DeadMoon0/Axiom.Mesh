namespace Axiom.Mesh.Cli;

/// <summary>An option of a command: <c>--json</c> (a flag) or <c>--limit &lt;n&gt;</c> (with a value).</summary>
/// <param name="Name">The option with its dashes: <c>--limit</c>.</param>
/// <param name="Value">The value's name in the usage, such as <c>n</c>; <see langword="null"/> for a flag.</param>
/// <param name="Required">Whether the command needs it.</param>
/// <param name="Help">One line about what it does.</param>
internal sealed record OptionSpec(string Name, string? Value, bool Required, string Help)
{
    /// <summary>A flag.</summary>
    /// <param name="name">The option with its dashes.</param>
    /// <param name="help">One line about it.</param>
    /// <returns>The spec.</returns>
    public static OptionSpec Flag(string name, string help) => new(name, null, Required: false, help);

    /// <summary>An option with a value that may be left out.</summary>
    /// <param name="name">The option with its dashes.</param>
    /// <param name="value">The value's name.</param>
    /// <param name="help">One line about it.</param>
    /// <returns>The spec.</returns>
    public static OptionSpec Valued(string name, string value, string help) => new(name, value, Required: false, help);

    /// <summary>An option with a value the command needs.</summary>
    /// <param name="name">The option with its dashes.</param>
    /// <param name="value">The value's name.</param>
    /// <param name="help">One line about it.</param>
    /// <returns>The spec.</returns>
    public static OptionSpec Needed(string name, string value, string help) => new(name, value, Required: true, help);

    /// <summary>An option with a value that may be given any number of times, each value kept in order.</summary>
    /// <param name="name">The option with its dashes.</param>
    /// <param name="value">The value's name.</param>
    /// <param name="help">One line about it.</param>
    /// <returns>The spec.</returns>
    public static OptionSpec Repeated(string name, string value, string help) => new(name, value, Required: false, help) { Repeatable = true };

    /// <summary>Whether the option may be given more than once; every value is kept.</summary>
    public bool Repeatable { get; init; }

    /// <summary>The usage form: <c>--json</c>, <c>--limit &lt;n&gt;</c>, optional ones in brackets.</summary>
    public string Usage
    {
        get
        {
            string form = Value is null ? Name : $"{Name} <{Value}>";
            return Required ? form : Repeatable ? $"[{form}]..." : $"[{form}]";
        }
    }
}
