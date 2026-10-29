using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core.Storage;
using Axiom.Mesh.Core.Tests.Support;

namespace Axiom.Mesh.Core.Tests.Regression;

/// <summary>The store directory as services read it directly (LOCAL-SPEC.md §9.7, layout 1).</summary>
public sealed class StoreLayoutTests
{
    // Damage: a service that reads objects directly finds them by this path alone; moving a file, nesting differently or
    // adding a byte to it breaks every such reader without a request failing.
    [Fact]
    public async Task AnObjectIsTheFileObjectsSlashH01SlashH23SlashItsHash_HoldingExactlyItsBytes()
    {
        using var temp = new TempDirectory();
        var files = new DirectoryObjectFiles(temp.Path, new DeterministicEntropy(3));
        Chunk chunk = Samples.Chunk(9, 4096);
        string hex = chunk.Hash.ToString();

        Assert.True(await files.WriteAtomicAsync(chunk.Hash, chunk.Bytes, CancellationToken.None));

        string expected = Path.Combine(Path.GetFullPath(temp.Path), "objects", hex[..2], hex[2..4], hex);
        Assert.Equal(expected, files.PathOf(chunk.Hash));
        Assert.Equal(chunk.Bytes.ToArray(), await File.ReadAllBytesAsync(expected));
        Assert.Equal(Path.GetFullPath(temp.Path), files.StorePath);
        Assert.Equal([expected], Directory.GetFiles(Path.Combine(temp.Path, "objects"), "*", SearchOption.AllDirectories).Select(Path.GetFullPath));
    }
}
