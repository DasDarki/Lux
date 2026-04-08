using Lux.Compiler.Codegen;
using Lux.IR;

namespace Lux.Compiler.Passes;

public sealed class CodegenPass() : Pass(PassName, PassScope.PerBuild, true, ManglePase.PassName)
{
    public const string PassName = "CodegenPass";

    public override bool Run(PassContext context)
    {
        foreach (var pkg in context.Pkgs)
        {
            foreach (var file in pkg.Files)
            {
                if (file.IsDeclarationFile) continue;

                var gen = new LuaGenerator(context.Config);
                EmitFile(context, pkg, gen, file);
                file.GeneratedLua = gen.Finish();
            }
        }

        return true;
    }

    private void EmitFile(PassContext ctx, PackageContext pkg, LuaGenerator gen, PreparsedFile file)
    {
        var exportedNames = new List<string>();
        CollectExportNames(ctx, pkg, file.Hir.Body, exportedNames);

        EmitStmtList(ctx, pkg, gen, file.Hir.Body);

        if (exportedNames.Count > 0)
        {
            if (file.Hir.Return != null)
            {
                EmitReturn(ctx, pkg, gen, file.Hir.Return);
                // TODO: in the future we could merge exports into an existing return table
            }
            else
            {
                gen.Write("return {");
                if (!gen.Minify) gen.NewLine();
                gen.Indent();
                for (var i = 0; i < exportedNames.Count; i++)
                {
                    gen.Write(exportedNames[i]);
                    gen.Write(" = ");
                    gen.Write(exportedNames[i]);
                    if (i < exportedNames.Count - 1) gen.Write(",");
                    if (!gen.Minify) gen.NewLine();
                }
                gen.Dedent();
                gen.WriteLine("}");
            }
        }
        else if (file.Hir.Return != null)
        {
            EmitReturn(ctx, pkg, gen, file.Hir.Return);
        }
    }

    private void CollectExportNames(PassContext ctx, PackageContext pkg, List<Stmt> stmts, List<string> names)
    {
        foreach (var stmt in stmts)
        {
            if (stmt is not ExportStmt export) continue;
            switch (export.Declaration)
            {
                case FunctionDecl fd:
                    if (fd.NamePath.Count > 0)
                        names.Add(ResolveName(ctx, pkg, fd.NamePath[0]));
                    break;
                case LocalFunctionDecl lfd:
                    names.Add(ResolveName(ctx, pkg, lfd.Name));
                    break;
                case LocalDecl ld:
                    foreach (var v in ld.Variables)
                        names.Add(ResolveName(ctx, pkg, v.Name));
                    break;
                case EnumDecl ed:
                    names.Add(ResolveName(ctx, pkg, ed.Name));
                    break;
            }
        }
    }

    #region Statements

    private void EmitStmtList(PassContext ctx, PackageContext pkg, LuaGenerator gen, List<Stmt> stmts)
    {
        foreach (var stmt in stmts)
        {
            gen.FlushHoisted();
            EmitStmt(ctx, pkg, gen, stmt);
        }
        gen.FlushHoisted();
    }

    private bool ShouldStrip(PassContext ctx, PackageContext pkg, Stmt stmt)
    {
        if (!ctx.Config.Code.StripUnused) return false;
        switch (stmt)
        {
            case LocalFunctionDecl lfd:
                return pkg.Syms.GetByID(lfd.Name.Sym, out var lfSym) && lfSym.Flags.HasFlag(SymbolFlags.Unused);
            case LocalDecl ld:
                return ld.Variables.All(v => pkg.Syms.GetByID(v.Name.Sym, out var vSym) && vSym.Flags.HasFlag(SymbolFlags.Unused));
            case ImportStmt import:
                if (import.Kind == ImportKind.SideEffect) return false;
                if (import.Alias != null)
                    return pkg.Syms.GetByID(import.Alias.Sym, out var aSym) && aSym.Flags.HasFlag(SymbolFlags.Unused);
                return import.Specifiers.Count > 0 && import.Specifiers.All(s =>
                {
                    var nr = s.Alias ?? s.Name;
                    return pkg.Syms.GetByID(nr.Sym, out var sSym) && sSym.Flags.HasFlag(SymbolFlags.Unused);
                });
            default:
                return false;
        }
    }

