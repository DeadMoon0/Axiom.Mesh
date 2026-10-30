using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Core;

namespace Axiom.Mesh.Host;

/// <summary>
/// The Unix domain socket on Linux (LOCAL-SPEC.md §2.1, §2.2): a socket file left by a crashed daemon is removed only
/// after a connection to it was refused, and one that answers belongs to a running daemon, so this one does not start;
/// the file is made 0600. For every connection the peer's pid, uid and gid come from <c>SO_PEERCRED</c> and its
/// executable from <c>/proc/&lt;pid&gt;/exe</c>; the user is the uid, as decimal digits.
/// </summary>
[UnsupportedOSPlatform("windows")]
internal sealed class UnixSocketListener : IPipeListener
{
    private const int SolSocket = 1;
    private const int SoPeerCred = 17;

    private readonly Socket socket;
    private readonly string path;

    private UnixSocketListener(Socket socket, string path, uint uid)
    {
        this.socket = socket;
        this.path = path;
        HostUser = uid.ToString(CultureInfo.InvariantCulture);
        RunningAsService = uid == 0;
    }

    /// <inheritdoc />
    public string Location => path;

    /// <inheritdoc />
    public string HostUser { get; }

    /// <inheritdoc />
    public bool RunningAsService { get; }

    /// <summary>Binds the socket file, taking it over only when nobody answers there.</summary>
    /// <param name="path">The socket's path.</param>
    /// <returns>The listener.</returns>
    /// <exception cref="HostException">A daemon answers at the path, or the path cannot be bound.</exception>
    public static UnixSocketListener Open(string path)
    {
        var endPoint = new UnixDomainSocketEndPoint(path);
        string? directory = Path.GetDirectoryName(path);
        if (directory is not null)
            HostFiles.CreatePrivateDirectory(directory);
        if (File.Exists(path))
            TakeOverStale(path, endPoint);

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Bind(endPoint);
            File.SetUnixFileMode(path, HostFiles.PrivateFile);
            socket.Listen(Limits.MaxUserConnections);
        }
        catch (SocketException ex)
        {
            socket.Dispose();
            throw new HostException(
                HostExitCode.Environment,
                MeshFault.Environment,
                $"The socket {path} cannot be bound: {ex.Message}",
                "Check that its directory exists and is the user's, then start the daemon again.");
        }

        return new UnixSocketListener(socket, path, GetUid());
    }

    /// <inheritdoc />
    public async ValueTask<PipeConnection> AcceptAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Socket client = await socket.AcceptAsync(cancellationToken).ConfigureAwait(false);
            byte[] credentials = new byte[12];
            try
            {
                client.GetRawSocketOption(SolSocket, SoPeerCred, credentials);
            }
            catch (SocketException)
            {
                client.Dispose();
                continue;
            }

            int pid = BinaryPrimitives.ReadInt32LittleEndian(credentials);
            uint uid = BinaryPrimitives.ReadUInt32LittleEndian(credentials.AsSpan(4));
            var identity = ProcessIdentity.Pipe(Math.Max(0, pid), uid.ToString(CultureInfo.InvariantCulture), ExecutableOf(pid));
            return new PipeConnection(new NetworkStream(client, ownsSocket: true), identity);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        socket.Dispose();
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Gone already.
        }

        return ValueTask.CompletedTask;
    }

    // §2.1: only a socket file that refuses a connection is stale.
    private static void TakeOverStale(string path, UnixDomainSocketEndPoint endPoint)
    {
        using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            probe.Connect(endPoint);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            File.Delete(path);
            return;
        }
        catch (SocketException ex)
        {
            throw new HostException(
                HostExitCode.Environment,
                MeshFault.Environment,
                $"{path} exists and cannot be probed: {ex.Message}",
                $"Check what {path} is; remove it if no daemon runs, then start again.");
        }

        throw new HostException(
            HostExitCode.PipeTaken,
            MeshFault.Environment,
            $"Another axiom-meshd answers at {path}, so this one does not start (LOCAL-SPEC.md §2.1).",
            "Use the daemon that runs, or stop it first.",
            "To run a second daemon, give it another socket with AXIOM_MESH_PIPE (and its clients the same variable) and another --home.");
    }

    private static string ExecutableOf(int pid)
    {
        try
        {
            return pid > 0 && File.ResolveLinkTarget($"/proc/{pid}/exe", returnFinalTarget: false) is { } target ? target.FullName : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    [DllImport("libc", EntryPoint = "getuid")]
    private static extern uint GetUid();
}
