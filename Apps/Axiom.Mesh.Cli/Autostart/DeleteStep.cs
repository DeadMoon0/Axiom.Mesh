namespace Axiom.Mesh.Cli;

/// <summary>Deletes a file; one that does not exist is no failure.</summary>
/// <param name="Path">The file, absolute.</param>
/// <param name="Removes">Whether the file existing is what "something was registered" means for an uninstall.</param>
internal sealed record DeleteStep(string Path, bool Removes) : AutostartStep
{
    /// <inheritdoc />
    public override string Describe() => "delete " + Path;
}
