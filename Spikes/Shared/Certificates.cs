using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Axiom.Mesh.Spikes;

/// <summary>Self-signed ECDSA P-256 device certificates, as the design states them, and the key they are pinned by.</summary>
public static class Certificates
{
    private static readonly Oid ServerAuth = new("1.3.6.1.5.5.7.3.1");
    private static readonly Oid ClientAuth = new("1.3.6.1.5.5.7.3.2");

    public static X509Certificate2 CreateDeviceCertificate(string deviceName)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={deviceName}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([ServerAuth, ClientAuth], false));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 ephemeral = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(10));

        // Windows' TLS stack (SChannel) cannot use a key that only lives in memory; a PKCS#12 round trip gives it one it can.
        return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.Exportable);
    }

    public static byte[] SubjectPublicKeyInfo(X509Certificate certificate)
    {
        X509Certificate2 loaded = certificate as X509Certificate2 ?? X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        return loaded.PublicKey.ExportSubjectPublicKeyInfo();
    }

    public static string Fingerprint(X509Certificate certificate) =>
        Convert.ToHexString(SHA256.HashData(SubjectPublicKeyInfo(certificate)))[..16].ToLowerInvariant();

    /// <summary>A pin set: accepts exactly these public keys.</summary>
    public static Func<ReadOnlyMemory<byte>, bool> Pins(params X509Certificate2[] trusted)
    {
        byte[][] keys = Array.ConvertAll(trusted, SubjectPublicKeyInfo);
        return spki =>
        {
            foreach (byte[] key in keys)
            {
                if (key.AsSpan().SequenceEqual(spki.Span))
                    return true;
            }

            return false;
        };
    }
}
