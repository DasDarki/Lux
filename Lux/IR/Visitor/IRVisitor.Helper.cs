using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Lux.Diagnostics;

namespace Lux.IR;

internal partial class IRVisitor
{
    private NodeID NewNodeID => nodeAlloc.Next();

    private TextSpan SpanFromCtx(ParserRuleContext ctx)
    {
        var start = ctx.Start;
        var stop = ctx.Stop ?? start;
        var startCol = start.Column + 1;
        var endCol = stop.Column + 1 + (stop.StopIndex - stop.StartIndex) + 1;
        return new TextSpan(filename, start.Line, startCol, stop.Line, endCol);
    }

    private TextSpan SpanFromTok(IToken tok) => TextSpan.Of(tok, filename);

    private TextSpan SpanFromTerm(ITerminalNode term) => TextSpan.Of(term, filename);

    private static string StripQuotes(string s) => s.Length >= 2 ? s[1..^1] : s;

    private static string StripLongBrackets(string s)
    {
        var eqCount = 0;
        for (var i = 1; i < s.Length && s[i] == '='; i++) eqCount++;
        var start = eqCount + 2;
        var end = s.Length - eqCount - 2;
        return start < end ? s[start..end] : string.Empty;
    }

    private string ParseStringValue(LuxParser.StrContext ctx)
    {
        return ctx switch
        {
            LuxParser.DoubleQuotedStrContext => StripQuotes(ctx.GetText()),
            LuxParser.SingleQuotedStrContext => StripQuotes(ctx.GetText()),
            LuxParser.LongStrContext => StripLongBrackets(ctx.GetText()),
            _ => ctx.GetText()
        };
    }

    private (string, TextSpan) ParseStringValueWithSpan(LuxParser.StrContext ctx)
    {
        var value = ParseStringValue(ctx);
        var span = SpanFromCtx(ctx);
        return (value, span);
    }

    private NameRef NameRefFromString(LuxParser.StrContext ctx)
    {
        var (value, span) = ParseStringValueWithSpan(ctx);
        return new NameRef(value, span);
    }
    
    private TypeRef? VisitTypeAnnotationOpt(LuxParser.TypeAnnotationContext? ctx)
    {
        if (ctx == null) return null;
        return (TypeRef)Visit(ctx.typeExpr());
    }

    private (List<Stmt> body, ReturnStmt? ret) VisitBlockContent(LuxParser.BlockContext ctx)
    {
        var stmts = new List<Stmt>();
        foreach (var stmt in ctx.stmt())
        {
            var node = Visit(stmt);
            if (node is Stmt s) stmts.Add(s);
        }

        ReturnStmt? ret = null;
        if (ctx.returnStat() != null)
            ret = (ReturnStmt)Visit(ctx.returnStat());

        return (stmts, ret);
    }

    private (List<Parameter> parameters, TypeRef? returnType, List<Stmt> body, ReturnStmt? ret) VisitFuncBodyContent(
        LuxParser.FuncBodyContext ctx)
    {
        var parameters = VisitParamListContent(ctx.paramList());
        var returnType = VisitTypeAnnotationOpt(ctx.typeAnnotation());
        var (body, ret) = VisitBlockContent(ctx.block());
        return (parameters, returnType, body, ret);
    }

    private (List<Parameter> parameters, TypeRef? returnType) VisitFuncSignatureContent(
        LuxParser.FuncSignatureContext ctx)
    {
        var parameters = VisitParamListContent(ctx.paramList());
        var returnType = VisitTypeAnnotationOpt(ctx.typeAnnotation());
        return (parameters, returnType);
    }