    private void EmitStmt(PassContext ctx, PackageContext pkg, LuaGenerator gen, Stmt stmt)
    {
        if (ShouldStrip(ctx, pkg, stmt)) return;

        switch (stmt)
        {
            case FunctionDecl fd:
                EmitFunctionDecl(ctx, pkg, gen, fd);
                break;
            case LocalFunctionDecl lfd:
                EmitLocalFunctionDecl(ctx, pkg, gen, lfd);
                break;
            case LocalDecl ld:
                EmitLocalDecl(ctx, pkg, gen, ld);
                break;
            case AssignStmt assign:
                EmitAssign(ctx, pkg, gen, assign);
                break;
            case ExprStmt exprStmt:
                EmitExprStmt(ctx, pkg, gen, exprStmt);
                break;
            case DoBlockStmt doBlock:
                gen.BeginBlock("do");
                EmitStmtList(ctx, pkg, gen, doBlock.Body);
                gen.EndBlock();
                break;
            case WhileStmt ws:
                gen.Write("while ");
                EmitExpr(ctx, pkg, gen, ws.Condition);
                gen.BeginBlock(" do");
                EmitStmtList(ctx, pkg, gen, ws.Body);
                gen.EndBlock();
                break;
            case RepeatStmt rs:
                gen.BeginBlock("repeat");
                EmitStmtList(ctx, pkg, gen, rs.Body);
                gen.Dedent();
                gen.Write("until ");
                EmitExpr(ctx, pkg, gen, rs.Condition);
                gen.NewLine();
                gen.Indent();
                gen.Dedent();
                break;
            case IfStmt ifStmt:
                EmitIf(ctx, pkg, gen, ifStmt);
                break;
            case NumericForStmt nf:
                EmitNumericFor(ctx, pkg, gen, nf);
                break;
            case GenericForStmt gf:
                EmitGenericFor(ctx, pkg, gen, gf);
                break;
            case ReturnStmt ret:
                EmitReturn(ctx, pkg, gen, ret);
                break;
            case BreakStmt:
                gen.WriteLine("break");
                gen.WriteSemicolon();
                break;
            case GotoStmt gs:
                if (gen.Features.HasGoto)
                    gen.WriteLine("goto " + gs.LabelName.Name);
                break;
            case LabelStmt ls:
                if (gen.Features.HasGoto)
                    gen.WriteLine("::" + ls.Name.Name + "::");
                break;
            case ImportStmt import:
                EmitImport(ctx, pkg, gen, import);
                break;
            case ExportStmt export:
                EmitStmt(ctx, pkg, gen, export.Declaration);
                break;
            case DeclareFunctionDecl:
            case DeclareVariableDecl:
            case DeclareModuleDecl:
                break;
            case EnumDecl ed:
                if (!ed.IsDeclare)
                    EmitEnumDecl(ctx, pkg, gen, ed);
                break;
        }
    }

    private void EmitEnumDecl(PassContext ctx, PackageContext pkg, LuaGenerator gen, EnumDecl ed)
    {
        var hasNumberValues = ed.Members.Any(m => m.Value is NumberLiteralExpr);
        gen.Write("local ");
        gen.Write(ResolveName(ctx, pkg, ed.Name));
        gen.Write(" = { ");
        long autoIndex = 0;
        for (var i = 0; i < ed.Members.Count; i++)
        {
            if (i > 0) gen.Write(", ");
            var m = ed.Members[i];
            gen.Write(m.Name.Name);
            gen.Write(" = ");
            switch (m.Value)
            {
                case NumberLiteralExpr nl:
                    gen.Write(nl.Raw);
                    if (long.TryParse(nl.Raw, out var parsed)) autoIndex = parsed + 1;
                    else autoIndex++;
                    break;
                case StringLiteralExpr sl:
                    gen.Write("\"");
                    gen.Write(sl.Value);
                    gen.Write("\"");
                    autoIndex++;
                    break;
                default:
                    if (hasNumberValues)
                    {
                        gen.Write(autoIndex.ToString());
                        autoIndex++;
                    }
                    else
                    {
                        gen.Write("\"");
                        gen.Write(m.Name.Name);
                        gen.Write("\"");
                    }
                    break;
            }
        }
        gen.Write(" }");
        gen.NewLine();
        gen.WriteSemicolon();
    }

    #endregion

    #region Declarations

