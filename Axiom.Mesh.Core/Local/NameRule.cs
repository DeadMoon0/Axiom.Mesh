namespace Axiom.Mesh.Core;

/// <summary>
/// The substance of a <c>name</c> (LOCAL-SPEC.md §3.2): ASCII letters, digits, <c>.</c>, <c>-</c> and <c>_</c>, never empty. The
/// codec checks the length (a bound); a request whose name breaks the character set is <c>Invalid</c>.
/// </summary>
internal static class NameRule
{
    /// <summary>Whether a text is a <c>name</c>.</summary>
    /// <param name="text">The text, within its length bound already.</param>
    /// <returns><see langword="true"/> when it is non-empty and every character is allowed.</returns>
    public static bool IsName(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        foreach (char c in text)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_'))
                return false;
        }

        return true;
    }
}
