using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Axiom.Mesh.Core;
using Microsoft.Win32.SafeHandles;

namespace Axiom.Mesh.Host;

/// <summary>
/// The named pipe on Windows (LOCAL-SPEC.md §2.1, §2.2). Every instance is created with <c>CreateNamedPipe</c> directly,
/// because .NET's <c>NamedPipeServerStream</c> does not set <c>PIPE_REJECT_REMOTE_CLIENTS</c> (a connection through
/// <c>\\machine\pipe\…</c> reaches it; measured on .NET 8 and 10): <c>FILE_FLAG_FIRST_PIPE_INSTANCE</c> on the first, so
/// a daemon whose pipe name is taken does not start; <c>PIPE_REJECT_REMOTE_CLIENTS</c> on all; an access list that
/// admits the daemon's own account (and, for a system service, the allow list) and nobody else. For every connection it
/// reads the client's first bytes (a named pipe impersonates only after data was read), then the client's process id
/// (<c>GetNamedPipeClientProcessId</c>), its executable, and its user from its impersonation token, and hands the bytes on.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsPipeListener : IPipeListener
{
    private const uint PipeAccessDuplex = 0x3;
    private const uint FileFlagOverlapped = 0x4000_0000;
    private const uint FileFlagFirstPipeInstance = 0x0008_0000;
    private const uint PipeRejectRemoteClients = 0x8;
    private const uint UnlimitedInstances = 255;
    private const uint BufferBytes = 64 * 1024;
    private const int ErrorAccessDenied = 5;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    private readonly string name;
    private readonly byte[] securityDescriptor;
    private readonly TimeProvider time;
    private readonly CancellationTokenSource stop = new();
    private readonly Channel<PipeConnection> ready = Channel.CreateBounded<PipeConnection>(new BoundedChannelOptions(Limits.MaxConnectionsWaitingForHello) { SingleReader = true });
    private readonly LinkedList<CancellationTokenSource> identifying = new();
    private NamedPipeServerStream? listening;
    private Task accepting = Task.CompletedTask;

    private WindowsPipeListener(string name, byte[] securityDescriptor, TimeProvider time, string hostUser, bool runningAsService, NamedPipeServerStream first)
    {
        this.name = name;
        this.securityDescriptor = securityDescriptor;
        this.time = time;
        HostUser = hostUser;
        RunningAsService = runningAsService;
        listening = first;
    }

    /// <inheritdoc />
    public string Location => @"\\.\pipe\" + name;

    /// <inheritdoc />
    public string HostUser { get; }

    /// <inheritdoc />
    public bool RunningAsService { get; }

    /// <summary>Creates the pipe's first instance and starts accepting.</summary>
    /// <param name="name">The pipe's name, without <c>\\.\pipe\</c>.</param>
    /// <param name="allowUsers">Users a system service admits besides its own account.</param>
    /// <param name="time">The clock the 10 seconds for a client's first bytes run on.</param>
    /// <returns>The listener.</returns>
    /// <exception cref="HostException">The name is taken: another Host serves it.</exception>
    public static WindowsPipeListener Open(string name, IReadOnlyList<string> allowUsers, TimeProvider time)
    {
        using WindowsIdentity self = WindowsIdentity.GetCurrent();
        bool service = self.IsSystem || (self.User is { } sid && (sid.IsWellKnown(WellKnownSidType.LocalServiceSid) || sid.IsWellKnown(WellKnownSidType.NetworkServiceSid)));
        byte[] descriptor = AccessList(self.User!, service ? allowUsers : []);
        NamedPipeServerStream first = Create(name, descriptor, first: true);
        var listener = new WindowsPipeListener(name, descriptor, time, self.Name, service, first);
        listener.accepting = Task.Run(listener.AcceptLoopAsync, CancellationToken.None);
        return listener;
    }

    /// <inheritdoc />
    public ValueTask<PipeConnection> AcceptAsync(CancellationToken cancellationToken) => ready.Reader.ReadAsync(cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync().ConfigureAwait(false);
        Interlocked.Exchange(ref listening, null)?.Dispose();
        try
        {
            await accepting.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }

        ready.Writer.TryComplete();
        while (ready.Reader.TryRead(out PipeConnection? left))
            await left.Stream.DisposeAsync().ConfigureAwait(false);
        stop.Dispose();
    }

    // The access list: the daemon's own account (and the allow list of a system service) may read and write; nobody else,
    // and nothing is inherited.
    private static byte[] AccessList(SecurityIdentifier self, IReadOnlyList<string> allowUsers)
    {
        var security = new PipeSecurity();
        security.SetOwner(self);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(self, PipeAccessRights.FullControl, AccessControlType.Allow));
        foreach (string user in allowUsers)
        {
            try
            {
                var sid = (SecurityIdentifier)new NTAccount(user).Translate(typeof(SecurityIdentifier));
                security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.ReadWrite, AccessControlType.Allow));
            }
            catch (IdentityNotMappedException)
            {
                // A user the machine does not know cannot connect anyway; the allow list check refuses it too.
            }
        }

        return security.GetSecurityDescriptorBinaryForm();
    }

    private static NamedPipeServerStream Create(string name, byte[] descriptor, bool first)
    {
        GCHandle pinned = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = pinned.AddrOfPinnedObject(),
                InheritHandle = 0,
            };
            uint openMode = PipeAccessDuplex | FileFlagOverlapped | (first ? FileFlagFirstPipeInstance : 0);
            SafePipeHandle handle = CreateNamedPipeW(@"\\.\pipe\" + name, openMode, PipeRejectRemoteClients, UnlimitedInstances, BufferBytes, BufferBytes, 0, ref attributes);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastPInvokeError();
                handle.Dispose();
                if (first && error == ErrorAccessDenied)
                {
                    throw new HostException(
                        HostExitCode.PipeTaken,
                        MeshFault.Environment,
                        $"The pipe \\\\.\\pipe\\{name} exists already: another axiom-meshd serves it, so this one does not start (LOCAL-SPEC.md §2.1).",
                        "Use the daemon that runs, or stop it first.",
                        "To run a second daemon on this machine, give it another pipe with AXIOM_MESH_PIPE (and its clients the same variable) and another --home.");
                }

                throw new HostException(
                    HostExitCode.Environment,
                    MeshFault.Environment,
                    $"The pipe \\\\.\\pipe\\{name} cannot be created (Windows error {error}).",
                    "Check that the pipe name is valid, then start the daemon again.");
            }

            return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, handle);
        }
        finally
        {
            pinned.Free();
        }
    }

    private async Task AcceptLoopAsync()
    {
        CancellationToken stopping = stop.Token;
        while (!stopping.IsCancellationRequested)
        {
            NamedPipeServerStream server = Volatile.Read(ref listening) ?? Create(name, securityDescriptor, first: false);
            try
            {
                await server.WaitForConnectionAsync(stopping).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                if (stopping.IsCancellationRequested)
                    return;
                Volatile.Write(ref listening, null);
                continue;
            }

            // A new instance listens before this connection is looked at, so the next client never finds the pipe gone.
            Volatile.Write(ref listening, Create(name, securityDescriptor, first: false));
            _ = IdentifyAsync(server, stopping);
        }
    }

    // §2.3: at most 16 connections wait for their first bytes here; a 17th closes the one that waited longest.
    private async Task IdentifyAsync(NamedPipeServerStream server, CancellationToken stopping)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Limits.LocalHelloSeconds), time);
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(stopping, timeout.Token);
        LinkedListNode<CancellationTokenSource> place;
        lock (identifying)
        {
            place = identifying.AddLast(waiting);
            if (identifying.Count > Limits.MaxConnectionsWaitingForHello)
            {
                identifying.First!.Value.Cancel();
                identifying.RemoveFirst();
            }
        }

        try
        {
            byte[] buffer = new byte[256];
            int read = await server.ReadAsync(buffer, waiting.Token).ConfigureAwait(false);
            if (read == 0)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                return;
            }

            ProcessIdentity identity = IdentityOf(server);
            await ready.Writer.WriteAsync(new PipeConnection(new PrefixedStream(buffer[..read], server), identity), stopping).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or ChannelClosedException)
        {
            await server.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            lock (identifying)
            {
                if (place.List is not null)
                    identifying.Remove(place);
            }
        }
    }

    private static ProcessIdentity IdentityOf(NamedPipeServerStream server)
    {
        uint pid = GetNamedPipeClientProcessId(server.SafePipeHandle, out uint clientPid) ? clientPid : 0;
        string user = "(unknown)";
        try
        {
            server.RunAsClient(() =>
            {
                using WindowsIdentity client = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
                user = client.Name;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // A client that forbids impersonation keeps its user unknown, and the admission refuses it.
        }

        return ProcessIdentity.Pipe((int)Math.Min(pid, int.MaxValue), user, ExecutableOf(pid));
    }

    private static string ExecutableOf(uint pid)
    {
        if (pid == 0)
            return string.Empty;
        using SafeProcessHandle process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process.IsInvalid)
            return string.Empty;
        char[] path = new char[1024];
        int length = path.Length;
        return QueryFullProcessImageNameW(process, 0, path, ref length) ? new string(path, 0, length) : string.Empty;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafePipeHandle CreateNamedPipeW(string name, uint openMode, uint pipeMode, uint maxInstances, uint outBufferSize, uint inBufferSize, uint defaultTimeout, ref SecurityAttributes securityAttributes);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, [Out] char[] exeName, ref int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        public int InheritHandle;
    }
}
