using System;

namespace Axiom.Mesh.Sync;

/// <summary>What a binding is, fixed for its life (SYNC-SERVICE.md §2): its id, directory, Archive, branch, symlink policy and ignore rule.</summary>
internal sealed class BindingSpec
{
    private BindingSpec(byte[] id, string directory, PackageId package, string label, SymlinkPolicy symlinks, IgnoreRule ignore)
    {
        Id = id;
        Directory = directory;
        Package = package;
        Label = label;
        Symlinks = symlinks;
        Ignore = ignore;
    }

    /// <summary>The binding's id.</summary>
    public byte[] Id { get; }

    /// <summary>The bound directory, absolute.</summary>
    public string Directory { get; }

    /// <summary>The Archive.</summary>
    public PackageId Package { get; }

    /// <summary>The branch.</summary>
    public string Label { get; }

    /// <summary>What to do with a symlink of the branch.</summary>
    public SymlinkPolicy Symlinks { get; }

    /// <summary>The names to leave alone.</summary>
    public IgnoreRule Ignore { get; }

    /// <summary>The spec a row describes; its patterns were checked when it was bound.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The spec.</returns>
    public static BindingSpec Of(BindingRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        string[] patterns = row.Ignore.Length == 0 ? [] : row.Ignore.Split('\n');
        IgnoreRule ignore = IgnoreRule.TryParse(patterns, out IgnoreRule? parsed, out _) ? parsed! : IgnoreRule.None;
        return new BindingSpec(row.Id, row.Directory, new PackageId(RowBytes.Hash(row.Package)), row.Label, (SymlinkPolicy)row.Symlinks, ignore);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Directory} with {Package} ({Label})";
}
