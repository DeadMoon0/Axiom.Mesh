using System;
using System.IO;
using System.Text;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Chat;

/// <summary>
/// Attachments in and out (CHAT-SERVICE.md §5.8, §5.9; PAYLOADS.md §3; S-D10): a file is attached as a ChunkList with its
/// size, a folder as a Tree whose size is the sum of its files' sizes, for display only; the name shown defaults to the
/// path's last name. Written back, a ChunkList becomes a file (<c>Fail</c> or <c>Replace</c>) and a Tree a folder
/// (<c>Merge</c> as well); nothing else is an attachment. Pure: it reads the facts it is given.
/// </summary>
internal static class AttachmentRule
{
    /// <summary>What <c>attach</c> stores a path as.</summary>
    /// <param name="path">The path as requested.</param>
    /// <param name="isFile">A regular file is there.</param>
    /// <param name="isDirectory">A directory is there.</param>
    /// <param name="kind">File or Folder when the verdict passes.</param>
    /// <returns>Pass, or why not: a relative path is <c>Invalid</c>, nothing there <c>NotFound</c>.</returns>
    public static Verdict ForAttach(string path, bool isFile, bool isDirectory, out AttachmentKind kind)
    {
        ArgumentNullException.ThrowIfNull(path);
        kind = AttachmentKind.Unassigned;
        if (!Path.IsPathFullyQualified(path))
            return Verdict.Refuse(Status.Invalid, "The path to attach must be absolute; the service resolves nothing against a directory of its own.", "path");
        if (!isFile && !isDirectory)
            return Verdict.Refuse(Status.NotFound, "Nothing is at the path to attach.", "path");
        kind = isDirectory ? AttachmentKind.Folder : AttachmentKind.File;
        return Verdict.Pass;
    }

    /// <summary>The name an attachment shows: the one requested, else the path's last name.</summary>
    /// <param name="requested">The name requested; empty for the default.</param>
    /// <param name="path">The absolute path attached.</param>
    /// <param name="name">The name, when the verdict passes.</param>
    /// <returns>Pass, or <c>Invalid</c> when the default name is empty or longer than <see cref="ChatLimits.AttachmentNameLength"/> bytes.</returns>
    public static Verdict NameOf(string requested, string path, out string name)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(path);
        name = requested.Length > 0 ? requested : Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return name.Length == 0 || Encoding.UTF8.GetByteCount(name) > ChatLimits.AttachmentNameLength
            ? Verdict.Refuse(Status.Invalid, $"An attachment's name is 1 to {ChatLimits.AttachmentNameLength} bytes; give one with the request.", "name")
            : Verdict.Pass;
    }

    /// <summary>The size a folder attachment shows (S-D10): the sum of its files' sizes, display only.</summary>
    /// <param name="published">The folder as stored.</param>
    /// <returns>Its size.</returns>
    public static ulong SizeOf(PublishedTree published)
    {
        ArgumentNullException.ThrowIfNull(published);
        return published.Bytes;
    }

    /// <summary>How <c>get</c> writes an attachment back.</summary>
    /// <param name="kind">What the attachment's object is: File for a ChunkList, Folder for a Tree, Unassigned for anything else.</param>
    /// <param name="existing">What to do when the path exists.</param>
    /// <param name="path">The path to write.</param>
    /// <returns>Pass, or <c>Invalid</c>: not an attachment, a relative path, <c>Merge</c> for a file, or an existing value that names nothing.</returns>
    public static Verdict ForGet(AttachmentKind kind, ExistingPath existing, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!Path.IsPathFullyQualified(path))
            return Verdict.Refuse(Status.Invalid, "The path to write must be absolute.", "path");
        if (kind == AttachmentKind.Unassigned)
            return Verdict.Refuse(Status.Invalid, "The object is neither a ChunkList nor a Tree, so it is no attachment.", "hash");
        if (existing is not (ExistingPath.Fail or ExistingPath.Replace or ExistingPath.Merge))
            return Verdict.Refuse(Status.Invalid, "Existing is Fail, Replace or Merge.", "existing");
        if (kind == AttachmentKind.File && existing == ExistingPath.Merge)
            return Verdict.Refuse(Status.Invalid, "A file cannot be merged into what is there; Merge is for folders. Use Fail or Replace.", "existing");
        return Verdict.Pass;
    }
}
