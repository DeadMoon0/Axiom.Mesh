using System.Collections.Generic;
using System.Linq;
using System.Text;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Sync.Tests.Support;

/// <summary>Fixed ids, Versions and folders for the rule tests: every value chosen, nothing random.</summary>
internal static class Facts
{
    public static DeviceId Laptop { get; } = new(Id(0x1a));

    public static DeviceId Desktop { get; } = new(Id(0xd3));

    public static DeviceId Phone { get; } = new(Id(0x9e));

    public static Hash32 Id(byte first) => new([first, .. Enumerable.Range(1, 31).Select(i => (byte)(first ^ i))]);

    public static ObjectHash Hash(byte first) => new(Id(first));

    public static EntryKey Key(ulong clock, DeviceId device, byte hash) => new(clock, device, Hash(hash));

    /// <summary>A Version with a chosen hash byte, clock and device, following the given Versions.</summary>
    public static VersionFact Version(byte hash, ulong clock, DeviceId device, string label = "main", ObjectHash tree = default, params VersionFact[] parents) =>
        new(Hash(hash), Key(clock, device, hash), [.. parents.Select(p => p.Hash).Order()], label, tree);

    public static IReadOnlyDictionary<ObjectHash, VersionFact> Dag(params VersionFact[] versions) => versions.ToDictionary(v => v.Hash);

    /// <summary>A file's entry: the ChunkList of its text.</summary>
    public static FlatEntry File(string text) => new(TreeEntryMode.File, ListOf(text).Hash, (ulong)Encoding.UTF8.GetByteCount(text));

    /// <summary>An executable file's entry.</summary>
    public static FlatEntry Executable(string text) => File(text) with { Mode = TreeEntryMode.ExecutableFile };

    /// <summary>A folder of files given as path and text; a path ending in '/' is an empty directory.</summary>
    public static FlatTree Folder(params (string Path, string Text)[] files) =>
        FlatTree.Of(files.Select(f => f.Path.EndsWith('/') ? new KeyValuePair<string, FlatEntry>(f.Path.TrimEnd('/'), FlatEntry.Directory) : new KeyValuePair<string, FlatEntry>(f.Path, File(f.Text))));

    /// <summary>The Trees of a folder, every one by hash, and its root.</summary>
    public static (Tree Root, Dictionary<ObjectHash, Tree> Trees) TreesOf(FlatTree folder)
    {
        Assert.True(TreeSet.TryBuild(folder, out TreeSet? set, out string? problem), problem);
        return (set!.Trees[^1], set.Trees.ToDictionary(t => t.Hash));
    }

    /// <summary>A Tree of files, with every Tree below it added to <paramref name="trees"/>.</summary>
    public static Tree Tree(Dictionary<ObjectHash, Tree> trees, params (string Path, string Text)[] files)
    {
        (Tree root, Dictionary<ObjectHash, Tree> made) = TreesOf(Folder(files));
        foreach ((ObjectHash hash, Tree tree) in made)
            trees[hash] = tree;
        return root;
    }

    /// <summary>The paths and texts a Tree holds, for assertions: files only, by path.</summary>
    public static IReadOnlyDictionary<string, ObjectHash> FilesOf(ObjectHash root, IReadOnlyDictionary<ObjectHash, Tree> trees)
    {
        Assert.True(TreeFlattener.TryOf(trees[root], trees, out FlatTree flat, out _));
        return flat.Files.ToDictionary(f => f.Key, f => f.Value.Hash);
    }

    public static ChunkList ListOf(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return ObjectBuilders.ChunkList((ulong)bytes.Length, bytes.Length == 0 ? [] : [ObjectBuilders.Chunk(bytes).Hash]);
    }

    public static IReadOnlySet<string> NoneBusy { get; } = new HashSet<string>();
}
