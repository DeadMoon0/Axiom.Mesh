using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Axiom.Mesh.Chat.Tests.Support;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Chat.Tests.Conventions;

/// <summary>
/// The method table of <c>axiom-mesh-chat/1</c> holds in both directions: every name in <see cref="ChatMethods"/> is one
/// request record's <c>Method</c>, and the service dispatches every one of them (it never answers "no such method" for a
/// name of the table).
/// </summary>
public sealed class MethodTableTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task EveryMethodOfTheTableHasItsRequestRecord_AndTheServiceDispatchesIt()
    {
        string[] table = [.. typeof(ChatMethods).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetRawConstantValue()!).Order()];
        string[] records = [.. typeof(ChatCall).Assembly.GetExportedTypes()
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IChatMethod<,>)))
            .Select(t => (string)t.GetProperty("Method", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!)
            .Order()];
        Assert.Equal(table, records);

        await using ChatMeshes chat = await ChatMeshes.StartAsync();
        foreach (string method in table)
        {
            LocalResult<ReadOnlyMemory<byte>> answered = await chat.App.SendAsync(new ServiceCall { Service = ChatProtocol.ServiceName, Method = method, Payload = new byte[] { 0xff } }, None);
            Assert.False(answered.Error?.Detail == "method", $"The service does not dispatch '{method}'.");
        }
    }
}
