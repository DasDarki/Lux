using Lux.Diagnostics;
using Lux.IR;

namespace Lux.Compiler.Passes;

/// <summary>
/// The resolve names pass is responsible for resolving all names in the source code. All usages of a name get
/// transformed and resolved through symbol IDs for later simple renaming/mangling. It also handles scope resolution
/// for symbols and checks if the usages are valid.
/// </summary>
public sealed class ResolveNamesPass() : Pass(PassName, PassScope.PerFile)
{
    public const string PassName = "ResolveNames";

    public override bool Run(PassContext context)
    {
        if (context.Pkg == null || context.File == null)
        {
            return false;
        }

        ResolveStmtListNames(context, context.File.Hir.Body, context.Pkg);

        if (context.File.Hir.Return != null)
        {
            ResolveStmtNames(context, context.File.Hir.Return, context.Pkg);
        }

        return true;
    }

    private void ResolveStmtListNames(PassContext pc, List<Stmt> stmts, PackageContext pkg)
    {
        foreach (var stmt in stmts)
        {
            ResolveStmtNames(pc, stmt, pkg);
        }
    }

    private void ResolveStmtNames(PassContext pc, Stmt stmt, PackageContext pkg)
    {
        switch (stmt)
        {
            case Decl decl:
                ResolveDeclNames(pc, decl, pkg);
                break;
            case AssignStmt assignStmt:
                foreach (var target in assignStmt.Targets)
                {
                    ResolveExprNames(pc, target, pkg);
                }

                foreach (var value in assignStmt.Values)
                {
                    ResolveExprNames(pc, value, pkg);
                }

                break;
            case ExprStmt exprStmt:
                ResolveExprNames(pc, exprStmt.Expression, pkg);
                break;
            case LabelStmt labelStmt:
            {
                pkg.Scopes.EnclosingScope(labelStmt.ID, out var scope);
                ResolveNameRef(pc, labelStmt.Name, scope, pkg);
            }
                break;
            case BreakStmt:
                break;
            case GotoStmt gotoStmt:
            {
                pkg.Scopes.EnclosingScope(gotoStmt.ID, out var scope);
                ResolveNameRef(pc, gotoStmt.LabelName, scope, pkg);
            }
                break;
            case DoBlockStmt doBlockStmt:
                ResolveStmtListNames(pc, doBlockStmt.Body, pkg);
                break;
            case WhileStmt whileStmt:
                ResolveExprNames(pc, whileStmt.Condition, pkg);
                ResolveStmtListNames(pc, whileStmt.Body, pkg);
                break;
            case RepeatStmt repeatStmt:
                ResolveStmtListNames(pc, repeatStmt.Body, pkg);
                ResolveExprNames(pc, repeatStmt.Condition, pkg);
                break;
            case IfStmt ifStmt:
                ResolveExprNames(pc, ifStmt.Condition, pkg);
                ResolveStmtListNames(pc, ifStmt.Body, pkg);
                foreach (var elseIf in ifStmt.ElseIfs)
                {
                    ResolveExprNames(pc, elseIf.Condition, pkg);
                    ResolveStmtListNames(pc, elseIf.Body, pkg);
                }

                if (ifStmt.ElseBody != null)
                {
                    ResolveStmtListNames(pc, ifStmt.ElseBody, pkg);
                }
                break;
            case NumericForStmt numericForStmt:
            {
                pkg.Scopes.EnclosingScope(numericForStmt.ID, out var scope);
                ResolveNameRef(pc, numericForStmt.VarName, scope, pkg);

                ResolveExprNames(pc, numericForStmt.Start, pkg);
                ResolveExprNames(pc, numericForStmt.Limit, pkg);
                if (numericForStmt.Step != null)
                {
                    ResolveExprNames(pc, numericForStmt.Step, pkg);
                }

                ResolveStmtListNames(pc, numericForStmt.Body, pkg);
            }
                break;
            case GenericForStmt genericForStmt:
            {
                pkg.Scopes.EnclosingScope(genericForStmt.ID, out var scope);
                foreach (var varName in genericForStmt.VarNames)
                {
                    ResolveNameRef(pc, varName, scope, pkg);
                }

                foreach (var iterator in genericForStmt.Iterators)
                {
                    ResolveExprNames(pc, iterator, pkg);
                }
                
                ResolveStmtListNames(pc, genericForStmt.Body, pkg);
            }
                break;
            case ReturnStmt returnStmt:
                foreach (var value in returnStmt.Values)
                {
                    ResolveExprNames(pc, value, pkg);
                }

                break;
            case ImportStmt importStmt:
            {
                pkg.Scopes.EnclosingScope(importStmt.ID, out var specifierScope);
                
                foreach (var specifier in importStmt.Specifiers)
                {
                    ResolveNameRef(pc, specifier.Name, specifierScope, pkg);
                    
                    if (specifier.Alias != null)
                    {
                        ResolveNameRef(pc, specifier.Alias, specifierScope, pkg);
                    }
                }
                
                pkg.Scopes.EnclosingScope(importStmt.ID, out var scope);
                ResolveNameRef(pc, importStmt.Module, scope, pkg);

                if (importStmt.Alias != null)
                {
                    ResolveNameRef(pc, importStmt.Alias, scope, pkg);
                }
            }
                break;
            case ExportStmt exportStmt:
                ResolveDeclNames(pc, exportStmt.Declaration, pkg);
                break;
            default:
                throw new InvalidOperationException($"Unexpected statement type: {stmt.GetType().Name}");
        }
    }

