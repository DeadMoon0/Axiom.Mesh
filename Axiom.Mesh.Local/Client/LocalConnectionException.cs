using System;
using System.Collections.Generic;

namespace Axiom.Mesh.Local;

/// <summary>
/// The connection to the Host is not there: it could not be opened, the Host refused the <c>Hello</c>, or it ended
/// (LOCAL-SPEC.md §2.3, §4.2, §4.3). Outstanding requests are never answered once a connection ends; a client
/// reconnects after <see cref="Reconnect.Delay"/> and re-establishes what it needs.
/// </summary>
/// <remarks>
/// A request the Host refused is never this exception: it is a <see cref="LocalResult{TOk}"/> with a status.
/// </remarks>
public sealed class LocalConnectionException : MeshException
{
    private readonly MeshFault fault;
    private readonly bool transient;

    private LocalConnectionException(string friendlyMessage, MeshFault fault, bool transient, IReadOnlyList<string> recoverySteps, Bye? bye, Exception? innerException)
        : base(friendlyMessage, recoverySteps, innerException)
    {
        this.fault = fault;
        this.transient = transient;
        Reason = bye?.Reason;
        Detail = bye?.Detail;
    }

    /// <summary>The reason of the Host's <c>Bye</c>, when the Host ended the connection; else <see langword="null"/>.</summary>
    public ByeReason? Reason { get; }

    /// <summary>The detail of the Host's <c>Bye</c>, for logs; else <see langword="null"/>.</summary>
    public string? Detail { get; }

    /// <inheritdoc />
    public override MeshFault Fault => fault;

    /// <inheritdoc />
    public override bool IsTransient => transient;

    /// <summary>Nothing answered at the pipe's name.</summary>
    internal static LocalConnectionException NotReachable(PipeLocation location, Exception? cause) => new(
        $"The Host is not reachable at {location.Path}.",
        MeshFault.Environment,
        transient: true,
        ["Start the Host with 'axiom-meshd run', then connect again.", $"Check that {PipeLocation.EnvironmentVariable} names the same pipe for the Host and this program, or is unset for both."],
        null,
        cause);

    /// <summary>The Host answered the <c>Hello</c> with <c>Bye</c> (§4.2).</summary>
    internal static LocalConnectionException Refused(Bye bye)
    {
        (string message, MeshFault fault, bool transient, string step) = bye.Reason switch
        {
            ByeReason.Duplicate => ("The Host refused the connection: another connection holds this service name.", MeshFault.Configuration, false, "Stop the other instance of the service, or connect when it ended."),
            ByeReason.Refused when bye.Detail == "busy" => ("The Host refused the connection: it serves as many connections as it may.", MeshFault.Environment, true, "Close a connection you no longer need, or wait and connect again."),
            ByeReason.Refused when bye.Detail == "version" => ("The Host refused the connection: it does not speak this version of the local protocol.", MeshFault.Environment, false, "Update the Host or this program so that both speak axiom-mesh-local/1."),
            ByeReason.Refused => ("The Host refused the service name or its token.", MeshFault.Configuration, false, "Connect with a service name the Host's configuration lists, and present the token it holds for that name."),
            _ => ($"The Host refused the connection with {bye.Reason}.", MeshFault.Environment, true, "Read the Host's log for the connection, then connect again."),
        };
        return new LocalConnectionException(Explain(message, bye), fault, transient, [step], bye, null);
    }

    /// <summary>The Host ended a Ready connection with <c>Bye</c>.</summary>
    internal static LocalConnectionException ClosedByHost(Bye bye) => new(
        Explain($"The Host closed the connection with {bye.Reason}.", bye),
        MeshFault.Environment,
        transient: bye.Reason != ByeReason.Violation,
        bye.Reason == ByeReason.Violation
            ? ["The Host saw this client break the protocol; read the Host's log, fix the client, then connect again."]
            : ["Connect again after Reconnect.Delay, and re-establish subscriptions and, for a service, its registration."],
        bye,
        null);

    /// <summary>The connection ended without a <c>Bye</c>, or broke.</summary>
    internal static LocalConnectionException Lost(string why, Exception? cause) => new(
        $"The connection to the Host ended: {why}",
        MeshFault.Environment,
        transient: true,
        ["Connect again after Reconnect.Delay, and re-establish subscriptions and, for a service, its registration."],
        null,
        cause);

    /// <summary>This client sent <c>Bye</c> or was disposed.</summary>
    internal static LocalConnectionException ClosedHere() => new(
        "The connection to the Host was closed by this client.",
        MeshFault.Caller,
        transient: false,
        ["Open a new LocalClient to send further requests."],
        null,
        null);

    /// <summary>The same failure again, for a later call on a connection that already ended.</summary>
    internal LocalConnectionException Again() =>
        new(FriendlyMessage, fault, transient, RecoverySteps, Reason is { } reason ? new Bye { Reason = reason, Detail = Detail ?? "" } : null, this);

    private static string Explain(string message, Bye bye) => bye.Detail.Length == 0 ? message : $"{message} ({bye.Detail})";
}
