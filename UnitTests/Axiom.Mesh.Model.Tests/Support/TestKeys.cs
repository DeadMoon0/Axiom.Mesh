using System;
using System.Collections.Generic;

namespace Axiom.Mesh.Tests.Support;

/// <summary>
/// The published test identities (SPEC.md §15): fixed P-256 PKCS#8 keys for three users and their devices. Never use
/// them for anything but tests and test vectors; their private keys are public.
/// </summary>
internal static class TestKeys
{
    private const string AlicePkcs8 = "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b0201010420004ebbf5410a8f4c5c7679665ea279eb0dbe1e1df4b933a928edff32ec4f08b9a14403420004df3eb141942f1d6fad31c730040e32a9a2ca3dd16847c336fc03ddc6c54b3ad6b0a2d6a1084509a9609026ea5682bf44655d66b979bdc5a5973fae1f8bfa7c37";
    private const string AliceLaptopPkcs8 = "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b0201010420947326d1496a8560bf7f00412a3ee0aab537ed8c12b732f0072892e28cf5af12a1440342000401d064ffb9b3ac6025513d850e05b49a429130bdde098f88c8c6a30794a273434f493f2e03bcb9cb0cd7de726849d9c0a6de5e48313f914723ee8daf40ac6bc9";
    private const string AlicePhonePkcs8 = "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b02010104200d8ccd74774dfb9bc98e4adbeab4e14a197348b6afda7b080cd411fac991eebba1440342000496d10b9dace6f139dfebbc50606d5c8234c01b54b1e99759405e845d8f945bdba8e6a716680e5935cfc239da87f677ac30082eb6c44e1e423f256ce9b50d74b2";
    private const string BobPkcs8 = "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b020101042066884d1560d131a3d6b5b44b857023017503335417dabc898b123ea27f6afaf2a144034200046be35e4fa046291d32c00662dbeaf9e684b535a576be0e6bfb940278ea96411ce1694baea23aa50cc2bc7ffc95efbe498d7becdd5b2a0c17b427c60d543c8c3f";
    private const string BobDesktopPkcs8 = "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b020101042019905496ec6c0a9087d25ebac11f8f0d971a0d6151f95a6339ee3deef4e6074da144034200045a9cef0ef8a3ff97a53d158a1648a867b6924dde0c7b7b72efa2be3ce748d1fdff4ee0a6dce14d5b75fb213b1947d76cbeb81d80dddc554a1be1556a4c81d6dc";
    private const string EvePkcs8 = "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b020101042046c15ad7a9306cdc0c7fad73c2d8fcf818b2b51df4a52353ff4bf2df258ae3dca14403420004159970f1bbc5be2822ac5ee0703335eb83a9ea63c3b467e2b7885565392e36a1953dc3d69640863393725dafcb625fc2de5f73fa5792e4438246841bbd4b4f77";
    private const string EveDevicePkcs8 = "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b020101042017d2d8ac795e5f43a5e8aecb925209e71b894a73ab4baa8aa2b64058b9d9b662a144034200044e9535046b1fed68119e0be0473b693ff81838f9ae1e54df1a479ce90c155d44a048da43ba5390cd5d03e7aff41512f035fedcbedf02928a1bd1cc727db73e2d";

    /// <summary>Alice's user key: she owns the sample Packages.</summary>
    public static EcdsaSigner Alice { get; } = Load(AlicePkcs8);

    /// <summary>Alice's laptop.</summary>
    public static EcdsaSigner AliceLaptop { get; } = Load(AliceLaptopPkcs8);

    /// <summary>Alice's phone.</summary>
    public static EcdsaSigner AlicePhone { get; } = Load(AlicePhonePkcs8);

    /// <summary>Bob's user key: a grantee.</summary>
    public static EcdsaSigner Bob { get; } = Load(BobPkcs8);

    /// <summary>Bob's desktop.</summary>
    public static EcdsaSigner BobDesktop { get; } = Load(BobDesktopPkcs8);

    /// <summary>Eve's user key: known to nobody.</summary>
    public static EcdsaSigner Eve { get; } = Load(EvePkcs8);

    /// <summary>Eve's device.</summary>
    public static EcdsaSigner EveDevice { get; } = Load(EveDevicePkcs8);

    /// <summary>Every identity with its published name, role and PKCS#8 hex, for the vectors.</summary>
    public static IReadOnlyList<(string Name, string Role, string Pkcs8, EcdsaSigner Signer)> All { get; } =
    [
        ("alice", "user", AlicePkcs8, Alice),
        ("alice-laptop", "device", AliceLaptopPkcs8, AliceLaptop),
        ("alice-phone", "device", AlicePhonePkcs8, AlicePhone),
        ("bob", "user", BobPkcs8, Bob),
        ("bob-desktop", "device", BobDesktopPkcs8, BobDesktop),
        ("eve", "user", EvePkcs8, Eve),
        ("eve-device", "device", EveDevicePkcs8, EveDevice),
    ];

    private static EcdsaSigner Load(string pkcs8) => EcdsaSigner.FromPkcs8(Convert.FromHexString(pkcs8));
}
