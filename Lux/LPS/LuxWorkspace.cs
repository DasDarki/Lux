using System.Collections.Concurrent;
using Antlr4.Runtime;
using Lux.Compiler.Passes;
using Lux.Configuration;
using Lux.Diagnostics;
using Lux.IR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

using LuxDiagnostic = Lux.Diagnostics.Diagnostic;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;

namespace Lux.LPS;

public sealed class LuxWorkspace
{
    private readonly ConcurrentDictionary<string, AnalysisResult> _results = new();
    private readonly ConcurrentDictionary<string, string> _openDocuments = new();
    private ILanguageServerFacade? _server;
    private Config _config = new();

    private string? _rootPath;

    public void Initialize(string? rootPath)
    {
        _rootPath = rootPath;
        if (rootPath != null)
        {
            var configPath = Path.Combine(rootPath, "lux.toml");
            var loaded = Config.LoadFromFile(configPath);
            if (loaded != null) _config = loaded;
        }
    }

    public void SetServer(ILanguageServerFacade server) => _server = server;

    public AnalysisResult? GetResult(string uri) =>
        _results.TryGetValue(uri, out var r) ? r : null;

    public void OnDocumentOpened(string uri, string text)
    {
        _openDocuments[uri] = text;
        AnalyzeDocument(uri, text);
    }

    public void OnDocumentChanged(string uri, string text)
    {
        _openDocuments[uri] = text;
        AnalyzeDocument(uri, text);
    }

    public void OnDocumentClosed(string uri)
    {
        _openDocuments.TryRemove(uri, out _);
        _results.TryRemove(uri, out _);
        _server?.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = DocumentUri.Parse(uri),
            Diagnostics = new Container<LspDiagnostic>()
        });
    }

    public void AnalyzeDocument(string uri, string sourceText)
    {
        var filePath = DocumentUri.GetFileSystemPath(DocumentUri.Parse(uri)) ?? uri;

        var diag = new DiagnosticsBag();
        var nodeAlloc = new IDAlloc<NodeID>();
        var symAlloc = new IDAlloc<SymID>();
        var scopeAlloc = new IDAlloc<ScopeID>();
        var types = new TypeTable(new IDAlloc<TypID>());
        var names = new NameMap();

        CommonTokenStream tokenStream;
        IRScript? hir;
        try
        {
            var inputStream = new AntlrInputStream(sourceText);
            var lexer = new LuxLexer(inputStream);
            lexer.RemoveErrorListeners();
            lexer.AddErrorListener(new DiagnosticsSymbolErrorListener(diag, filePath));
            tokenStream = new CommonTokenStream(lexer);
            var parser = new LuxParser(tokenStream);
            parser.RemoveErrorListeners();
            parser.AddErrorListener(new DiagnosticsTokenErrorListener(diag, filePath));
            var visitor = new IRVisitor(filePath, nodeAlloc, diag, _config);
            var ir = visitor.Visit(parser.script());
            hir = ir as IRScript;
        }
        catch
        {
            PublishDiagnostics(uri, diag);
            return;
        }

        if (hir == null)
        {
            PublishDiagnostics(uri, diag);
            return;
        }

        var scopes = new ScopeGraph(diag, scopeAlloc);
        var pkg = new PackageContext(filePath, new SymbolArena(symAlloc), scopes, types, scopes.Root);
        var file = new PreparsedFile(filePath, sourceText) { Hir = hir };
        pkg.Files.Add(file);

        try
        {
            var pm = new PassManager();
            pm.BuildOrder(PassManager.CheckPipeline);
            pm.Run(diag, [pkg], types, symAlloc, scopeAlloc, nodeAlloc, names, new Dictionary<string, object>(), _config);
        }
        catch
        {
        }

        var nodeRegistry = NodeFinder.BuildNodeRegistry(hir);

        var result = new AnalysisResult
        {
            Uri = uri,
            FilePath = filePath,
            SourceText = sourceText,
            File = file,
            Package = pkg,
            Diagnostics = diag,
            TokenStream = tokenStream,
            NodeRegistry = nodeRegistry
        };

        _results[uri] = result;
        PublishDiagnostics(uri, diag);
    }

    private void PublishDiagnostics(string uri, DiagnosticsBag bag)
    {
        var lspDiags = bag.Diagnostics
            .Where(d => d.Span != TextSpan.Empty)
            .Select(ToLspDiagnostic)
            .ToList();

        _server?.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = DocumentUri.Parse(uri),
            Diagnostics = new Container<LspDiagnostic>(lspDiags)
        });
    }

    private static LspDiagnostic ToLspDiagnostic(LuxDiagnostic d)
    {
        return new LspDiagnostic
        {
            Range = SpanToRange(d.Span),
            Severity = d.Level switch
            {
                DiagnosticLevel.Error => DiagnosticSeverity.Error,
                DiagnosticLevel.Warning => DiagnosticSeverity.Warning,
                DiagnosticLevel.Info => DiagnosticSeverity.Information,
                _ => DiagnosticSeverity.Hint
            },
            Source = "lux",
            Message = d.Message,
            Code = d.Code.ToString()
        };
    }

    public static OmniSharp.Extensions.LanguageServer.Protocol.Models.Range SpanToRange(TextSpan span)
    {
        return new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
            new Position(Math.Max(0, span.StartLn - 1), Math.Max(0, span.StartCol - 1)),
            new Position(Math.Max(0, span.EndLn - 1), Math.Max(0, span.EndCol - 1))
        );
    }

    public string FormatType(TypeTable types, TypID typId)
    {
        if (typId == TypID.Invalid) return "any";
        if (!types.GetByID(typId, out var typ)) return "unknown";
        return ((string)typ.Key).Replace("<invalid>", "any");
    }

    public string FormatType(TypeTable types, IR.Type typ)
    {
        return ((string)typ.Key).Replace("<invalid>", "any");
    }

    public List<Symbol> CollectVisibleSymbols(AnalysisResult result, ScopeID scopeId)
    {
        var symbols = new List<Symbol>();
        var seen = new HashSet<string>();
        var currentScope = scopeId;

        while (currentScope != ScopeID.Invalid)
        {
            if (!result.Scopes.LookupAll(currentScope, "").Any())
            {
            }

            foreach (var (id, sym) in result.Syms.ByID)
            {
                if (sym.Owner == currentScope && seen.Add(sym.Name))
                    symbols.Add(sym);
            }

            if (!result.Scopes.ParentScope(currentScope, out var parent))
                break;
            currentScope = parent;
        }

        return symbols;
    }
}
