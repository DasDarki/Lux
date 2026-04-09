using Lux.IR;

namespace Lux.Compiler.Passes;

/// <summary>
/// The bind declare pass is responsible for binding the declarations in the source code. It takes care of declaring
/// symbols and binding them with their nodes to their respective scopes.
/// </summary>
public sealed class BindDeclarePass() : Pass(PassName, PassScope.PerFile, dependencies: ResolveLibsPass.PassName)
{
    public const string PassName = "BindDeclare";

    public override bool Run(PassContext context)
    {
        if (context.Pkg == null || context.File == null)
        {
            return false;
        }

        if (!BindStmtListScopes(context, context.File.Hir.Body, context.Pkg.Root))
        {
            return false;
        }

        if (context.File.Hir.Return != null)
        {
            return BindStmtScopes(context, context.File.Hir.Return, context.Pkg.Root);
        }

        return true;
    }

    private bool BindStmtListScopes(PassContext ctx, List<Stmt> stmts, ScopeID scope)
    {
        foreach (var stmt in stmts)
        {
            if (!BindStmtScopes(ctx, stmt, scope))
            {
                return false;
            }
        }

        return true;
    }

    private bool BindStmtScopes(PassContext ctx, Stmt stmt, ScopeID scope)
    {
        if (stmt == null)
        {
            return true;
        }

        var pkg = ctx.Pkg!;
        pkg.Scopes.BindNode(stmt.ID, scope);

        switch (stmt)
        {
            case Decl decl:
                return BindDeclScopes(ctx, decl, scope);
            case AssignStmt assign:
                foreach (var target in assign.Targets)
                {
                    if (!BindExprScopes(ctx, target, scope))
                    {
                        return false;
                    }
                }

                foreach (var value in assign.Values)
                {
                    if (!BindExprScopes(ctx, value, scope))
                    {
                        return false;
                    }
                }

                return true;
            case ExprStmt exprStmt:
                return BindExprScopes(ctx, exprStmt.Expression, scope);
            case LabelStmt:
            case BreakStmt:
            case GotoStmt:
                return true;
            case DoBlockStmt doBlock:
                var doScope = pkg.Scopes.NewScope(scope);
                if (!BindStmtListScopes(ctx, doBlock.Body, doScope))
                {
                    return false;
                }

                return true;
            case WhileStmt whileStmt:
                var whileScope = pkg.Scopes.NewScope(scope);
                if (!BindExprScopes(ctx, whileStmt.Condition, whileScope))
                {
                    return false;
                }

                if (!BindStmtListScopes(ctx, whileStmt.Body, whileScope))
                {
                    return false;
                }

                return true;
            case RepeatStmt repeatStmt:
                var repeatScope = pkg.Scopes.NewScope(scope);
                if (!BindStmtListScopes(ctx, repeatStmt.Body, repeatScope))
                {
                    return false;
                }

                if (!BindExprScopes(ctx, repeatStmt.Condition, repeatScope))
                {
                    return false;
                }

                return true;
            case IfStmt ifStmt:
                var ifScope = pkg.Scopes.NewScope(scope);
                if (!BindExprScopes(ctx, ifStmt.Condition, ifScope))
                {
                    return false;
                }

                if (!BindStmtListScopes(ctx, ifStmt.Body, ifScope))
                {
                    return false;
                }

                foreach (var elseIf in ifStmt.ElseIfs)
                {
                    var elseIfScope = pkg.Scopes.NewScope(scope);
                    if (!BindExprScopes(ctx, elseIf.Condition, elseIfScope))
                    {
                        return false;
                    }

                    if (!BindStmtListScopes(ctx, elseIf.Body, elseIfScope))
                    {
                        return false;
                    }
                }

                if (ifStmt.ElseBody != null)
                {
                    var elseScope = pkg.Scopes.NewScope(scope);
                    if (!BindStmtListScopes(ctx, ifStmt.ElseBody, elseScope))
                    {
                        return false;
                    }
                }

                return true;
            case NumericForStmt numericFor:
                var numericForScope = pkg.Scopes.NewScope(scope);
                pkg.Scopes.BindNode(numericFor.ID, numericForScope);
                DeclareSymbol(ctx, numericForScope, numericFor.VarName.Name, SymbolKind.Variable, numericFor.ID);
                if (!BindExprScopes(ctx, numericFor.Start, numericForScope))
                {
                    return false;
                }

                if (!BindExprScopes(ctx, numericFor.Limit, numericForScope))
                {
                    return false;
                }

                if (numericFor.Step != null)
                {
                    if (!BindExprScopes(ctx, numericFor.Step, numericForScope))
                    {
                        return false;
                    }
                }

                if (!BindStmtListScopes(ctx, numericFor.Body, numericForScope))
                {
                    return false;
                }

                return true;
            case GenericForStmt genericFor:
                var genericForScope = pkg.Scopes.NewScope(scope);
                pkg.Scopes.BindNode(genericFor.ID, genericForScope);
                foreach (var varName in genericFor.VarNames)
                {
                    DeclareSymbol(ctx, genericForScope, varName.Name, SymbolKind.Variable, genericFor.ID);
                }
                foreach (var iterator in genericFor.Iterators)
                {
                    if (!BindExprScopes(ctx, iterator, genericForScope))
                    {
                        return false;
                    }
                }

                if (!BindStmtListScopes(ctx, genericFor.Body, genericForScope))
                {
                    return false;
                }

                return true;
            case ReturnStmt returnStmt:
                foreach (var value in returnStmt.Values)
                {
                    if (!BindExprScopes(ctx, value, scope))
                    {
                        return false;
                    }
                }

                return true;
            case ImportStmt importStmt:
                foreach (var specifier in importStmt.Specifiers)
                {
                    var declName = specifier.Alias ?? specifier.Name;
                    DeclareSymbol(ctx, scope, declName.Name, SymbolKind.Variable, importStmt.ID);
                }

                if (importStmt.Alias != null)
                {
                    DeclareSymbol(ctx, scope, importStmt.Alias.Name, SymbolKind.Variable, importStmt.ID);
                }
                return true;
            case ExportStmt exportStmt:
                return BindDeclScopes(ctx, exportStmt.Declaration, scope);

            default:
                throw new InvalidOperationException($"Unsupported statement type: {stmt.GetType().Name}");
        }
    }

