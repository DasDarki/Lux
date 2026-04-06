using Lux.Diagnostics;
using Lux.IR;
using Type = Lux.IR.Type;

namespace Lux.Compiler.Passes;

/// <summary>
/// The infer types pass is responsible for inferring the types of expressions in the source code. It takes care of
/// inferring the types of variables, function return types, and other expressions based on their usage and context.
/// </summary>
public sealed class InferTypesPass() : Pass(PassName, PassScope.PerFile, dependencies: [ResolveNamesPass.PassName, ResolveTypeRefsPass.PassName])
{
    public const string PassName = "InferTypes";

    public override bool Run(PassContext context)
    {
        if (context.Pkg == null || context.File == null)
        {
            return false;
        }

        ResolveStmts(context, context.File.Hir.Body);

        if (context.File.Hir.Return != null)
        {
            ResolveStmt(context, context.File.Hir.Return);
        }

        return true;
    }

    private void ResolveStmts(PassContext pc, List<Stmt> stmts)
    {
        for (var i = 0; i < stmts.Count; i++)
        {
            if (IsTerminator(stmts[i]) && i < stmts.Count - 1)
            {
                pc.Diag.Report(stmts[i + 1].Span, DiagnosticCode.WrnUnreachableCode);
                break;
            }
        }

        foreach (var stmt in stmts)
        {
            ResolveStmt(pc, stmt);
        }
    }

    private void ResolveStmt(PassContext pc, Stmt stmt)
    {
        switch (stmt)
        {
            case Decl decl:
                ResolveDecl(pc, decl);
                break;
            case AssignStmt assignStmt:
                ResolveAssignStmt(pc, assignStmt);
                break;
            case ExprStmt exprStmt:
                SynthesizeExpr(pc, exprStmt.Expression);
                break;
            case LabelStmt:
            case BreakStmt:
            case GotoStmt:
                break;
            case DoBlockStmt doBlockStmt:
                ResolveStmts(pc, doBlockStmt.Body);
                break;
            case WhileStmt whileStmt:
                SynthesizeExpr(pc, whileStmt.Condition);
                ResolveStmts(pc, whileStmt.Body);
                break;
            case RepeatStmt repeatStmt:
                ResolveStmts(pc, repeatStmt.Body);
                SynthesizeExpr(pc, repeatStmt.Condition);
                break;
            case IfStmt ifStmt:
            {
                var tCond = SynthesizeExpr(pc, ifStmt.Condition);
                EnsureBoolLike(pc, ifStmt.Condition.Span, tCond);
                ResolveStmts(pc, ifStmt.Body);
                foreach (var elseIf in ifStmt.ElseIfs)
                {
                    var tEC = SynthesizeExpr(pc, elseIf.Condition);
                    EnsureBoolLike(pc, elseIf.Condition.Span, tEC);
                    ResolveStmts(pc, elseIf.Body);
                }
                if (ifStmt.ElseBody != null)
                {
                    ResolveStmts(pc, ifStmt.ElseBody);
                }
                break;
            }
            case NumericForStmt nf:
            {
                var ts = SynthesizeExpr(pc, nf.Start);
                EnsureAssignable(pc, nf.Start.Span, pc.Types.PrimNumber.ID, ts);
                var tl = SynthesizeExpr(pc, nf.Limit);
                EnsureAssignable(pc, nf.Limit.Span, pc.Types.PrimNumber.ID, tl);
                if (nf.Step != null)
                {
                    var tStep = SynthesizeExpr(pc, nf.Step);
                    EnsureAssignable(pc, nf.Step.Span, pc.Types.PrimNumber.ID, tStep);
                }
                pc.Pkg!.Syms.SetType(nf.VarName.Sym, pc.Types.PrimNumber.ID);
                ResolveStmts(pc, nf.Body);
                break;
            }
            case GenericForStmt gf:
            {
                foreach (var iter in gf.Iterators)
                {
                    SynthesizeExpr(pc, iter);
                }
                foreach (var vn in gf.VarNames)
                {
                    pc.Pkg!.Syms.SetType(vn.Sym, pc.Types.PrimAny.ID);
                }
                ResolveStmts(pc, gf.Body);
                break;
            }
            case ReturnStmt returnStmt:
                foreach (var value in returnStmt.Values)
                {
                    SynthesizeExpr(pc, value);
                }
                break;
            case ImportStmt:
                break;
            case ExportStmt exportStmt:
                ResolveDecl(pc, exportStmt.Declaration);
                break;
            default:
                throw new InvalidOperationException($"Unknown statement kind: {stmt.GetType().Name}");
        }
    }

