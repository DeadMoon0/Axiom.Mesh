using System;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Axiom.Mesh.Core;

/// <summary>
/// What this device presents in a handshake: its device certificate (SPEC.md §2.4) with the TLS stack's context for
/// it, built once so that no handshake builds a chain again, and the device key's public half and id.
/// </summary>
internal sealed class DeviceCredential : IDisposable
{
    private DeviceCredential(X509Certificate2 certificate, SslStreamCertificateContext context, PublicKey key)
    {
        Certificate = certificate;
        Context = context;
        Key = key;
    }

    /// <summary>The device certificate with its private key.</summary>
    public X509Certificate2 Certificate { get; }

    /// <summary>The TLS stack's context for the certificate, offered as server and as client certificate.</summary>
    public SslStreamCertificateContext Context { get; }

    /// <summary>The device key's public half.</summary>
    public PublicKey Key { get; }

    /// <summary>The device id: <c>H</c> of the key (SPEC.md §2.3).</summary>
    public DeviceId Device => Key.AsDeviceId();

    /// <summary>Builds the credential of a device key.</summary>
    /// <param name="pkcs8">The device key as PKCS#8; not kept.</param>
    /// <param name="now">The current time, for the certificate's validity.</param>
    /// <returns>The credential; dispose it when the node stops.</returns>
    /// <exception cref="ArgumentException">The bytes are not a P-256 key pair.</exception>
    public static DeviceCredential Create(ReadOnlySpan<byte> pkcs8, DateTimeOffset now)
    {
        using ECDsa key = ECDsa.Create();
        try
        {
            key.ImportPkcs8PrivateKey(pkcs8, out _);
        }
        catch (CryptographicException ex)
        {
            throw new ArgumentException("The device key is not a PKCS#8 P-256 key pair.", nameof(pkcs8), ex);
        }

        X509Certificate2 certificate = DeviceCertificate.Create(key, now);
        try
        {
            if (!DeviceCertificate.TryGetPoint(certificate, out ReadOnlyMemory<byte> point) || !PublicKey.TryParse(point.Span, out PublicKey? publicKey))
                throw new ArgumentException("The device key's public point is not on P-256.", nameof(pkcs8));
            return new DeviceCredential(certificate, SslStreamCertificateContext.Create(certificate, null, offline: true), publicKey);
        }
        catch
        {
            certificate.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose() => Certificate.Dispose();
}
