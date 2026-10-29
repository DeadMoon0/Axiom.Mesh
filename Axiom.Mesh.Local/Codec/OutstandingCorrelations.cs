namespace Axiom.Mesh.Local;

/// <summary>
/// The correlations of one connection's requests whose <c>Response</c> has not yet been sent (LOCAL-SPEC.md §3.4): at
/// most 64, none of them 0, none twice. The Host's codec begins one per request it reads and ends it per response it
/// writes, so the rules hold for every connection without the Host's handlers knowing them.
/// </summary>
internal sealed class OutstandingCorrelations
{
    private readonly uint[] slots = new uint[Limits.MaxOutstandingRequests];
    private readonly object gate = new();
    private int count;

    /// <summary>The number outstanding.</summary>
    public int Count
    {
        get
        {
            lock (gate)
                return count;
        }
    }

    /// <summary>Begins a request with <paramref name="correlation"/>.</summary>
    /// <param name="correlation">The request's correlation.</param>
    /// <returns><see cref="CorrelationCheck.Begun"/>, or which rule it breaks.</returns>
    public CorrelationCheck TryBegin(uint correlation)
    {
        if (correlation == 0)
            return CorrelationCheck.Zero;

        lock (gate)
        {
            for (int i = 0; i < count; i++)
            {
                if (slots[i] == correlation)
                    return CorrelationCheck.Outstanding;
            }

            if (count == slots.Length)
                return CorrelationCheck.TooMany;
            slots[count++] = correlation;
            return CorrelationCheck.Begun;
        }
    }

    /// <summary>Ends the request with <paramref name="correlation"/>, when its response is sent; nothing when it is not outstanding.</summary>
    /// <param name="correlation">The response's correlation.</param>
    public void End(uint correlation)
    {
        lock (gate)
        {
            for (int i = 0; i < count; i++)
            {
                if (slots[i] == correlation)
                {
                    slots[i] = slots[--count];
                    return;
                }
            }
        }
    }
}
