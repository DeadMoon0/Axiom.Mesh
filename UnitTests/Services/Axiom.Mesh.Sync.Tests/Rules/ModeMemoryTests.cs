namespace Axiom.Mesh.Sync.Tests.Rules;

/// <summary>
/// SPEC.md §8.4, §11.4: where the file system states an executable bit the scan's mode stands; where it has none, a file the
/// branch made executable stays executable.
/// </summary>
public sealed class ModeMemoryTests
{
    [Theory]
    [InlineData(TreeEntryMode.File, TreeEntryMode.ExecutableFile, false, TreeEntryMode.ExecutableFile)] // Windows keeps the bit a Linux device set
    [InlineData(TreeEntryMode.File, TreeEntryMode.ExecutableFile, true, TreeEntryMode.File)] // Linux states it: the bit was removed
    [InlineData(TreeEntryMode.ExecutableFile, TreeEntryMode.File, true, TreeEntryMode.ExecutableFile)] // Linux states it: the bit was set
    [InlineData(TreeEntryMode.File, null, false, TreeEntryMode.File)] // a new file on Windows is a file
    public void TheModeIsTheScansWhereTheBitIsKnown_ElseTheRememberedExecutableStays(TreeEntryMode scanned, TreeEntryMode? known, bool bitKnown, TreeEntryMode expected)
    {
        Assert.Equal(expected, ModeMemory.Of(scanned, known, bitKnown));
    }
}