    private void EmitFunctionDecl(PassContext ctx, PackageContext pkg, LuaGenerator gen, FunctionDecl fd)
    {
        gen.Write("function ");
        EmitNamePath(ctx, pkg, gen, fd.NamePath);
        if (fd.MethodName != null)
        {
            gen.Write(":");
            gen.Write(fd.MethodName.Name);
        }
        gen.Write("(");
        EmitParamList(ctx, pkg, gen, fd.Parameters);
        gen.Write(")");
        gen.NewLine();
        gen.Indent();
        EmitStmtList(ctx, pkg, gen, fd.Body);
        if (fd.ReturnStmt != null)
            EmitReturn(ctx, pkg, gen, fd.ReturnStmt);
        gen.EndBlock();
    }

    private void EmitLocalFunctionDecl(PassContext ctx, PackageContext pkg, LuaGenerator gen, LocalFunctionDecl lfd)
    {
        gen.Write("local function ");
        gen.Write(ResolveName(ctx, pkg, lfd.Name));
        gen.Write("(");
        EmitParamList(ctx, pkg, gen, lfd.Parameters);
        gen.Write(")");
        gen.NewLine();
        gen.Indent();
        EmitStmtList(ctx, pkg, gen, lfd.Body);
        if (lfd.ReturnStmt != null)
            EmitReturn(ctx, pkg, gen, lfd.ReturnStmt);
        gen.EndBlock();
    }

    private void EmitLocalDecl(PassContext ctx, PackageContext pkg, LuaGenerator gen, LocalDecl ld)
    {
        gen.Write("local ");
        for (var i = 0; i < ld.Variables.Count; i++)
        {
            if (i > 0) gen.Write(", ");
            var v = ld.Variables[i];
            gen.Write(ResolveName(ctx, pkg, v.Name));
            if (gen.Features.HasConstLocal && v.Attribute == "const")
                gen.Write(" <const>");
            else if (gen.Features.HasCloseLocal && v.Attribute == "close")
                gen.Write(" <close>");
        }

        if (ld.Values.Count > 0)
        {
            gen.Write(" = ");
            EmitExprList(ctx, pkg, gen, ld.Values);
        }
        gen.NewLine();
        gen.WriteSemicolon();
    }

    #endregion

    #region Assignment

    private void EmitAssign(PassContext ctx, PackageContext pkg, LuaGenerator gen, AssignStmt assign)
    {
        for (var i = 0; i < assign.Targets.Count; i++)
        {
            if (i > 0) gen.Write(", ");
            EmitExpr(ctx, pkg, gen, assign.Targets[i]);
        }
        gen.Write(" = ");
        EmitExprList(ctx, pkg, gen, assign.Values);
        gen.NewLine();
        gen.WriteSemicolon();
    }

    #endregion

    #region Control Flow

    private void EmitIf(PassContext ctx, PackageContext pkg, LuaGenerator gen, IfStmt ifStmt)
    {
        gen.Write("if ");
        EmitExpr(ctx, pkg, gen, ifStmt.Condition);
        gen.Write(" then");
        gen.NewLine();
        gen.Indent();
        EmitStmtList(ctx, pkg, gen, ifStmt.Body);
        gen.Dedent();

        foreach (var elseIf in ifStmt.ElseIfs)
        {
            gen.Write("elseif ");
            EmitExpr(ctx, pkg, gen, elseIf.Condition);
            gen.Write(" then");
            gen.NewLine();
            gen.Indent();
            EmitStmtList(ctx, pkg, gen, elseIf.Body);
            gen.Dedent();
        }

        if (ifStmt.ElseBody != null)
        {
            gen.WriteLine("else");
            gen.Indent();
            EmitStmtList(ctx, pkg, gen, ifStmt.ElseBody);
            gen.Dedent();
        }

        gen.WriteLine("end");
    }

    private void EmitNumericFor(PassContext ctx, PackageContext pkg, LuaGenerator gen, NumericForStmt nf)
    {
        gen.Write("for ");
        gen.Write(ResolveName(ctx, pkg, nf.VarName));
        gen.Write(" = ");
        EmitExpr(ctx, pkg, gen, nf.Start);
        gen.Write(", ");
        EmitExpr(ctx, pkg, gen, nf.Limit);
        if (nf.Step != null)
        {
            gen.Write(", ");
            EmitExpr(ctx, pkg, gen, nf.Step);
        }
        gen.Write(" do");
        gen.NewLine();
        gen.Indent();
        EmitStmtList(ctx, pkg, gen, nf.Body);
        gen.EndBlock();
    }

