using Lux.Diagnostics;
using Lux.IR;

namespace Lux.LPS;

public static class NodeFinder
{
    public static Node? Find(IRScript script, int line, int col)
    {
        Node? best = null;
        SearchStmtList(script.Body, line, col, ref best);
        if (script.Return != null)
            SearchStmt(script.Return, line, col, ref best);
        return best;
    }

    public static NameRef? FindNameRef(IRScript script, int line, int col)
    {
        NameRef? best = null;
        SearchStmtListForNameRef(script.Body, line, col, ref best);
        if (script.Return != null)
            SearchStmtForNameRef(script.Return, line, col, ref best);
        return best;
    }

    public static List<NameRef> CollectAllNameRefs(IRScript script)
    {
        var refs = new List<NameRef>();
        CollectFromStmtList(script.Body, refs);
        if (script.Return != null)
            CollectFromStmt(script.Return, refs);
        return refs;
    }

    public static Dictionary<NodeID, Node> BuildNodeRegistry(IRScript script)
    {
        var reg = new Dictionary<NodeID, Node>();
        reg[script.ID] = script;
        RegisterStmtList(script.Body, reg);
        if (script.Return != null)
            RegisterStmt(script.Return, reg);
        return reg;
    }

    private static bool Contains(TextSpan span, int line, int col)
    {
        if (line < span.StartLn || line > span.EndLn) return false;
        if (line == span.StartLn && col < span.StartCol) return false;
        if (line == span.EndLn && col > span.EndCol) return false;
        return true;
    }

    private static bool IsTighter(Node candidate, Node? current)
    {
        if (current == null) return true;
        var cs = candidate.Span;
        var bs = current.Span;
        var cLines = cs.EndLn - cs.StartLn;
        var bLines = bs.EndLn - bs.StartLn;
        if (cLines < bLines) return true;
        if (cLines == bLines)
            return (cs.EndCol - cs.StartCol) < (bs.EndCol - bs.StartCol);
        return false;
    }

    #region Find Node

    private static void SearchStmtList(List<Stmt> stmts, int line, int col, ref Node? best)
    {
        foreach (var stmt in stmts)
            SearchStmt(stmt, line, col, ref best);
    }

    private static void SearchStmt(Stmt stmt, int line, int col, ref Node? best)
    {
        if (!Contains(stmt.Span, line, col)) return;
        if (IsTighter(stmt, best)) best = stmt;

        switch (stmt)
        {
            case FunctionDecl fd:
                foreach (var p in fd.Parameters) SearchNode(p, line, col, ref best);
                SearchStmtList(fd.Body, line, col, ref best);
                if (fd.ReturnStmt != null) SearchStmt(fd.ReturnStmt, line, col, ref best);
                break;
            case LocalFunctionDecl lfd:
                foreach (var p in lfd.Parameters) SearchNode(p, line, col, ref best);
                SearchStmtList(lfd.Body, line, col, ref best);
                if (lfd.ReturnStmt != null) SearchStmt(lfd.ReturnStmt, line, col, ref best);
                break;
            case LocalDecl ld:
                foreach (var v in ld.Values) SearchExpr(v, line, col, ref best);
                break;
            case AssignStmt a:
                foreach (var t in a.Targets) SearchExpr(t, line, col, ref best);
                foreach (var v in a.Values) SearchExpr(v, line, col, ref best);
                break;
            case ExprStmt es:
                SearchExpr(es.Expression, line, col, ref best);
                break;
            case DoBlockStmt db:
                SearchStmtList(db.Body, line, col, ref best);
                break;
            case WhileStmt ws:
                SearchExpr(ws.Condition, line, col, ref best);
                SearchStmtList(ws.Body, line, col, ref best);
                break;
            case RepeatStmt rs:
                SearchStmtList(rs.Body, line, col, ref best);
                SearchExpr(rs.Condition, line, col, ref best);
                break;
            case IfStmt ifs:
                SearchExpr(ifs.Condition, line, col, ref best);
                SearchStmtList(ifs.Body, line, col, ref best);
                foreach (var ei in ifs.ElseIfs)
                {
                    SearchExpr(ei.Condition, line, col, ref best);
                    SearchStmtList(ei.Body, line, col, ref best);
                }
                if (ifs.ElseBody != null) SearchStmtList(ifs.ElseBody, line, col, ref best);
                break;
            case NumericForStmt nf:
                SearchExpr(nf.Start, line, col, ref best);
                SearchExpr(nf.Limit, line, col, ref best);
                if (nf.Step != null) SearchExpr(nf.Step, line, col, ref best);
                SearchStmtList(nf.Body, line, col, ref best);
                break;
            case GenericForStmt gf:
                foreach (var iter in gf.Iterators) SearchExpr(iter, line, col, ref best);
                SearchStmtList(gf.Body, line, col, ref best);
                break;
            case ReturnStmt ret:
                foreach (var v in ret.Values) SearchExpr(v, line, col, ref best);
                break;
            case ExportStmt exp:
                SearchStmt(exp.Declaration, line, col, ref best);
                break;
        }
    }

