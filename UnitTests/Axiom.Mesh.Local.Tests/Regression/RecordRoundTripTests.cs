using System;
using System.Buffers;
using System.Linq;
using System.Reflection;
using Axiom.Mesh.Local.Tests.Support;
using Axiom.ProtoStream.Codecs;

namespace Axiom.Mesh.Local.Tests.Regression;

/// <summary>
/// Every record of LOCAL-SPEC.md §5 and every OK body, request, event and callback body reads back what was written,
/// exactly to its end, and no strict prefix of it reads: a structure always knows where it ends.
/// </summary>
public sealed class RecordRoundTripTests
{
    public static TheoryData<string> Types() =>
    [
        .. Samples.RecordTypes()
            .Concat(Samples.RequestTypes())
            .Concat(Samples.EventTypes())
            .Append(typeof(ForwardedCall))
            .Select(t => t.Name)
            .Order(StringComparer.Ordinal),
    ];

    // Damage: a record whose reader stopped early or ran on would read a list of records as garbage from the second
    // element on, and a truncated body would read as a shorter valid one instead of failing.
    [Theory]
    [MemberData(nameof(Types))]
    public void EveryRecordReadsBackWhatWasWritten_AndNoStrictPrefixReads(string name)
    {
        Type type = typeof(LocalProtocol).Assembly.GetExportedTypes().Single(t => t.Name == name);
        object sample = Samples.Of(type);
        byte[] written = Write(sample);

        Assert.True(TryRead(type, written, out object? read, out bool atEnd), $"{name} did not read back.");
        Assert.True(atEnd, $"{name} left bytes after reading.");
        Assert.Equal(written, Write(read!));

        for (int length = 0; length < written.Length; length++)
            Assert.False(TryRead(type, written[..length], out _, out bool prefixAtEnd) && prefixAtEnd, $"{name} read from its first {length} of {written.Length} bytes.");
    }

    private static byte[] Write(object value)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new WireWriter(output);
        switch (value)
        {
            case LocalRequest request:
                request.Write(ref writer);
                break;
            case LocalEvent @event:
                @event.Write(ref writer);
                break;
            case LocalCallback callback:
                callback.Write(ref writer);
                break;
            default:
                Writer(value.GetType())(value, output);
                break;
        }

        return output.WrittenSpan.ToArray();
    }

    // Records implement IWireRecord<T>.Write(ref WireWriter); reflection cannot pass a ref struct, so a generic helper does.
    private static Action<object, ArrayBufferWriter<byte>> Writer(Type type) =>
        (Action<object, ArrayBufferWriter<byte>>)typeof(RecordRoundTripTests).GetMethod(nameof(WriteRecord), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type).CreateDelegate(typeof(Action<object, ArrayBufferWriter<byte>>));

    private static void WriteRecord<T>(object value, ArrayBufferWriter<byte> output)
        where T : class, IWireRecord<T>
    {
        var writer = new WireWriter(output);
        ((T)value).Write(ref writer);
    }

    private static bool TryRead(Type type, byte[] bytes, out object? value, out bool atEnd)
    {
        object?[] arguments = [bytes, null, null];
        bool read = (bool)typeof(RecordRoundTripTests).GetMethod(nameof(ReadRecord), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type).Invoke(null, arguments)!;
        value = arguments[1];
        atEnd = (bool)arguments[2]!;
        return read;
    }

    private static bool ReadRecord<T>(byte[] bytes, out object? value, out bool atEnd)
        where T : class, IWireRecord<T>
    {
        var reader = new WireReader(bytes);
        bool read = T.TryRead(ref reader, out T? typed);
        value = typed;
        atEnd = reader.IsAtEnd;
        return read;
    }
}
