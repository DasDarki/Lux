namespace Lux.IR;

internal partial class IRVisitor
{
    #region Literals

    public override Node VisitNilLiteral(LuxParser.NilLiteralContext context)
        => new NilLiteralExpr(NewNodeID, SpanFromCtx(context));

    public override Node VisitTrueLiteral(LuxParser.TrueLiteralContext context)
        => new BoolLiteralExpr(NewNodeID, SpanFromCtx(context), true);

    public override Node VisitFalseLiteral(LuxParser.FalseLiteralContext context)
        => new BoolLiteralExpr(NewNodeID, SpanFromCtx(context), false);

    public override Node VisitNumberLiteral(LuxParser.NumberLiteralContext context)
        => Visit(context.number());

    public override Node VisitStringLiteral(LuxParser.StringLiteralContext context)
        => Visit(context.str());

    public override Node VisitVarargExpr(LuxParser.VarargExprContext context)
        => new VarargExpr(NewNodeID, SpanFromCtx(context));

    #endregion

    #region Number Literals

    public override Node VisitIntLit(LuxParser.IntLitContext context)
        => new NumberLiteralExpr(NewNodeID, SpanFromCtx(context), context.GetText(), NumberKind.Int);

    public override Node VisitHexLit(LuxParser.HexLitContext context)
        => new NumberLiteralExpr(NewNodeID, SpanFromCtx(context), context.GetText(), NumberKind.Hex);

    public override Node VisitFloatLit(LuxParser.FloatLitContext context)
        => new NumberLiteralExpr(NewNodeID, SpanFromCtx(context), context.GetText(), NumberKind.Float);

    public override Node VisitHexFloatLit(LuxParser.HexFloatLitContext context)
        => new NumberLiteralExpr(NewNodeID, SpanFromCtx(context), context.GetText(), NumberKind.HexFloat);

    #endregion

    #region String Literals

    public override Node VisitDoubleQuotedStr(LuxParser.DoubleQuotedStrContext context)
        => new StringLiteralExpr(NewNodeID, SpanFromCtx(context), StripQuotes(context.GetText()));

    public override Node VisitSingleQuotedStr(LuxParser.SingleQuotedStrContext context)
        => new StringLiteralExpr(NewNodeID, SpanFromCtx(context), StripQuotes(context.GetText()));

    public override Node VisitLongStr(LuxParser.LongStrContext context)
        => new StringLiteralExpr(NewNodeID, SpanFromCtx(context), StripLongBrackets(context.GetText()));

    #endregion

    #region Binary Expressions

