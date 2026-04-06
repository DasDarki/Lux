namespace Lux.IR;

/// <summary>
/// Represents a pre-parsed source file after ANTLR converted the source code into the high-level intermediate representation (HIR).
/// </summary>
public sealed class PreparsedFile(string? filename, string content)
{
    /// <summary>
    /// Thze filename (full path) of the source file. May be null, if the source file is not from a file, such as from a string or from a network stream.
    /// </summary>
    public string? Filename { get; } = filename;

    /// <summary>
    /// The source code content of the source file. 
    /// </summary>
    public string Content { get; } = content;

    /// <summary>
    /// The HIR of the source file (script). Can be null until the preparsing process is completed.
    /// </summary>
    public IRScript Hir { get; set; } = null!;

    public string? GeneratedLua { get; set; }
}