    private void EmitGenericFor(PassContext ctx, PackageContext pkg, LuaGenerator gen, GenericForStmt gf)
    {
        gen.Write("for ");
        for (var i = 0; i < gf.VarNames.Count; i++)
        {
            if (i > 0) gen.Write(", ");
            gen.Write(ResolveName(ctx, pkg, gf.VarNames[i]));
        }
        gen.Write(" in ");
        EmitGenericForIterators(ctx, pkg, gen, gf.Iterators);
        gen.Write(" do");
        gen.NewLine();
        gen.Indent();
        EmitStmtList(ctx, pkg, gen, gf.Body);
        gen.EndBlock();
    }

    private void EmitGenericForIterators(PassContext ctx, PackageContext pkg, LuaGenerator gen, List<Expr> iterators)
    {
        for (var i = 0; i < iterators.Count; i++)
        {
            if (i > 0) gen.Write(", ");
            var iter = iterators[i];
            if (gen.IndexBase != 1 && iter is FunctionCallExpr call && IsIpairsCall(ctx, pkg, call))
            {
                var helper = gen.GetIpairsHelper();
                gen.Write(helper);
                gen.Write("(");
                EmitExprList(ctx, pkg, gen, call.Arguments);
                gen.Write(")");
            }
            else
            {
                EmitExpr(ctx, pkg, gen, iter);
            }
        }
    }

    private bool IsIpairsCall(PassContext ctx, PackageContext pkg, FunctionCallExpr call)
    {
        if (call.Callee is NameExpr nameExpr)
        {
            var resolved = ResolveName(ctx, pkg, nameExpr.Name);
            return resolved == "ipairs";
        }
        return false;
    }

    private void EmitReturn(PassContext ctx, PackageContext pkg, LuaGenerator gen, ReturnStmt ret)
    {
        if (ret.Values.Count == 0)
        {
            gen.WriteLine("return");
        }
        else
        {
            gen.Write("return ");
            EmitExprList(ctx, pkg, gen, ret.Values);
            gen.NewLine();
        }
        gen.WriteSemicolon();
    }

    #endregion

    #region Import / Export

    private void EmitImport(PassContext ctx, PackageContext pkg, LuaGenerator gen, ImportStmt import)
    {
        var modExpr = gen.EmitImport(import.Module.Name);

        switch (import.Kind)
        {
            case ImportKind.SideEffect:
                gen.WriteLine(modExpr);
                break;
            case ImportKind.Default:
            case ImportKind.Namespace:
            {
                var alias = import.Alias != null
                    ? ResolveName(ctx, pkg, import.Alias)
                    : import.Module.Name;
                gen.Write("local ");
                gen.Write(alias);
                gen.Write(" = ");
                gen.WriteLine(modExpr);
                break;
            }
            case ImportKind.Named:
            {
                var temp = gen.FreshTemp("_mod");
                gen.Write("local ");
                gen.Write(temp);
                gen.Write(" = ");
                gen.WriteLine(modExpr);

                foreach (var spec in import.Specifiers)
                {
                    var localName = spec.Alias != null
                        ? ResolveName(ctx, pkg, spec.Alias)
                        : ResolveName(ctx, pkg, spec.Name);
                    gen.Write("local ");
                    gen.Write(localName);
                    gen.Write(" = ");
                    gen.Write(temp);
                    gen.Write(".");
                    gen.WriteLine(spec.Name.Name);
                }
                break;
            }
        }
        gen.WriteSemicolon();
    }

    #endregion

    #region Expressions

    private void EmitExprList(PassContext ctx, PackageContext pkg, LuaGenerator gen, List<Expr> exprs)
    {
        for (var i = 0; i < exprs.Count; i++)
        {
            if (i > 0) gen.Write(", ");
            EmitExpr(ctx, pkg, gen, exprs[i]);
        }
    }

    private void EmitExprStmt(PassContext ctx, PackageContext pkg, LuaGenerator gen, ExprStmt exprStmt)
    {
        EmitExpr(ctx, pkg, gen, exprStmt.Expression);
        gen.NewLine();
        gen.WriteSemicolon();
    }

