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
        "and", "break", "do", "else", "elseif", "end", "false", "for",
        "function", "goto", "if", "in", "local", "nil", "not", "or",
        "repeat", "return", "then", "true", "until", "while",
        "as", "declare", "export", "from", "import", "module"
    ];

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken ct)
    {
        var items = new List<CompletionItem>();

        var result = workspace.GetResult(request.TextDocument.Uri.ToString());
        if (result != null)
        {
            var line = request.Position.Line + 1;
            var col = request.Position.Character + 1;

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
                items.Add(new CompletionItem
                {
                    Label = sym.Name,
                    Kind = sym.Kind == LuxSymbolKind.Function
                        ? CompletionItemKind.Function
                        : CompletionItemKind.Variable,
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
        if (finalType is not StructType finalStruct) return new List<CompletionItem>();

        var items = new List<CompletionItem>();
        foreach (var f in finalStruct.Fields)
        {
            items.Add(new CompletionItem
            {
                Label = f.Name.Name,
                Kind = f.Type is FunctionType ? CompletionItemKind.Method : CompletionItemKind.Field,
                Detail = workspace.FormatType(result.Types, f.Type.ID)
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
            TriggerCharacters = new Container<string>(".", "?"),
            ResolveProvider = false
        };
    }
}