    private static void SearchExpr(Expr expr, int line, int col, ref Node? best)
    {
        if (!Contains(expr.Span, line, col)) return;
        if (IsTighter(expr, best)) best = expr;

        switch (expr)
        {
            case ParenExpr pe:
                SearchExpr(pe.Inner, line, col, ref best);
                break;
            case BinaryExpr bin:
                SearchExpr(bin.Left, line, col, ref best);
                SearchExpr(bin.Right, line, col, ref best);
                break;
            case UnaryExpr un:
                SearchExpr(un.Operand, line, col, ref best);
                break;
            case DotAccessExpr dot:
                SearchExpr(dot.Object, line, col, ref best);
                break;
            case IndexAccessExpr idx:
                SearchExpr(idx.Object, line, col, ref best);
                SearchExpr(idx.Index, line, col, ref best);
                break;
            case FunctionCallExpr call:
                SearchExpr(call.Callee, line, col, ref best);
                foreach (var a in call.Arguments) SearchExpr(a, line, col, ref best);
                break;
            case MethodCallExpr mc:
                SearchExpr(mc.Object, line, col, ref best);
                foreach (var a in mc.Arguments) SearchExpr(a, line, col, ref best);
                break;
            case FunctionDefExpr fd:
                foreach (var p in fd.Parameters) SearchNode(p, line, col, ref best);
                SearchStmtList(fd.Body, line, col, ref best);
                if (fd.ReturnStmt != null) SearchStmt(fd.ReturnStmt, line, col, ref best);
                break;
            case TableConstructorExpr tc:
                foreach (var f in tc.Fields)
                {
                    if (f.Key != null) SearchExpr(f.Key, line, col, ref best);
                    SearchExpr(f.Value, line, col, ref best);
                }
                break;
        }
    }

    private static void SearchNode(Node node, int line, int col, ref Node? best)
    {
        if (!Contains(node.Span, line, col)) return;
        if (IsTighter(node, best)) best = node;
    }

    #endregion

    #region Find NameRef

    private static bool NameRefContains(NameRef nr, int line, int col) => Contains(nr.Span, line, col);

    private static void CheckNameRef(NameRef? nr, int line, int col, ref NameRef? best)
    {
        if (nr != null && NameRefContains(nr, line, col)) best = nr;
    }

    private static void SearchStmtListForNameRef(List<Stmt> stmts, int line, int col, ref NameRef? best)
    {
        foreach (var stmt in stmts)
            SearchStmtForNameRef(stmt, line, col, ref best);
    }

