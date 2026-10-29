using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Axiom.Mesh.Local;

/// <summary>
/// Every request of LOCAL-SPEC.md §14.1 by message id: name, section, roles and body reader, generated from the request
/// classes. The Host's frame codec reads request bodies through it, and the Host's dispatcher checks roles with it
/// before any handler runs, so dispatching is data, not a switch.
/// </summary>
/// <remarks>
/// A request id in the request range that the table lacks is decoded as an <see cref="UnknownRequest"/> and answered
/// <c>UnknownRequest</c> (§1.5). A Host that implements only part of the table answers the rest <c>UnknownRequest</c>
/// itself; the table says what the protocol knows, not what a Host serves.
/// </remarks>
public sealed class RequestTable
{
    private readonly FrozenDictionary<ushort, RequestDescriptor> byId;

    private RequestTable(RequestDescriptor[] descriptors)
    {
        All = [.. descriptors.OrderBy(d => d.Id)];
        byId = descriptors.ToFrozenDictionary(d => d.Id);
    }

    /// <summary>The requests of <c>axiom-mesh-local/1</c>.</summary>
    public static RequestTable Default { get; } = new(RequestCatalog.Create());

    /// <summary>Every request, ascending by id.</summary>
    public IReadOnlyList<RequestDescriptor> All { get; }

    /// <summary>Finds the request with message id <paramref name="id"/>.</summary>
    /// <param name="id">A message id.</param>
    /// <param name="descriptor">The request, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the protocol knows the id as a request.</returns>
    public bool TryGet(ushort id, [NotNullWhen(true)] out RequestDescriptor? descriptor) => byId.TryGetValue(id, out descriptor);
}
