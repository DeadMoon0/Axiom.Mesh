namespace Axiom.Mesh.Cli;

/// <summary>Writes a text file, creating its directory.</summary>
/// <param name="Path">The file, absolute.</param>
/// <param name="Text">Its content.</param>
/// <param name="Utf16">Whether it is written as UTF-16 with a byte order mark (a task definition for <c>schtasks</c>); else UTF-8 without one.</param>
internal sealed record WriteStep(string Path, string Text, bool Utf16) : AutostartStep
{
    /// <inheritdoc />
    public override string Describe() => "write " + Path;
}