    private void ResolveDecl(PassContext pc, Decl decl)
    {
        switch (decl)
        {
            case FunctionDecl fd:
                ResolveFunctionLike(pc, fd.Parameters, fd.ReturnType, fd.Body, fd.ReturnStmt,
                    fd.NamePath.Count == 1 && fd.MethodName == null ? fd.NamePath[0] : null);
                break;
            case LocalFunctionDecl lfd:
                ResolveFunctionLike(pc, lfd.Parameters, lfd.ReturnType, lfd.Body, lfd.ReturnStmt, lfd.Name);
                break;
            case LocalDecl ld:
                ResolveLocalDecl(pc, ld);
                break;
            case DeclareFunctionDecl dfd:
            {
                var paramTypes = new List<Type>();
                foreach (var param in dfd.Parameters)
                {
                    var t = ResolveParamType(pc, param);
                    paramTypes.Add(t);
                    if (param.Name.Sym != SymID.Invalid)
                    {
                        pc.Pkg!.Syms.SetType(param.Name.Sym, t.ID);
                    }
                }
                var ret = dfd.ReturnType != null && dfd.ReturnType.ResolvedType != TypID.Invalid
                    ? GetType(pc, dfd.ReturnType.ResolvedType)
                    : pc.Types.PrimNil;
                var funcTyp = pc.Types.FuncOf(paramTypes, ret);
                if (dfd.NamePath.Count == 1 && dfd.MethodName == null)
                {
                    pc.Pkg!.Syms.SetType(dfd.NamePath[0].Sym, funcTyp);
                }
                break;
            }
            case DeclareVariableDecl dvd:
            {
                var t = dvd.TypeAnnotation.ResolvedType;
                if (t != TypID.Invalid)
                {
                    pc.Pkg!.Syms.SetType(dvd.Name.Sym, t);
                }
                break;
            }
            case DeclareModuleDecl dmd:
                foreach (var member in dmd.Members)
                {
                    ResolveDecl(pc, member);
                }
                break;
            default:
                throw new InvalidOperationException($"Unknown declaration kind: {decl.GetType().Name}");
        }
    }

    private void ResolveFunctionLike(PassContext pc, List<Parameter> parameters, TypeRef? returnTypeRef,
        List<Stmt> body, ReturnStmt? returnStmt, NameRef? funcName)
    {
        var paramTypes = new List<Type>();
        foreach (var param in parameters)
        {
            var t = ResolveParamType(pc, param);
            paramTypes.Add(t);
            if (param.Name.Sym != SymID.Invalid)
            {
                pc.Pkg!.Syms.SetType(param.Name.Sym, t.ID);
            }
        }

        ResolveStmts(pc, body);
        if (returnStmt != null)
        {
            ResolveStmt(pc, returnStmt);
        }

        Type returnType;
        if (returnTypeRef != null && returnTypeRef.ResolvedType != TypID.Invalid)
        {
            returnType = GetType(pc, returnTypeRef.ResolvedType);
            var collected = CollectReturnTypes(pc, body);
            if (returnStmt != null)
            {
                collected.Add((ComputeReturnType(pc, returnStmt.Values), returnStmt.Span));
            }
            foreach (var (typ, span) in collected)
            {
                EnsureAssignable(pc, span, returnType.ID, typ);
            }
        }
        else
        {
            var collected = CollectReturnTypes(pc, body);
            if (returnStmt != null)
            {
                collected.Add((ComputeReturnType(pc, returnStmt.Values), returnStmt.Span));
            }

            if (collected.Count == 0)
            {
                returnType = pc.Types.PrimNil;
            }
            else
            {
                var baseType = collected[0].typ;
                for (var i = 1; i < collected.Count; i++)
                {
                    var rt = collected[i].typ;
                    if (rt == baseType) continue;
                    if (IsTypeAssignable(pc, baseType, rt)) continue;
                    if (IsTypeAssignable(pc, rt, baseType))
                    {
                        baseType = rt;
                    }
                    else
                    {
                        baseType = pc.Types.PrimAny.ID;
                        break;
                    }
                }
                returnType = GetType(pc, baseType);
            }
        }

        var funcTyp = pc.Types.FuncOf(paramTypes, returnType);
        if (funcName != null && funcName.Sym != SymID.Invalid)
        {
            pc.Pkg!.Syms.SetType(funcName.Sym, funcTyp);
        }
    }

