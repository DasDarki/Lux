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

        var hoveredNode = NodeFinder.Find(result.Hir, line, col);
        var nameRef = NodeFinder.FindNameRef(result.Hir, line, col);
        if (nameRef != null && nameRef.Sym != SymID.Invalid && result.Syms.GetByID(nameRef.Sym, out var sym))
        {
            var declaredType = sym.Type;
            var effectiveType = declaredType;
            if (hoveredNode is NameExpr ne && ne.Name == nameRef && ne.Type != TypID.Invalid)
            {
                effectiveType = ne.Type;
            }

            var typeStr = workspace.FormatType(result.Types, effectiveType);
            var kind = sym.Kind == LuxSymbolKind.Function ? "function" : "variable";
            string display;

            if (sym.Kind == LuxSymbolKind.Function && result.Types.GetByID(sym.Type, out var typ) && typ is FunctionType ft)
            {
                var parms = string.Join(", ", ft.ParamTypes.Select(p => workspace.FormatType(result.Types, p)));
                var ret = workspace.FormatType(result.Types, ft.ReturnType);
                display = $"(function) {sym.Name}({parms}) -> {ret}";
            }
            else if (effectiveType != declaredType)
            {
                var declaredStr = workspace.FormatType(result.Types, declaredType);
                display = $"({kind}) {sym.Name}: {typeStr}\n-- narrowed from {declaredStr}";
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

        if (hoveredNode is Expr expr && expr.Type != TypID.Invalid)
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
