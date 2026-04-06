namespace Lux.Configuration;

/// <summary>
/// The [code] section of the <see cref="Config"/>. The code section is used to configure specific parts of the code
/// generation and to solve fuck-ups of the Lua creators.
/// </summary>
public sealed class CodeSection
{
    /// <summary>
    /// Overrides the index base used by Lua. Every good developer hates Lua's "wE ArE cOOL ANd SpECiAl aND StaRT aT 1".
    /// </summary>
    public int IndexBase { get; set; } = 0;
    
    /// <summary>
    /// Overrides the concat operator. Although this will just ADD the possiblity to use "+" in a string based context.
    /// </summary>
    public string ConcatOperator { get; set; } = "+";
    
    /// <summary>
    /// Allows `Hello {name}`. Which translates to "Hello " .. tostring(name).
    /// </summary>
    public bool StringInterpolation  { get; set; } = true;

    /// <summary>
    /// Sets the policy on how to handle semicolons in code.
    /// </summary>
    public Policy Semicolons { get; set; } = Policy.Optional;

    /// <summary>
    /// Sets the code of the import statement that is used by the transpiler. %s is replaced by the string to define the imported file.
    /// </summary>
    public string ImportStatement { get; set; } = "require(%s)";
    
    /// <summary>
    /// Whether to strip unused variables, functions, and other declarations from the generated code. This can help to
    /// reduce the size of the generated code and improve performance, but it may also make debugging more difficult if
    /// you need to inspect the generated code.
    /// </summary>
    public bool StripUnused { get; set; } = true;

    internal void Merge(CodeSection section)
    {
        IndexBase = Config.MergeVal(IndexBase, section.IndexBase, 0);
        ConcatOperator = Config.MergeVal(ConcatOperator, section.ConcatOperator, "+");
        StringInterpolation = Config.MergeVal(StringInterpolation, section.StringInterpolation, true);
        Semicolons = Config.MergeVal(Semicolons, section.Semicolons, Policy.Optional);
        ImportStatement = Config.MergeVal(ImportStatement, section.ImportStatement, "require(%s)");
        StripUnused = Config.MergeVal(StripUnused, section.StripUnused, true);
    }
}