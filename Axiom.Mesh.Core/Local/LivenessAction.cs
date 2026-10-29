namespace Axiom.Mesh.Core;

/// <summary>What liveness does next (LOCAL-SPEC.md §4.4).</summary>
internal enum LivenessAction
{
    /// <summary>Nothing yet: look again after the step's wait.</summary>
    Wait = 0,

    /// <summary>Send a <c>Ping</c>.</summary>
    Ping = 1,

    /// <summary>Close with <c>Bye(Unresponsive)</c>.</summary>
    Unresponsive = 2,
}