    private void ResolveLocalDecl(PassContext pc, LocalDecl ld)
    {
        var valueTypes = new List<TypID>();
        foreach (var value in ld.Values)
        {
            valueTypes.Add(SynthesizeExpr(pc, value));
        }

        for (var i = 0; i < ld.Variables.Count; i++)
        {
            var variable = ld.Variables[i];
            var annotated = variable.TypeAnnotation?.ResolvedType ?? TypID.Invalid;
            var inferred = i < valueTypes.Count ? valueTypes[i] : pc.Types.PrimNil.ID;

            TypID finalType;
            if (annotated != TypID.Invalid)
            {
                if (i < valueTypes.Count && !IsTypeAssignable(pc, annotated, inferred))
                {
                    pc.Diag.Report(variable.Span, DiagnosticCode.ErrTypeMismatch,
                        TypeName(pc, annotated), TypeName(pc, inferred));
                }
                finalType = annotated;
            }
            else
            {
                finalType = inferred != TypID.Invalid ? inferred : pc.Types.PrimAny.ID;
            }

            if (variable.Name.Sym != SymID.Invalid)
            {
                pc.Pkg!.Syms.SetType(variable.Name.Sym, finalType);
            }
        }
    }

    private void ResolveAssignStmt(PassContext pc, AssignStmt stmt)
    {
        var valueTypes = new List<TypID>();
        foreach (var value in stmt.Values)
        {
            valueTypes.Add(SynthesizeExpr(pc, value));
        }

        for (var i = 0; i < stmt.Targets.Count; i++)
        {
            var target = stmt.Targets[i];
            var targetType = SynthesizeExpr(pc, target);
            if (i < valueTypes.Count && targetType != TypID.Invalid && targetType != pc.Types.PrimAny.ID)
            {
                if (!IsTypeAssignable(pc, targetType, valueTypes[i]))
                {
                    pc.Diag.Report(target.Span, DiagnosticCode.ErrTypeMismatch,
                        TypeName(pc, targetType), TypeName(pc, valueTypes[i]));
                }
            }
        }
    }

    private Type ResolveParamType(PassContext pc, Parameter param)
    {
        if (param.IsVararg)
        {
            return pc.Types.PrimAny;
        }
        if (param.TypeAnnotation != null && param.TypeAnnotation.ResolvedType != TypID.Invalid)
        {
            return GetType(pc, param.TypeAnnotation.ResolvedType);
        }
        return pc.Types.PrimAny;
    }

    private TypID SynthesizeExpr(PassContext pc, Expr expr)
    {
        var tt = pc.Types;
        TypID result;

        switch (expr)
        {
            case NilLiteralExpr:
                result = tt.PrimNil.ID;
                break;
            case BoolLiteralExpr:
                result = tt.PrimBool.ID;
                break;
            case NumberLiteralExpr:
                result = tt.PrimNumber.ID;
                break;
            case StringLiteralExpr:
                result = tt.PrimString.ID;
                break;
            case VarargExpr:
                result = tt.PrimAny.ID;
                break;
            case NameExpr nameExpr:
                result = LookupSymbolType(pc, nameExpr.Name.Sym);
                break;
            case ParenExpr paren:
                result = SynthesizeExpr(pc, paren.Inner);
                break;
            case BinaryExpr bin:
                result = InferBinary(pc, bin);
                break;
            case UnaryExpr un:
                result = InferUnary(pc, un);
                break;
            case FunctionDefExpr fde:
                result = InferFunctionDef(pc, fde);
                break;
            case DotAccessExpr dot:
                result = InferDotAccess(pc, dot);
                break;
            case IndexAccessExpr idx:
                result = InferIndexAccess(pc, idx);
                break;
            case FunctionCallExpr call:
                result = InferFunctionCall(pc, call);
                break;
            case MethodCallExpr mc:
                result = InferMethodCall(pc, mc);
                break;
            case TableConstructorExpr tc:
                result = InferTableConstructor(pc, tc);
                break;
            default:
                result = tt.PrimAny.ID;
                break;
        }

        expr.Type = result;
        return result;
    }