    private bool BindDeclScopes(PassContext ctx, Decl decl, ScopeID scope)
    {
        var pkg = ctx.Pkg!;
        switch (decl)
        {
            case FunctionDecl funcDecl:
            {
                if (funcDecl.NamePath.Count == 1 && funcDecl.MethodName == null)
                {
                    DeclareSymbol(ctx, scope, funcDecl.NamePath[0].Name, SymbolKind.Function, funcDecl.ID);
                }

                var funcScope = pkg.Scopes.NewScope(scope);
                pkg.Scopes.BindNode(funcDecl.ID, funcScope);
                foreach (var param in funcDecl.Parameters)
                {
                    pkg.Scopes.BindNode(param.ID, funcScope);
                    DeclareSymbol(ctx, funcScope, param.Name.Name, SymbolKind.Variable, param.ID);
                    if (param.DefaultValue != null && !BindExprScopes(ctx, param.DefaultValue, funcScope))
                        return false;
                }

                if (funcDecl.ReturnType != null)
                {
                    pkg.Scopes.BindNode(funcDecl.ReturnType.ID, funcScope);
                }

                if (!BindStmtListScopes(ctx, funcDecl.Body, funcScope))
                {
                    return false;
                }

                if (funcDecl.ReturnStmt != null)
                {
                    if (!BindStmtScopes(ctx, funcDecl.ReturnStmt, funcScope))
                    {
                        return false;
                    }
                }

                return true;
            }
            case LocalFunctionDecl localFuncDecl:
            {
                DeclareSymbol(ctx, scope, localFuncDecl.Name.Name, SymbolKind.Function, localFuncDecl.ID);

                var localFuncScope = pkg.Scopes.NewScope(scope);
                pkg.Scopes.BindNode(localFuncDecl.ID, localFuncScope);
                foreach (var param in localFuncDecl.Parameters)
                {
                    pkg.Scopes.BindNode(param.ID, localFuncScope);
                    DeclareSymbol(ctx, localFuncScope, param.Name.Name, SymbolKind.Variable, param.ID);
                    if (param.DefaultValue != null && !BindExprScopes(ctx, param.DefaultValue, localFuncScope))
                        return false;
                }

                if (localFuncDecl.ReturnType != null)
                {
                    pkg.Scopes.BindNode(localFuncDecl.ReturnType.ID, localFuncScope);
                }

                if (!BindStmtListScopes(ctx, localFuncDecl.Body, localFuncScope))
                {
                    return false;
                }

                if (localFuncDecl.ReturnStmt != null)
                {
                    if (!BindStmtScopes(ctx, localFuncDecl.ReturnStmt, localFuncScope))
                    {
                        return false;
                    }
                }

                return true;
            }
            case LocalDecl localDecl:
            {
                pkg.Scopes.BindNode(localDecl.ID, scope);

                foreach (var expr in localDecl.Values)
                {
                    if (!BindExprScopes(ctx, expr, scope))
                    {
                        return false;
                    }
                }

                foreach (var variable in localDecl.Variables)
                {
                    DeclareSymbol(ctx, scope, variable.Name.Name, SymbolKind.Variable, localDecl.ID);
                }

                return true;
            }
            case DeclareFunctionDecl declareFuncDecl:
            {
                if (declareFuncDecl.NamePath.Count == 1 && declareFuncDecl.MethodName == null)
                {
                    DeclareSymbol(ctx, scope, declareFuncDecl.NamePath[0].Name, SymbolKind.Function, declareFuncDecl.ID);
                }

                var declareFuncScope = pkg.Scopes.NewScope(scope);
                pkg.Scopes.BindNode(declareFuncDecl.ID, declareFuncScope);
                foreach (var param in declareFuncDecl.Parameters)
                {
                    pkg.Scopes.BindNode(param.ID, declareFuncScope);
                    DeclareSymbol(ctx, declareFuncScope, param.Name.Name, SymbolKind.Variable, param.ID);
                }

                if (declareFuncDecl.ReturnType != null)
                {
                    pkg.Scopes.BindNode(declareFuncDecl.ReturnType.ID, declareFuncScope);
                }

                return true;
            }
            case DeclareVariableDecl declareVarDecl:
            {
                DeclareSymbol(ctx, scope, declareVarDecl.Name.Name, SymbolKind.Variable, declareVarDecl.ID);
                pkg.Scopes.BindNode(declareVarDecl.ID, scope);
                return true;
            }
            case DeclareModuleDecl declareModuleDecl:
            {
                DeclareSymbol(ctx, scope, declareModuleDecl.ModuleName.Name, SymbolKind.Variable, declareModuleDecl.ID);
                var moduleScope = pkg.Scopes.NewScope(scope);
                pkg.Scopes.BindNode(declareModuleDecl.ID, moduleScope);

                foreach (var member in declareModuleDecl.Members)
                {
                    if (!BindDeclScopes(ctx, member, moduleScope))
                    {
                        return false;
                    }
                }

                return true;
            }
            case EnumDecl enumDecl:
            {
                DeclareSymbol(ctx, scope, enumDecl.Name.Name, SymbolKind.Enum, enumDecl.ID);
                pkg.Scopes.BindNode(enumDecl.ID, scope);
                foreach (var member in enumDecl.Members)
                {
                    if (member.Value != null && !BindExprScopes(ctx, member.Value, scope))
                    {
                        return false;
                    }
                }
                return true;
            }

            default:
                throw new InvalidOperationException($"Unsupported declaration type: {decl.GetType().Name}");
        }
    }

