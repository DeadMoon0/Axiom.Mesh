namespace Axiom.Mesh.Cli;

/// <summary>One step of an <see cref="AutostartPlan"/>, run in order by <see cref="AutostartShell"/>.</summary>
internal abstract record AutostartStep
{
    /// <summary>The step as one line, for the output.</summary>
    /// <returns>The line.</returns>
    public abstract string Describe();
}
