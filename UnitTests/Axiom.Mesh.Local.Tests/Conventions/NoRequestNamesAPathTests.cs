using System;
using System.Linq;
using System.Reflection;

namespace Axiom.Mesh.Local.Tests.Conventions;

/// <summary>
/// The Host opens no path a client names (LOCAL-SPEC.md draft 4, DESIGN.md §11, fundamentals plan §6f): writing a file into
/// the store and a Tree onto disk are the client's helpers, run as the client's own user, so no request of the local
/// protocol carries a path, a directory or a file name for the Host to open.
/// </summary>
public sealed class NoRequestNamesAPathTests
{
    private static readonly string[] PathWords = ["path", "directory", "folder", "file"];

    [Fact]
    public void NoRequestHasAFieldThatNamesAPathForTheHostToOpen()
    {
        var offenders = RequestTable.Default.All
            .SelectMany(d => d.RequestType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => (Request: d.Name, Property: p)))
            .Where(x => x.Property.PropertyType == typeof(string) && PathWords.Any(w => x.Property.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .Select(x => $"{x.Request}.{x.Property.Name}")
            .ToList();

        Assert.True(RequestTable.Default.All.Count() > 40, "The request table looks empty; the test reads nothing.");
        Assert.True(offenders.Count == 0, "Requests that name a path for the Host to open: " + string.Join(", ", offenders));
    }
}