    private void EmitExpr(PassContext ctx, PackageContext pkg, LuaGenerator gen, Expr expr)
    {
        switch (expr)
        {
            case NilLiteralExpr:
                gen.Write("nil");
                break;
            case BoolLiteralExpr b:
                gen.Write(b.Value ? "true" : "false");
                break;
            case NumberLiteralExpr n:
                gen.Write(n.Raw);
                break;
            case StringLiteralExpr s:
                EmitString(ctx, pkg, gen, s);
                break;
            case VarargExpr:
                gen.Write("...");
                break;
            case NameExpr name:
                gen.Write(ResolveName(ctx, pkg, name.Name));
                break;
            case ParenExpr paren:
                gen.Write("(");
                EmitExpr(ctx, pkg, gen, paren.Inner);
                gen.Write(")");
                break;
            case BinaryExpr bin:
                EmitBinary(ctx, pkg, gen, bin);
                break;
            case UnaryExpr un:
                EmitUnary(ctx, pkg, gen, un);
                break;
            case FunctionDefExpr fd:
                EmitFunctionDef(ctx, pkg, gen, fd);
                break;
            case DotAccessExpr dot:
                if (dot.IsOptional)
                {
                    EmitOptionalDotAccess(ctx, pkg, gen, dot);
                }
                else
                {
                    EmitExpr(ctx, pkg, gen, dot.Object);
                    gen.Write(".");
                    gen.Write(dot.FieldName.Name);
                }
                break;
            case IndexAccessExpr idx:
                EmitIndexAccess(ctx, pkg, gen, idx);
                break;
            case FunctionCallExpr call:
                if (call.IsOptional)
                    EmitOptionalFunctionCall(ctx, pkg, gen, call);
                else
                    EmitFunctionCall(ctx, pkg, gen, call);
                break;
            case MethodCallExpr mc:
                EmitMethodCall(ctx, pkg, gen, mc);
                break;
            case TableConstructorExpr tc:
                EmitTableConstructor(ctx, pkg, gen, tc);
                break;
            case InterpolatedStringExpr interp:
                EmitInterpolatedString(ctx, pkg, gen, interp);
                break;
            case NonNilAssertExpr nna:
                EmitExpr(ctx, pkg, gen, nna.Inner);
                break;
        }
    }

    private void EmitInterpolatedString(PassContext ctx, PackageContext pkg, LuaGenerator gen, InterpolatedStringExpr interp)
    {
        if (interp.Parts.Count == 0)
        {
            gen.Write("\"\"");
            return;
        }

        var first = true;
        foreach (var part in interp.Parts)
        {
            if (!first) gen.Write(" .. ");
            first = false;

            switch (part)
            {
                case InterpTextPart text:
                    gen.Write("\"");
                    gen.Write(EscapeLuaString(text.Text));
                    gen.Write("\"");
                    break;
                case InterpExprPart exprPart:
                    if (exprPart.Expression is StringLiteralExpr)
                    {
                        EmitExpr(ctx, pkg, gen, exprPart.Expression);
                    }
                    else
                    {
                        gen.Write("tostring(");
                        EmitExpr(ctx, pkg, gen, exprPart.Expression);
                        gen.Write(")");
                    }
                    break;
            }
        }
    }

    private void EmitString(PassContext ctx, PackageContext pkg, LuaGenerator gen, StringLiteralExpr s)
    {
        gen.Write("\"");
        gen.Write(EscapeLuaString(s.Value));
        gen.Write("\"");
    }

