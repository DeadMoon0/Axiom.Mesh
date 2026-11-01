using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LDB;
using Axiom.LDB.Entity;

namespace Axiom.Mesh.Chat;

/// <summary>
/// The chat index's tables on a database (CHAT-SERVICE.md §10, S-D2): the service's state database the runtime opened
/// (<c>session.State.Database</c>, <c>&lt;home&gt;/services/chat/index</c>), or any other, in memory for a test.
/// </summary>
internal static class ChatDatabase
{
    /// <summary>Ensures the tables on a database and returns the index over it.</summary>
    /// <param name="database">The database; the caller owns and closes it.</param>
    /// <param name="cancellationToken">Cancels opening.</param>
    /// <returns>The index.</returns>
    public static async Task<ChatIndex> OpenAsync(Database database, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        await using (LdbContext context = await LdbContext.OpenAsync(database, cancellationToken).ConfigureAwait(false))
        {
            await context.EnsureTableAsync<MetaRow>(cancellationToken).ConfigureAwait(false);
            await context.EnsureTableAsync<ChannelRow>(cancellationToken).ConfigureAwait(false);
            await context.EnsureTableAsync<MessageRow>(cancellationToken).ConfigureAwait(false);
            await context.EnsureTableAsync<AttachmentRow>(cancellationToken).ConfigureAwait(false);
            await context.EnsureTableAsync<RedactionRow>(cancellationToken).ConfigureAwait(false);
            await context.EnsureTableAsync<PendingRow>(cancellationToken).ConfigureAwait(false);
        }

        return new ChatIndex(database);
    }

    /// <summary>Opens a database in a directory, for a benchmark or a tool; the caller closes it.</summary>
    /// <param name="directory">The directory; created when absent.</param>
    /// <param name="cancellationToken">Cancels opening.</param>
    /// <returns>The database.</returns>
    public static Task<Database> OpenDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        Directory.CreateDirectory(directory);
        return Database.OpenAsync(directory, DatabaseOptions.Default, cancellationToken);
    }
}
