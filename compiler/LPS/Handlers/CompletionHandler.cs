using Lux.IR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using LuxSymbolKind = Lux.IR.SymbolKind;
using LuxType = Lux.IR.Type;

namespace Lux.LPS.Handlers;

public sealed class CompletionHandler(LuxWorkspace workspace) : CompletionHandlerBase
{
    private static readonly string[] Keywords =
    [
        "and", "break", "do", "else", "elseif", "end", "enum", "false", "for",
        "function", "goto", "if", "in", "local", "nil", "not", "or",
        "repeat", "return", "then", "true", "until", "while",
        "as", "async", "await", "case", "class", "constructor", "declare", "export", "extends", "from",
        "abstract", "implements", "import", "interface", "match", "meta", "module", "mut", "new", "override",
        "protected", "static", "super", "when", "typeof", "instanceof"
    ];

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken ct)
    {
        var items = new List<CompletionItem>();

        var result = workspace.GetResult(request.TextDocument.Uri.ToString());
        if (result != null)
        {
            var line = request.Position.Line + 1;
            var col = request.Position.Character + 1;

            var importItems = TryImportPathCompletion(result, request.Position);
            if (importItems != null)
                return Task.FromResult(new CompletionList(importItems));

            var importSpecItems = TryImportSpecifierCompletion(result, request.Position);
            if (importSpecItems != null)
                return Task.FromResult(new CompletionList(importSpecItems));

            var memberItems = TryMemberCompletion(result, request.Position);
            if (memberItems != null)
            {
                return Task.FromResult(new CompletionList(memberItems));
            }

            foreach (var kw in Keywords)
            {
                items.Add(new CompletionItem
                {
                    Label = kw,
                    Kind = CompletionItemKind.Keyword
                });
            }

            var node = NodeFinder.Find(result.Hir, line, col);
            var scopeId = result.Package.Root;
            if (node != null)
                result.Scopes.EnclosingScope(node.ID, out scopeId);

            var symbols = workspace.CollectVisibleSymbols(result, scopeId);
            foreach (var sym in symbols)
            {
                var typeStr = workspace.FormatType(result.Types, sym.Type);
                var kind = sym.Kind switch
                {
                    LuxSymbolKind.Function => CompletionItemKind.Function,
                    LuxSymbolKind.Enum => CompletionItemKind.Enum,
                    LuxSymbolKind.Class => CompletionItemKind.Class,
                    LuxSymbolKind.Interface => CompletionItemKind.Interface,
                    LuxSymbolKind.TypeParam => CompletionItemKind.TypeParameter,
                    _ => CompletionItemKind.Variable
                };
                items.Add(new CompletionItem
                {
                    Label = sym.Name,
                    Kind = kind,
                    Detail = typeStr
                });
            }
        }
        else
        {
            foreach (var kw in Keywords)
            {
                items.Add(new CompletionItem
                {
                    Label = kw,
                    Kind = CompletionItemKind.Keyword
                });
            }
        }

        return Task.FromResult(new CompletionList(items));
    }

    /// <summary>
    /// Detects whether the cursor position is preceded by a member-access operator (`.` or `?.`),
    /// resolves the receiver chain to a type, and returns its struct fields as completion items.
    /// Returns null if this is not a member-completion context.
    /// </summary>
    private List<CompletionItem>? TryMemberCompletion(AnalysisResult result, OmniSharp.Extensions.LanguageServer.Protocol.Models.Position pos)
    {
        var lines = result.SourceText.Split('\n');
        if (pos.Line < 0 || pos.Line >= lines.Length) return null;
        var lineText = lines[pos.Line];
        if (lineText.EndsWith("\r")) lineText = lineText[..^1];

        var cursor = Math.Min(pos.Character, lineText.Length);

        // Skip the partially-typed member name immediately before the cursor.
        var nameEnd = cursor;
        var i = cursor;
        while (i > 0 && (char.IsLetterOrDigit(lineText[i - 1]) || lineText[i - 1] == '_')) i--;
        var nameStart = i;

        // Now require a `.` or `?.` immediately before the (optional) name.
        if (i <= 0 || lineText[i - 1] != '.') return null;
        i--;
        if (i > 0 && lineText[i - 1] == '?') i--;

        // Walk back the receiver chain: (NAME (\??\.NAME)*).
        var chainEnd = i;
        var j = i;
        while (true)
        {
            // Consume a NAME segment.
            var segEnd = j;
            while (j > 0 && (char.IsLetterOrDigit(lineText[j - 1]) || lineText[j - 1] == '_')) j--;
            if (j == segEnd) return null; // expected an identifier

            if (j > 0 && lineText[j - 1] == '.')
            {
                j--;
                if (j > 0 && lineText[j - 1] == '?') j--;
                continue;
            }
            break;
        }

        var chainText = lineText.Substring(j, chainEnd - j);
        if (string.IsNullOrEmpty(chainText)) return null;

        // Parse chainText into segments.
        var segments = new List<(string Name, bool Optional)>();
        var k = 0;
        while (k < chainText.Length)
        {
            var optional = false;
            if (k > 0)
            {
                if (chainText[k] == '?' && k + 1 < chainText.Length && chainText[k + 1] == '.')
                {
                    optional = true;
                    k += 2;
                }
                else if (chainText[k] == '.')
                {
                    k++;
                }
                else
                {
                    return null;
                }
            }
            var nStart = k;
            while (k < chainText.Length && (char.IsLetterOrDigit(chainText[k]) || chainText[k] == '_')) k++;
            if (k == nStart) return null;
            segments.Add((chainText.Substring(nStart, k - nStart), optional));
        }
        if (segments.Count == 0) return null;

        // Resolve head symbol via the enclosing scope at the cursor.
        var lspLine = pos.Line + 1;
        var lspCol = Math.Max(1, pos.Character);
        var node = NodeFinder.Find(result.Hir, lspLine, lspCol);
        var scopeId = result.Package.Root;
        if (node != null) result.Scopes.EnclosingScope(node.ID, out scopeId);

        if (!result.Scopes.Lookup(scopeId, segments[0].Name, out var headSym)) return null;
        if (!result.Syms.GetByID(headSym, out var headSymbol)) return null;
        var currentTypeId = headSymbol.Type;

        // Walk the chain.
        for (var s = 1; s < segments.Count; s++)
        {
            currentTypeId = StripNil(result.Types, currentTypeId);
            if (!result.Types.GetByID(currentTypeId, out var t)) return null;
            if (t is not StructType st) return null;
            var field = st.Fields.FirstOrDefault(f => f.Name.Name == segments[s].Name);
            if (field == null) return null;
            currentTypeId = field.Type.ID;
        }

        // Final type at the position right before the trailing `.` / `?.`.
        var finalTypeId = StripNil(result.Types, currentTypeId);
        if (!result.Types.GetByID(finalTypeId, out var finalType)) return null;

        if (finalType is EnumType enumType)
        {
            var items = new List<CompletionItem>();
            foreach (var m in enumType.Members)
            {
                items.Add(new CompletionItem
                {
                    Label = m.Name,
                    Kind = CompletionItemKind.EnumMember,
                    Detail = m.Value != null ? $"{enumType.Name}.{m.Name} = {m.Value}" : $"{enumType.Name}.{m.Name}"
                });
            }
            return items;
        }

        if (finalType is ClassType classType)
        {
            var classItems = new List<CompletionItem>();
            foreach (var (name, field) in classType.InstanceFields)
            {
                classItems.Add(new CompletionItem
                {
                    Label = name,
                    Kind = CompletionItemKind.Field,
                    Detail = workspace.FormatType(result.Types, field.Type.ID)
                });
            }
            foreach (var (name, method) in classType.Methods)
            {
                classItems.Add(new CompletionItem
                {
                    Label = name,
                    Kind = CompletionItemKind.Method,
                    Detail = workspace.FormatType(result.Types, method.ID)
                });
            }
            foreach (var (name, method) in classType.StaticMethods)
            {
                classItems.Add(new CompletionItem
                {
                    Label = name,
                    Kind = CompletionItemKind.Method,
                    Detail = workspace.FormatType(result.Types, method.ID)
                });
            }
            foreach (var (name, getter) in classType.Getters)
            {
                classItems.Add(new CompletionItem
                {
                    Label = name,
                    Kind = CompletionItemKind.Property,
                    Detail = workspace.FormatType(result.Types, getter.ReturnType.ID)
                });
            }
            if (classType.ConstructorType != null)
            {
                classItems.Add(new CompletionItem
                {
                    Label = "new",
                    Kind = CompletionItemKind.Constructor,
                    Detail = workspace.FormatType(result.Types, classType.ConstructorType.ID)
                });
            }
            return classItems;
        }

        if (finalType is not StructType finalStruct) return new List<CompletionItem>();

        var structItems = new List<CompletionItem>();
        foreach (var f in finalStruct.Fields)
        {
            structItems.Add(new CompletionItem
            {
                Label = f.Name.Name,
                Kind = f.Type is FunctionType ? CompletionItemKind.Method : CompletionItemKind.Field,
                Detail = workspace.FormatType(result.Types, f.Type.ID)
            });
        }
        return structItems;
    }

    private List<CompletionItem>? TryImportPathCompletion(AnalysisResult result,
        OmniSharp.Extensions.LanguageServer.Protocol.Models.Position pos)
    {
        var lines = result.SourceText.Split('\n');
        if (pos.Line < 0 || pos.Line >= lines.Length) return null;
        var lineText = lines[pos.Line].TrimEnd('\r');

        var trimmed = lineText.TrimStart();
        if (!trimmed.StartsWith("import ") && !trimmed.StartsWith("from ")) return null;

        var cursor = Math.Min(pos.Character, lineText.Length);
        var quoteChar = '\0';
        var quoteStart = -1;
        for (var i = 0; i < cursor; i++)
        {
            if (lineText[i] == '"' || lineText[i] == '\'')
            {
                if (quoteChar == '\0')
                {
                    quoteChar = lineText[i];
                    quoteStart = i + 1;
                }
                else if (lineText[i] == quoteChar)
                {
                    quoteChar = '\0';
                    quoteStart = -1;
                }
            }
        }

        if (quoteChar == '\0' || quoteStart < 0) return null;

        var partial = lineText.Substring(quoteStart, cursor - quoteStart);
        var fileDir = Path.GetDirectoryName(result.FilePath);
        if (fileDir == null) return null;

        string searchDir;
        string prefix;
        var lastSlash = partial.LastIndexOfAny(['/', '\\']);
        if (lastSlash >= 0)
        {
            var relDir = partial[..(lastSlash + 1)];
            searchDir = Path.GetFullPath(Path.Combine(fileDir, relDir));
            prefix = relDir;
        }
        else
        {
            searchDir = fileDir;
            prefix = partial.StartsWith("./") ? "./" : "";
        }

        if (!Directory.Exists(searchDir)) return null;

        var items = new List<CompletionItem>();
        foreach (var dir in Directory.GetDirectories(searchDir))
        {
            var name = Path.GetFileName(dir);
            items.Add(new CompletionItem
            {
                Label = name,
                Kind = CompletionItemKind.Folder,
                InsertText = prefix + name + "/"
            });
        }

        foreach (var file in Directory.GetFiles(searchDir, "*.lux"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(result.FilePath),
                    StringComparison.OrdinalIgnoreCase))
                continue;
            items.Add(new CompletionItem
            {
                Label = name,
                Kind = CompletionItemKind.File,
                InsertText = prefix + name
            });
        }

        foreach (var file in Directory.GetFiles(searchDir, "*.d.lux"))
        {
            var name = Path.GetFileName(file);
            var baseName = name[..^6];
            items.Add(new CompletionItem
            {
                Label = baseName,
                Kind = CompletionItemKind.File,
                InsertText = prefix + baseName
            });
        }

        return items;
    }

    private List<CompletionItem>? TryImportSpecifierCompletion(AnalysisResult result,
        OmniSharp.Extensions.LanguageServer.Protocol.Models.Position pos)
    {
        var lines = result.SourceText.Split('\n');
        if (pos.Line < 0 || pos.Line >= lines.Length) return null;
        var lineText = lines[pos.Line].TrimEnd('\r');

        var trimmed = lineText.TrimStart();
        if (!trimmed.StartsWith("import ")) return null;

        var cursor = Math.Min(pos.Character, lineText.Length);
        var braceIdx = lineText.IndexOf('{');
        var closeBraceIdx = lineText.IndexOf('}');
        if (braceIdx < 0 || cursor <= braceIdx || (closeBraceIdx >= 0 && cursor > closeBraceIdx))
            return null;

        string? moduleName = null;
        foreach (var stmt in result.Hir.Body)
        {
            if (stmt is not ImportStmt import) continue;
            if (import.Span.StartLn != pos.Line + 1) continue;
            moduleName = import.Module.Name;
            break;
        }

        if (moduleName == null) return null;

        var exports = workspace.CollectExportsFromModule(result, moduleName);
        if (exports == null) return null;

        var items = new List<CompletionItem>();
        foreach (var (name, info) in exports)
        {
            var typeStr = workspace.FormatType(result.Types, info.Type.ID);
            items.Add(new CompletionItem
            {
                Label = name,
                Kind = info.SymKind == IR.SymbolKind.Function
                    ? CompletionItemKind.Function
                    : CompletionItemKind.Variable,
                Detail = typeStr
            });
        }

        return items;
    }

    private static TypID StripNil(TypeTable types, TypID id)
    {
        if (!types.GetByID(id, out var t)) return id;
        if (t is UnionType u)
        {
            var nonNil = u.Types.Where(m => m.Kind != TypeKind.PrimitiveNil).ToList();
            if (nonNil.Count == 1) return nonNil[0].ID;
            if (nonNil.Count > 1) return types.UnionOf(nonNil);
        }
        return id;
    }

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken ct)
        => Task.FromResult(request);

    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability capability, ClientCapabilities clientCapabilities)
    {
        return new CompletionRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("lux"),
            TriggerCharacters = new Container<string>(".", "?", "/", "\"", "'", " "),
            ResolveProvider = false
        };
    }
}
