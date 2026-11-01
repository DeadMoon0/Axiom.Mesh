using Axiom.Mesh.Local;

namespace Axiom.Mesh.Cli.Tests.Rules;

/// <summary>
/// The grammar of the chat commands (plan §9.3 item 4): each parses from its good line; <c>--file</c> may be given once per
/// attachment, every other option once; a limit outside 1 to 10 000 and an existing value that names nothing are usage
/// errors before the Host is asked.
/// </summary>
public sealed class ChatCommandLineTests
{
    // Every chat command from its good line, with each of its options.
    [Theory]
    [InlineData("chat list", typeof(ChatListCommand))] // chat list
    [InlineData("chat list --json", typeof(ChatListCommand))] // chat list as JSON
    [InlineData("chat show Family --limit 10000 --reverse --after 3f2a --json", typeof(ChatShowCommand))] // chat show with every option
    [InlineData("chat thread 3f2a --json", typeof(ChatThreadCommand))] // chat thread
    [InlineData("chat send Family --text hi --file a.jpg --file docs --reply 3f2a", typeof(ChatSendCommand))] // chat send with two files and a reply
    [InlineData("chat withdraw 3f2a", typeof(ChatWithdrawCommand))] // chat withdraw
    [InlineData("chat get 3f2a photo.jpg --out x.jpg --existing merge", typeof(ChatGetCommand))] // chat get
    [InlineData("chat watch Family", typeof(ChatWatchCommand))] // chat watch
    public void EveryChatCommandParsesFromItsGoodLine(string line, System.Type expected)
    {
        Assert.IsType(expected, Parse(line));
    }

    // --file keeps every value in order; the defaults of chat show and chat get are 50 messages and Fail.
    [Fact]
    public void FileIsGivenOncePerAttachmentInOrder_AndTheDefaultsAreFiftyMessagesAndFail()
    {
        var send = (ChatSendCommand)Parse("chat send Family --text hi --file a.jpg --file b.jpg");
        var show = (ChatShowCommand)Parse("chat show Family");
        var get = (ChatGetCommand)Parse("chat get 3f2a photo.jpg --out x.jpg");

        Assert.Equal(["a.jpg", "b.jpg"], send.Files);
        Assert.Equal((50, false, null), (show.Limit, show.Reverse, show.After));
        Assert.Equal(ExistingPath.Fail, get.Existing);
    }

    // A line that gives a value outside its range or an option twice that may be given once is a usage error.
    [Theory]
    [InlineData("chat show Family --limit 0")] // a limit below 1
    [InlineData("chat show Family --limit 10001")] // a limit above 10 000
    [InlineData("chat send Family --text a --text b")] // --text twice
    [InlineData("chat send Family")] // no --text
    [InlineData("chat get 3f2a photo.jpg --out x --existing sometimes")] // an existing value that names nothing
    [InlineData("chat get 3f2a --out x")] // no attachment named
    public void AValueOutsideItsRangeOrAnOptionGivenTwiceIsAUsageError(string line)
    {
        Assert.IsType<UsageError>(CommandLine.Parse(line.Split(' ')));
    }

    private static Command Parse(string line) =>
        Assert.IsType<ParsedCommand>(CommandLine.Parse(line.Split(' '))).Command;
}