    private static void SearchStmtForNameRef(Stmt stmt, int line, int col, ref NameRef? best)
    {
        if (!Contains(stmt.Span, line, col)) return;

        switch (stmt)
        {
            case FunctionDecl fd:
                foreach (var n in fd.NamePath) CheckNameRef(n, line, col, ref best);
                CheckNameRef(fd.MethodName, line, col, ref best);
                foreach (var p in fd.Parameters) CheckNameRef(p.Name, line, col, ref best);
                SearchStmtListForNameRef(fd.Body, line, col, ref best);
                if (fd.ReturnStmt != null) SearchStmtForNameRef(fd.ReturnStmt, line, col, ref best);
                break;
            case LocalFunctionDecl lfd:
                CheckNameRef(lfd.Name, line, col, ref best);
                foreach (var p in lfd.Parameters) CheckNameRef(p.Name, line, col, ref best);
                SearchStmtListForNameRef(lfd.Body, line, col, ref best);
                if (lfd.ReturnStmt != null) SearchStmtForNameRef(lfd.ReturnStmt, line, col, ref best);
                break;
            case LocalDecl ld:
                foreach (var v in ld.Variables) CheckNameRef(v.Name, line, col, ref best);
                foreach (var v in ld.Values) SearchExprForNameRef(v, line, col, ref best);
                break;
            case AssignStmt a:
                foreach (var t in a.Targets) SearchExprForNameRef(t, line, col, ref best);
                foreach (var v in a.Values) SearchExprForNameRef(v, line, col, ref best);
                break;
            case ExprStmt es:
                SearchExprForNameRef(es.Expression, line, col, ref best);
                break;
            case DoBlockStmt db:
                SearchStmtListForNameRef(db.Body, line, col, ref best);
                break;
            case WhileStmt ws:
                SearchExprForNameRef(ws.Condition, line, col, ref best);
                SearchStmtListForNameRef(ws.Body, line, col, ref best);
                break;
            case RepeatStmt rs:
                SearchStmtListForNameRef(rs.Body, line, col, ref best);
                SearchExprForNameRef(rs.Condition, line, col, ref best);
                break;
            case IfStmt ifs:
                SearchExprForNameRef(ifs.Condition, line, col, ref best);
                SearchStmtListForNameRef(ifs.Body, line, col, ref best);
                foreach (var ei in ifs.ElseIfs)
                {
                    SearchExprForNameRef(ei.Condition, line, col, ref best);
                    SearchStmtListForNameRef(ei.Body, line, col, ref best);
                }
                if (ifs.ElseBody != null) SearchStmtListForNameRef(ifs.ElseBody, line, col, ref best);
                break;
            case NumericForStmt nf:
                CheckNameRef(nf.VarName, line, col, ref best);
                SearchExprForNameRef(nf.Start, line, col, ref best);
                SearchExprForNameRef(nf.Limit, line, col, ref best);
                if (nf.Step != null) SearchExprForNameRef(nf.Step, line, col, ref best);
                SearchStmtListForNameRef(nf.Body, line, col, ref best);
                break;
            case GenericForStmt gf:
                foreach (var vn in gf.VarNames) CheckNameRef(vn, line, col, ref best);
                foreach (var iter in gf.Iterators) SearchExprForNameRef(iter, line, col, ref best);
                SearchStmtListForNameRef(gf.Body, line, col, ref best);
                break;
            case ReturnStmt ret:
                foreach (var v in ret.Values) SearchExprForNameRef(v, line, col, ref best);
                break;
            case ImportStmt imp:
                CheckNameRef(imp.Module, line, col, ref best);
                CheckNameRef(imp.Alias, line, col, ref best);
                foreach (var s in imp.Specifiers)
                {
                    CheckNameRef(s.Name, line, col, ref best);
                    CheckNameRef(s.Alias, line, col, ref best);
                }
                break;
            case ExportStmt exp:
                SearchStmtForNameRef(exp.Declaration, line, col, ref best);
                break;
            case GotoStmt gs:
                CheckNameRef(gs.LabelName, line, col, ref best);
                break;
            case LabelStmt ls:
                CheckNameRef(ls.Name, line, col, ref best);
                break;
        }
    }

