using System;
using System.Threading.Tasks;
using Axiom.Mesh.Chat;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli;

/// <summary>
/// <c>axiom-mesh chat watch &lt;channel&gt;</c>: prints every message and withdrawal of a Channel as the chat service
/// pushes it (CHAT-SERVICE.md §6), until Ctrl-C. A lapse is reported and the watch goes on.
/// </summary>
/// <param name="Channel">The Channel as typed: id, prefix or name.</param>
internal sealed record ChatWatchCommand(string Channel) : Command
{
    /// <inheritdoc />
    public override async Task<CliExitCode> RunAsync(CommandContext context)
    {
        PackageId package = await IdResolver.PackageAsync(context, Channel).ConfigureAwait(false);
        LocalClient client = await context.UserAsync().ConfigureAwait(false);
        context.Error.WriteLine(WatchCommand.Started);
        try
        {
            while (true)
            {
                Subscription<ServiceEvent> events = await context.SubscribeAsync<ServiceEvent>(client, ChatEventReader.FilterOf(package)).ConfigureAwait(false);
                try
                {
                    await foreach (ServiceEvent received in events.WithCancellation(context.Cancellation).ConfigureAwait(false))
                    {
                        if (!ChatEventReader.TryRead(received, out ChatEvent? chat) || ChatView.WatchLine(chat) is not { } line)
                            continue;
                        context.Output.WriteLine(line);
                        await context.Output.FlushAsync(context.Cancellation).ConfigureAwait(false);
                    }

                    context.Error.WriteLine($"the watch fell behind and missed {events.Lapse?.Dropped ?? 0} events; 'axiom-mesh chat show' lists the messages; watching again");
                }
                finally
                {
                    await events.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (context.Cancellation.IsCancellationRequested)
        {
            return CliExitCode.Ok;
        }
    }
}