    public override Node VisitLogicalOrExpr(LuxParser.LogicalOrExprContext context)
        => new BinaryExpr(NewNodeID, SpanFromCtx(context), BinaryOp.Or, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));

    public override Node VisitLogicalAndExpr(LuxParser.LogicalAndExprContext context)
        => new BinaryExpr(NewNodeID, SpanFromCtx(context), BinaryOp.And, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));

    public override Node VisitComparisonExpr(LuxParser.ComparisonExprContext context)
    {
        var op = context.compareOp() switch
        {
            LuxParser.LtOpContext => BinaryOp.Lt,
            LuxParser.GtOpContext => BinaryOp.Gt,
            LuxParser.LteOpContext => BinaryOp.Lte,
            LuxParser.GteOpContext => BinaryOp.Gte,
            LuxParser.NeqOpContext => BinaryOp.Neq,
            LuxParser.EqOpContext => BinaryOp.Eq,
            _ => throw new InvalidOperationException("Unknown compare op")
        };
        return new BinaryExpr(NewNodeID, SpanFromCtx(context), op, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));
    }

    public override Node VisitBitwiseOrExpr(LuxParser.BitwiseOrExprContext context)
        => new BinaryExpr(NewNodeID, SpanFromCtx(context), BinaryOp.BitwiseOr, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));

    public override Node VisitBitwiseXorExpr(LuxParser.BitwiseXorExprContext context)
        => new BinaryExpr(NewNodeID, SpanFromCtx(context), BinaryOp.BitwiseXor, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));

    public override Node VisitBitwiseAndExpr(LuxParser.BitwiseAndExprContext context)
        => new BinaryExpr(NewNodeID, SpanFromCtx(context), BinaryOp.BitwiseAnd, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));

    public override Node VisitBitShiftExpr(LuxParser.BitShiftExprContext context)
    {
        var op = context.shiftOp() switch
        {
            LuxParser.LshiftOpContext => BinaryOp.LShift,
            LuxParser.RshiftOpContext => BinaryOp.RShift,
            _ => throw new InvalidOperationException("Unknown shift op")
        };
        return new BinaryExpr(NewNodeID, SpanFromCtx(context), op, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));
    }

    public override Node VisitConcatExpr(LuxParser.ConcatExprContext context)
        => new BinaryExpr(NewNodeID, SpanFromCtx(context), BinaryOp.Concat, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));

    public override Node VisitAdditiveExpr(LuxParser.AdditiveExprContext context)
    {
        var op = context.additiveOp() switch
        {
            LuxParser.AddOpContext => BinaryOp.Add,
            LuxParser.SubOpContext => BinaryOp.Sub,
            _ => throw new InvalidOperationException("Unknown additive op")
        };
        return new BinaryExpr(NewNodeID, SpanFromCtx(context), op, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));
    }

    public override Node VisitMultiplicativeExpr(LuxParser.MultiplicativeExprContext context)
    {
        var op = context.multiplicativeOp() switch
        {
            LuxParser.MulOpContext => BinaryOp.Mul,
            LuxParser.DivOpContext => BinaryOp.Div,
            LuxParser.FloorDivOpContext => BinaryOp.FloorDiv,
            LuxParser.ModOpContext => BinaryOp.Mod,
            _ => throw new InvalidOperationException("Unknown multiplicative op")
        };
        return new BinaryExpr(NewNodeID, SpanFromCtx(context), op, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));
    }

    public override Node VisitPowerExpr(LuxParser.PowerExprContext context)
        => new BinaryExpr(NewNodeID, SpanFromCtx(context), BinaryOp.Pow, (Expr)Visit(context.expr(0)), (Expr)Visit(context.expr(1)));

    #endregion

    #region Unary Expressions

    public override Node VisitUnaryExpr(LuxParser.UnaryExprContext context)
    {
        var op = context.unaryOp() switch
        {
            LuxParser.LogicalNotOpContext => UnaryOp.LogicalNot,
            LuxParser.LengthOpContext => UnaryOp.Length,
            LuxParser.NegateOpContext => UnaryOp.Negate,
            LuxParser.BitwiseNotOpContext => UnaryOp.BitwiseNot,
            _ => throw new InvalidOperationException("Unknown unary op")
        };
        return new UnaryExpr(NewNodeID, SpanFromCtx(context), op, (Expr)Visit(context.expr()));
    }

    #endregion

    #region Prefix Expressions

    public override Node VisitPrefixExpr(LuxParser.PrefixExprContext context)
        => Visit(context.prefixExp());

    public override Node VisitPrefixExp(LuxParser.PrefixExpContext context)
        => BuildSuffixChain(context.varOrExp(), context.suffix());

    public override Node VisitNameVarOrExp(LuxParser.NameVarOrExpContext context)
        => new NameExpr(NewNodeID, SpanFromCtx(context), new NameRef(context.NAME().GetText(), SpanFromTerm(context.NAME())));

    public override Node VisitParenVarOrExp(LuxParser.ParenVarOrExpContext context)
        => new ParenExpr(NewNodeID, SpanFromCtx(context), (Expr)Visit(context.expr()));

    #endregion

    #region Variable references (assignment targets)

    public override Node VisitNameVar(LuxParser.NameVarContext context)
        => new NameExpr(NewNodeID, SpanFromCtx(context), new NameRef(context.NAME().GetText(), SpanFromTerm(context.NAME())));

    public override Node VisitFieldVar(LuxParser.FieldVarContext context)
    {
        var obj = BuildSuffixChain(context.varOrExp(), context.suffix());
        return new DotAccessExpr(NewNodeID, SpanFromCtx(context), obj, NameRefFromTerm(context.NAME()));
    }

    public override Node VisitIndexVar(LuxParser.IndexVarContext context)
    {
        var obj = BuildSuffixChain(context.varOrExp(), context.suffix());
        return new IndexAccessExpr(NewNodeID, SpanFromCtx(context), obj, (Expr)Visit(context.expr()));
    }

    #endregion

    #region Function Calls

    public override Node VisitDirectCall(LuxParser.DirectCallContext context)
    {
        var callee = BuildSuffixChain(context.varOrExp(), context.suffix());
        return new FunctionCallExpr(NewNodeID, SpanFromCtx(context), callee, VisitArgsContent(context.args()));
    }

    public override Node VisitMethodCall(LuxParser.MethodCallContext context)
    {
        var obj = BuildSuffixChain(context.varOrExp(), context.suffix());
        return new MethodCallExpr(NewNodeID, SpanFromCtx(context), obj, NameRefFromTerm(context.NAME()), VisitArgsContent(context.args()));
    }

    #endregion

    #region Function Definitions (anonymous)

    public override Node VisitFunctionDefExpr(LuxParser.FunctionDefExprContext context)
        => Visit(context.functionDef());

    public override Node VisitFunctionDef(LuxParser.FunctionDefContext context)
    {
        var (parameters, returnType, body, ret) = VisitFuncBodyContent(context.funcBody());
        return new FunctionDefExpr(NewNodeID, SpanFromCtx(context), parameters, returnType, body, ret);
    }

    #endregion

    #region Tables

    public override Node VisitTableConstructorExpr(LuxParser.TableConstructorExprContext context)
        => Visit(context.tableConstructor());

    public override Node VisitTableConstructor(LuxParser.TableConstructorContext context)
    {
        var fields = new List<TableField>();
        if (context.fieldList() != null)
        {
            foreach (var field in context.fieldList().field())
                fields.Add(VisitTableFieldContent(field));
        }

        return new TableConstructorExpr(NewNodeID, SpanFromCtx(context), fields);
    }

    private TableField VisitTableFieldContent(LuxParser.FieldContext ctx)
    {
        return ctx switch
        {
            LuxParser.BracketFieldContext bf => new TableField(
                TableFieldKind.Bracket, (Expr)Visit(bf.expr(0)), null, (Expr)Visit(bf.expr(1)), SpanFromCtx(bf)),
            LuxParser.NameFieldContext nf => new TableField(
                TableFieldKind.Named, null, NameRefFromTerm(nf.NAME()), (Expr)Visit(nf.expr()), SpanFromCtx(nf)),
            LuxParser.ValueFieldContext vf => new TableField(
                TableFieldKind.Positional, null, null, (Expr)Visit(vf.expr()), SpanFromCtx(vf)),
            _ => throw new InvalidOperationException($"Unknown field type: {ctx.GetType().Name}")
        };
    }

    #endregion
}
