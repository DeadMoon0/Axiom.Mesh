using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Authentication;

namespace Axiom.Mesh.Core;

/// <summary>
/// Keeps the mesh port from issuing a TLS 1.3 session ticket where .NET runs on OpenSSL (SPEC.md §4.2: "Session tickets
/// MUST NOT be issued").
/// </summary>
/// <remarks>
/// <para>With <c>AllowTlsResume = false</c> .NET's shim sets only <c>SSL_OP_NO_TICKET</c> on the server's context
/// (<c>CryptoNative_SslCtxSetCaching</c> in dotnet/runtime <c>src/native/libs/System.Security.Cryptography.Native/pal_ssl.c</c>,
/// release/8.0 and release/10.0 alike; nothing in the shim or in <c>Interop.OpenSsl.cs</c> calls <c>SSL_CTX_set_num_tickets</c>).
/// In TLS 1.3 that option does not suppress tickets: "In TLSv1.3 a stateful ticket will be sent. [...] it is possible to
/// suppress all tickets (stateful and stateless) from being sent by calling SSL_CTX_set_num_tickets(3) or
/// SSL_set_num_tickets(3)" (OpenSSL 3.0, <c>doc/man3/SSL_CTX_set_options.pod</c>, <c>SSL_OP_NO_TICKET</c>). So the first
/// run of <c>tls-scan.yml</c> saw a ticket "valid for 7200 seconds" that no cache would ever resume (STRUCTURAL-DEBT.md 86).</para>
/// <para>The <c>SSL*</c> of a handshake lives in <see cref="SslStream"/>'s private field <c>_securityContext</c> (a
/// <c>SafeSslHandle</c>, <c>SslStream.Protocol.cs</c> in both releases), allocated when the ClientHello is processed and
/// consulted for the ticket count only after the client's Finished (<c>ossl_statem_server13_write_transition</c>, case
/// <c>TLS_ST_SR_FINISHED</c>: <c>s-&gt;num_tickets &gt; s-&gt;sent_tickets</c>). Between the two the stream writes its first
/// flight, so <see cref="MeshTlsLayer"/> calls <see cref="IssueNone"/> from a <see cref="FirstWriteHook"/> under the
/// stream. The call goes to the libssl the runtime itself loaded (found in <c>/proc/self/maps</c>, else by the shim's
/// own names), never to a second copy.</para>
/// <para>Windows (SChannel) and macOS are left as they are; <see cref="Applies"/> is false there.</para>
/// </remarks>
internal static class OpenSslTickets
{
    private const string ContextFieldName = "_securityContext";
    private const string Library = "axiom-mesh-libssl";

    // The runtime binds the import once it resolved; a failed resolution is retried by the next handshake, which fails
    // closed again. So no state lives here (the package's convention: no mutable static state).
    static OpenSslTickets() => NativeLibrary.SetDllImportResolver(typeof(OpenSslTickets).Assembly, Resolve);

    /// <summary>Whether this runtime's TLS is OpenSSL, where the shim leaves TLS 1.3 tickets on.</summary>
    public static bool Applies { get; } = OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD();

    /// <summary>
    /// Sets the handshake's ticket count to zero (<c>SSL_set_num_tickets(ssl, 0)</c>); to be called once the server stream
    /// has processed the ClientHello and before it reads the client's Finished.
    /// </summary>
    /// <param name="server">The server-side stream in its handshake.</param>
    /// <exception cref="AuthenticationException">The handle or libssl is not where this runtime keeps it; the handshake fails
    /// rather than issue a ticket.</exception>
    public static void IssueNone(SslStream server)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (ContextOf(server) is not SafeHandle handle || handle.IsInvalid)
            throw new AuthenticationException($"SslStream.{ContextFieldName} holds no TLS handle before the first flight; this runtime is not one OpenSslTickets knows, and it would issue a session ticket (SPEC.md §4.2).");

        bool referenced = false;
        try
        {
            handle.DangerousAddRef(ref referenced);
            int result;
            try
            {
                result = SslSetNumTickets(handle.DangerousGetHandle(), 0);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                throw new AuthenticationException($"The runtime's libssl offers no SSL_set_num_tickets, so the handshake would issue a session ticket (SPEC.md §4.2): {ex.Message}", ex);
            }

            if (result != 1)
                throw new AuthenticationException("SSL_set_num_tickets(ssl, 0) failed, so the handshake would issue a session ticket (SPEC.md §4.2).");
        }
        finally
        {
            if (referenced)
                handle.DangerousRelease();
        }
    }

    // Blittable throughout, so a plain DllImport needs no marshalling stub (the project allows no unsafe code for LibraryImport).
    [DllImport(Library, EntryPoint = "SSL_set_num_tickets", ExactSpelling = true)]
    private static extern int SslSetNumTickets(IntPtr ssl, nuint count);

    // typeof(SslStream).GetField with a constant name: the trimmer keeps that field, so this is AOT-safe without a hint.
    private static object? ContextOf(SslStream server) =>
        typeof(SslStream).GetField(ContextFieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(server);

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath) =>
        name == Library ? LoadTheRuntimesLibssl() : IntPtr.Zero;

    // The library the .NET shim dlopen'd (opensslshim.c tries libssl.so.3, then libssl.so.1.1, then libssl.so.1.0.2, or
    // DOTNET_OPENSSL_VERSION_OVERRIDE); dlopen of a path already loaded returns that same library.
    private static IntPtr LoadTheRuntimesLibssl()
    {
        foreach (string path in MappedLibsslPaths())
        {
            if (NativeLibrary.TryLoad(path, out IntPtr loaded))
                return loaded;
        }

        foreach (string name in new[] { "libssl.so.3", "libssl.so.1.1" })
        {
            if (NativeLibrary.TryLoad(name, out IntPtr loaded))
                return loaded;
        }

        throw new DllNotFoundException("No libssl is mapped into this process and neither libssl.so.3 nor libssl.so.1.1 loads.");
    }

    private static IEnumerable<string> MappedLibsslPaths()
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines("/proc/self/maps");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            int at = line.IndexOf('/');
            if (at < 0)
                continue;
            string path = line[at..].Trim();
            if (Path.GetFileName(path).StartsWith("libssl.so", StringComparison.Ordinal) && seen.Add(path))
                yield return path;
        }
    }
}