    private bool BindExprScopes(PassContext ctx, Expr expr, ScopeID scope)
    {
        if (expr == null)
        {
            return true;
        }

        var pkg = ctx.Pkg!;
        pkg.Scopes.BindNode(expr.ID, scope);

        switch (expr)
        {
            case NilLiteralExpr:
            case BoolLiteralExpr:
            case NumberLiteralExpr:
            case StringLiteralExpr:
            case VarargExpr:
            case NameExpr:
                return true;
            case FunctionDefExpr funcDef:
                var funcScope = pkg.Scopes.NewScope(scope);
                foreach (var param in funcDef.Parameters)
                {
                    pkg.Scopes.BindNode(param.ID, funcScope);
                    DeclareSymbol(ctx, funcScope, param.Name.Name, SymbolKind.Variable, param.ID);
                    if (param.DefaultValue != null && !BindExprScopes(ctx, param.DefaultValue, funcScope))
                        return false;
                }

                if (funcDef.ReturnType != null)
                {
                    pkg.Scopes.BindNode(funcDef.ReturnType.ID, funcScope);
                }

                foreach (var stmt in funcDef.Body)
                {
                    if (!BindStmtScopes(ctx, stmt, funcScope))
                    {
                        return false;
                    }
                }

                if (funcDef.ReturnStmt != null)
                {
                    if (!BindStmtScopes(ctx, funcDef.ReturnStmt, funcScope))
                    {
                        return false;
                    }
                }

                return true;
            case BinaryExpr binary:
                return BindExprScopes(ctx, binary.Left, scope) && BindExprScopes(ctx, binary.Right, scope);
            case UnaryExpr unary:
                return BindExprScopes(ctx, unary.Operand, scope);
            case ParenExpr paren:
                return BindExprScopes(ctx, paren.Inner, scope);
            case DotAccessExpr dotAccess:
                return BindExprScopes(ctx, dotAccess.Object, scope);
            case IndexAccessExpr indexAccess:
                return BindExprScopes(ctx, indexAccess.Object, scope) && BindExprScopes(ctx, indexAccess.Index, scope);
            case FunctionCallExpr funcCall:
                if (!BindExprScopes(ctx, funcCall.Callee, scope))
                {
                    return false;
                }

                foreach (var arg in funcCall.Arguments)
                {
                    if (!BindExprScopes(ctx, arg, scope))
                    {
                        return false;
                    }
                }

                return true;
            case MethodCallExpr methodCall:
                if (!BindExprScopes(ctx, methodCall.Object, scope))
                {
                    return false;
                }

                foreach (var arg in methodCall.Arguments)
                {
                    if (!BindExprScopes(ctx, arg, scope))
                    {
                        return false;
                    }
                }

                return true;
            case InterpolatedStringExpr interpolatedString:
                foreach (var part in interpolatedString.Parts)
                {
                    if (part is InterpExprPart exprPart)
                    {
                        if (!BindExprScopes(ctx, exprPart.Expression, scope))
                        {
                            return false;
                        }
                    }
                }

                return true;
            case NonNilAssertExpr nonNilAssert:
                return BindExprScopes(ctx, nonNilAssert.Inner, scope);
            case TypeCheckExpr typeCheck:
                pkg.Scopes.BindNode(typeCheck.TargetType.ID, scope);
                return BindExprScopes(ctx, typeCheck.Inner, scope);
            case TypeCastExpr typeCast:
                pkg.Scopes.BindNode(typeCast.TargetType.ID, scope);
                return BindExprScopes(ctx, typeCast.Inner, scope);
            case TableConstructorExpr tableConstructor:
                foreach (var field in tableConstructor.Fields)
                {
                    if (field.Key != null)
                    {
                        if (!BindExprScopes(ctx, field.Key, scope))
                        {
                            return false;
                        }
                    }

                    if (!BindExprScopes(ctx, field.Value, scope))
                    {
                        return false;
                    }
                }

                return true;

            default:
                throw new InvalidOperationException($"Unsupported expression type: {expr.GetType().Name}");
        }
    }

    private static void DeclareSymbol(PassContext ctx, ScopeID scope, string name, SymbolKind kind, NodeID decl)
    {
        var pkg = ctx.Pkg!;
        var symId = pkg.Syms.NewSymbol(kind, name, scope, TypID.Invalid, decl);
        pkg.Scopes.DeclareSymbol(scope, name, symId, pkg.Syms);
    }
}
