namespace Axiom.Mesh.Chat;

/// <summary>What <see cref="CatchUpPlan"/> decided the index does when the service connects.</summary>
internal enum IndexAction
{
    /// <summary>Assigned to nothing.</summary>
    Unassigned = 0,

    /// <summary>Keep the rows; the runtime resumes one past the cursor.</summary>
    Keep = 1,

    /// <summary>Drop the rows; the runtime replays every Entry from store sequence 1.</summary>
    Discard = 2,

    /// <summary>Drop the rows and rebuild them from each followed Channel's history, since the runtime resumes past what is lost.</summary>
    DiscardAndRebuild = 3,
}
