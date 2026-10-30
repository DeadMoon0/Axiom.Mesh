using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;
using Axiom.Mesh.Host.Tests.Support;

namespace Axiom.Mesh.Host.Tests.Rules;

/// <summary>
/// The daemon's key files (fundamentals plan §2.6, D3): a key written is read back exactly; on Windows the file holds it
/// wrapped with DPAPI for the user, never in the clear; on Linux the directory is 0700 and each file 0600, and a start
/// with a wider key directory is refused with the steps out.
/// </summary>
public sealed class FileKeyStoreTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task AKeyWrittenIsReadBackExactly_AndAKeyNeverWrittenIsAbsent()
    {
        using var home = new TempHome();
        FileKeyStore keys = FileKeyStore.Open(home.Paths.Keys);
        byte[] pkcs8 = NewKey();

        await keys.WriteAsync(KeyRole.Device, pkcs8, None);

        Assert.Equal(pkcs8, await FileKeyStore.Open(home.Paths.Keys).ReadAsync(KeyRole.Device, None));
        Assert.Null(await keys.ReadAsync(KeyRole.User, None));
    }

    [OsFact("windows")]
    public async Task OnWindowsTheFileHoldsTheKeyWrappedWithDpapi_NeverInTheClear()
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var home = new TempHome();
        FileKeyStore keys = FileKeyStore.Open(home.Paths.Keys);
        byte[] pkcs8 = NewKey();

        await keys.WriteAsync(KeyRole.User, pkcs8, None);

        byte[] stored = await File.ReadAllBytesAsync(Path.Combine(home.Paths.Keys, "user.key"));
        Assert.NotEqual(pkcs8, stored);
        Assert.False(Contains(stored, pkcs8[^32..]), "the key's last 32 bytes appear in the file");
        File.Copy(Path.Combine(home.Paths.Keys, "user.key"), Path.Combine(home.Paths.Keys, "device.key"));
        await Assert.ThrowsAsync<HostException>(async () => await keys.ReadAsync(KeyRole.Device, None));
    }

    [OsFact("linux")]
    public async Task OnLinuxTheDirectoryIs0700AndEachFile0600_AWiderDirectoryIsRefused()
    {
        if (!OperatingSystem.IsLinux())
            return;
        using var home = new TempHome();
        FileKeyStore keys = FileKeyStore.Open(home.Paths.Keys);
        await keys.WriteAsync(KeyRole.Device, NewKey(), None);

        Assert.Equal(HostFiles.PrivateDirectory, File.GetUnixFileMode(home.Paths.Keys));
        Assert.Equal(HostFiles.PrivateFile, File.GetUnixFileMode(Path.Combine(home.Paths.Keys, "device.key")));

        File.SetUnixFileMode(home.Paths.Keys, HostFiles.PrivateDirectory | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
        var refused = Assert.Throws<HostException>(() => FileKeyStore.Open(home.Paths.Keys));
        Assert.Contains("chmod 700", refused.RecoverySteps[0], StringComparison.Ordinal);
    }

    private static byte[] NewKey()
    {
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        return key.ExportPkcs8PrivateKey();
    }

    private static bool Contains(byte[] haystack, byte[] needle) =>
        Enumerable.Range(0, haystack.Length - needle.Length + 1).Any(i => haystack.AsSpan(i, needle.Length).SequenceEqual(needle));
}
