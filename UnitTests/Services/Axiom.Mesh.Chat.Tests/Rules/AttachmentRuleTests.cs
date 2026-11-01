using System;
using System.IO;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Chat.Tests.Rules;

/// <summary>
/// Attachments (CHAT-SERVICE.md §5.8, §5.9; PAYLOADS.md §3; S-D10): a file is attached as a ChunkList, a folder as a Tree
/// whose size is the sum of its files'; the name defaults to the path's last name; written back, a ChunkList becomes a file
/// (Fail or Replace) and a Tree a folder (Merge too).
/// </summary>
public sealed class AttachmentRuleTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\data" : "/data";

    // §5.8, S-D10: an absolute file is a File, a folder a Folder; a relative path is Invalid, nothing there NotFound; the
    // name is the one asked for, else the last name of the path; a folder's size is the sum of its files'.
    [Fact]
    public void AFileIsAttachedAsAChunkList_AFolderAsATreeSizedByItsFiles_NamedByThePathUnlessNamed()
    {
        string file = Path.Combine(Root, "photo.jpg");
        string folder = Path.Combine(Root, "album") + Path.DirectorySeparatorChar;

        Assert.True(AttachmentRule.ForAttach(file, isFile: true, isDirectory: false, out AttachmentKind fileKind).IsPass);
        Assert.True(AttachmentRule.ForAttach(folder, isFile: false, isDirectory: true, out AttachmentKind folderKind).IsPass);
        Assert.Equal((AttachmentKind.File, AttachmentKind.Folder), (fileKind, folderKind));
        Assert.Equal(Status.Invalid, AttachmentRule.ForAttach("photo.jpg", true, false, out _).Status);
        Assert.Equal(Status.NotFound, AttachmentRule.ForAttach(file, false, false, out _).Status);
        Assert.True(AttachmentRule.NameOf(string.Empty, folder, out string defaulted).IsPass);
        Assert.True(AttachmentRule.NameOf("holiday", file, out string named).IsPass);
        Assert.Equal(("album", "holiday"), (defaulted, named));
        Assert.Equal(Status.Invalid, AttachmentRule.NameOf(new string('n', ChatLimits.AttachmentNameLength + 1), file, out _).Status);
        Assert.Equal(1_500ul, AttachmentRule.SizeOf(new PublishedTree { Root = default, Files = 2, Bytes = 1_500, Objects = [] }));
    }

    // §5.9: a ChunkList is written as a file with Fail or Replace, Merge is Invalid for it; a Tree takes all three; anything
    // else is no attachment; the path must be absolute.
    [Theory]
    [InlineData("File", ExistingPath.Fail, true)] // a file, Fail
    [InlineData("File", ExistingPath.Replace, true)] // a file, Replace
    [InlineData("File", ExistingPath.Merge, false)] // a file cannot be merged
    [InlineData("Folder", ExistingPath.Merge, true)] // a folder may be merged into one there
    [InlineData("Unassigned", ExistingPath.Fail, false)] // neither ChunkList nor Tree
    [InlineData("Folder", ExistingPath.Unassigned, false)] // an existing value naming nothing
    public void AChunkListIsWrittenAsAFile_ATreeAsAFolder_MergeOnlyForAFolder(string kind, ExistingPath existing, bool allowed)
    {
        Assert.Equal(allowed, AttachmentRule.ForGet(Enum.Parse<AttachmentKind>(kind), existing, Path.Combine(Root, "out")).IsPass);
        Assert.Equal(Status.Invalid, AttachmentRule.ForGet(AttachmentKind.File, ExistingPath.Fail, "relative").Status);
    }
}
