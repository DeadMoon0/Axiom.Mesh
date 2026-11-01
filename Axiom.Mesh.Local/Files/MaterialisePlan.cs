using System;
using System.Collections.Generic;
using System.Linq;

namespace Axiom.Mesh.Local;

/// <summary>
/// What writing a Tree to disk does (SPEC.md §8.4, §11.4), decided before a byte is written: the directories to create,
/// the files to write, and the names that are conflicts (a name the platform cannot hold, a case-insensitive collision,
/// an unknown mode, a directory whose Tree is not at hand). Pure: it reads the Trees it is given and nothing else.
/// </summary>
public sealed class MaterialisePlan
{
    private MaterialisePlan(IReadOnlyList<string> directories, IReadOnlyList<PlannedFile> files, IReadOnlyList<string> conflicts)
    {
        Directories = directories;
        Files = files;
        Conflicts = conflicts;
    }

    /// <summary>The subdirectories to create, parents before children, relative with <c>/</c>.</summary>
    public IReadOnlyList<string> Directories { get; }

    /// <summary>The files to write, depth first in Tree order.</summary>
    public IReadOnlyList<PlannedFile> Files { get; }

    /// <summary>The relative paths that are conflicts, not written.</summary>
    public IReadOnlyList<string> Conflicts { get; }

    /// <summary>The bytes the plan writes, by the sizes the Trees state.</summary>
    public ulong TotalBytes => Files.Aggregate(0ul, (sum, f) => sum + f.Size);

    /// <summary>The plan of a Tree.</summary>
    /// <param name="root">The Tree written into the target directory.</param>
    /// <param name="trees">The Trees its directory entries name, by hash; one missing makes its entry a conflict.</param>
    /// <param name="windows">The target file system is Windows'.</param>
    /// <returns>The plan.</returns>
    public static MaterialisePlan Of(Tree root, IReadOnlyDictionary<ObjectHash, Tree> trees, bool windows)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(trees);
        var directories = new List<string>();
        var files = new List<PlannedFile>();
        var conflicts = new List<string>();
        var visiting = new HashSet<ObjectHash> { root.Hash };
        Walk(root, string.Empty, trees, windows, visiting, directories, files, conflicts);
        return new MaterialisePlan(directories, files, conflicts);
    }

    private static void Walk(Tree tree, string prefix, IReadOnlyDictionary<ObjectHash, Tree> trees, bool windows, HashSet<ObjectHash> visiting, List<string> directories, List<PlannedFile> files, List<string> conflicts)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (TreeEntry entry in tree.Entries)
        {
            string path = prefix + entry.Name;
            if (!FileNameRule.MayWrite(entry.Name, windows) || !taken.Add(entry.Name))
            {
                conflicts.Add(path);
                continue;
            }

            switch (entry.Mode)
            {
                // A Tree that names itself, directly or further down, cannot exist (its hash would cover itself), but a
                // plan must end whatever it is given.
                case TreeEntryMode.Directory when trees.TryGetValue(entry.Hash, out Tree? child) && visiting.Add(child.Hash):
                    directories.Add(path);
                    Walk(child, path + "/", trees, windows, visiting, directories, files, conflicts);
                    visiting.Remove(child.Hash);
                    break;
                case TreeEntryMode.File:
                    files.Add(new PlannedFile(path, entry.Hash, PlannedFileKind.File, entry.Size));
                    break;
                case TreeEntryMode.ExecutableFile:
                    files.Add(new PlannedFile(path, entry.Hash, PlannedFileKind.Executable, entry.Size));
                    break;
                case TreeEntryMode.Symlink:
                    files.Add(new PlannedFile(path, entry.Hash, PlannedFileKind.Link, entry.Size));
                    break;
                default:
                    conflicts.Add(path);
                    break;
            }
        }
    }
}