    private void EmitBinary(PassContext ctx, PackageContext pkg, LuaGenerator gen, BinaryExpr bin)
    {
        if (bin.Op == BinaryOp.NilCoalesce)
        {
            EmitNilCoalesce(ctx, pkg, gen, bin);
            return;
        }

        var isConcatOp = gen.IsConfiguredConcatOp(bin.Op) && bin.Op != BinaryOp.Concat;
        var isStringContext = isConcatOp && IsStringTyped(ctx, pkg, bin);

        if (isStringContext)
        {
            var helper = gen.GetConcatHelper();
            gen.Write(helper);
            gen.Write("(");
            EmitExpr(ctx, pkg, gen, bin.Left);
            gen.Write(", ");
            EmitExpr(ctx, pkg, gen, bin.Right);
            gen.Write(")");
            return;
        }

        if (bin.Op == BinaryOp.FloorDiv && !gen.Features.HasFloorDiv)
        {
            var helper = gen.GetFloorDivHelper();
            gen.Write(helper);
            gen.Write("(");
            EmitExpr(ctx, pkg, gen, bin.Left);
            gen.Write(", ");
            EmitExpr(ctx, pkg, gen, bin.Right);
            gen.Write(")");
            return;
        }

        if (gen.IsBitwiseOp(bin.Op) && gen.Features.BitwiseStyle == BitwiseStyle.BitLib)
        {
            gen.Write(gen.BinaryOpToLua(bin.Op));
            gen.Write("(");
            EmitExpr(ctx, pkg, gen, bin.Left);
            gen.Write(", ");
            EmitExpr(ctx, pkg, gen, bin.Right);
            gen.Write(")");
            return;
        }

        if (gen.IsBitwiseOp(bin.Op) && !gen.Features.HasBitwise)
            return;

        EmitExpr(ctx, pkg, gen, bin.Left);
        gen.Write(" ");
        gen.Write(gen.BinaryOpToLua(bin.Op));
        gen.Write(" ");
        EmitExpr(ctx, pkg, gen, bin.Right);
    }

    private void EmitOptionalDotAccess(PassContext ctx, PackageContext pkg, LuaGenerator gen, DotAccessExpr dot)
    {
        gen.Write("(function() local __v = (");
        EmitExpr(ctx, pkg, gen, dot.Object);
        gen.Write("); if __v == nil then return nil else return __v.");
        gen.Write(dot.FieldName.Name);
        gen.Write(" end end)()");
    }

    private void EmitOptionalFunctionCall(PassContext ctx, PackageContext pkg, LuaGenerator gen, FunctionCallExpr call)
    {
        gen.Write("(function() local __f = (");
        EmitExpr(ctx, pkg, gen, call.Callee);
        gen.Write("); if __f == nil then return nil else return __f(");
        for (var i = 0; i < call.Arguments.Count; i++)
        {
            if (i > 0) gen.Write(", ");
            EmitExpr(ctx, pkg, gen, call.Arguments[i]);
        }
        gen.Write(") end end)()");
    }

    private void EmitNilCoalesce(PassContext ctx, PackageContext pkg, LuaGenerator gen, BinaryExpr bin)
    {
        gen.Write("(function() local __v = (");
        EmitExpr(ctx, pkg, gen, bin.Left);
        gen.Write("); if __v ~= nil then return __v else return (");
        EmitExpr(ctx, pkg, gen, bin.Right);
        gen.Write(") end end)()");
    }

    private void EmitUnary(PassContext ctx, PackageContext pkg, LuaGenerator gen, UnaryExpr un)
    {
        if (un.Op == UnaryOp.BitwiseNot && gen.Features.BitwiseStyle == BitwiseStyle.BitLib)
        {
            gen.Write("bit.bnot(");
            EmitExpr(ctx, pkg, gen, un.Operand);
            gen.Write(")");
            return;
        }

        if (un.Op == UnaryOp.BitwiseNot && !gen.Features.HasBitwise)
            return;

        gen.Write(gen.UnaryOpToLua(un.Op));
        var needsParens = un.Operand is BinaryExpr or UnaryExpr;
        if (needsParens) gen.Write("(");
        EmitExpr(ctx, pkg, gen, un.Operand);
        if (needsParens) gen.Write(")");
    }

    private void EmitFunctionDef(PassContext ctx, PackageContext pkg, LuaGenerator gen, FunctionDefExpr fd)
    {
        gen.Write("function(");
        EmitParamList(ctx, pkg, gen, fd.Parameters);
        gen.Write(")");
        gen.NewLine();
        gen.Indent();
        EmitStmtList(ctx, pkg, gen, fd.Body);
        if (fd.ReturnStmt != null)
            EmitReturn(ctx, pkg, gen, fd.ReturnStmt);
        gen.Dedent();
        gen.Write("end");
    }

    private void EmitIndexAccess(PassContext ctx, PackageContext pkg, LuaGenerator gen, IndexAccessExpr idx)
    {
        EmitExpr(ctx, pkg, gen, idx.Object);
        gen.Write("[");
        if (gen.IndexBase != 1)
        {
            gen.Write(gen.AdjustIndex(ExprToInline(ctx, pkg, gen, idx.Index)));
        }
        else
        {
            EmitExpr(ctx, pkg, gen, idx.Index);
        }
        gen.Write("]");
    }

