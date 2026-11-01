using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Testing;

namespace Axiom.Mesh.Service.Tests.Support;

/// <summary>Waits on a subscription's events: the first that matches, within <see cref="TestMesh.Deadline"/>.</summary>
internal static class Events
{
    /// <summary>The first event that matches.</summary>
    /// <typeparam name="T">The event.</typeparam>
    /// <param name="events">The subscription.</param>
    /// <param name="match">What is awaited.</param>
    /// <returns>The event.</returns>
    /// <exception cref="TimeoutException">None matched within the deadline.</exception>
    public static async Task<T> FirstAsync<T>(IAsyncEnumerable<T> events, Func<T, bool> match)
    {
        using var deadline = new CancellationTokenSource(TestMesh.Deadline);
        try
        {
            await foreach (T value in events.WithCancellation(deadline.Token))
            {
                if (match(value))
                    return value;
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            // Fall through to the timeout.
        }

        throw new TimeoutException($"No {typeof(T).Name} matched within {TestMesh.Deadline.TotalSeconds:0} s.");
    }
}
