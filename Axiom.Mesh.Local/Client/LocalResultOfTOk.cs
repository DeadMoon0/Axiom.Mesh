using System;
using System.Diagnostics.CodeAnalysis;

namespace Axiom.Mesh.Local;

/// <summary>
/// The outcome of a request (LOCAL-SPEC.md §3.4): its OK body, or the status and <see cref="Local.Error"/> the Host
/// refused it with. A refusal is a value, never an exception: check <see cref="IsOk"/> (or use
/// <see cref="TryGetValue"/>) and read <see cref="Value"/> or <see cref="Error"/>.
/// </summary>
/// <typeparam name="TOk">The OK body of the request.</typeparam>
public readonly struct LocalResult<TOk>
{
    private readonly TOk? value;

    private LocalResult(Status status, TOk? value, Error? error)
    {
        Status = status;
        this.value = value;
        Error = error;
    }

    /// <summary>The status: <see cref="Status.Ok"/>, or why the Host refused (§11.1); a value this side does not know is kept.</summary>
    public Status Status { get; }

    /// <summary>Whether the request succeeded.</summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsOk => Status == Status.Ok;

    /// <summary>The OK body.</summary>
    /// <exception cref="InvalidOperationException">The request failed; check <see cref="IsOk"/> first.</exception>
    public TOk Value => IsOk
        ? value!
        : throw new InvalidOperationException($"The request failed with {Status}: {Error.Message} Check IsOk, or use TryGetValue, before reading Value.");

    /// <summary>Why the request failed, for display and logs; <see langword="null"/> when it succeeded.</summary>
    public Error? Error { get; }

    /// <summary>A success.</summary>
    /// <param name="value">The OK body.</param>
    /// <returns>The result.</returns>
    public static LocalResult<TOk> Ok(TOk value) => new(Status.Ok, value, null);

    /// <summary>A refusal.</summary>
    /// <param name="status">Why, anything but <see cref="Status.Ok"/>.</param>
    /// <param name="error">The explanation.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentException"><paramref name="status"/> is <see cref="Status.Ok"/>.</exception>
    public static LocalResult<TOk> Failure(Status status, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (status == Status.Ok)
            throw new ArgumentException("A refusal needs a status other than Ok.", nameof(status));
        return new LocalResult<TOk>(status, default, error);
    }

    /// <summary>The OK body, when the request succeeded.</summary>
    /// <param name="ok">The OK body.</param>
    /// <returns><see langword="true"/> when the request succeeded.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out TOk ok)
    {
        ok = IsOk ? value! : default;
        return IsOk;
    }

    /// <inheritdoc />
    public override string ToString() => IsOk ? $"Ok: {value}" : $"{Status}: {Error.Message}";
}
