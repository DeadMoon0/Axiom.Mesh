namespace Axiom.Mesh.Local;

/// <summary>The outcome of beginning a request's correlation (LOCAL-SPEC.md §3.4); every value but the first is a violation.</summary>
internal enum CorrelationCheck : byte
{
    /// <summary>The request is outstanding now.</summary>
    Begun = 0,

    /// <summary>The correlation is 0.</summary>
    Zero = 1,

    /// <summary>A request with the same correlation is still outstanding.</summary>
    Outstanding = 2,

    /// <summary>64 requests are outstanding already; this is the 65th.</summary>
    TooMany = 3,
}
