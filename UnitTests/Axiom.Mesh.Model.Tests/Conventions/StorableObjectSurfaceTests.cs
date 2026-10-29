using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Axiom.Mesh.Tests.Conventions;

/// <summary>
/// The object typestate is the security spine (plan §4, ARCHITECTURE.md rule 4): nothing outside Model can make a
/// storable object of a signed kind without the signature and the writer's rights having been checked.
/// </summary>
public sealed class StorableObjectSurfaceTests
{
    // The storable objects, and the one way each is obtained outside Model.
    private static readonly Dictionary<Type, string> Storable = new()
    {
        [typeof(Chunk)] = "Objects.TryParse",
        [typeof(ChunkList)] = "Objects.TryParse",
        [typeof(Tree)] = "Objects.TryParse",
        [typeof(OpaqueObject)] = "Objects.TryParse",
        [typeof(VerifiedManifest)] = "VerifiedManifest.TryVerify",
        [typeof(VerifiedRoster)] = "VerifiedRoster.TryVerify",
        [typeof(VerifiedGrant)] = "VerifiedGrant.TryVerify",
        [typeof(AdmittedEntry)] = "EntryAdmission.Decide",
    };

    // Damage: a public or protected constructor anywhere in the typestate (or a type outside Model able to derive from
    // it) would let any caller wrap unverified bytes as a storable object, and the store and the Object message would
    // accept them: a forged Entry, Manifest, Roster or Grant would be stored, served and forwarded.
    [Fact]
    public void NoStorableObjectCanBeMadeOutsideModelWithoutVerification()
    {
        Assembly model = typeof(StorableObject).Assembly;
        Type[] typestate = [typeof(ParsedObject), typeof(StorableObject), typeof(VerifiedEntry), typeof(EntryContent), typeof(ManifestContent), typeof(RosterContent), typeof(GrantContent), typeof(AdmissionDecision), .. model.GetExportedTypes().Where(t => typeof(ParsedObject).IsAssignableFrom(t) || typeof(AdmissionDecision).IsAssignableFrom(t))];

        string[] openConstructors = [.. typestate.Distinct()
            .SelectMany(t => t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Select(c => (Type: t, Constructor: c)))
            .Where(x => x.Constructor.IsPublic || x.Constructor.IsFamily || x.Constructor.IsFamilyOrAssembly)
            .Select(x => x.Type.FullName!)];
        Assert.True(openConstructors.Length == 0, "Typestate types constructible outside Model: " + string.Join(", ", openConstructors));

        string[] unsealed = [.. model.GetExportedTypes().Where(t => typeof(ParsedObject).IsAssignableFrom(t) && !t.IsAbstract && !t.IsSealed).Select(t => t.FullName!)];
        Assert.True(unsealed.Length == 0, "Parsed objects a caller could derive from: " + string.Join(", ", unsealed));

        Type[] storable = [.. model.GetExportedTypes().Where(t => typeof(StorableObject).IsAssignableFrom(t) && !t.IsAbstract)];
        Assert.Equal(Storable.Keys.OrderBy(t => t.Name), storable.OrderBy(t => t.Name));

        // The only public members that produce a signed storable object (or a verified Entry) are the verifications,
        // the admission, and the producers, which verify what they sign.
        Type[] signedStates = [typeof(VerifiedManifest), typeof(VerifiedRoster), typeof(VerifiedGrant), typeof(VerifiedEntry), typeof(AdmittedEntry)];
        string[] producers = [.. model.GetExportedTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => !m.IsSpecialName && Produces(m, signedStates))
            .Select(m => m.DeclaringType!.Name + "." + m.Name)
            .Distinct()
            .Order(StringComparer.Ordinal)];
        Assert.Equal(
            ["EntryAdmission.Decide", "EntryDraft.Sign", "GrantDraft.Sign", "ITrustSnapshotView.TryGetGrant", "ITrustSnapshotView.TryGetManifest", "ManifestDraft.Sign", "RosterDraft.Sign", "VerifiedEntry.TryVerify", "VerifiedGrant.TryVerify", "VerifiedManifest.TryVerify", "VerifiedRoster.TryVerify"],
            producers);
    }

    private static bool Produces(MethodInfo method, Type[] states) =>
        states.Contains(method.ReturnType)
        || method.GetParameters().Any(p => p.IsOut && states.Contains(p.ParameterType.GetElementType()))
        || (method.ReturnType == typeof(AdmissionDecision));
}