    private static void SearchExprForNameRef(Expr expr, int line, int col, ref NameRef? best)
    {
        if (!Contains(expr.Span, line, col)) return;

        switch (expr)
        {
            case NameExpr ne:
                CheckNameRef(ne.Name, line, col, ref best);
                break;
            case ParenExpr pe:
                SearchExprForNameRef(pe.Inner, line, col, ref best);
                break;
            case BinaryExpr bin:
                SearchExprForNameRef(bin.Left, line, col, ref best);
                SearchExprForNameRef(bin.Right, line, col, ref best);
                break;
            case UnaryExpr un:
                SearchExprForNameRef(un.Operand, line, col, ref best);
                break;
            case DotAccessExpr dot:
                SearchExprForNameRef(dot.Object, line, col, ref best);
                CheckNameRef(dot.FieldName, line, col, ref best);
                break;
            case IndexAccessExpr idx:
                SearchExprForNameRef(idx.Object, line, col, ref best);
                SearchExprForNameRef(idx.Index, line, col, ref best);
                break;
            case FunctionCallExpr call:
                SearchExprForNameRef(call.Callee, line, col, ref best);
                foreach (var a in call.Arguments) SearchExprForNameRef(a, line, col, ref best);
                break;
            case MethodCallExpr mc:
                SearchExprForNameRef(mc.Object, line, col, ref best);
                CheckNameRef(mc.MethodName, line, col, ref best);
                foreach (var a in mc.Arguments) SearchExprForNameRef(a, line, col, ref best);
                break;
            case FunctionDefExpr fd:
                foreach (var p in fd.Parameters) CheckNameRef(p.Name, line, col, ref best);
                SearchStmtListForNameRef(fd.Body, line, col, ref best);
                if (fd.ReturnStmt != null) SearchStmtForNameRef(fd.ReturnStmt, line, col, ref best);
                break;
            case TableConstructorExpr tc:
                foreach (var f in tc.Fields)
                {
                    CheckNameRef(f.Name, line, col, ref best);
                    if (f.Key != null) SearchExprForNameRef(f.Key, line, col, ref best);
                    SearchExprForNameRef(f.Value, line, col, ref best);
                }
                break;
        }
    }

    #endregion

    #region Collect NameRefs

    private static void CollectFromStmtList(List<Stmt> stmts, List<NameRef> refs)
    {
        foreach (var stmt in stmts) CollectFromStmt(stmt, refs);
    }

    private static void AddRef(NameRef? nr, List<NameRef> refs)
    {
        if (nr != null && nr.Sym != SymID.Invalid) refs.Add(nr);
    }

