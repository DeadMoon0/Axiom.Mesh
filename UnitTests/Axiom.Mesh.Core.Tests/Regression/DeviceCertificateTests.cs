using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Axiom.Mesh.Core.Tests.Regression;

/// <summary>The device certificate as the node builds it from a device key (SPEC.md §2.4, DESIGN.md §6.3).</summary>
public sealed class DeviceCertificateTests
{
    // Damage: a certificate whose key went missing in the PKCS#12 round trip, or whose subject key is not the device key,
    // fails every handshake on Windows or pins the wrong device; a lost extended key usage fails it on stricter stacks.
    [Fact]
    public void ADeviceCertificateSurvivesThePkcs12RoundTrip_CarryingTheDeviceKeyItsIdAndBothUsages()
    {
        TestIdentity laptop = TestIdentity.Of("alice-laptop");
        using ECDsa key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(laptop.DevicePkcs8(), out _);

        using X509Certificate2 certificate = DeviceCertificate.Create(key, TestIdentity.Epoch);

        Assert.True(certificate.HasPrivateKey);
        Assert.True(DeviceCertificate.TryGetPoint(certificate, out ReadOnlyMemory<byte> point));
        Assert.Equal(laptop.Device.PublicKey.Encoded.ToArray(), point.ToArray());
        Assert.Equal("CN=" + laptop.DeviceId, certificate.Subject);
        Assert.Equal(TestIdentity.Epoch.AddDays(-1).UtcDateTime, certificate.NotBefore.ToUniversalTime());
        Assert.Equal((TestIdentity.Epoch + DeviceCertificate.Validity).UtcDateTime, certificate.NotAfter.ToUniversalTime());
        X509EnhancedKeyUsageExtension usages = Assert.Single(certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>());
        Assert.Equal(["1.3.6.1.5.5.7.3.1", "1.3.6.1.5.5.7.3.2"], usages.EnhancedKeyUsages.Cast<Oid>().Select(o => o.Value));
        Assert.Equal(X509KeyUsageFlags.DigitalSignature, Assert.Single(certificate.Extensions.OfType<X509KeyUsageExtension>()).KeyUsages);
        using ECDsa held = certificate.GetECDsaPrivateKey()!;
        byte[] data = [1, 2, 3];
        byte[] signature = held.SignData(data, HashAlgorithmName.SHA256);
        Assert.True(key.VerifyData(data, signature, HashAlgorithmName.SHA256));
    }

    // Damage: a reader that took a key on another curve would hash an id SPEC.md §2.3 never defined and resolve nobody,
    // or worse, somebody else.
    [Fact]
    public void OnlyAnUncompressedP256PointIsReadFromACertificate()
    {
        using ECDsa p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var request = new CertificateRequest("CN=other", p384, HashAlgorithmName.SHA256);
        using X509Certificate2 other = request.CreateSelfSigned(TestIdentity.Epoch, TestIdentity.Epoch.AddDays(1));

        Assert.False(DeviceCertificate.TryGetPoint(other, out ReadOnlyMemory<byte> point));
        Assert.True(point.IsEmpty);
    }
}