    private TypID InferBinary(PassContext pc, BinaryExpr bin)
    {
        var tt = pc.Types;
        var l = SynthesizeExpr(pc, bin.Left);
        var r = SynthesizeExpr(pc, bin.Right);

        if (IsConfiguredConcatOp(pc, bin.Op)
            && (l == tt.PrimString.ID || r == tt.PrimString.ID))
        {
            EnsureConcatable(pc, bin.Left.Span, l);
            EnsureConcatable(pc, bin.Right.Span, r);
            return tt.PrimString.ID;
        }

        switch (bin.Op)
        {
            case BinaryOp.Add:
            case BinaryOp.Sub:
            case BinaryOp.Mul:
            case BinaryOp.Div:
            case BinaryOp.FloorDiv:
            case BinaryOp.Mod:
            case BinaryOp.Pow:
                EnsureAssignable(pc, bin.Left.Span, tt.PrimNumber.ID, l);
                EnsureAssignable(pc, bin.Right.Span, tt.PrimNumber.ID, r);
                return tt.PrimNumber.ID;
            case BinaryOp.Concat:
                EnsureConcatable(pc, bin.Left.Span, l);
                EnsureConcatable(pc, bin.Right.Span, r);
                return tt.PrimString.ID;
            case BinaryOp.BitwiseAnd:
            case BinaryOp.BitwiseOr:
            case BinaryOp.BitwiseXor:
            case BinaryOp.LShift:
            case BinaryOp.RShift:
                EnsureAssignable(pc, bin.Left.Span, tt.PrimNumber.ID, l);
                EnsureAssignable(pc, bin.Right.Span, tt.PrimNumber.ID, r);
                return tt.PrimNumber.ID;
            case BinaryOp.Eq:
            case BinaryOp.Neq:
                return tt.PrimBool.ID;
            case BinaryOp.Lt:
            case BinaryOp.Gt:
            case BinaryOp.Lte:
            case BinaryOp.Gte:
                if (!IsNumberLike(pc, l) && !IsStringLike(pc, l))
                {
                    pc.Diag.Report(bin.Left.Span, DiagnosticCode.ErrTypeMismatch, "number or string", TypeName(pc, l));
                }
                if (!IsNumberLike(pc, r) && !IsStringLike(pc, r))
                {
                    pc.Diag.Report(bin.Right.Span, DiagnosticCode.ErrTypeMismatch, "number or string", TypeName(pc, r));
                }
                return tt.PrimBool.ID;
            case BinaryOp.And:
            case BinaryOp.Or:
                if (l == r) return l;
                if (l == tt.PrimAny.ID || r == tt.PrimAny.ID) return tt.PrimAny.ID;
                return pc.Types.UnionOf([GetType(pc, l), GetType(pc, r)]);
            default:
                pc.Diag.Report(bin.Span, DiagnosticCode.ErrInvalidOperator, bin.Op.ToString());
                return tt.PrimAny.ID;
        }
    }

    private TypID InferUnary(PassContext pc, UnaryExpr un)
    {
        var tt = pc.Types;
        var t = SynthesizeExpr(pc, un.Operand);
        switch (un.Op)
        {
            case UnaryOp.Negate:
                EnsureAssignable(pc, un.Operand.Span, tt.PrimNumber.ID, t);
                return tt.PrimNumber.ID;
            case UnaryOp.LogicalNot:
                return tt.PrimBool.ID;
            case UnaryOp.Length:
                if (!IsStringLike(pc, t) && !IsTableLike(pc, t) && t != tt.PrimAny.ID)
                {
                    pc.Diag.Report(un.Operand.Span, DiagnosticCode.ErrTypeMismatch, "string or table", TypeName(pc, t));
                }
                return tt.PrimNumber.ID;
            case UnaryOp.BitwiseNot:
                EnsureAssignable(pc, un.Operand.Span, tt.PrimNumber.ID, t);
                return tt.PrimNumber.ID;
            default:
                return tt.PrimAny.ID;
        }
    }

