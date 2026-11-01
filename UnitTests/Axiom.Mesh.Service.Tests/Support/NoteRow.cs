using Axiom.LDB.Entity;

namespace Axiom.Mesh.Service.Tests.Support;

/// <summary>A service's own row, declared in another assembly than the runtime's cursor, as a real service's rows are.</summary>
[LdbEntity("Notes")]
internal sealed partial class NoteRow
{
    /// <summary>The note's key.</summary>
    [Key]
    [Column(MaxLength = 64)]
    public string Key { get; set; } = string.Empty;

    /// <summary>What the note says.</summary>
    [Column(MaxLength = 256)]
    public string Text { get; set; } = string.Empty;
}
