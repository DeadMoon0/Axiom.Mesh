namespace Axiom.Mesh.Core.Storage;

/// <summary>The names of the <see cref="MetaRow"/>s.</summary>
internal static class MetaKeys
{
    /// <summary>The store epoch (LOCAL-SPEC.md §1.2, §9.3): 32 random bytes made with the database, never changed after.</summary>
    public const string StoreEpoch = "store.epoch";

    /// <summary>The last store sequence assigned (LOCAL-SPEC.md §5.6); 0 before the first Entry.</summary>
    public const string StoreSeq = "store.seq";

    /// <summary>The schema version the tables were made by.</summary>
    public const string SchemaVersion = "schema.version";

    /// <summary>The number of stored objects.</summary>
    public const string StoreObjects = "store.objects";

    /// <summary>The bytes of stored objects.</summary>
    public const string StoreBytes = "store.bytes";

    /// <summary>The bytes of stored objects with at least one pin.</summary>
    public const string StorePinnedBytes = "store.pinned";

    /// <summary>The own user key, 65 bytes; absent before an identity exists.</summary>
    public const string SelfUserKey = "self.user";

    /// <summary>The own device id, 32 bytes; absent before an identity exists.</summary>
    public const string SelfDevice = "self.device";
}
