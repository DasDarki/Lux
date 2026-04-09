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
                List<Parameter>? declParams = null;
                if (sym.DeclaringNode != NodeID.Invalid && result.NodeRegistry.TryGetValue(sym.DeclaringNode, out var dn))
                {
                    declParams = dn switch
                    {
                        FunctionDecl fd => fd.Parameters,
                        LocalFunctionDecl lfd => lfd.Parameters,
                        _ => null
                    };
                }

                var paramParts = new List<string>();
                if (declParams != null)
                {
                    var ri = 0;
                    foreach (var dp in declParams)
                    {
                        if (dp.IsVararg)
                        {
                            var vaType = ft.VarargType != null
                                ? workspace.FormatType(result.Types, ft.VarargType)
                                : "any";
                            var vaName = dp.Name.Name != "..." ? dp.Name.Name : "";
                            paramParts.Add($"...{vaName}: {vaType}");
                        }
                        else
                        {
                            var pType = ri < ft.ParamTypes.Count
                                ? workspace.FormatType(result.Types, ft.ParamTypes[ri])
                                : "any";
                            var part = $"{dp.Name.Name}: {pType}";
                            if (dp.DefaultValue != null) part += " = ...";
                            paramParts.Add(part);
                            ri++;
                        }
                    }
                }
                else
                {
                    paramParts.AddRange(ft.ParamTypes.Select((p, i) =>
                    {
                        var s = workspace.FormatType(result.Types, p);
                        return ft.DefaultParams.Contains(i) ? $"{s} = ..." : s;
                    }));
                    if (ft.IsVararg)
                    {
                        var vaType = ft.VarargType != null
                            ? workspace.FormatType(result.Types, ft.VarargType)
                            : "any";
                        paramParts.Add($"...: {vaType}");
                    }
                }

                var ret = workspace.FormatType(result.Types, ft.ReturnType);
                display = $"(function) {sym.Name}({string.Join(", ", paramParts)}) -> {ret}";
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