    private static void CollectFromStmt(Stmt stmt, List<NameRef> refs)
    {
        switch (stmt)
        {
            case FunctionDecl fd:
                foreach (var n in fd.NamePath) AddRef(n, refs);
                AddRef(fd.MethodName, refs);
                foreach (var p in fd.Parameters) AddRef(p.Name, refs);
                CollectFromStmtList(fd.Body, refs);
                if (fd.ReturnStmt != null) CollectFromStmt(fd.ReturnStmt, refs);
                break;
            case LocalFunctionDecl lfd:
                AddRef(lfd.Name, refs);
                foreach (var p in lfd.Parameters) AddRef(p.Name, refs);
                CollectFromStmtList(lfd.Body, refs);
                if (lfd.ReturnStmt != null) CollectFromStmt(lfd.ReturnStmt, refs);
                break;
            case LocalDecl ld:
                foreach (var v in ld.Variables) AddRef(v.Name, refs);
                foreach (var v in ld.Values) CollectFromExpr(v, refs);
                break;
            case AssignStmt a:
                foreach (var t in a.Targets) CollectFromExpr(t, refs);
                foreach (var v in a.Values) CollectFromExpr(v, refs);
                break;
            case ExprStmt es:
                CollectFromExpr(es.Expression, refs);
                break;
            case DoBlockStmt db:
                CollectFromStmtList(db.Body, refs);
                break;
            case WhileStmt ws:
                CollectFromExpr(ws.Condition, refs);
                CollectFromStmtList(ws.Body, refs);
                break;
            case RepeatStmt rs:
                CollectFromStmtList(rs.Body, refs);
                CollectFromExpr(rs.Condition, refs);
                break;
            case IfStmt ifs:
                CollectFromExpr(ifs.Condition, refs);
                CollectFromStmtList(ifs.Body, refs);
                foreach (var ei in ifs.ElseIfs)
                {
                    CollectFromExpr(ei.Condition, refs);
                    CollectFromStmtList(ei.Body, refs);
                }
                if (ifs.ElseBody != null) CollectFromStmtList(ifs.ElseBody, refs);
                break;
            case NumericForStmt nf:
                AddRef(nf.VarName, refs);
                CollectFromExpr(nf.Start, refs);
                CollectFromExpr(nf.Limit, refs);
                if (nf.Step != null) CollectFromExpr(nf.Step, refs);
                CollectFromStmtList(nf.Body, refs);
                break;
            case GenericForStmt gf:
                foreach (var vn in gf.VarNames) AddRef(vn, refs);
                foreach (var iter in gf.Iterators) CollectFromExpr(iter, refs);
                CollectFromStmtList(gf.Body, refs);
                break;
            case ReturnStmt ret:
                foreach (var v in ret.Values) CollectFromExpr(v, refs);
                break;
            case ImportStmt imp:
                AddRef(imp.Module, refs);
                AddRef(imp.Alias, refs);
                foreach (var s in imp.Specifiers) { AddRef(s.Name, refs); AddRef(s.Alias, refs); }
                break;
            case ExportStmt exp:
                CollectFromStmt(exp.Declaration, refs);
                break;
            case GotoStmt gs:
                AddRef(gs.LabelName, refs);
                break;
            case LabelStmt ls:
                AddRef(ls.Name, refs);
                break;
        }
    }

    private static void CollectFromExpr(Expr expr, List<NameRef> refs)
    {
        switch (expr)
        {
            case NameExpr ne:
                AddRef(ne.Name, refs);
                break;
            case ParenExpr pe:
                CollectFromExpr(pe.Inner, refs);
                break;
            case BinaryExpr bin:
                CollectFromExpr(bin.Left, refs);
                CollectFromExpr(bin.Right, refs);
                break;
            case UnaryExpr un:
                CollectFromExpr(un.Operand, refs);
                break;
            case DotAccessExpr dot:
                CollectFromExpr(dot.Object, refs);
                AddRef(dot.FieldName, refs);
                break;
            case IndexAccessExpr idx:
                CollectFromExpr(idx.Object, refs);
                CollectFromExpr(idx.Index, refs);
                break;
            case FunctionCallExpr call:
                CollectFromExpr(call.Callee, refs);
                foreach (var a in call.Arguments) CollectFromExpr(a, refs);
                break;
            case MethodCallExpr mc:
                CollectFromExpr(mc.Object, refs);
                AddRef(mc.MethodName, refs);
                foreach (var a in mc.Arguments) CollectFromExpr(a, refs);
                break;
            case FunctionDefExpr fd:
                foreach (var p in fd.Parameters) AddRef(p.Name, refs);
                CollectFromStmtList(fd.Body, refs);
                if (fd.ReturnStmt != null) CollectFromStmt(fd.ReturnStmt, refs);
                break;
            case TableConstructorExpr tc:
                foreach (var f in tc.Fields)
                {
                    AddRef(f.Name, refs);
                    if (f.Key != null) CollectFromExpr(f.Key, refs);
                    CollectFromExpr(f.Value, refs);
                }
                break;
        }
    }

    #endregion

    #region Node Registry

    private static void RegisterStmtList(List<Stmt> stmts, Dictionary<NodeID, Node> reg)
    {
        foreach (var s in stmts) RegisterStmt(s, reg);
    }

