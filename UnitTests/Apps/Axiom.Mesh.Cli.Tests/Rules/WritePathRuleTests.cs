using System.Collections.Generic;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli.Tests.Rules;

/// <summary>
/// S-D5, LOCAL-SPEC.md §9.2: the command line writes an Entry through the service that holds its kind while that service is
/// connected, and directly as <c>cli</c> otherwise, so that writing keeps working when the service is down.
/// </summary>
public sealed class WritePathRuleTests
{
    private static readonly Local.Service Chat = new() { Name = "chat", Spawned = true, Kinds = [PackageKind.Channel], EntryKinds = [EntryKind.Message, EntryKind.Redaction] };
    private static readonly Local.Service Cli = new() { Name = "cli", Connected = true, Kinds = [PackageKind.Archive, PackageKind.Channel] };

    // A connected service that may hold the Channel kind takes the write.
    [Fact]
    public void AConnectedServiceOfTheKindTakesTheWrite()
    {
        WritePath path = WritePathRule.Decide([Cli, Chat with { Connected = true }], PackageKind.Channel);

        Assert.Equal("chat", path.Service);
        Assert.True(path.IsViaService);
    }

    [Theory]
    [InlineData(false, 2)] // the chat service is not connected: write directly
    [InlineData(true, 1)] // the chat service holds Channels, not Archives: write directly
    public void WithoutAConnectedServiceOfTheKindTheCommandLineWritesDirectly(bool chatConnected, ushort kind)
    {
        IReadOnlyList<Local.Service> services = [Cli, Chat with { Connected = chatConnected }];

        Assert.Same(WritePath.Direct, WritePathRule.Decide(services, (PackageKind)kind));
    }

    // The command line's own entry never counts as the holder, connected or not.
    [Fact]
    public void TheCommandLinesOwnServiceIsNeverTheHolder()
    {
        Assert.False(WritePathRule.Decide([Cli], PackageKind.Channel).IsViaService);
    }
}