    private List<Parameter> VisitParamListContent(LuxParser.ParamListContext? ctx)
    {
        if (ctx == null) return [];

        switch (ctx)
        {
            case LuxParser.ParamListWithNamesContext withNames:
            {
                var result = withNames.param().Select(p => new Parameter(
                    NewNodeID,
                    NameRefFromTerm(p.NAME()),
                    VisitTypeAnnotationOpt(p.typeAnnotation()),
                    false,
                    SpanFromCtx(p)
                )).ToList();

                if (withNames.varargParam() != null)
                {
                    var vp = withNames.varargParam();
                    var span = SpanFromCtx(vp);
                    result.Add(new Parameter(NewNodeID, NameRefFromText("...", span), VisitTypeAnnotationOpt(vp.typeAnnotation()), true, span));
                }

                return result;
            }
            case LuxParser.ParamListVarargContext vararg:
            {
                var vp = vararg.varargParam();
                var span = SpanFromCtx(vp);
                return [new Parameter(NewNodeID, NameRefFromText("...", span), VisitTypeAnnotationOpt(vp.typeAnnotation()), true, span)];
            }
            default:
                return [];
        }
    }

    private (List<NameRef> namePath, NameRef? methodName) VisitFuncNameContent(LuxParser.FuncNameContext ctx)
    {
        var allNames = ctx.NAME().Select(NameRefFromTerm).ToList();
        NameRef? methodName = null;
        if (ctx.COLON() != null && allNames.Count > 0)
        {
            methodName = allNames[^1];
            allNames.RemoveAt(allNames.Count - 1);
        }

        return (allNames, methodName);
    }

    private List<AttribVar> VisitAttribNameListContent(LuxParser.AttribNameListContext ctx)
    {
        return ctx.attribName().Select(a => new AttribVar(
            NameRefFromTerm(a.NAME()),
            a.attrib()?.NAME()?.GetText(),
            VisitTypeAnnotationOpt(a.typeAnnotation()),
            SpanFromCtx(a)
        )).ToList();
    }

    private List<Expr> VisitArgsContent(LuxParser.ArgsContext ctx)
    {
        return ctx switch
        {
            LuxParser.ParenArgsContext paren =>
                paren.exprList()?.expr().Select(e => (Expr)Visit(e)).ToList() ?? [],
            LuxParser.TableArgsContext table =>
                [(Expr)Visit(table.tableConstructor())],
            LuxParser.StringArgsContext str =>
                [(Expr)Visit(str.str())],
            _ => []
        };
    }

    private Expr BuildSuffixChain(LuxParser.VarOrExpContext varOrExp, LuxParser.SuffixContext[] suffixes)
    {
        var result = (Expr)Visit(varOrExp);
        foreach (var suffix in suffixes)
            result = WrapWithSuffix(result, suffix);
        return result;
    }

    private Expr WrapWithSuffix(Expr obj, LuxParser.SuffixContext suffix)
    {
        return suffix switch
        {
            LuxParser.DotSuffixContext dot =>
                new DotAccessExpr(NewNodeID, SpanFromCtx(dot), obj, NameRefFromTerm(dot.NAME())),
            LuxParser.OptDotSuffixContext odot =>
                new DotAccessExpr(NewNodeID, SpanFromCtx(odot), obj, NameRefFromTerm(odot.NAME()), isOptional: true),
            LuxParser.IndexSuffixContext idx =>
                new IndexAccessExpr(NewNodeID, SpanFromCtx(idx), obj, (Expr)Visit(idx.expr())),
            LuxParser.MethodCallSuffixContext mc =>
                new MethodCallExpr(NewNodeID, SpanFromCtx(mc), obj, NameRefFromTerm(mc.NAME()), VisitArgsContent(mc.args())),
            LuxParser.CallSuffixContext call =>
                new FunctionCallExpr(NewNodeID, SpanFromCtx(call), obj, VisitArgsContent(call.args())),
            LuxParser.OptCallSuffixContext optCall =>
                new FunctionCallExpr(NewNodeID, SpanFromCtx(optCall), obj, VisitArgsContent(optCall.args()), isOptional: true),
            _ => throw new InvalidOperationException($"Unknown suffix type: {suffix.GetType().Name}")
        };
    }

    private NameRef NameRefFromTerm(ITerminalNode node)
    {
        return new NameRef(node.GetText(), SpanFromTerm(node));
    }
    
    private NameRef NameRefFromText(string name, TextSpan? span = null)
    {
        return new NameRef(name, span ?? TextSpan.Empty);
    }
}
