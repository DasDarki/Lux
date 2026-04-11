using System.Collections.Concurrent;
using Antlr4.Runtime;
using Lux.Compiler;
using Lux.Compiler.Passes;
using Lux.Configuration;
using Lux.Diagnostics;
using Lux.IR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

using LuxDiagnostic = Lux.Diagnostics.Diagnostic;
using LuxDiagnosticCode = Lux.Diagnostics.DiagnosticCode;
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
        ReanalyzeImporters(uri);
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
        var fileDir = Path.GetDirectoryName(Path.GetFullPath(filePath));

        var diag = new DiagnosticsBag();
        var nodeAlloc = new IDAlloc<NodeID>();
        var symAlloc = new IDAlloc<SymID>();
        var scopeAlloc = new IDAlloc<ScopeID>();
        var types = new TypeTable(new IDAlloc<TypID>());
        var names = new NameMap();

        var effectiveConfig = _config.Clone();
        if (fileDir != null)
            effectiveConfig.Source = fileDir;

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
            var visitor = new IRVisitor(filePath, nodeAlloc, diag, effectiveConfig);
            var ir = visitor.Visit(parser.script());
            hir = ir as IRScript;
        }
        catch
        {
            PublishDiagnostics(uri, filePath, diag);
            return;
        }

        if (hir == null)
        {
            PublishDiagnostics(uri, filePath, diag);
            return;
        }

        var scopes = new ScopeGraph(diag, scopeAlloc);
        var pkg = new PackageContext(filePath, new SymbolArena(symAlloc), scopes, types, scopes.Root);
        var file = new PreparsedFile(filePath, sourceText) { Hir = hir };
        pkg.Files.Add(file);

        var cache = new Dictionary<string, object>();

        try
        {
            var pm1 = new PassManager();
            pm1.BuildOrder(PassManager.SingleFilePhase1);
            pm1.Run(diag, [pkg], types, symAlloc, scopeAlloc, nodeAlloc, names, cache, effectiveConfig);
        }
        catch
        {
        }

        var importedFiles = PostResolveImports(hir, filePath, pkg, types, diag, effectiveConfig);

        try
        {
            var pm2 = new PassManager();
            pm2.BuildOrder(PassManager.SingleFilePhase2);
            pm2.Run(diag, [pkg], types, symAlloc, scopeAlloc, nodeAlloc, names, cache, effectiveConfig);
        }
        catch
        {
        }

        var nodeRegistry = NodeFinder.BuildNodeRegistry(hir);
        var fileMap = new Dictionary<NodeID, string>();
        foreach (var (id, _) in nodeRegistry)
            fileMap.TryAdd(id, filePath);

        foreach (var impFile in importedFiles)
        {
            if (impFile.Filename == null) continue;
            foreach (var (id, node) in NodeFinder.BuildNodeRegistry(impFile.Hir))
            {
                nodeRegistry.TryAdd(id, node);
                fileMap.TryAdd(id, impFile.Filename);
            }
        }

        var result = new AnalysisResult
        {
            Uri = uri,
            FilePath = filePath,
            SourceText = sourceText,
            File = file,
            Package = pkg,
            Diagnostics = diag,
            TokenStream = tokenStream,
            NodeRegistry = nodeRegistry,
            FileMap = fileMap
        };

        _results[uri] = result;
        PublishDiagnostics(uri, filePath, diag);
    }

    private List<PreparsedFile> PostResolveImports(IRScript hir, string importerPath,
        PackageContext pkg, TypeTable types, DiagnosticsBag diag, Config effectiveConfig)
    {
        var importedFiles = new List<PreparsedFile>();

        foreach (var stmt in hir.Body)
        {
            if (stmt is not ImportStmt import) continue;

            var moduleName = import.Module.Name;
            if (moduleName.EndsWith(".lux"))
                moduleName = moduleName[..^4];

            var dir = Path.GetDirectoryName(Path.GetFullPath(importerPath));
            if (dir == null) continue;

            string? resolvedPath = null;
            foreach (var ext in new[] { ".lux", ".d.lux" })
            {
                var candidate = Path.GetFullPath(Path.Combine(dir, moduleName + ext));
                if (File.Exists(candidate))
                {
                    resolvedPath = candidate;
                    break;
                }
            }

            if (resolvedPath == null)
            {
                diag.Report(import.Module.Span, LuxDiagnosticCode.ErrModuleNotFound, moduleName);
                continue;
            }

            var importAnalysis = AnalyzeImportedFile(resolvedPath, effectiveConfig);
            if (importAnalysis == null) continue;

            importedFiles.Add(importAnalysis.File);

            var exports = CollectExports(importAnalysis);
            var allTopLevel = CollectAllTopLevel(importAnalysis);

            switch (import.Kind)
            {
                case ImportKind.Named:
                    foreach (var spec in import.Specifiers)
                    {
                        var memberName = spec.Name.Name;
                        if (exports.TryGetValue(memberName, out var exportInfo))
                        {
                            var importName = spec.Alias ?? spec.Name;
                            SetImportedType(pkg, types, importName.Name, exportInfo);
                        }
                        else if (allTopLevel.ContainsKey(memberName))
                        {
                            diag.Report(spec.Name.Span, LuxDiagnosticCode.ErrSymbolNotExported, memberName, moduleName);
                        }
                        else
                        {
                            diag.Report(spec.Name.Span, LuxDiagnosticCode.ErrSymbolNotFound, memberName, moduleName);
                        }
                    }
                    break;

                case ImportKind.Namespace:
                    if (import.Alias != null)
                    {
                        var fields = exports.Select(kvp =>
                        {
                            var fieldType = ImportType(types, kvp.Value.Type, importAnalysis.Types);
                            return new StructType.Field(
                                new NameRef(kvp.Key, TextSpan.Empty), fieldType);
                        });
                        var structType = new StructType(fields);
                        var declared = types.DeclareType(structType);
                        SetImportedSymbolType(pkg, import.Alias.Name, declared.ID);
                    }
                    break;
            }
        }

        return importedFiles;
    }

    private AnalysisResult? AnalyzeImportedFile(string filePath, Config baseConfig)
    {
        string source;
        try { source = File.ReadAllText(filePath); }
        catch { return null; }

        var openDoc = _openDocuments.FirstOrDefault(kv =>
        {
            var docPath = DocumentUri.GetFileSystemPath(DocumentUri.Parse(kv.Key));
            return docPath != null && string.Equals(Path.GetFullPath(docPath), Path.GetFullPath(filePath),
                StringComparison.OrdinalIgnoreCase);
        });
        if (openDoc.Value != null) source = openDoc.Value;

        var diag = new DiagnosticsBag();
        var nodeAlloc = new IDAlloc<NodeID>();
        var symAlloc = new IDAlloc<SymID>();
        var scopeAlloc = new IDAlloc<ScopeID>();
        var typesLocal = new TypeTable(new IDAlloc<TypID>());
        var names = new NameMap();

        var config = baseConfig.Clone();
        var fileDir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (fileDir != null) config.Source = fileDir;

        IRScript? hir;
        CommonTokenStream tokenStream;
        try
        {
            var inputStream = new AntlrInputStream(source);
            var lexer = new LuxLexer(inputStream);
            lexer.RemoveErrorListeners();
            tokenStream = new CommonTokenStream(lexer);
            var parser = new LuxParser(tokenStream);
            parser.RemoveErrorListeners();
            var visitor = new IRVisitor(filePath, nodeAlloc, diag, config);
            hir = visitor.Visit(parser.script()) as IRScript;
        }
        catch { return null; }

        if (hir == null) return null;

        var scopes = new ScopeGraph(diag, scopeAlloc);
        var pkg = new PackageContext(filePath, new SymbolArena(symAlloc), scopes, typesLocal, scopes.Root);
        var file = new PreparsedFile(filePath, source) { Hir = hir };
        pkg.Files.Add(file);

        try
        {
            var pm = new PassManager();
            pm.BuildOrder(PassManager.SingleFilePipeline);
            pm.Run(diag, [pkg], typesLocal, symAlloc, scopeAlloc, nodeAlloc, names, new Dictionary<string, object>(), config);
        }
        catch { }

        var nodeRegistry = NodeFinder.BuildNodeRegistry(hir);

        return new AnalysisResult
        {
            Uri = DocumentUri.FromFileSystemPath(filePath).ToString(),
            FilePath = filePath,
            SourceText = source,
            File = file,
            Package = pkg,
            Diagnostics = diag,
            TokenStream = tokenStream,
            NodeRegistry = nodeRegistry,
            FileMap = nodeRegistry.ToDictionary(kv => kv.Key, _ => filePath)
        };
    }

    public record struct ExportInfo(IR.Type Type, IR.SymbolKind SymKind, SymID Sym, NodeID DeclaringNode);

    private static Dictionary<string, ExportInfo> CollectExports(AnalysisResult result)
    {
        var exports = new Dictionary<string, ExportInfo>();
        foreach (var stmt in result.Hir.Body)
        {
            if (stmt is not ExportStmt export) continue;
            foreach (var (name, symId) in GetDeclaredNames(export.Declaration))
            {
                if (result.Scopes.Lookup(result.Package.Root, name, out var scopeSym))
                {
                    if (result.Syms.GetByID(scopeSym, out var sym) && result.Types.GetByID(sym.Type, out var typ))
                        exports[name] = new ExportInfo(typ, sym.Kind, scopeSym, sym.DeclaringNode);
                }
                else if (symId != SymID.Invalid && result.Syms.GetByID(symId, out var directSym) &&
                         result.Types.GetByID(directSym.Type, out var directTyp))
                {
                    exports[name] = new ExportInfo(directTyp, directSym.Kind, symId, directSym.DeclaringNode);
                }
            }
        }
        return exports;
    }

    private static Dictionary<string, SymID> CollectAllTopLevel(AnalysisResult result)
    {
        var all = new Dictionary<string, SymID>();
        foreach (var stmt in result.Hir.Body)
        {
            foreach (var (name, sym) in GetDeclaredNames(stmt))
                all.TryAdd(name, sym);
            if (stmt is ExportStmt exp)
                foreach (var (name, sym) in GetDeclaredNames(exp.Declaration))
                    all.TryAdd(name, sym);
        }
        return all;
    }

    private static List<(string Name, SymID Sym)> GetDeclaredNames(Stmt stmt)
    {
        return stmt switch
        {
            FunctionDecl { NamePath.Count: > 0 } fd => [(fd.NamePath[0].Name, fd.NamePath[0].Sym)],
            LocalFunctionDecl lfd => [(lfd.Name.Name, lfd.Name.Sym)],
            LocalDecl ld => ld.Variables.Select(v => (v.Name.Name, v.Name.Sym)).ToList(),
            _ => []
        };
    }

    private void SetImportedType(PackageContext pkg, TypeTable types, string name, ExportInfo exportInfo)
    {
        var importedType = ImportType(types, exportInfo.Type, null);
        if (pkg.Scopes.Lookup(pkg.Root, name, out var symId) && pkg.Syms.GetByID(symId, out var sym))
        {
            sym.Type = importedType.ID;
            if (exportInfo.DeclaringNode != NodeID.Invalid)
                sym.DeclaringNode = exportInfo.DeclaringNode;
        }
    }

    private static void SetImportedSymbolType(PackageContext pkg, string name, TypID typeId)
    {
        if (pkg.Scopes.Lookup(pkg.Root, name, out var symId) && pkg.Syms.GetByID(symId, out var sym))
        {
            sym.Type = typeId;
        }
    }

    private static IR.Type ImportType(TypeTable dstTypes, IR.Type srcType, TypeTable? srcTypes)
    {
        return srcType switch
        {
            FunctionType ft => dstTypes.DeclareType(new FunctionType(
                ft.ParamTypes.Select(p => ImportType(dstTypes, p, srcTypes)),
                ft.ParamNames,
                ImportType(dstTypes, ft.ReturnType, srcTypes),
                ft.IsVararg,
                ft.VarargType != null ? ImportType(dstTypes, ft.VarargType, srcTypes) : null,
                ft.DefaultParams.Count > 0 ? [..ft.DefaultParams] : null)),
            UnionType ut => dstTypes.DeclareType(new UnionType(
                ut.Types.Select(t => ImportType(dstTypes, t, srcTypes)))),
            TableArrayType ta => dstTypes.DeclareType(new TableArrayType(
                ImportType(dstTypes, ta.ElementType, srcTypes))),
            TableMapType tm => dstTypes.DeclareType(new TableMapType(
                ImportType(dstTypes, tm.KeyType, srcTypes),
                ImportType(dstTypes, tm.ValueType, srcTypes))),
            StructType st => dstTypes.DeclareType(new StructType(
                st.Fields.Select(f => new StructType.Field(f.Name, ImportType(dstTypes, f.Type, srcTypes))))),
            _ => dstTypes.DeclareType(new IR.Type(srcType.Kind))
        };
    }

    private void PublishDiagnostics(string uri, string filePath, DiagnosticsBag bag)
    {
        var fullPath = Path.GetFullPath(filePath);
        var lspDiags = bag.Diagnostics
            .Where(d => d.Span != TextSpan.Empty)
            .Where(d => d.Span.File == null ||
                        string.Equals(Path.GetFullPath(d.Span.File), fullPath,
                            StringComparison.OrdinalIgnoreCase))
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
        return PrettifyTypeKey(typ.Key);
    }

    public string FormatType(TypeTable types, IR.Type typ)
    {
        return PrettifyTypeKey(typ.Key);
    }

    private static string PrettifyTypeKey(string key)
    {
        return key
            .Replace("<invalid>", "any")
            .Replace("PrimitiveNumber", "number")
            .Replace("PrimitiveBool", "boolean")
            .Replace("PrimitiveString", "string")
            .Replace("PrimitiveNil", "nil")
            .Replace("PrimitiveAny", "any");
    }

    public Dictionary<string, ExportInfo>? CollectExportsFromModule(AnalysisResult result, string moduleName)
    {
        if (moduleName.EndsWith(".lux")) moduleName = moduleName[..^4];

        var dir = Path.GetDirectoryName(result.FilePath);
        if (dir == null) return null;

        string? resolvedPath = null;
        foreach (var ext in new[] { ".lux", ".d.lux" })
        {
            var candidate = Path.GetFullPath(Path.Combine(dir, moduleName + ext));
            if (File.Exists(candidate)) { resolvedPath = candidate; break; }
        }
        if (resolvedPath == null) return null;

        var effectiveConfig = _config.Clone();
        effectiveConfig.Source = dir;
        var imported = AnalyzeImportedFile(resolvedPath, effectiveConfig);
        if (imported == null) return null;

        return CollectExports(imported);
    }

    private void ReanalyzeImporters(string changedUri)
    {
        var changedPath = DocumentUri.GetFileSystemPath(DocumentUri.Parse(changedUri));
        if (changedPath == null) return;
        var changedFull = Path.GetFullPath(changedPath);

        foreach (var (otherUri, otherText) in _openDocuments)
        {
            if (string.Equals(otherUri, changedUri, StringComparison.OrdinalIgnoreCase)) continue;
            if (!_results.TryGetValue(otherUri, out var otherResult)) continue;

            var imports = otherResult.Hir.Body.OfType<ImportStmt>().Any(import =>
            {
                var dir = Path.GetDirectoryName(otherResult.FilePath);
                if (dir == null) return false;
                var modName = import.Module.Name;
                if (modName.EndsWith(".lux")) modName = modName[..^4];
                var candidate = Path.GetFullPath(Path.Combine(dir, modName + ".lux"));
                return string.Equals(candidate, changedFull, StringComparison.OrdinalIgnoreCase);
            });

            if (imports)
                AnalyzeDocument(otherUri, otherText);
        }
    }

    public List<Symbol> CollectVisibleSymbols(AnalysisResult result, ScopeID scopeId)
    {
        var symbols = new List<Symbol>();
        var seen = new HashSet<string>();
        var currentScope = scopeId;

        while (currentScope != ScopeID.Invalid)
        {
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
