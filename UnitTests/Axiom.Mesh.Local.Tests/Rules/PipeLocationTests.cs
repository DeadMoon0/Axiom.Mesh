namespace Axiom.Mesh.Local.Tests.Rules;

/// <summary>
/// Where the pipe is (LOCAL-SPEC.md §2.1): <c>AXIOM_MESH_PIPE</c> when set and not empty, else the platform's default;
/// a client never guesses another name.
/// </summary>
public sealed class PipeLocationTests
{
    [Theory]
    [InlineData(LocalPlatform.Windows, null, null, null, null, PipeKind.NamedPipe, @"\\.\pipe\axiom-mesh")] // the Windows default
    [InlineData(LocalPlatform.Windows, "", null, null, null, PipeKind.NamedPipe, @"\\.\pipe\axiom-mesh")] // an empty variable counts as unset
    [InlineData(LocalPlatform.Windows, "axiom-mesh-test", null, null, null, PipeKind.NamedPipe, @"\\.\pipe\axiom-mesh-test")] // a bare name gets the pipe prefix
    [InlineData(LocalPlatform.Windows, @"\\.\pipe\other", null, null, null, PipeKind.NamedPipe, @"\\.\pipe\other")] // a full pipe path is kept
    [InlineData(LocalPlatform.Linux, null, "/run/user/1000", null, 1000u, PipeKind.UnixSocket, "/run/user/1000/axiom-mesh.sock")] // XDG_RUNTIME_DIR first
    [InlineData(LocalPlatform.Linux, null, "/run/user/1000/", null, null, PipeKind.UnixSocket, "/run/user/1000/axiom-mesh.sock")] // a trailing slash is not doubled
    [InlineData(LocalPlatform.Linux, null, null, null, 1001u, PipeKind.UnixSocket, "/run/user/1001/axiom-mesh.sock")] // else /run/user/<uid>
    [InlineData(LocalPlatform.Linux, "/tmp/test.sock", "/run/user/1000", null, 1000u, PipeKind.UnixSocket, "/tmp/test.sock")] // the variable wins on Linux
    [InlineData(LocalPlatform.MacOS, null, null, "/var/folders/xy/T/", null, PipeKind.UnixSocket, "/var/folders/xy/T/axiom-mesh.sock")] // $TMPDIR on macOS
    [InlineData(LocalPlatform.MacOS, "/tmp/mac.sock", null, "/var/folders/xy/T/", null, PipeKind.UnixSocket, "/tmp/mac.sock")] // and the variable wins there too
    public void ThePipeIsTheVariableElseThePlatformsDefault(LocalPlatform platform, string? pipe, string? xdg, string? tmp, uint? uid, PipeKind kind, string path)
    {
        var environment = new PipeEnvironment { Pipe = pipe, XdgRuntimeDir = xdg, TmpDir = tmp, UserId = uid };

        PipeLocation location = PipeLocation.Resolve(platform, environment);

        Assert.Equal(kind, location.Kind);
        Assert.Equal(path, location.Path);
    }

    [Fact]
    public void AWindowsPipeIsOpenedByItsNameWithoutThePrefix()
    {
        PipeLocation location = PipeLocation.Resolve(LocalPlatform.Windows, new PipeEnvironment());

        Assert.Equal("axiom-mesh", location.PipeName);
    }

    [Theory]
    [InlineData(LocalPlatform.Android)] // the app hosts the Host: an in-memory pair, no pipe
    [InlineData(LocalPlatform.IOS)] // the same on iOS
    [InlineData(LocalPlatform.Unknown)] // a platform the protocol does not name has no pipe either
    public void APlatformWithoutAPipeIsAConfigurationError(LocalPlatform platform)
    {
        var failure = Assert.Throws<MeshConfigurationException>(() => PipeLocation.Resolve(platform, new PipeEnvironment { Pipe = "ignored" }));

        Assert.Equal(MeshFault.Configuration, failure.Fault);
    }

    [Fact]
    public void ADefaultThatNeedsAMissingVariableIsAConfigurationError_NeverAGuess()
    {
        Assert.Throws<MeshConfigurationException>(() => PipeLocation.Resolve(LocalPlatform.Linux, new PipeEnvironment()));
        Assert.Throws<MeshConfigurationException>(() => PipeLocation.Resolve(LocalPlatform.MacOS, new PipeEnvironment()));
    }
}
