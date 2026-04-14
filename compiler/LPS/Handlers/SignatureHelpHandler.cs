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

        var callNode = NodeFinder.FindEnclosingCall(result.Hir, line, col);
        if (callNode == null) return Task.FromResult<SignatureHelp?>(null);

        var calleeSym = SymID.Invalid;
        List<Expr> arguments;

        switch (callNode)
        {
            case FunctionCallExpr fce:
                if (fce.Callee is NameExpr ne) calleeSym = ne.Name.Sym;
                arguments = fce.Arguments;
                break;
            case MethodCallExpr mce:
                calleeSym = mce.MethodName.Sym;
                arguments = mce.Arguments;
                break;
            default:
                return Task.FromResult<SignatureHelp?>(null);
        }

        if (calleeSym == SymID.Invalid || !result.Syms.GetByID(calleeSym, out var sym))
            return Task.FromResult<SignatureHelp?>(null);

        if (!result.Types.GetByID(sym.Type, out var typ) || typ is not FunctionType ft)
            return Task.FromResult<SignatureHelp?>(null);

        var activeParam = CountActiveParam(result.SourceText, request.Position);

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
                var argName = $"arg{i}";
                if (ft.ParamNames != null && i < ft.ParamNames.Count && !string.IsNullOrEmpty(ft.ParamNames[i]))
                    argName = ft.ParamNames[i];
                paramInfos.Add(new ParameterInformation
                {
                    Label = new ParameterInformationLabel($"{argName}: {pType}{defaultHint}")
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
            ActiveParameter = Math.Min(activeParam, Math.Max(0, paramInfos.Count - 1))
        });
    }

    private static int CountActiveParam(string sourceText, OmniSharp.Extensions.LanguageServer.Protocol.Models.Position pos)
    {
        var lines = sourceText.Split('\n');
        if (pos.Line < 0 || pos.Line >= lines.Length) return 0;
        var lineText = lines[pos.Line].TrimEnd('\r');
        var cursor = Math.Min(pos.Character, lineText.Length);

        var depth = 0;
        var commas = 0;

        for (var i = cursor - 1; i >= 0; i--)
        {
            var ch = lineText[i];
            switch (ch)
            {
                case ')' or ']' or '}':
                    depth++;
                    break;
                case '(' or '[' or '{':
                    if (depth == 0) return commas;
                    depth--;
                    break;
                case ',' when depth == 0:
                    commas++;
                    break;
            }
        }

        return commas;
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
