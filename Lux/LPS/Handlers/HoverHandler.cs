using Lux.IR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using LuxSymbolKind = Lux.IR.SymbolKind;

namespace Lux.LPS.Handlers;

public sealed class HoverHandler(LuxWorkspace workspace) : HoverHandlerBase
{
    public override Task<Hover?> Handle(HoverParams request, CancellationToken ct)
    {
        var result = workspace.GetResult(request.TextDocument.Uri.ToString());
        if (result == null) return Task.FromResult<Hover?>(null);

        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;

        var nameRef = NodeFinder.FindNameRef(result.Hir, line, col);
        if (nameRef != null && nameRef.Sym != SymID.Invalid && result.Syms.GetByID(nameRef.Sym, out var sym))
        {
            var typeStr = workspace.FormatType(result.Types, sym.Type);
            var kind = sym.Kind == LuxSymbolKind.Function ? "function" : "variable";
            string display;

            if (sym.Kind == LuxSymbolKind.Function && result.Types.GetByID(sym.Type, out var typ) && typ is FunctionType ft)
            {
                var parms = string.Join(", ", ft.ParamTypes.Select(p => workspace.FormatType(result.Types, p)));
                var ret = workspace.FormatType(result.Types, ft.ReturnType);
                display = $"(function) {sym.Name}({parms}) -> {ret}";
            }
            else
            {
                display = $"({kind}) {sym.Name}: {typeStr}";
            }

            return Task.FromResult<Hover?>(new Hover
            {
                Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = $"```lux\n{display}\n```"
                }),
                Range = LuxWorkspace.SpanToRange(nameRef.Span)
            });
        }

        var node = NodeFinder.Find(result.Hir, line, col);
        if (node is Expr expr && expr.Type != TypID.Invalid)
        {
            var typeStr = workspace.FormatType(result.Types, expr.Type);
            return Task.FromResult<Hover?>(new Hover
            {
                Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = $"```lux\n{typeStr}\n```"
                }),
                Range = LuxWorkspace.SpanToRange(expr.Span)
            });
        }

        return Task.FromResult<Hover?>(null);
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability, ClientCapabilities clientCapabilities)
    {
        return new HoverRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("lux")
        };
    }
}
