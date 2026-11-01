using System.IO;

namespace Axiom.Mesh.Cli;

/// <summary>
/// The directory a sync command names: the one typed, made absolute; without one, the bound folder the current directory lies
/// in (<see cref="SyncMarker"/>), else empty, which the service reads as every binding.
/// </summary>
internal static class SyncPaths
{
    /// <summary>The directory to send.</summary>
    /// <param name="context">The command's context: the current directory and the file system's check.</param>
    /// <param name="typed">The directory as typed, or <see langword="null"/>.</param>
    /// <returns>An absolute path, or empty for every binding.</returns>
    public static string Of(CommandContext context, string? typed) =>
        typed is not null
            ? Path.GetFullPath(Path.Combine(context.Environment.CurrentDirectory, typed))
            : SyncMarker.Find(context.Environment.CurrentDirectory, context.Environment.Machine.FileExists) ?? string.Empty;
}