    private void ResolveDeclNames(PassContext pc, Decl decl, PackageContext pkg)
    {
        switch (decl)
        {
            case FunctionDecl functionDecl:
            {
                pkg.Scopes.EnclosingScope(functionDecl.ID, out var scope);
                if (functionDecl.NamePath.Count > 0)
                {
                    ResolveNameRefForDecl(pc, functionDecl.NamePath[0], scope, pkg, functionDecl.ID);
                }

                foreach (var p in functionDecl.Parameters)
                {
                    pkg.Scopes.EnclosingScope(p.ID, out var paramScope);
                    ResolveNameRef(pc, p.Name, paramScope, pkg);
                    if (p.DefaultValue != null)
                        ResolveExprNames(pc, p.DefaultValue, pkg);
                }

                ResolveStmtListNames(pc, functionDecl.Body, pkg);

                if (functionDecl.ReturnStmt != null)
                {
                    ResolveStmtNames(pc, functionDecl.ReturnStmt, pkg);
                }

                break;
            }
            case LocalFunctionDecl localFunctionDecl:
            {
                pkg.Scopes.EnclosingScope(localFunctionDecl.ID, out var scope);
                ResolveNameRef(pc, localFunctionDecl.Name, scope, pkg);

                foreach (var p in localFunctionDecl.Parameters)
                {
                    pkg.Scopes.EnclosingScope(p.ID, out var paramScope);
                    ResolveNameRef(pc, p.Name, paramScope, pkg);
                    if (p.DefaultValue != null)
                        ResolveExprNames(pc, p.DefaultValue, pkg);
                }

                ResolveStmtListNames(pc, localFunctionDecl.Body, pkg);

                if (localFunctionDecl.ReturnStmt != null)
                {
                    ResolveStmtNames(pc, localFunctionDecl.ReturnStmt, pkg);
                }

                break;
            }
            case LocalDecl localDecl:
            {
                pkg.Scopes.EnclosingScope(localDecl.ID, out var scope);
                
                foreach (var variable in localDecl.Variables)
                {
                    ResolveNameRef(pc, variable.Name, scope, pkg);
                }
                
                foreach (var value in localDecl.Values)
                {
                    ResolveExprNames(pc, value, pkg);
                }
                break;
            }
            case DeclareFunctionDecl declareFunctionDecl:
            {
                pkg.Scopes.EnclosingScope(declareFunctionDecl.ID, out var scope);
                if (declareFunctionDecl.NamePath.Count > 0)
                {
                    ResolveNameRef(pc, declareFunctionDecl.NamePath[0], scope, pkg);
                }

                foreach (var p in declareFunctionDecl.Parameters)
                {
                    pkg.Scopes.EnclosingScope(p.ID, out var paramScope);
                    ResolveNameRef(pc, p.Name, paramScope, pkg);
                }

                break;
            }
            case DeclareVariableDecl declareVariableDecl:
            {
                pkg.Scopes.EnclosingScope(declareVariableDecl.ID, out var scope);
                ResolveNameRef(pc, declareVariableDecl.Name, scope, pkg);
                break;
            }
            case DeclareModuleDecl declareModuleDecl:
            {
                pkg.Scopes.EnclosingScope(declareModuleDecl.ID, out var scope);
                ResolveNameRef(pc, declareModuleDecl.ModuleName, scope, pkg);

                foreach (var member in declareModuleDecl.Members)
                {
                    ResolveDeclNames(pc, member, pkg);
                }

                break;
            }
            case EnumDecl enumDecl:
            {
                pkg.Scopes.EnclosingScope(enumDecl.ID, out var scope);
                ResolveNameRef(pc, enumDecl.Name, scope, pkg);
                foreach (var member in enumDecl.Members)
                {
                    if (member.Value != null)
                        ResolveExprNames(pc, member.Value, pkg);
                }
                break;
            }
        }
    }

