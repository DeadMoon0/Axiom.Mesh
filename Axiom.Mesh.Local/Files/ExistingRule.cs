namespace Axiom.Mesh.Local;

/// <summary>
/// What <see cref="ExistingPath"/> means for what is at the target directory: <c>Fail</c> refuses anything there with
/// <c>AlreadyExists</c>; <c>Replace</c> deletes a regular file or a directory materialised earlier (it holds the marker
/// file), never another directory; <c>Merge</c> writes into a directory; nothing is ever written through a symlink or
/// reparse point. Pure.
/// </summary>
internal static class ExistingRule
{
    /// <summary>Decides.</summary>
    /// <param name="mode">What the caller asked for.</param>
    /// <param name="state">What is there.</param>
    /// <returns>The decision.</returns>
    public static ExistingDecision Decide(ExistingPath mode, PathState state)
    {
        if (mode is not (ExistingPath.Fail or ExistingPath.Replace or ExistingPath.Merge))
            return Refuse(Status.Invalid, $"Existing value {(byte)mode} names nothing; use Fail, Replace or Merge.");
        if (state == PathState.Absent)
            return new ExistingDecision(ExistingAction.Create, Status.Ok, string.Empty);
        if (mode == ExistingPath.Fail)
            return Refuse(Status.AlreadyExists, "The directory exists.");
        if (state == PathState.Link)
            return Refuse(Status.Invalid, "The path is a symlink or reparse point; nothing is written through one.");

        return (mode, state) switch
        {
            (ExistingPath.Merge, PathState.Directory or PathState.MarkedDirectory) => new ExistingDecision(ExistingAction.WriteInto, Status.Ok, string.Empty),
            (ExistingPath.Merge, _) => Refuse(Status.Invalid, "The path is a file; a Tree cannot be written into it."),
            (_, PathState.MarkedDirectory) => new ExistingDecision(ExistingAction.ReplaceDirectory, Status.Ok, string.Empty),
            (_, PathState.File) => new ExistingDecision(ExistingAction.ReplaceFile, Status.Ok, string.Empty),
            _ => Refuse(Status.Invalid, $"The directory was not materialised here (it holds no {StoreFiles.MarkerName}); it is not deleted."),
        };
    }

    private static ExistingDecision Refuse(Status status, string problem) => new(ExistingAction.Refuse, status, problem);
}
