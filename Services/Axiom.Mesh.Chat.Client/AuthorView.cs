using System.Diagnostics.CodeAnalysis;
using Axiom.Mesh.Local;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Chat;

/// <summary>Who wrote a message (CHAT-SERVICE.md §4): the device, and its user and both names as this Host knows them.</summary>
public sealed record AuthorView : IWireRecord<AuthorView>
{
    /// <summary>The writing device.</summary>
    public DeviceId DeviceId { get; init; }

    /// <summary>The device's user; all zero when this Host knows no Roster that lists the device.</summary>
    public UserId UserId { get; init; }

    /// <summary>The user's display name; empty when unknown.</summary>
    public string UserName { get; init; } = "";

    /// <summary>The device's name in its user's Roster; empty when unknown.</summary>
    public string DeviceName { get; init; } = "";

    /// <inheritdoc />
    public static bool TryRead(ref WireReader reader, [NotNullWhen(true)] out AuthorView? value)
    {
        var read = new AuthorView
        {
            DeviceId = DeviceId.Read(ref reader),
            UserId = UserId.Read(ref reader),
            UserName = Wire.ReadString(ref reader, ChatLimits.UserNameLength),
            DeviceName = Wire.ReadString(ref reader, ChatLimits.DeviceNameLength),
        };
        value = reader.Ok ? read : null;
        return value is not null;
    }

    /// <inheritdoc />
    public void Write(ref WireWriter writer)
    {
        DeviceId.Write(ref writer);
        UserId.Write(ref writer);
        Wire.WriteString(ref writer, UserName, ChatLimits.UserNameLength);
        Wire.WriteString(ref writer, DeviceName, ChatLimits.DeviceNameLength);
    }
}
