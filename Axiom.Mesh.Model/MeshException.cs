using System;
using System.Collections.Generic;
using System.Text;

namespace Axiom.Mesh;

/// <summary>
/// Base of every exception Axiom.Mesh throws at its caller.
/// </summary>
/// <remarks>
/// <para>
/// A failure says what is true, what to do next and, when something is missing, what does exist. The fix should be a
/// copy, not an expedition. The recovery steps and the available options are therefore part of the type, not of
/// whoever formats the message.
/// </para>
/// <para>
/// Two facts decide what a caller does with a failure, so every concrete type states them: <see cref="Fault"/> (who
/// has to change something) and <see cref="IsTransient"/> (whether the same call can succeed when retried unchanged).
/// </para>
/// <para>
/// Mistakes made by a peer are never a <see cref="MeshException"/>: they stay protocol violations or faulty content,
/// and end as a closed connection or a dropped object, never as an exception in the caller's code. Something that
/// might not be there is a <c>Try…</c> method, not an exception.
/// </para>
/// </remarks>
public abstract class MeshException : Exception
{
    private const int HeaderRuleLength = 70;

    private readonly IReadOnlyList<string> recoverySteps;
    private readonly IReadOnlyList<string> availableOptions = [];

    /// <summary>Initializes the exception with a message and the steps out of it.</summary>
    /// <param name="friendlyMessage">What is true, in one or two sentences.</param>
    /// <param name="recoverySteps">What to do next; at least one step, none of them blank.</param>
    protected MeshException(string friendlyMessage, IReadOnlyList<string> recoverySteps)
        : this(friendlyMessage, recoverySteps, null)
    {
    }

    /// <summary>Initializes the exception with a message, the steps out of it and the underlying cause.</summary>
    /// <param name="friendlyMessage">What is true, in one or two sentences.</param>
    /// <param name="recoverySteps">What to do next; at least one step, none of them blank.</param>
    /// <param name="innerException">The underlying cause, or <see langword="null"/>.</param>
    protected MeshException(string friendlyMessage, IReadOnlyList<string> recoverySteps, Exception? innerException)
        : base(friendlyMessage, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(friendlyMessage);
        FriendlyMessage = friendlyMessage;
        this.recoverySteps = RequireSteps(recoverySteps);
    }

    /// <summary>What is true, without the guidance.</summary>
    public string FriendlyMessage { get; }

    /// <summary>What to do next, one action per entry; never empty.</summary>
    /// <remarks>
    /// Set by the constructor; an <c>init</c> property as well, so a thrower that knows better can replace the general
    /// advice without a new constructor every package compiles against. The replacement is checked like the original.
    /// </remarks>
    public IReadOnlyList<string> RecoverySteps
    {
        get => recoverySteps;
        init => recoverySteps = RequireSteps(value);
    }

    /// <summary>What does exist, when the failure is about something missing; empty otherwise.</summary>
    public IReadOnlyList<string> AvailableOptions
    {
        get => availableOptions;
        init => availableOptions = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Who has to change something for the call to succeed.</summary>
    public abstract MeshFault Fault { get; }

    /// <summary>
    /// <see langword="true"/> when the same call can succeed if retried unchanged; <see langword="false"/> when
    /// something has to change first.
    /// </summary>
    public abstract bool IsTransient { get; }

    /// <summary>The friendly message followed by the recovery steps and the available options.</summary>
    /// <remarks>Built when read, because the steps and options may be set after the constructor ran.</remarks>
    public override string Message => Guidance(new StringBuilder(FriendlyMessage)).ToString();

    /// <summary>The guidance first, then the fault, the cause and the stack trace.</summary>
    /// <remarks>
    /// The advice comes first because it is what a reader acts on; the cause follows because the advice alone is not
    /// the whole truth. The layout is the Axiom family's (LWSE's <c>LWSEException</c>), with the tag
    /// <c>[MESH ERROR]</c>.
    /// </remarks>
    /// <returns>The formatted exception.</returns>
    public override string ToString()
    {
        StringBuilder text = new StringBuilder()
            .Append("[MESH ERROR] ").AppendLine(GetType().Name)
            .AppendLine(new string('=', HeaderRuleLength))
            .Append(FriendlyMessage);
        Guidance(text);
        text.AppendLine().AppendLine().Append("Fault: ").Append(Fault)
            .Append(IsTransient ? " (transient: retrying can succeed)" : " (retrying unchanged cannot succeed)");

        if (InnerException is not null)
            text.AppendLine().AppendLine().AppendLine("Cause:").Append(Indent(InnerException.ToString()));

        if (StackTrace is { Length: > 0 } stackTrace)
            text.AppendLine().AppendLine().Append(stackTrace);

        return text.ToString();
    }

    private StringBuilder Guidance(StringBuilder text)
    {
        text.AppendLine().AppendLine().Append("Recovery:");
        foreach (string step in RecoverySteps)
            text.AppendLine().Append("  -> ").Append(step);

        if (AvailableOptions.Count > 0)
        {
            text.AppendLine().AppendLine().Append("Available:");
            foreach (string option in AvailableOptions)
                text.AppendLine().Append("  * ").Append(option);
        }

        return text;
    }

    private static IReadOnlyList<string> RequireSteps(IReadOnlyList<string> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0)
            throw new ArgumentException("A Mesh exception must name at least one recovery step.", nameof(steps));
        foreach (string step in steps)
        {
            if (string.IsNullOrWhiteSpace(step))
                throw new ArgumentException("A recovery step must not be blank.", nameof(steps));
        }

        return steps;
    }

    private static string Indent(string text) =>
        "  " + text.Replace(Environment.NewLine, Environment.NewLine + "  ", StringComparison.Ordinal);
}