    private TypID InferFunctionDef(PassContext pc, FunctionDefExpr fde)
    {
        var paramTypes = new List<Type>();
        foreach (var param in fde.Parameters)
        {
            var t = ResolveParamType(pc, param);
            paramTypes.Add(t);
            if (param.Name.Sym != SymID.Invalid)
            {
                pc.Pkg!.Syms.SetType(param.Name.Sym, t.ID);
            }
        }

        ResolveStmts(pc, fde.Body);
        if (fde.ReturnStmt != null)
        {
            ResolveStmt(pc, fde.ReturnStmt);
        }

        Type returnType;
        if (fde.ReturnType != null && fde.ReturnType.ResolvedType != TypID.Invalid)
        {
            returnType = GetType(pc, fde.ReturnType.ResolvedType);
        }
        else
        {
            var collected = CollectReturnTypes(pc, fde.Body);
            if (fde.ReturnStmt != null)
            {
                collected.Add((ComputeReturnType(pc, fde.ReturnStmt.Values), fde.ReturnStmt.Span));
            }

            if (collected.Count == 0)
            {
                returnType = pc.Types.PrimNil;
            }
            else
            {
                var baseType = collected[0].typ;
                for (var i = 1; i < collected.Count; i++)
                {
                    var rt = collected[i].typ;
                    if (rt == baseType) continue;
                    if (IsTypeAssignable(pc, baseType, rt)) continue;
                    if (IsTypeAssignable(pc, rt, baseType))
                    {
                        baseType = rt;
                    }
                    else
                    {
                        baseType = pc.Types.PrimAny.ID;
                        break;
                    }
                }
                returnType = GetType(pc, baseType);
            }
        }

        return pc.Types.FuncOf(paramTypes, returnType);
    }

    private TypID InferDotAccess(PassContext pc, DotAccessExpr dot)
    {
        var baseTyp = SynthesizeExpr(pc, dot.Object);
        if (!pc.Pkg!.Types.GetByID(baseTyp, out var baseType))
        {
            return pc.Types.PrimAny.ID;
        }

        switch (baseType)
        {
            case StructType st:
            {
                var field = st.Fields.FirstOrDefault(f => f.Name.Name == dot.FieldName.Name);
                if (field == null)
                {
                    pc.Diag.Report(dot.FieldName.Span, DiagnosticCode.ErrTypeNotIndexable,
                        $"{baseType.Key} has no field '{dot.FieldName.Name}'");
                    return pc.Types.PrimAny.ID;
                }
                return field.Type.ID;
            }
            case TableMapType mt:
                return mt.ValueType.ID;
            case { Kind: TypeKind.PrimitiveAny }:
                return pc.Types.PrimAny.ID;
            default:
                return pc.Types.PrimAny.ID;
        }
    }

    private TypID InferIndexAccess(PassContext pc, IndexAccessExpr idx)
    {
        var baseTyp = SynthesizeExpr(pc, idx.Object);
        var indexTyp = SynthesizeExpr(pc, idx.Index);
        if (!pc.Pkg!.Types.GetByID(baseTyp, out var baseType))
        {
            return pc.Types.PrimAny.ID;
        }

        switch (baseType)
        {
            case TableArrayType at:
                if (!IsTypeAssignable(pc, pc.Types.PrimNumber.ID, indexTyp) && indexTyp != pc.Types.PrimAny.ID)
                {
                    pc.Diag.Report(idx.Index.Span, DiagnosticCode.ErrTypeMismatch, "number", TypeName(pc, indexTyp));
                }
                return at.ElementType.ID;
            case TableMapType mt:
                if (!IsTypeAssignable(pc, mt.KeyType.ID, indexTyp) && indexTyp != pc.Types.PrimAny.ID)
                {
                    pc.Diag.Report(idx.Index.Span, DiagnosticCode.ErrTypeMismatch,
                        TypeName(pc, mt.KeyType.ID), TypeName(pc, indexTyp));
                }
                return mt.ValueType.ID;
            case not null when baseType.Kind == TypeKind.PrimitiveAny:
                return pc.Types.PrimAny.ID;
            case not null when baseType.Kind == TypeKind.PrimitiveString:
                return pc.Types.PrimString.ID;
            default:
                pc.Diag.Report(idx.Object.Span, DiagnosticCode.ErrTypeNotIndexable, TypeName(pc, baseTyp));
                return pc.Types.PrimAny.ID;
        }
    }