    private void ResolveExprNames(PassContext pc, Expr expr, PackageContext pkg)
    {
        switch (expr)
        {
            case NilLiteralExpr:
            case BoolLiteralExpr:
            case NumberLiteralExpr:
            case StringLiteralExpr:
            case VarargExpr:
                break;
            case FunctionDefExpr functionDefExpr:
                foreach (var p in functionDefExpr.Parameters)
                {
                    pkg.Scopes.EnclosingScope(p.ID, out var scope);
                    ResolveNameRef(pc, p.Name, scope, pkg);
                    if (p.DefaultValue != null)
                        ResolveExprNames(pc, p.DefaultValue, pkg);
                }

                ResolveStmtListNames(pc, functionDefExpr.Body, pkg);

                if (functionDefExpr.ReturnStmt != null)
                {
                    ResolveStmtNames(pc, functionDefExpr.ReturnStmt, pkg);
                }

                break;
            case BinaryExpr binaryExpr:
                ResolveExprNames(pc, binaryExpr.Left, pkg);
                ResolveExprNames(pc, binaryExpr.Right, pkg);
                break;
            case UnaryExpr unaryExpr:
                ResolveExprNames(pc, unaryExpr.Operand, pkg);
                break;
            case NameExpr nameExpr:
            {
                pkg.Scopes.EnclosingScope(nameExpr.ID, out var scope);
                ResolveNameRef(pc, nameExpr.Name, scope, pkg);
            }
                break;
            case ParenExpr parenExpr:
                ResolveExprNames(pc, parenExpr.Inner, pkg);
                break;
            case DotAccessExpr dotAccessExpr:
            {
                ResolveExprNames(pc, dotAccessExpr.Object, pkg);
            }
                break;
            case IndexAccessExpr indexAccessExpr:
                ResolveExprNames(pc, indexAccessExpr.Object, pkg);
                ResolveExprNames(pc, indexAccessExpr.Index, pkg);
                break;
            case FunctionCallExpr functionCallExpr:
                ResolveExprNames(pc, functionCallExpr.Callee, pkg);
                foreach (var arg in functionCallExpr.Arguments)
                {
                    ResolveExprNames(pc, arg, pkg);
                }

                break;
            case MethodCallExpr methodCallExpr:
            {
                ResolveExprNames(pc, methodCallExpr.Object, pkg);
                foreach (var arg in methodCallExpr.Arguments)
                {
                    ResolveExprNames(pc, arg, pkg);
                }
            }
                break;
            case InterpolatedStringExpr interpolatedStringExpr:
            {
                foreach (var part in interpolatedStringExpr.Parts)
                {
                    if (part is InterpExprPart exprPart)
                    {
                        ResolveExprNames(pc, exprPart.Expression, pkg);
                    }
                }
            }
                break;
            case NonNilAssertExpr nonNilAssert:
                ResolveExprNames(pc, nonNilAssert.Inner, pkg);
                break;
            case IncDecExpr incDec:
                ResolveExprNames(pc, incDec.Target, pkg);
                break;
            case TypeCheckExpr typeCheck:
                ResolveExprNames(pc, typeCheck.Inner, pkg);
                break;
            case TypeCastExpr typeCast:
                ResolveExprNames(pc, typeCast.Inner, pkg);
                break;
            case TableConstructorExpr tableConstructorExpr:
            {
                foreach (var field in tableConstructorExpr.Fields)
                {
                    if (field.Key != null)
                    {
                        ResolveExprNames(pc, field.Key, pkg);
                    }

                    ResolveExprNames(pc, field.Value, pkg);
                }
            }
                break;

            default:
                throw new InvalidOperationException($"Unexpected expression type: {expr.GetType().Name}");
        }
    }

    private void ResolveNameRef(PassContext pc, NameRef nameRef, ScopeID start, PackageContext pkg)
    {
        var all = pkg.Scopes.LookupAll(start, nameRef.Name);
        if (all.Count > 0)
        {
            nameRef.Sym = all[0];
            if (all.Count > 1)
            {
                nameRef.Overloads = all;
            }
        }
        else
        {
            pc.Diag.Report(nameRef.Span, DiagnosticCode.ErrUndeclaredSymbol, nameRef.Name);
        }
    }

    private void ResolveNameRefForDecl(PassContext pc, NameRef nameRef, ScopeID start, PackageContext pkg, NodeID declNode)
    {
        var all = pkg.Scopes.LookupAll(start, nameRef.Name);
        if (all.Count > 0)
        {
            var matched = all.FirstOrDefault(id =>
                pkg.Syms.GetByID(id, out var s) && s.DeclaringNode == declNode);
            nameRef.Sym = matched != SymID.Invalid ? matched : all[0];
            if (all.Count > 1)
            {
                nameRef.Overloads = all;
            }
        }
        else
        {
            pc.Diag.Report(nameRef.Span, DiagnosticCode.ErrUndeclaredSymbol, nameRef.Name);
        }
    }
}