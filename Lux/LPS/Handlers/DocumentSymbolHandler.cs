using Lux.IR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using LspSymbolKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind;

namespace Lux.LPS.Handlers;

public sealed class DocumentSymbolHandler(LuxWorkspace workspace) : DocumentSymbolHandlerBase
{
    public override Task<SymbolInformationOrDocumentSymbolContainer?> Handle(
        DocumentSymbolParams request, CancellationToken ct)
    {
        var result = workspace.GetResult(request.TextDocument.Uri.ToString());
        if (result == null)
            return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(null);

        var symbols = new List<SymbolInformationOrDocumentSymbol>();
        CollectSymbols(result.Hir.Body, symbols);
        return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(
            new SymbolInformationOrDocumentSymbolContainer(symbols));
    }

    private void CollectSymbols(List<Stmt> stmts, List<SymbolInformationOrDocumentSymbol> symbols)
    {
        foreach (var stmt in stmts)
        {
            switch (stmt)
            {
                case FunctionDecl fd:
                {
                    var name = string.Join(".", fd.NamePath.Select(n => n.Name));
                    if (fd.MethodName != null) name += ":" + fd.MethodName.Name;
                    symbols.Add(new DocumentSymbol
                    {
                        Name = name,
                        Kind = LspSymbolKind.Function,
                        Range = LuxWorkspace.SpanToRange(fd.Span),
                        SelectionRange = LuxWorkspace.SpanToRange(fd.NamePath[0].Span)
                    });
                    break;
                }
                case LocalFunctionDecl lfd:
                    symbols.Add(new DocumentSymbol
                    {
                        Name = lfd.Name.Name,
                        Kind = LspSymbolKind.Function,
                        Range = LuxWorkspace.SpanToRange(lfd.Span),
                        SelectionRange = LuxWorkspace.SpanToRange(lfd.Name.Span)
                    });
                    break;
                case LocalDecl ld:
                    foreach (var v in ld.Variables)
                    {
                        symbols.Add(new DocumentSymbol
                        {
                            Name = v.Name.Name,
                            Kind = LspSymbolKind.Variable,
                            Range = LuxWorkspace.SpanToRange(v.Span),
                            SelectionRange = LuxWorkspace.SpanToRange(v.Name.Span)
                        });
                    }
                    break;
                case ExportStmt exp:
                    CollectSymbols([exp.Declaration], symbols);
                    break;
            }
        }
    }

    protected override DocumentSymbolRegistrationOptions CreateRegistrationOptions(
        DocumentSymbolCapability capability, ClientCapabilities clientCapabilities)
    {
        return new DocumentSymbolRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("lux")
        };
    }
}