    private TypID InferFunctionCall(PassContext pc, FunctionCallExpr call)
    {
        var argTypes = new List<TypID>();
        foreach (var arg in call.Arguments)
        {
            argTypes.Add(SynthesizeExpr(pc, arg));
        }

        var calleeTyp = SynthesizeExpr(pc, call.Callee);
        if (!pc.Pkg!.Types.GetByID(calleeTyp, out var calleeType))
        {
            return pc.Types.PrimAny.ID;
        }

        if (calleeType is not FunctionType fnType)
        {
            if (calleeType.Kind == TypeKind.PrimitiveAny)
            {
                return pc.Types.PrimAny.ID;
            }
            pc.Diag.Report(call.Callee.Span, DiagnosticCode.ErrTypeMismatch, "function", TypeName(pc, calleeTyp));
            return pc.Types.PrimAny.ID;
        }

        CheckCallArguments(pc, call.Span, fnType, argTypes);
        return fnType.ReturnType.ID;
    }

    private TypID InferMethodCall(PassContext pc, MethodCallExpr mc)
    {
        var objTyp = SynthesizeExpr(pc, mc.Object);
        var argTypes = new List<TypID>();
        foreach (var arg in mc.Arguments)
        {
            argTypes.Add(SynthesizeExpr(pc, arg));
        }

        if (!pc.Pkg!.Types.GetByID(objTyp, out var objType))
        {
            return pc.Types.PrimAny.ID;
        }

        if (objType is StructType st)
        {
            var field = st.Fields.FirstOrDefault(f => f.Name.Name == mc.MethodName.Name);
            if (field?.Type is FunctionType fnType)
            {
                return fnType.ReturnType.ID;
            }
        }

        return pc.Types.PrimAny.ID;
    }

    private TypID InferTableConstructor(PassContext pc, TableConstructorExpr tc)
    {
        var tt = pc.Types;

        if (tc.Fields.Count == 0)
        {
            return tt.ArrayOf(tt.PrimAny);
        }

        var allPositional = tc.Fields.All(f => f.Kind == TableFieldKind.Positional);
        var allNamed = tc.Fields.All(f => f.Kind == TableFieldKind.Named);

        if (allPositional)
        {
            var elemType = SynthesizeExpr(pc, tc.Fields[0].Value);
            for (var i = 1; i < tc.Fields.Count; i++)
            {
                var et = SynthesizeExpr(pc, tc.Fields[i].Value);
                if (!IsTypeAssignable(pc, elemType, et) && !IsTypeAssignable(pc, et, elemType))
                {
                    elemType = tt.PrimAny.ID;
                }
            }
            return tt.ArrayOf(GetType(pc, elemType));
        }

        if (allNamed)
        {
            var fields = new List<StructType.Field>();
            foreach (var field in tc.Fields)
            {
                var fValType = SynthesizeExpr(pc, field.Value);
                fields.Add(new StructType.Field(field.Name!, GetType(pc, fValType)));
            }
            return tt.StructOf(fields);
        }

        var keyType = tt.PrimAny.ID;
        var valueType = TypID.Invalid;
        foreach (var field in tc.Fields)
        {
            TypID kt;
            if (field.Key != null)
            {
                kt = SynthesizeExpr(pc, field.Key);
            }
            else if (field.Name != null)
            {
                kt = tt.PrimString.ID;
            }
            else
            {
                kt = tt.PrimNumber.ID;
            }

            var vt = SynthesizeExpr(pc, field.Value);
            if (valueType == TypID.Invalid)
            {
                keyType = kt;
                valueType = vt;
            }
            else
            {
                if (!IsTypeAssignable(pc, keyType, kt) && !IsTypeAssignable(pc, kt, keyType))
                {
                    keyType = tt.PrimAny.ID;
                }
                if (!IsTypeAssignable(pc, valueType, vt) && !IsTypeAssignable(pc, vt, valueType))
                {
                    valueType = tt.PrimAny.ID;
                }
            }
        }

        return tt.MapOf(GetType(pc, keyType), GetType(pc, valueType));
    }

    private void CheckCallArguments(PassContext pc, TextSpan span, FunctionType fnType, List<TypID> argTypes)
    {
        var paramCount = fnType.ParamTypes.Count;
        var argCount = argTypes.Count;

        if (argCount > paramCount)
        {
            var lastParam = paramCount > 0 ? fnType.ParamTypes[paramCount - 1] : null;
            if (lastParam is not { Kind: TypeKind.PrimitiveAny })
            {
                pc.Diag.Report(span, DiagnosticCode.ErrFuncParamMismatch, paramCount, argCount);
                return;
            }
        }

        for (var i = 0; i < paramCount; i++)
        {
            if (i >= argCount) break;
            var paramType = fnType.ParamTypes[i].ID;
            var argType = argTypes[i];
            if (!IsTypeAssignable(pc, paramType, argType))
            {
                pc.Diag.Report(span, DiagnosticCode.ErrTypeMismatch,
                    TypeName(pc, paramType), TypeName(pc, argType));
            }
        }
    }