    private void EmitFunctionCall(PassContext ctx, PackageContext pkg, LuaGenerator gen, FunctionCallExpr call)
    {
        EmitExpr(ctx, pkg, gen, call.Callee);
        gen.Write("(");
        EmitExprList(ctx, pkg, gen, call.Arguments);
        gen.Write(")");
    }

    private void EmitMethodCall(PassContext ctx, PackageContext pkg, LuaGenerator gen, MethodCallExpr mc)
    {
        EmitExpr(ctx, pkg, gen, mc.Object);
        gen.Write(":");
        gen.Write(mc.MethodName.Name);
        gen.Write("(");
        EmitExprList(ctx, pkg, gen, mc.Arguments);
        gen.Write(")");
    }

    private void EmitTableConstructor(PassContext ctx, PackageContext pkg, LuaGenerator gen, TableConstructorExpr tc)
    {
        if (tc.Fields.Count == 0)
        {
            gen.Write("{}");
            return;
        }

        gen.Write("{");
        if (!gen.Minify) gen.NewLine();
        gen.Indent();

        for (var i = 0; i < tc.Fields.Count; i++)
        {
            var field = tc.Fields[i];
            switch (field.Kind)
            {
                case TableFieldKind.Named:
                    gen.Write(field.Name!.Name);
                    gen.Write(" = ");
                    EmitExpr(ctx, pkg, gen, field.Value);
                    break;
                case TableFieldKind.Bracket:
                    gen.Write("[");
                    if (gen.IndexBase != 1)
                        gen.Write(gen.AdjustIndex(ExprToInline(ctx, pkg, gen, field.Key!)));
                    else
                        EmitExpr(ctx, pkg, gen, field.Key!);
                    gen.Write("] = ");
                    EmitExpr(ctx, pkg, gen, field.Value);
                    break;
                case TableFieldKind.Positional:
                    EmitExpr(ctx, pkg, gen, field.Value);
                    break;
            }

            if (i < tc.Fields.Count - 1)
                gen.Write(",");
            if (!gen.Minify) gen.NewLine();
        }

        gen.Dedent();
        gen.Write("}");
    }

    #endregion

    #region Helpers

    private void EmitParamList(PassContext ctx, PackageContext pkg, LuaGenerator gen, List<Parameter> parameters)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            if (i > 0) gen.Write(", ");
            var param = parameters[i];
            if (param.IsVararg)
            {
                gen.Write("...");
            }
            else
            {
                gen.Write(ResolveName(ctx, pkg, param.Name));
            }
        }
    }

    private void EmitNamePath(PassContext ctx, PackageContext pkg, LuaGenerator gen, List<NameRef> namePath)
    {
        for (var i = 0; i < namePath.Count; i++)
        {
            if (i > 0) gen.Write(".");
            if (i == 0)
                gen.Write(ResolveName(ctx, pkg, namePath[i]));
            else
                gen.Write(namePath[i].Name);
        }
    }

    private string ResolveName(PassContext ctx, PackageContext pkg, NameRef nameRef)
    {
        if (nameRef.Sym == SymID.Invalid)
            return nameRef.Name;

        if (ctx.Names.GetMangled(nameRef.Sym, out var mangled))
            return mangled;

        if (ctx.Names.GetOriginal(nameRef.Sym, out var original))
            return original;

        return nameRef.Name;
    }

    private bool IsStringTyped(PassContext ctx, PackageContext pkg, BinaryExpr bin)
    {
        return IsStringType(ctx, pkg, bin.Left.Type) || IsStringType(ctx, pkg, bin.Right.Type);
    }

    private bool IsStringType(PassContext ctx, PackageContext pkg, TypID t)
    {
        return t == ctx.Types.PrimString.ID;
    }

    private string ExprToInline(PassContext ctx, PackageContext pkg, LuaGenerator gen, Expr expr)
    {
        var sub = new LuaGenerator(gen.Config);
        EmitExpr(ctx, pkg, sub, expr);
        return sub.Finish();
    }

    private static string EscapeLuaString(string s)
    {
        return s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t")
            .Replace("\0", "\\0");
    }

    #endregion
}