    private static void RegisterStmt(Stmt stmt, Dictionary<NodeID, Node> reg)
    {
        reg[stmt.ID] = stmt;
        switch (stmt)
        {
            case FunctionDecl fd:
                foreach (var p in fd.Parameters) reg[p.ID] = p;
                RegisterStmtList(fd.Body, reg);
                if (fd.ReturnStmt != null) RegisterStmt(fd.ReturnStmt, reg);
                break;
            case LocalFunctionDecl lfd:
                foreach (var p in lfd.Parameters) reg[p.ID] = p;
                RegisterStmtList(lfd.Body, reg);
                if (lfd.ReturnStmt != null) RegisterStmt(lfd.ReturnStmt, reg);
                break;
            case LocalDecl ld:
                foreach (var v in ld.Values) RegisterExpr(v, reg);
                break;
            case AssignStmt a:
                foreach (var t in a.Targets) RegisterExpr(t, reg);
                foreach (var v in a.Values) RegisterExpr(v, reg);
                break;
            case ExprStmt es:
                RegisterExpr(es.Expression, reg);
                break;
            case DoBlockStmt db:
                RegisterStmtList(db.Body, reg);
                break;
            case WhileStmt ws:
                RegisterExpr(ws.Condition, reg);
                RegisterStmtList(ws.Body, reg);
                break;
            case RepeatStmt rs:
                RegisterStmtList(rs.Body, reg);
                RegisterExpr(rs.Condition, reg);
                break;
            case IfStmt ifs:
                RegisterExpr(ifs.Condition, reg);
                RegisterStmtList(ifs.Body, reg);
                foreach (var ei in ifs.ElseIfs) RegisterStmtList(ei.Body, reg);
                if (ifs.ElseBody != null) RegisterStmtList(ifs.ElseBody, reg);
                break;
            case NumericForStmt nf:
                RegisterExpr(nf.Start, reg);
                RegisterExpr(nf.Limit, reg);
                if (nf.Step != null) RegisterExpr(nf.Step, reg);
                RegisterStmtList(nf.Body, reg);
                break;
            case GenericForStmt gf:
                foreach (var iter in gf.Iterators) RegisterExpr(iter, reg);
                RegisterStmtList(gf.Body, reg);
                break;
            case ReturnStmt ret:
                foreach (var v in ret.Values) RegisterExpr(v, reg);
                break;
            case ExportStmt exp:
                RegisterStmt(exp.Declaration, reg);
                break;
        }
    }

    private static void RegisterExpr(Expr expr, Dictionary<NodeID, Node> reg)
    {
        reg[expr.ID] = expr;
        switch (expr)
        {
            case ParenExpr pe: RegisterExpr(pe.Inner, reg); break;
            case BinaryExpr bin: RegisterExpr(bin.Left, reg); RegisterExpr(bin.Right, reg); break;
            case UnaryExpr un: RegisterExpr(un.Operand, reg); break;
            case DotAccessExpr dot: RegisterExpr(dot.Object, reg); break;
            case IndexAccessExpr idx: RegisterExpr(idx.Object, reg); RegisterExpr(idx.Index, reg); break;
            case FunctionCallExpr call:
                RegisterExpr(call.Callee, reg);
                foreach (var a in call.Arguments) RegisterExpr(a, reg);
                break;
            case MethodCallExpr mc:
                RegisterExpr(mc.Object, reg);
                foreach (var a in mc.Arguments) RegisterExpr(a, reg);
                break;
            case FunctionDefExpr fd:
                foreach (var p in fd.Parameters) reg[p.ID] = p;
                RegisterStmtList(fd.Body, reg);
                if (fd.ReturnStmt != null) RegisterStmt(fd.ReturnStmt, reg);
                break;
            case TableConstructorExpr tc:
                foreach (var f in tc.Fields)
                {
                    if (f.Key != null) RegisterExpr(f.Key, reg);
                    RegisterExpr(f.Value, reg);
                }
                break;
        }
    }

    #endregion
}
