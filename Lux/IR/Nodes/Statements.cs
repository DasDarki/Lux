using Lux.Diagnostics;

namespace Lux.IR;

public sealed class AssignStmt(NodeID id, TextSpan span, List<Expr> targets, List<Expr> values) : Stmt(id, span)
{
    public List<Expr> Targets { get; } = targets;
    public List<Expr> Values { get; } = values;
}

public sealed class ExprStmt(NodeID id, TextSpan span, Expr expression) : Stmt(id, span)
{
    public Expr Expression { get; } = expression;
}

public sealed class LabelStmt(NodeID id, TextSpan span, NameRef name) : Stmt(id, span)
{
    public NameRef Name { get; } = name;
}

public sealed class BreakStmt(NodeID id, TextSpan span) : Stmt(id, span);

public sealed class GotoStmt(NodeID id, TextSpan span, NameRef labelName) : Stmt(id, span)
{
    public NameRef LabelName { get; } = labelName;
}

public sealed class DoBlockStmt(NodeID id, TextSpan span, List<Stmt> body) : Stmt(id, span)
{
    public List<Stmt> Body { get; } = body;
}

public sealed class WhileStmt(NodeID id, TextSpan span, Expr condition, List<Stmt> body) : Stmt(id, span)
{
    public Expr Condition { get; } = condition;
    public List<Stmt> Body { get; } = body;
}

public sealed class RepeatStmt(NodeID id, TextSpan span, List<Stmt> body, Expr condition) : Stmt(id, span)
{
    public List<Stmt> Body { get; } = body;
    public Expr Condition { get; } = condition;
}

public sealed class IfStmt(
    NodeID id, TextSpan span,
    Expr condition, List<Stmt> body,
    List<ElseIfClause> elseIfs, List<Stmt>? elseBody
) : Stmt(id, span)
{
    public Expr Condition { get; } = condition;
    public List<Stmt> Body { get; } = body;
    public List<ElseIfClause> ElseIfs { get; } = elseIfs;
    public List<Stmt>? ElseBody { get; } = elseBody;
}

public sealed class NumericForStmt(
    NodeID id, TextSpan span,
    NameRef varName, Expr start, Expr limit, Expr? step,
    List<Stmt> body
) : Stmt(id, span)
{
    public NameRef VarName { get; } = varName;
    public Expr Start { get; } = start;
    public Expr Limit { get; } = limit;
    public Expr? Step { get; } = step;
    public List<Stmt> Body { get; } = body;
}

public sealed class GenericForStmt(
    NodeID id, TextSpan span,
    List<NameRef> varNames, List<Expr> iterators,
    List<Stmt> body
) : Stmt(id, span)
{
    public List<NameRef> VarNames { get; } = varNames;
    public List<Expr> Iterators { get; } = iterators;
    public List<Stmt> Body { get; } = body;
}

public sealed class ReturnStmt(NodeID id, TextSpan span, List<Expr> values) : Stmt(id, span)
{
    public List<Expr> Values { get; } = values;
}

public sealed class ImportStmt(NodeID id, TextSpan span, ImportKind kind, NameRef module) : Stmt(id, span)
{
    public ImportKind Kind { get; } = kind;
    public NameRef Module { get; } = module;
    public List<ImportSpecifier> Specifiers { get; init; } = [];
    public NameRef? Alias { get; init; }
}

public sealed class ExportStmt(NodeID id, TextSpan span, Decl declaration) : Stmt(id, span)
{
    public Decl Declaration { get; } = declaration;
}
