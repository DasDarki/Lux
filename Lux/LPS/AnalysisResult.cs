using Antlr4.Runtime;
using Lux.Diagnostics;
using Lux.IR;

namespace Lux.LPS;

public sealed class AnalysisResult
{
    public required string Uri { get; init; }
    public required string FilePath { get; init; }
    public required string SourceText { get; init; }
    public required PreparsedFile File { get; init; }
    public required PackageContext Package { get; init; }
    public required DiagnosticsBag Diagnostics { get; init; }
    public required CommonTokenStream TokenStream { get; init; }
    public required Dictionary<NodeID, Node> NodeRegistry { get; init; }

    public IRScript Hir => File.Hir;
    public SymbolArena Syms => Package.Syms;
    public ScopeGraph Scopes => Package.Scopes;
    public TypeTable Types => Package.Types;
}
