using System;
using System.Linq;
using System.Reflection;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Chat.Tests.Conventions;

/// <summary>
/// The event table of <c>axiom-mesh-chat/1</c> holds in both directions: every name in <see cref="ChatEvents"/> is one
/// <see cref="ChatEvent"/> record's <c>Name</c>, and <see cref="ChatEventReader"/> reads each from a <c>ServiceEvent</c> of
/// the chat service, and none of another service.
/// </summary>
public sealed class EventTableTests
{
    [Fact]
    public void EveryEventOfTheTableHasItsRecord_AndTheReaderReadsEachOne()
    {
        string[] table = [.. typeof(ChatEvents).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetRawConstantValue()!).Order()];
        ChatEvent[] samples = [new MessageEvent(), new WithdrawnEvent(), new ChannelEvent(), new JobEvent()];
        Type[] records = [.. typeof(ChatCall).Assembly.GetExportedTypes().Where(t => t.IsSubclassOf(typeof(ChatEvent)) && !t.IsAbstract)];

        Assert.Equal(table, samples.Select(s => s.Name).Order());
        Assert.Equal(records.Select(t => t.Name).Order(), samples.Select(s => s.GetType().Name).Order());
        foreach (ChatEvent sample in samples)
        {
            byte[] payload = sample switch
            {
                MessageEvent m => ChatCodec.Encode(m),
                WithdrawnEvent w => ChatCodec.Encode(w),
                ChannelEvent c => ChatCodec.Encode(c),
                JobEvent j => ChatCodec.Encode(j),
                _ => throw new InvalidOperationException(sample.Name),
            };
            Assert.True(ChatEventReader.TryRead(new ServiceEvent { Service = ChatProtocol.ServiceName, Event = sample.Name, Payload = payload }, out ChatEvent? read));
            Assert.Equal(sample.GetType(), read!.GetType());
            Assert.False(ChatEventReader.TryRead(new ServiceEvent { Service = "sync", Event = sample.Name, Payload = payload }, out _));
        }
    }
}
