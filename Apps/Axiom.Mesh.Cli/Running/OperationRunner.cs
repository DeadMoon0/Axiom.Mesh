using System.Globalization;
using System.Threading.Tasks;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Cli;

/// <summary>
/// Runs one operation of LOCAL-SPEC.md §8 to its end: subscribes to the connection's operations first, starts it, follows
/// its <c>OperationChanged</c> events (progress on standard error when that is a terminal), and returns its result
/// record, or leaves as a <see cref="CliFailure"/> with the operation's status.
/// </summary>
internal static class OperationRunner
{
    /// <summary>Starts an operation on a connection and waits for its end.</summary>
    /// <typeparam name="TRequest">The request that starts it.</typeparam>
    /// <typeparam name="TResult">Its result record (§8.6).</typeparam>
    /// <param name="context">The command's context.</param>
    /// <param name="client">The connection that starts it; its operations are its own.</param>
    /// <param name="start">The request.</param>
    /// <param name="what">What it does, for the progress line.</param>
    /// <returns>The result.</returns>
    public static async Task<TResult> RunAsync<TRequest, TResult>(CommandContext context, LocalClient client, IRequest<TRequest, uint> start, string what)
        where TRequest : LocalRequest, IRequest<TRequest, uint>
        where TResult : class, IWireRecord<TResult>
    {
        Subscription<OperationChanged> events = await context.SubscribeAsync<OperationChanged>(client, Filter.Any).ConfigureAwait(false);
        uint id;
        try
        {
            id = context.Ok(await context.TrySendAsync(client, start).ConfigureAwait(false));
        }
        catch
        {
            await events.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        Operation done;
        while (true)
        {
            Operation? ended = null;
            try
            {
                await foreach (OperationChanged changed in events.WithCancellation(context.Cancellation).ConfigureAwait(false))
                {
                    Operation operation = changed.Operation;
                    if (operation.OperationId != id)
                        continue;
                    if (context.Environment.ErrorIsTerminal && operation.State == OperationState.Running)
                        Progress(context, what, operation);
                    if (operation.State != OperationState.Running)
                    {
                        ended = operation;
                        break;
                    }
                }
            }
            finally
            {
                await events.DisposeAsync().ConfigureAwait(false);
            }

            ended ??= context.Ok(await context.TrySendAsync(client, new GetOperation { OperationId = id }).ConfigureAwait(false)) is { State: not OperationState.Running } now ? now : null;
            if (ended is not null)
            {
                done = ended;
                break;
            }

            // The subscription lapsed while the operation ran: follow it again.
            events = await context.SubscribeAsync<OperationChanged>(client, Filter.Any).ConfigureAwait(false);
        }

        if (done.State != OperationState.Done)
            throw CliFailure.Refused(done.Status == Status.Ok ? Status.Cancelled : done.Status, new Error { Message = done.Message }, context.DeviceName);
        return Decode<TResult>(done);
    }

    /// <summary>An operation's result record.</summary>
    /// <typeparam name="TResult">The record.</typeparam>
    /// <param name="operation">The operation, Done.</param>
    /// <returns>The record.</returns>
    public static TResult Decode<TResult>(Operation operation)
        where TResult : class, IWireRecord<TResult>
    {
        var reader = new WireReader(operation.Result);
        return TResult.TryRead(ref reader, out TResult? result) && result is not null
            ? result
            : throw CliFailure.Problem($"the Host's result of operation {operation.OperationId.ToString(CultureInfo.InvariantCulture)} does not parse; update this tool to the Host's version");
    }

    private static void Progress(CommandContext context, string what, Operation operation)
    {
        string amount = operation.Total == 0
            ? operation.Done.ToString(CultureInfo.InvariantCulture)
            : $"{operation.Done.ToString(CultureInfo.InvariantCulture)}/{operation.Total.ToString(CultureInfo.InvariantCulture)}";
        context.Error.Write($"\r{what}: {amount} {Formats.Word(operation.Unit)} {operation.Message}".TrimEnd());
    }
}
