using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;

namespace Axiom.Mesh.Host;

/// <summary>
/// The daemon's private keys in files (fundamentals plan §2.6, D3): one PKCS#8 key per role in the key directory. On
/// Windows each is wrapped with DPAPI for the current user, so a copy of the file is useless to anyone else; on Linux
/// the directory is 0700 and each file 0600, and a start with either wider is refused. Writes are atomic: a crash leaves
/// the old key or the new one.
/// </summary>
internal sealed class FileKeyStore : IKeyStore
{
    private readonly string directory;

    private FileKeyStore(string directory) => this.directory = directory;

    /// <summary>The key directory.</summary>
    public string Directory => directory;

    /// <summary>Opens the key directory, creating it private when it does not exist.</summary>
    /// <param name="directory">The key directory.</param>
    /// <returns>The key store.</returns>
    /// <exception cref="HostException">On Linux, the directory or a key file grants anything to its group or to others.</exception>
    public static FileKeyStore Open(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        string full = Path.GetFullPath(directory);
        HostFiles.CreatePrivateDirectory(full);
        if (!OperatingSystem.IsWindows())
        {
            CheckPrivate(full, full, File.GetUnixFileMode(full));
            foreach (string file in System.IO.Directory.EnumerateFiles(full))
                CheckPrivate(full, file, File.GetUnixFileMode(file));
        }

        return new FileKeyStore(full);
    }

    /// <inheritdoc />
    public async ValueTask<byte[]?> ReadAsync(KeyRole role, CancellationToken cancellationToken)
    {
        string path = PathOf(role);
        if (!File.Exists(path))
            return null;
        byte[] stored = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (!OperatingSystem.IsWindows())
            return stored;
        try
        {
            return ProtectedData.Unprotect(stored, Purpose(role), DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            throw new HostException(
                HostExitCode.Configuration,
                MeshFault.Environment,
                $"The {Name(role)} key in {path} cannot be unwrapped: it was wrapped for another Windows user or on another machine ({ex.Message}).",
                "Start the daemon as the Windows user that created the key, on the machine it was created on.",
                "Or move the key directory aside to start without an identity; the device then needs a new identity or enrolment.");
        }
        finally
        {
            if (OperatingSystem.IsWindows())
                CryptographicOperations.ZeroMemory(stored);
        }
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(KeyRole role, ReadOnlyMemory<byte> pkcs8, CancellationToken cancellationToken)
    {
        if (role is not (KeyRole.Device or KeyRole.User))
            throw new ArgumentOutOfRangeException(nameof(role), role, "Store a device or a user key.");
        string path = PathOf(role);
        byte[] stored = OperatingSystem.IsWindows()
            ? ProtectedData.Protect(pkcs8.ToArray(), Purpose(role), DataProtectionScope.CurrentUser)
            : pkcs8.ToArray();
        try
        {
            string temporary = path + ".tmp";
            await HostFiles.WritePrivateAsync(temporary, stored, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(stored);
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"keys in {directory}";

    private string PathOf(KeyRole role) => Path.Combine(directory, Name(role) + ".key");

    private static string Name(KeyRole role) => role switch
    {
        KeyRole.Device => "device",
        KeyRole.User => "user",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "A device or a user key."),
    };

    // DPAPI's optional entropy binds a wrapped key to its role: a device key file copied over the user key does not unwrap.
    private static byte[] Purpose(KeyRole role) => Encoding.UTF8.GetBytes("axiom-mesh/key/1/" + Name(role));

    private static void CheckPrivate(string keys, string path, UnixFileMode mode)
    {
        if (!HostFiles.IsWiderThanOwner(mode))
            return;
        throw new HostException(
            HostExitCode.Configuration,
            MeshFault.Environment,
            $"{path} is readable or writable by others (mode {Convert.ToString((int)mode, 8)}); private keys must be the user's alone.",
            $"Run: chmod 700 {keys} && chmod 600 {keys}/*.key",
            "Then check who else may have copied the keys; if anyone could have, move the key directory aside and create a new identity.");
    }
}
