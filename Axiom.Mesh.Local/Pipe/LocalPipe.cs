using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.Local;

/// <summary>
/// The one place Axiom.Mesh.Local touches the operating system (LOCAL-SPEC.md §2.1): it finds the Host's pipe from the
/// running process and connects to it. Everything else of the package is a function of its inputs.
/// </summary>
public static class LocalPipe
{
    private const int ConnectTimeoutMilliseconds = 5_000;

    /// <summary>The pipe of the Host this process would talk to: its platform, <c>AXIOM_MESH_PIPE</c> and the defaults of §2.1.</summary>
    /// <returns>The location.</returns>
    /// <exception cref="MeshConfigurationException">The platform has no pipe, or the variable the default needs is missing.</exception>
    public static PipeLocation Locate() => PipeLocation.Resolve(CurrentPlatform(), CurrentEnvironment());

    /// <summary>
    /// The pipe of the Host this process would talk to when the Host's configuration names one: <c>AXIOM_MESH_PIPE</c>,
    /// else <paramref name="configuredName"/> (<c>pipe.name</c> in the Host's <c>config.json</c>), else the defaults of
    /// §2.1, the same precedence the Host listens by.
    /// </summary>
    /// <param name="configuredName">The configured name; <see langword="null"/> or empty when the configuration names none.</param>
    /// <returns>The location.</returns>
    /// <exception cref="MeshConfigurationException">The platform has no pipe, or the variable the default needs is missing.</exception>
    public static PipeLocation Locate(string? configuredName) =>
        PipeLocation.Resolve(CurrentPlatform(), CurrentEnvironment() with { ConfiguredName = configuredName });

    /// <summary>Connects to the Host's pipe: a named pipe on Windows, a Unix domain socket elsewhere.</summary>
    /// <param name="location">Where the pipe is, from <see cref="Locate()"/> or <see cref="PipeLocation.Resolve"/>.</param>
    /// <param name="cancellationToken">Cancels the connect.</param>
    /// <returns>A duplex stream, for <c>LocalClient.OpenAsync</c>.</returns>
    /// <exception cref="LocalConnectionException">Nothing answered at the pipe within 5 seconds: the Host is not running, or listens elsewhere.</exception>
    public static async Task<Stream> ConnectAsync(PipeLocation location, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        try
        {
            return location.Kind switch
            {
                PipeKind.NamedPipe => await ConnectNamedPipeAsync(location, cancellationToken).ConfigureAwait(false),
                PipeKind.UnixSocket => await ConnectUnixSocketAsync(location, cancellationToken).ConfigureAwait(false),
                _ => throw new ArgumentException("The location names no pipe.", nameof(location)),
            };
        }
        catch (Exception ex) when (ex is IOException or SocketException or TimeoutException or UnauthorizedAccessException)
        {
            throw LocalConnectionException.NotReachable(location, ex);
        }
    }

    private static async Task<Stream> ConnectNamedPipeAsync(PipeLocation location, CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(".", location.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(ConnectTimeoutMilliseconds, cancellationToken).ConfigureAwait(false);
            return pipe;
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<Stream> ConnectUnixSocketAsync(PipeLocation location, CancellationToken cancellationToken)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeoutMilliseconds);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(location.Path), timeout.Token).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            socket.Dispose();
            throw new TimeoutException($"Nothing answered at {location.Path} within {ConnectTimeoutMilliseconds} ms.");
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static LocalPlatform CurrentPlatform() =>
        OperatingSystem.IsWindows() ? LocalPlatform.Windows
        : OperatingSystem.IsAndroid() ? LocalPlatform.Android
        : OperatingSystem.IsIOS() ? LocalPlatform.IOS
        : OperatingSystem.IsMacOS() ? LocalPlatform.MacOS
        : OperatingSystem.IsLinux() ? LocalPlatform.Linux
        : LocalPlatform.Unknown;

    private static PipeEnvironment CurrentEnvironment()
    {
        string? runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        return new PipeEnvironment
        {
            Pipe = Environment.GetEnvironmentVariable(PipeLocation.EnvironmentVariable),
            XdgRuntimeDir = runtimeDir,
            TmpDir = Environment.GetEnvironmentVariable("TMPDIR"),
            UserId = OperatingSystem.IsLinux() && string.IsNullOrEmpty(runtimeDir) ? GetUid() : null,
        };
    }

    [DllImport("libc", EntryPoint = "getuid")]
    private static extern uint GetUid();
}
