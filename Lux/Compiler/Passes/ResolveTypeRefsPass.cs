using Lux.IR;
using Type = Lux.IR.Type;

namespace Lux.Compiler.Passes;

/// <summary>
/// The resolve type refs pass is responsible for resolving type references in the source code. It binds their
/// type references to their respective type IDs.
/// </summary>
public class ResolveTypeRefsPass() : Pass(PassName, PassScope.PerBuild)
{
    public const string PassName = "ResolveTypeRefs";

    private PassContext _ctx = null!;
    private PackageContext _pkg = null!;

    public override bool Run(PassContext context)
    {
        _ctx = context;
        foreach (var pkg in context.Pkgs)
        {
            _pkg = pkg;
            foreach (var f in pkg.Files)
            {
                DeclareEnumTypes(pkg, f.Hir.Body);
                ResolveStmtListTypes(pkg.Types, f.Hir.Body);

                if (f.Hir.Return != null)
                {
                    ResolveStmtTypes(pkg.Types, f.Hir.Return);
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Pre-pass: walk top-level statements and create the EnumType for every EnumDecl so that NamedTypeRefs
    /// referring to enums can be resolved later regardless of declaration order.
    /// </summary>
    private void DeclareEnumTypes(PackageContext pkg, List<Stmt> stmts)
    {
        foreach (var stmt in stmts)
        {
            EnumDecl? enumDecl = stmt switch
            {
                EnumDecl ed => ed,
                ExportStmt es when es.Declaration is EnumDecl ed2 => ed2,
                _ => null
            };
            if (enumDecl == null) continue;
            DeclareEnumType(pkg, enumDecl);
        }
    }

    private void DeclareEnumType(PackageContext pkg, EnumDecl decl)
    {
        if (decl.Name.Sym == SymID.Invalid) return;
        if (!pkg.Syms.GetByID(decl.Name.Sym, out var sym)) return;
        if (sym.Type != TypID.Invalid) return;

        var hasStringValues = decl.Members.Any(m => m.Value is StringLiteralExpr);
        var baseType = hasStringValues ? pkg.Types.PrimString : pkg.Types.PrimNumber;
        var members = new List<EnumType.Member>();
        long autoIndex = 0;
        foreach (var m in decl.Members)
        {
            object? value = m.Value switch
            {
                NumberLiteralExpr nl => nl.Raw,
                StringLiteralExpr sl => sl.Value,
                _ => null
            };
            if (value == null && !hasStringValues)
            {
                value = autoIndex.ToString();
            }
            if (value is string s && long.TryParse(s, out var parsed))
            {
                autoIndex = parsed + 1;
            }
            else
            {
                autoIndex++;
            }
            members.Add(new EnumType.Member(m.Name.Name, value));
        }

        var enumType = pkg.Types.EnumOf(decl.Name.Name, members, baseType);
        pkg.Syms.SetType(decl.Name.Sym, enumType.ID);
    }
    
    private void ResolveStmtListTypes(TypeTable tt, List<Stmt> stmts)
    {
        foreach (var stmt in stmts)
        {
            ResolveStmtTypes(tt, stmt);
        }
    }
    
    private void ResolveStmtTypes(TypeTable tt, Stmt stmt)
    {
        switch (stmt)
        {
            case Decl decl:
                ResolveDeclTypes(tt, decl);
                break;
            case AssignStmt assignStmt:
                foreach (var target in assignStmt.Targets)
                {
                    ResolveExprTypes(tt, target);
                }

                foreach (var value in assignStmt.Values)
                {
                    ResolveExprTypes(tt, value);
                }
                break;
            case ExprStmt exprStmt:
                ResolveExprTypes(tt, exprStmt.Expression);
                break;
            case LabelStmt:
            case BreakStmt:
            case GotoStmt:
                break;
            case DoBlockStmt doBlockStmt:
                ResolveStmtListTypes(tt, doBlockStmt.Body);
                break;
            case WhileStmt whileStmt:
                ResolveExprTypes(tt, whileStmt.Condition);
                ResolveStmtListTypes(tt, whileStmt.Body);
                break;
            case RepeatStmt repeatStmt:
                ResolveStmtListTypes(tt, repeatStmt.Body);
                ResolveExprTypes(tt, repeatStmt.Condition);
                break;
            case IfStmt ifStmt:
                ResolveExprTypes(tt, ifStmt.Condition);
                ResolveStmtListTypes(tt, ifStmt.Body);
                foreach (var elseIf in ifStmt.ElseIfs)
                {
                    ResolveExprTypes(tt, elseIf.Condition);
                    ResolveStmtListTypes(tt, elseIf.Body);
                }
                
                if (ifStmt.ElseBody != null)
                {
                    ResolveStmtListTypes(tt, ifStmt.ElseBody);
                }
                
                break;
            case NumericForStmt numericForStmt:
                ResolveExprTypes(tt, numericForStmt.Start);
                ResolveExprTypes(tt, numericForStmt.Limit);
                if (numericForStmt.Step != null)
                {
                    ResolveExprTypes(tt, numericForStmt.Step);
                }
                ResolveStmtListTypes(tt, numericForStmt.Body);
                break;
            case GenericForStmt genericForStmt:
                foreach (var iterator in genericForStmt.Iterators)
                {
                    ResolveExprTypes(tt, iterator);
                }
                ResolveStmtListTypes(tt, genericForStmt.Body);
                break;
            case ReturnStmt returnStmt:
                foreach (var value in returnStmt.Values)
                {
                    ResolveExprTypes(tt, value);
                }
                break;
            case ImportStmt:
                break;
            case ExportStmt exportStmt:
                ResolveDeclTypes(tt, exportStmt.Declaration);
                break;
            case MatchStmt matchStmt:
                ResolveExprTypes(tt, matchStmt.Scrutinee);
                foreach (var arm in matchStmt.Arms)
                {
                    if (arm.Pattern.ValueExpr != null) ResolveExprTypes(tt, arm.Pattern.ValueExpr);
                    if (arm.Pattern.TypeRef != null) ResolveTypeRef(tt, arm.Pattern.TypeRef);
                    if (arm.Guard != null) ResolveExprTypes(tt, arm.Guard);
                    ResolveStmtListTypes(tt, arm.Body);
                }
                break;
            default:
                throw new InvalidOperationException($"Unknown statement kind: {stmt.GetType().Name}");
        }
    }

    private void ResolveDeclTypes(TypeTable tt, Decl decl)
    {
        switch (decl)
        {
            case FunctionDecl functionDecl:
                foreach (var param in functionDecl.Parameters)
                {
                    if (param.TypeAnnotation != null)
                    {
                        ResolveTypeRef(tt, param.TypeAnnotation);
                    }
                }

                if (functionDecl.ReturnType != null)
                {
                    ResolveTypeRef(tt, functionDecl.ReturnType);
                }
                
                ResolveStmtListTypes(tt, functionDecl.Body);
                
                if (functionDecl.ReturnStmt != null)
                {
                    ResolveStmtTypes(tt, functionDecl.ReturnStmt);
                }
                
                break;
            case LocalFunctionDecl localFunctionDecl:
                foreach (var param in localFunctionDecl.Parameters)
                {
                    if (param.TypeAnnotation != null)
                    {
                        ResolveTypeRef(tt, param.TypeAnnotation);
                    }
                }

                if (localFunctionDecl.ReturnType != null)
                {
                    ResolveTypeRef(tt, localFunctionDecl.ReturnType);
                }
                
                ResolveStmtListTypes(tt, localFunctionDecl.Body);
                
                if (localFunctionDecl.ReturnStmt != null)
                {
                    ResolveStmtTypes(tt, localFunctionDecl.ReturnStmt);
                }
                
                break;
            case LocalDecl localDecl:
                foreach (var variable in localDecl.Variables)
                {
                    if (variable.TypeAnnotation != null)
                    {
                        ResolveTypeRef(tt, variable.TypeAnnotation);
                    }
                }
                
                foreach (var value in localDecl.Values)
                {
                    ResolveExprTypes(tt, value);
                }
                
                break;
            case DeclareFunctionDecl declareFunctionDecl:
                foreach (var param in declareFunctionDecl.Parameters)
                {
                    if (param.TypeAnnotation != null)
                    {
                        ResolveTypeRef(tt, param.TypeAnnotation);
                    }
                }

                if (declareFunctionDecl.ReturnType != null)
                {
                    ResolveTypeRef(tt, declareFunctionDecl.ReturnType);
                }
                
                break;
            case DeclareVariableDecl declareVariableDecl:
                ResolveTypeRef(tt, declareVariableDecl.TypeAnnotation);
                break;
            case DeclareModuleDecl declareModuleDecl:
                foreach (var member in declareModuleDecl.Members)
                {
                    ResolveDeclTypes(tt, member);
                }

                break;
            case EnumDecl enumDecl:
                if (enumDecl.IsDeclare)
                {
                    DeclareEnumType(_pkg, enumDecl);
                }
                foreach (var member in enumDecl.Members)
                {
                    if (member.TypeAnnotation != null)
                    {
                        ResolveTypeRef(tt, member.TypeAnnotation);
                    }
                }
                break;
            default:
                throw new InvalidOperationException($"Unknown declaration kind: {decl.GetType().Name}");
        }
    }

    private void ResolveExprTypes(TypeTable tt, Expr expr)
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
                foreach (var param in functionDefExpr.Parameters)
                {
                    if (param.TypeAnnotation != null)
                    {
                        ResolveTypeRef(tt, param.TypeAnnotation);
                    }
                }
                
                if (functionDefExpr.ReturnType != null)
                {
                    ResolveTypeRef(tt, functionDefExpr.ReturnType);
                }
                
                ResolveStmtListTypes(tt, functionDefExpr.Body);
                break;
            case BinaryExpr binaryExpr:
                ResolveExprTypes(tt, binaryExpr.Left);
                ResolveExprTypes(tt, binaryExpr.Right);
                break;
            case UnaryExpr unaryExpr:
                ResolveExprTypes(tt, unaryExpr.Operand);
                break;
            case NameExpr:
                break;
            case ParenExpr parenExpr:
                ResolveExprTypes(tt, parenExpr.Inner);
                break;
            case DotAccessExpr dotAccessExpr:
                ResolveExprTypes(tt, dotAccessExpr.Object);
                break;
            case IndexAccessExpr indexAccessExpr:
                ResolveExprTypes(tt, indexAccessExpr.Object);
                ResolveExprTypes(tt, indexAccessExpr.Index);
                break;
            case FunctionCallExpr functionCallExpr:
                ResolveExprTypes(tt, functionCallExpr.Callee);
                
                foreach (var arg in functionCallExpr.Arguments)
                {
                    ResolveExprTypes(tt, arg);
                }
                
                break;
            case MethodCallExpr methodCallExpr:
                ResolveExprTypes(tt, methodCallExpr.Object);
                
                foreach (var arg in methodCallExpr.Arguments)
                {
                    ResolveExprTypes(tt, arg);
                }
                
                break;
            case InterpolatedStringExpr interpolatedStringExpr:
                foreach (var part in interpolatedStringExpr.Parts)
                {
                    if (part is InterpExprPart exprPart)
                    {
                        ResolveExprTypes(tt, exprPart.Expression);
                    }
                }
                break;
            case NonNilAssertExpr nonNilAssert:
                ResolveExprTypes(tt, nonNilAssert.Inner);
                break;
            case IncDecExpr incDec:
                ResolveExprTypes(tt, incDec.Target);
                break;
            case TypeCheckExpr typeCheck:
                ResolveExprTypes(tt, typeCheck.Inner);
                ResolveTypeRef(tt, typeCheck.TargetType);
                break;
            case TypeCastExpr typeCast:
                ResolveExprTypes(tt, typeCast.Inner);
                ResolveTypeRef(tt, typeCast.TargetType);
                break;
            case TableConstructorExpr tableConstructorExpr:
                foreach (var field in tableConstructorExpr.Fields)
                {
                    if (field.Key != null)
                    {
                        ResolveExprTypes(tt, field.Key);
                    }

                    ResolveExprTypes(tt, field.Value);
                }

                break;
            case AwaitExpr awaitExpr:
                ResolveExprTypes(tt, awaitExpr.Expression);
                break;
            case MatchExpr matchExpr:
                ResolveExprTypes(tt, matchExpr.Scrutinee);
                foreach (var arm in matchExpr.Arms)
                {
                    if (arm.Pattern.ValueExpr != null) ResolveExprTypes(tt, arm.Pattern.ValueExpr);
                    if (arm.Pattern.TypeRef != null) ResolveTypeRef(tt, arm.Pattern.TypeRef);
                    if (arm.Guard != null) ResolveExprTypes(tt, arm.Guard);
                    ResolveExprTypes(tt, arm.Value);
                }
                break;
            default:
                throw new InvalidOperationException($"Unknown expression kind: {expr.GetType().Name}");
        }
    }
    
    private Type ResolveTypeRef(TypeTable tt, TypeRef tr)
    {
        if (tr.ResolvedType != TypID.Invalid)
        {
            tt.GetByID(tr.ResolvedType, out var resolvedType);
            return resolvedType;
        }

        if (tr is NamedTypeRef nrt)
        {
            if (_pkg.Scopes.Lookup(_pkg.Root, nrt.Name.Name, out var symId)
                && _pkg.Syms.GetByID(symId, out var sym)
                && sym.Type != TypID.Invalid)
            {
                tt.GetByID(sym.Type, out var resolved);
                nrt.Name.Sym = symId;
                tr.ResolvedType = sym.Type;
                return resolved;
            }

            _ctx.Diag.Report(tr.Span, Lux.Diagnostics.DiagnosticCode.ErrUndeclaredSymbol, nrt.Name.Name);
            tr.ResolvedType = tt.PrimAny.ID;
            return tt.PrimAny;
        }

        if (tr is NullableTypeRef nt)
        {
            var inner = ResolveTypeRef(tt, nt.InnerType);
            var unionType = tt.DeclareType(new UnionType([inner, tt.PrimNil]));
            tr.ResolvedType = unionType.ID;
            return unionType;
        }

        Type result;
        switch (tr.Kind)
        {
            case TypeKind.PrimitiveAny:
                result = tt.PrimAny;
                break;
            case TypeKind.PrimitiveNil:
                result = tt.PrimNil;
                break;
            case TypeKind.PrimitiveString:
                result = tt.PrimString;
                break;
            case TypeKind.PrimitiveNumber:
                result = tt.PrimNumber;
                break;
            case TypeKind.PrimitiveBool:
                result = tt.PrimBool;
                break;
            case TypeKind.TableArray:
            {
                var et = ResolveTypeRef(tt, ((ArrayTypeRef) tr).ElementType);
                result = tt.DeclareType(new TableArrayType(et));
                break;
            }
            case TypeKind.TableMap:
            {
                var t = (MapTypeRef) tr;
                var kt = ResolveTypeRef(tt, t.KeyType);
                var vt = ResolveTypeRef(tt, t.ValueType);
                result = tt.DeclareType(new TableMapType(kt, vt));
                break;
            }
            case TypeKind.Tuple:
            {
                var t = (TupleTypeRef) tr;
                result = tt.DeclareType(new TupleType(t.ElementTypes.Select(et => new TupleType.Field(ResolveTypeRef(tt, et))).ToList()));
                break;
            }
            case TypeKind.Union:
            {
                var t = (UnionTypeRef) tr;
                result = tt.DeclareType(new UnionType(t.Types.Select(ut => ResolveTypeRef(tt, ut)).ToList()));
                break;
            }
            case TypeKind.Struct:
            {
                var t = (StructTypeRef) tr;
                result = tt.DeclareType(new StructType(t.Fields.Select(f => new StructType.Field(f.Name, ResolveTypeRef(tt, f.Type), f.IsMeta)).ToList()));
                break;
            }
            case TypeKind.Function:
            {
                var t = (FunctionTypeRef) tr;
                var paramTypes = t.ParamTypes.Select(pt => ResolveTypeRef(tt, pt)).ToList();
                var returnType = ResolveTypeRef(tt, t.ReturnType);
                result = tt.DeclareType(new FunctionType(paramTypes, returnType));
                break;
            }
            default:
                throw new InvalidOperationException($"Unknown type kind: {tr.Kind}");
        }

        tr.ResolvedType = result.ID;
        return result;
    }
}