    private List<(TypID typ, TextSpan span)> CollectReturnTypes(PassContext pc, List<Stmt> stmts)
    {
        var result = new List<(TypID, TextSpan)>();
        foreach (var stmt in stmts)
        {
            CollectReturnTypesStmt(pc, stmt, result);
        }
        return result;
    }

    private void CollectReturnTypesStmt(PassContext pc, Stmt stmt, List<(TypID, TextSpan)> result)
    {
        switch (stmt)
        {
            case ReturnStmt rs:
                result.Add((ComputeReturnType(pc, rs.Values), rs.Span));
                break;
            case DoBlockStmt db:
                foreach (var s in db.Body) CollectReturnTypesStmt(pc, s, result);
                break;
            case IfStmt ifs:
                foreach (var s in ifs.Body) CollectReturnTypesStmt(pc, s, result);
                foreach (var e in ifs.ElseIfs)
                {
                    foreach (var s in e.Body) CollectReturnTypesStmt(pc, s, result);
                }
                if (ifs.ElseBody != null)
                {
                    foreach (var s in ifs.ElseBody) CollectReturnTypesStmt(pc, s, result);
                }
                break;
            case WhileStmt ws:
                foreach (var s in ws.Body) CollectReturnTypesStmt(pc, s, result);
                break;
            case RepeatStmt rp:
                foreach (var s in rp.Body) CollectReturnTypesStmt(pc, s, result);
                break;
            case NumericForStmt nf:
                foreach (var s in nf.Body) CollectReturnTypesStmt(pc, s, result);
                break;
            case GenericForStmt gf:
                foreach (var s in gf.Body) CollectReturnTypesStmt(pc, s, result);
                break;
        }
    }

    private TypID ComputeReturnType(PassContext pc, List<Expr> values)
    {
        if (values.Count == 0)
        {
            return pc.Types.PrimNil.ID;
        }
        if (values.Count == 1)
        {
            return values[0].Type != TypID.Invalid ? values[0].Type : SynthesizeExpr(pc, values[0]);
        }
        var fields = new List<TupleType.Field>();
        foreach (var v in values)
        {
            var t = v.Type != TypID.Invalid ? v.Type : SynthesizeExpr(pc, v);
            fields.Add(new TupleType.Field(GetType(pc, t)));
        }
        return pc.Types.TupleOf(fields);
    }

    private bool IsTerminator(Stmt stmt)
    {
        return stmt is ReturnStmt or BreakStmt or GotoStmt;
    }

    private bool IsTypeAssignable(PassContext pc, TypID dst, TypID src)
    {
        if (dst == src) return true;
        if (dst == TypID.Invalid || src == TypID.Invalid) return false;
        var tt = pc.Types;
        if (dst == tt.PrimAny.ID || src == tt.PrimAny.ID) return true;

        if (pc.Pkg!.Types.GetByID(dst, out var dstType) && dstType is UnionType unionDst)
        {
            foreach (var member in unionDst.Types)
            {
                if (IsTypeAssignable(pc, member.ID, src)) return true;
            }
            return false;
        }

        if (pc.Pkg.Types.GetByID(src, out var srcType) && srcType is UnionType unionSrc)
        {
            foreach (var member in unionSrc.Types)
            {
                if (!IsTypeAssignable(pc, dst, member.ID)) return false;
            }
            return true;
        }

        return StructEqual(pc, dst, src);
    }

