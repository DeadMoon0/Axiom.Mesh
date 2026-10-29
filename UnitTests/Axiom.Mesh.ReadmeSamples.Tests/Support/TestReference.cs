namespace Axiom.Mesh.ReadmeSamples.Tests.Support;

/// <summary>A test a document names, written <c>`&lt;Project&gt;: &lt;Class&gt;.&lt;Method&gt;`</c>.</summary>
/// <param name="Project">The test project without the <c>Axiom.Mesh.</c> prefix (<c>Model.Tests</c>), or <c>Shared</c>.</param>
/// <param name="Type">The class that declares the test.</param>
/// <param name="Method">The test method.</param>
internal sealed record TestReference(string Project, string Type, string Method)
{
    /// <inheritdoc />
    public override string ToString() => $"{Project}: {Type}.{Method}";
}
