namespace Axiom.Mesh.Service;

/// <summary>What a service does after its connection failed (<see cref="ReconnectRule"/>).</summary>
public enum ReconnectAction
{
    /// <summary>Not stated: the value of an uninitialised field; never a decision.</summary>
    Unknown = 0,

    /// <summary>Connect again after the delay.</summary>
    Retry = 1,

    /// <summary>Exit with the code.</summary>
    Exit = 2,
}