    private bool StructEqual(PassContext pc, TypID a, TypID b)
    {
        if (a == b) return true;
        if (!pc.Pkg!.Types.GetByID(a, out var ta)) return false;
        if (!pc.Pkg.Types.GetByID(b, out var tb)) return false;
        if (ta.Kind != tb.Kind) return false;

        switch (ta)
        {
            case TableArrayType aa when tb is TableArrayType ba:
                return aa.ElementType.ID == ba.ElementType.ID;
            case TableMapType am when tb is TableMapType bm:
                return am.KeyType.ID == bm.KeyType.ID && am.ValueType.ID == bm.ValueType.ID;
            case TupleType at when tb is TupleType bt:
                if (at.Fields.Count != bt.Fields.Count) return false;
                for (var i = 0; i < at.Fields.Count; i++)
                {
                    if (at.Fields[i].Type.ID != bt.Fields[i].Type.ID) return false;
                }
                return true;
            case FunctionType af when tb is FunctionType bf:
                if (af.ParamTypes.Count != bf.ParamTypes.Count) return false;
                for (var i = 0; i < af.ParamTypes.Count; i++)
                {
                    if (af.ParamTypes[i].ID != bf.ParamTypes[i].ID) return false;
                }
                return af.ReturnType.ID == bf.ReturnType.ID;
            case StructType sa when tb is StructType sb:
                if (sa.Fields.Count != sb.Fields.Count) return false;
                foreach (var fa in sa.Fields)
                {
                    var fb = sb.Fields.FirstOrDefault(f => f.Name.Name == fa.Name.Name);
                    if (fb == null || fb.Type.ID != fa.Type.ID) return false;
                }
                return true;
            default:
                return false;
        }
    }

    private void EnsureAssignable(PassContext pc, TextSpan span, TypID expected, TypID actual)
    {
        if (!IsTypeAssignable(pc, expected, actual))
        {
            pc.Diag.Report(span, DiagnosticCode.ErrTypeMismatch, TypeName(pc, expected), TypeName(pc, actual));
        }
    }

    private void EnsureBoolLike(PassContext pc, TextSpan span, TypID t)
    {
        if (t == pc.Types.PrimAny.ID || t == pc.Types.PrimBool.ID) return;
        pc.Diag.Report(span, DiagnosticCode.ErrTypeMismatch, "boolean", TypeName(pc, t));
    }

    private void EnsureConcatable(PassContext pc, TextSpan span, TypID t)
    {
        var tt = pc.Types;
        if (t == tt.PrimAny.ID || t == tt.PrimString.ID || t == tt.PrimNumber.ID) return;
        pc.Diag.Report(span, DiagnosticCode.ErrTypeMismatch, "string or number", TypeName(pc, t));
    }

    private bool IsConfiguredConcatOp(PassContext pc, BinaryOp op)
    {
        var configured = pc.Config.Code.ConcatOperator;
        if (string.IsNullOrEmpty(configured)) return false;
        var mapped = configured switch
        {
            "+" => BinaryOp.Add,
            "-" => BinaryOp.Sub,
            "*" => BinaryOp.Mul,
            "/" => BinaryOp.Div,
            "//" => BinaryOp.FloorDiv,
            "%" => BinaryOp.Mod,
            "^" => BinaryOp.Pow,
            ".." => BinaryOp.Concat,
            _ => (BinaryOp?)null
        };
        return mapped == op;
    }

    private bool IsNumberLike(PassContext pc, TypID t) =>
        t == pc.Types.PrimNumber.ID || t == pc.Types.PrimAny.ID;

    private bool IsStringLike(PassContext pc, TypID t) =>
        t == pc.Types.PrimString.ID || t == pc.Types.PrimAny.ID;

    private bool IsTableLike(PassContext pc, TypID t)
    {
        if (!pc.Pkg!.Types.GetByID(t, out var type)) return false;
        return type.Kind is TypeKind.TableArray or TypeKind.TableMap or TypeKind.Struct;
    }

    private TypID LookupSymbolType(PassContext pc, SymID sym)
    {
        if (sym == SymID.Invalid) return pc.Types.PrimAny.ID;
        if (!pc.Pkg!.Syms.GetByID(sym, out var symbol)) return pc.Types.PrimAny.ID;
        return symbol.Type != TypID.Invalid ? symbol.Type : pc.Types.PrimAny.ID;
    }

    private Type GetType(PassContext pc, TypID id)
    {
        if (id == TypID.Invalid) return pc.Types.PrimAny;
        return pc.Pkg!.Types.GetByID(id, out var t) ? t : pc.Types.PrimAny;
    }

    private string TypeName(PassContext pc, TypID id)
    {
        if (id == TypID.Invalid) return "<invalid>";
        return pc.Pkg!.Types.GetByID(id, out var t) ? t.Key.Value : "<unknown>";
    }
}
