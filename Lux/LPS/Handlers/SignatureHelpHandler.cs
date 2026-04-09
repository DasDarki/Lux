using Lux.IR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Lux.LPS.Handlers;

public sealed class SignatureHelpHandler(LuxWorkspace workspace) : SignatureHelpHandlerBase
{
    public override Task<SignatureHelp?> Handle(SignatureHelpParams request, CancellationToken ct)
    {
        var result = workspace.GetResult(request.TextDocument.Uri.ToString());
        if (result == null) return Task.FromResult<SignatureHelp?>(null);

        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;

        var node = NodeFinder.Find(result.Hir, line, col);
        FunctionCallExpr? callExpr = null;
        MethodCallExpr? methodExpr = null;

        var current = node;
        while (current != null)
        {
            if (current is FunctionCallExpr fce) { callExpr = fce; break; }
            if (current is MethodCallExpr mce) { methodExpr = mce; break; }
            if (!result.NodeRegistry.TryGetValue(current.ID, out _)) break;

            Node? parent = null;
            foreach (var (_, n) in result.NodeRegistry)
            {
                if (n is FunctionCallExpr fc && (fc.Callee == current || fc.Arguments.Contains(current as Expr)))
                { parent = fc; break; }
                if (n is MethodCallExpr mc && (mc.Object == current || mc.Arguments.Contains(current as Expr)))
                { parent = mc; break; }
            }
            current = parent;
        }

        var calleeSym = SymID.Invalid;
        int activeParam;

        if (callExpr != null)
        {
            if (callExpr.Callee is NameExpr ne)
                calleeSym = ne.Name.Sym;
            activeParam = CountActiveParam(callExpr.Arguments, line, col);
        }
        else if (methodExpr != null)
        {
            calleeSym = methodExpr.MethodName.Sym;
            activeParam = CountActiveParam(methodExpr.Arguments, line, col);
        }
        else
        {
            return Task.FromResult<SignatureHelp?>(null);
        }

        if (calleeSym == SymID.Invalid || !result.Syms.GetByID(calleeSym, out var sym))
            return Task.FromResult<SignatureHelp?>(null);

        if (!result.Types.GetByID(sym.Type, out var typ) || typ is not FunctionType ft)
            return Task.FromResult<SignatureHelp?>(null);

        var paramInfos = new List<ParameterInformation>();
        List<Parameter>? declParams = null;

        if (sym.DeclaringNode != NodeID.Invalid && result.NodeRegistry.TryGetValue(sym.DeclaringNode, out var declNode))
        {
            declParams = declNode switch
            {
                FunctionDecl fd => fd.Parameters,
                LocalFunctionDecl lfd => lfd.Parameters,
                _ => null
            };
        }

        var regularIdx = 0;
        if (declParams != null)
        {
            foreach (var dp in declParams)
            {
                if (dp.IsVararg)
                {
                    var vaType = ft.VarargType != null
                        ? workspace.FormatType(result.Types, ft.VarargType)
                        : "any";
                    var vaName = dp.Name.Name != "..." ? dp.Name.Name : "...";
                    paramInfos.Add(new ParameterInformation
                    {
                        Label = new ParameterInformationLabel($"...{vaName}: {vaType}")
                    });
                }
                else
                {
                    var pType = regularIdx < ft.ParamTypes.Count
                        ? workspace.FormatType(result.Types, ft.ParamTypes[regularIdx])
                        : "any";
                    var label2 = $"{dp.Name.Name}: {pType}";
                    if (dp.DefaultValue != null)
                        label2 += " = ...";
                    paramInfos.Add(new ParameterInformation
                    {
                        Label = new ParameterInformationLabel(label2)
                    });
                    regularIdx++;
                }
            }
        }
        else
        {
            for (var i = 0; i < ft.ParamTypes.Count; i++)
            {
                var pType = workspace.FormatType(result.Types, ft.ParamTypes[i]);
                var defaultHint = ft.DefaultParams.Contains(i) ? " = ..." : "";
                paramInfos.Add(new ParameterInformation
                {
                    Label = new ParameterInformationLabel($"arg{i}: {pType}{defaultHint}")
                });
            }
            if (ft.IsVararg)
            {
                var vaType = ft.VarargType != null
                    ? workspace.FormatType(result.Types, ft.VarargType)
                    : "any";
                paramInfos.Add(new ParameterInformation
                {
                    Label = new ParameterInformationLabel($"...: {vaType}")
                });
            }
        }

        var retType = workspace.FormatType(result.Types, ft.ReturnType);
        var paramStr = string.Join(", ", paramInfos.Select(p => p.Label.Label));
        var label = $"{sym.Name}({paramStr}) -> {retType}";

        var sigInfo = new SignatureInformation
        {
            Label = label,
            Parameters = new Container<ParameterInformation>(paramInfos)
        };

        return Task.FromResult<SignatureHelp?>(new SignatureHelp
        {
            Signatures = new Container<SignatureInformation>(sigInfo),
            ActiveSignature = 0,
            ActiveParameter = activeParam
        });
    }

    private static int CountActiveParam(List<Expr> arguments, int line, int col)
    {
        for (var i = arguments.Count - 1; i >= 0; i--)
        {
            var arg = arguments[i];
            if (line > arg.Span.StartLn || (line == arg.Span.StartLn && col >= arg.Span.StartCol))
                return i;
        }
        return 0;
    }

    protected override SignatureHelpRegistrationOptions CreateRegistrationOptions(
        SignatureHelpCapability capability, ClientCapabilities clientCapabilities)
    {
        return new SignatureHelpRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("lux"),
            TriggerCharacters = new Container<string>("(", ",")
        };
    }
}
