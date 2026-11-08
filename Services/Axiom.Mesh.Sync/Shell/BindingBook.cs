using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Axiom.Mesh.Sync;

/// <summary>
/// Every binding of the service and its worker (SYNC-SERVICE.md §2): found by directory, by a path inside one, or by Archive;
/// added by <c>bind</c>, removed by <c>unbind</c>.
/// </summary>
internal sealed class BindingBook
{
    private readonly object gate = new();
    private readonly List<BindingWorker> workers = [];

    /// <summary>How many bindings there are.</summary>
    public int Count
    {
        get
        {
            lock (gate)
                return workers.Count;
        }
    }

    /// <summary>Every bound directory.</summary>
    public IReadOnlyList<string> Directories
    {
        get
        {
            lock (gate)
                return [.. workers.Select(w => w.Spec.Directory)];
        }
    }

    /// <summary>Every binding's view, by directory.</summary>
    /// <returns>The views.</returns>
    public IReadOnlyList<Binding> Views()
    {
        BindingWorker[] all;
        lock (gate)
            all = [.. workers];
        return [.. all.Select(w => w.View()).OrderBy(b => b.Directory, StringComparer.Ordinal)];
    }

    /// <summary>Adds a worker and starts it.</summary>
    /// <param name="worker">The worker.</param>
    public void Add(BindingWorker worker)
    {
        ArgumentNullException.ThrowIfNull(worker);
        lock (gate)
            workers.Add(worker);
        worker.Start();
    }

    /// <summary>Removes a worker and stops it.</summary>
    /// <param name="worker">The worker.</param>
    /// <returns>A task that completes when it stopped.</returns>
    public async Task RemoveAsync(BindingWorker worker)
    {
        ArgumentNullException.ThrowIfNull(worker);
        lock (gate)
            workers.Remove(worker);
        await worker.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>The binding of exactly this directory.</summary>
    /// <param name="directory">The directory, normalised.</param>
    /// <returns>Its worker, or <see langword="null"/>.</returns>
    public BindingWorker? Exactly(string directory)
    {
        lock (gate)
            return workers.FirstOrDefault(w => Same(w.Spec.Directory, directory));
    }

    /// <summary>The binding a path lies in: its directory, or a directory above it.</summary>
    /// <param name="path">The path, normalised.</param>
    /// <returns>Its worker, or <see langword="null"/>.</returns>
    public BindingWorker? Holding(string path)
    {
        lock (gate)
            return workers.FirstOrDefault(w => Same(w.Spec.Directory, path) || path.StartsWith(w.Spec.Directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, Comparison));
    }

    /// <summary>Queues a run of every binding.</summary>
    /// <returns>How many.</returns>
    public int TriggerAll()
    {
        BindingWorker[] all;
        lock (gate)
            all = [.. workers];
        foreach (BindingWorker worker in all)
            worker.Trigger();
        return all.Length;
    }

    /// <summary>Queues a run of every binding of an Archive.</summary>
    /// <param name="package">The Archive.</param>
    public void TriggerPackage(PackageId package)
    {
        BindingWorker[] of;
        lock (gate)
            of = [.. workers.Where(w => w.Spec.Package == package)];
        foreach (BindingWorker worker in of)
            worker.Trigger();
    }

    /// <summary>Queues a run of every binding whose last run waited for the Version DAG.</summary>
    public void TriggerDeferred()
    {
        BindingWorker[] all;
        lock (gate)
            all = [.. workers];
        foreach (BindingWorker worker in all)
            worker.TriggerIfDeferred();
    }

    /// <summary>Stops every worker.</summary>
    /// <returns>A task that completes when all stopped.</returns>
    public async Task StopAllAsync()
    {
        BindingWorker[] all;
        lock (gate)
        {
            all = [.. workers];
            workers.Clear();
        }

        foreach (BindingWorker worker in all)
            await worker.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        lock (gate)
            return workers.Count == 0 ? "no bindings" : string.Join("; ", workers);
    }

    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool Same(string a, string b) => string.Equals(a.TrimEnd(Path.DirectorySeparatorChar), b.TrimEnd(Path.DirectorySeparatorChar), Comparison);
}
