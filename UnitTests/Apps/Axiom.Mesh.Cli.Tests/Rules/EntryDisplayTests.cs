using System;
using System.Linq;
using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli.Tests.Rules;

/// <summary>
/// What the command line shows of an Entry (SPEC.md §8.5, §9.4): a Message's text, a Version's label, and for a withdrawn
/// Entry that it was withdrawn, never its text, in a table, in JSON and in <c>watch</c>.
/// </summary>
public sealed class EntryDisplayTests
{
    private static readonly ObjectHash Hash = new(new Hash32(Enumerable.Repeat((byte)0xab, 32).ToArray()));

    [Fact]
    public void ARedactedMessageIsShownAsWithdrawnNeverItsText()
    {
        Entry withdrawn = Message("the secret I took back") with { Redacted = true };

        string table = string.Join('\n', HistoryView.Text([withdrawn]));
        string json = HistoryView.Json([withdrawn]);
        string shown = string.Join('\n', HistoryView.ShowText(withdrawn));
        string watched = HistoryView.WatchLine(withdrawn);

        foreach (string text in new[] { table, json, shown, watched })
        {
            Assert.Contains("withdrawn", text, StringComparison.Ordinal);
            Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AMessageShowsItsTextOnOneLine_AVersionItsLabelAndTree_ARedactionItsTarget()
    {
        Assert.Equal("hello mesh", EntrySummary.Of(Message("hello\nmesh")));
        Assert.Equal(EntrySummary.MaxText, EntrySummary.Of(Message(new string('x', 500))).Length);

        var version = new VersionPayload { Tree = Hash, Label = "main", Prerelease = "beta.2", Notes = string.Empty };
        Assert.Equal("version main-beta.2, tree abababababab", EntrySummary.Of(Of(EntryKind.Version, PayloadEncoder.Encode(version))));
        Assert.Equal("withdraws abababababab", EntrySummary.Of(Of(EntryKind.Redaction, PayloadEncoder.Encode(new RedactionPayload { Target = Hash }))));
        Assert.Equal("3 bytes", EntrySummary.Of(Of((EntryKind)70, new byte[3])));
    }

    private static Entry Message(string text) =>
        Of(EntryKind.Message, PayloadEncoder.Encode(new MessagePayload { Text = text, Attachments = [], ReplyTo = default }));

    private static Entry Of(EntryKind kind, byte[] payload) => new()
    {
        Hash = Hash,
        StoreSeq = 1,
        Stored = 1_800_000_000_000,
        PackageId = default,
        DeviceId = default,
        Clock = 0,
        Parents = [],
        Kind = kind,
        Payload = payload,
    };
}
