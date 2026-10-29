using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Axiom.Mesh.Core;

/// <summary>
/// The self-signed X.509 certificate a device presents in every TLS handshake (SPEC.md §2.4): its subject key is the
/// device key, it carries Digital Signature and both server and client authentication, it is valid for ten years and
/// is named <c>CN=&lt;device id&gt;</c> for logs. Nothing else in it means anything: peers pin the key, never the
/// certificate (§4.3).
/// </summary>
public static class DeviceCertificate
{
    private const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";
    private const string ClientAuthentication = "1.3.6.1.5.5.7.3.2";
    private const string EcPublicKey = "1.2.840.10045.2.1";

    // The DER encoding of the curve's object identifier 1.2.840.10045.3.1.7 (secp256r1, P-256), as a certificate's key
    // algorithm parameters carry it.
    private static ReadOnlySpan<byte> P256Parameters => [0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07];

    /// <summary>How long a new certificate is valid (SPEC.md §2.4).</summary>
    public static TimeSpan Validity { get; } = TimeSpan.FromDays(3653);

    /// <summary>
    /// Creates the certificate of a device key. It goes through a PKCS#12 round trip, because Windows' TLS stack cannot
    /// use a key that only lives in memory (DESIGN.md §6.3); dispose it when the node stops, which deletes the key the
    /// round trip persisted.
    /// </summary>
    /// <param name="deviceKey">The device's P-256 key pair; it is not kept.</param>
    /// <param name="now">The current time: validity starts a day before it (clock skew) and ends ten years after.</param>
    /// <returns>The certificate with its private key.</returns>
    /// <exception cref="ArgumentException">The key is not a P-256 key pair.</exception>
    public static X509Certificate2 Create(ECDsa deviceKey, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(deviceKey);
        ECParameters parameters = deviceKey.ExportParameters(includePrivateParameters: false);
        if (!parameters.Curve.IsNamed || parameters.Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
            throw new ArgumentException("A device key is a P-256 key pair (SPEC.md §2.3).", nameof(deviceKey));

        Span<byte> point = stackalloc byte[PublicKey.Length];
        point[0] = 0x04;
        parameters.Q.X.AsSpan().CopyTo(point[1..]);
        parameters.Q.Y.AsSpan().CopyTo(point[33..]);
        string device = new DeviceId(Hash32.Of(point)).ToString();

        var request = new CertificateRequest("CN=" + device, deviceKey, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(ServerAuthentication), new Oid(ClientAuthentication)], false));
        using X509Certificate2 ephemeral = request.CreateSelfSigned(now.AddDays(-1), now.Add(Validity));
        byte[] pkcs12 = ephemeral.Export(X509ContentType.Pkcs12);
        try
        {
            return X509CertificateLoader.LoadPkcs12(pkcs12, null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs12);
        }
    }

    /// <summary>
    /// Reads the 65-byte uncompressed point of a certificate's subject key, when the key is an EC key on P-256: the
    /// only thing about a peer's certificate that counts (SPEC.md §4.3). Whether the point is on the curve is not
    /// checked here.
    /// </summary>
    /// <param name="certificate">A certificate a peer presented.</param>
    /// <param name="point">The point, or empty.</param>
    /// <returns><see langword="true"/> when the key is a P-256 point in uncompressed form.</returns>
    public static bool TryGetPoint(X509Certificate certificate, out ReadOnlyMemory<byte> point)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        point = ReadOnlyMemory<byte>.Empty;
        if (certificate.GetKeyAlgorithm() != EcPublicKey || !certificate.GetKeyAlgorithmParameters().AsSpan().SequenceEqual(P256Parameters))
            return false;
        byte[] key = certificate.GetPublicKey();
        if (key.Length != PublicKey.Length || key[0] != 0x04)
            return false;
        point = key;
        return true;
    }
}
