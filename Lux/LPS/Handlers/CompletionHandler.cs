using Lux.IR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using LuxSymbolKind = Lux.IR.SymbolKind;

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

        foreach (var kw in Keywords)
        {
            items.Add(new CompletionItem
            {
                Label = kw,
                Kind = CompletionItemKind.Keyword
            });
        }

        var result = workspace.GetResult(request.TextDocument.Uri.ToString());
        if (result != null)
        {
            var line = request.Position.Line + 1;
            var col = request.Position.Character + 1;

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

        return Task.FromResult(new CompletionList(items));
    }

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken ct)
        => Task.FromResult(request);

    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability capability, ClientCapabilities clientCapabilities)
    {
        return new CompletionRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("lux"),
            TriggerCharacters = new Container<string>("."),
            ResolveProvider = false
        };
    }
}
