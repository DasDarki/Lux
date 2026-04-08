using Lux.Diagnostics;

namespace Lux.IR;

public sealed class NilLiteralExpr(NodeID id, TextSpan span) : Expr(id, span);

public sealed class BoolLiteralExpr(NodeID id, TextSpan span, bool value) : Expr(id, span)
{
    public bool Value { get; } = value;
}

public sealed class NumberLiteralExpr(NodeID id, TextSpan span, string raw, NumberKind kind) : Expr(id, span)
{
    public string Raw { get; } = raw;
    public NumberKind Kind { get; } = kind;
}

public sealed class StringLiteralExpr(NodeID id, TextSpan span, string value) : Expr(id, span)
{
    public string Value { get; } = value;
}

public sealed class InterpolatedStringExpr(NodeID id, TextSpan span, List<InterpStringPart> parts) : Expr(id, span)
{
    public List<InterpStringPart> Parts { get; } = parts;
}

public abstract class InterpStringPart(TextSpan span)
{
    public TextSpan Span { get; } = span;
}

public sealed class InterpTextPart(TextSpan span, string text) : InterpStringPart(span)
{
    public string Text { get; } = text;
}

public sealed class InterpExprPart(TextSpan span, Expr expression) : InterpStringPart(span)
{
    public Expr Expression { get; } = expression;
}

public sealed class VarargExpr(NodeID id, TextSpan span) : Expr(id, span);

public sealed class FunctionDefExpr(
    NodeID id, TextSpan span,
    List<Parameter> parameters, TypeRef? returnType,
    List<Stmt> body, ReturnStmt? returnStmt
) : Expr(id, span)
{
    public List<Parameter> Parameters { get; } = parameters;
    public TypeRef? ReturnType { get; } = returnType;
    public List<Stmt> Body { get; } = body;
    public ReturnStmt? ReturnStmt { get; } = returnStmt;
}

public sealed class BinaryExpr(NodeID id, TextSpan span, BinaryOp op, Expr left, Expr right) : Expr(id, span)
{
    public BinaryOp Op { get; } = op;
    public Expr Left { get; } = left;
    public Expr Right { get; } = right;
}

public sealed class UnaryExpr(NodeID id, TextSpan span, UnaryOp op, Expr operand) : Expr(id, span)
{
    public UnaryOp Op { get; } = op;
    public Expr Operand { get; } = operand;
}

public sealed class NameExpr(NodeID id, TextSpan span, NameRef name) : Expr(id, span)
{
    public NameRef Name { get; } = name;
}

public sealed class ParenExpr(NodeID id, TextSpan span, Expr inner) : Expr(id, span)
{
    public Expr Inner { get; } = inner;
}

public sealed class DotAccessExpr(NodeID id, TextSpan span, Expr obj, NameRef fieldName, bool isOptional = false) : Expr(id, span)
{
    public Expr Object { get; } = obj;
    public NameRef FieldName { get; } = fieldName;
    public bool IsOptional { get; } = isOptional;
}

public sealed class IndexAccessExpr(NodeID id, TextSpan span, Expr obj, Expr index) : Expr(id, span)
{
    public Expr Object { get; } = obj;
    public Expr Index { get; } = index;
}

public sealed class FunctionCallExpr(NodeID id, TextSpan span, Expr callee, List<Expr> arguments, bool isOptional = false) : Expr(id, span)
{
    public Expr Callee { get; } = callee;
    public List<Expr> Arguments { get; } = arguments;
    public bool IsOptional { get; } = isOptional;
}

public sealed class MethodCallExpr(NodeID id, TextSpan span, Expr obj, NameRef methodName, List<Expr> arguments) : Expr(id, span)
{
    public Expr Object { get; } = obj;
    public NameRef MethodName { get; } = methodName;
    public List<Expr> Arguments { get; } = arguments;
}

public sealed class NonNilAssertExpr(NodeID id, TextSpan span, Expr inner) : Expr(id, span)
{
    public Expr Inner { get; } = inner;
}

public sealed class TableConstructorExpr(NodeID id, TextSpan span, List<TableField> fields) : Expr(id, span)
{
    public List<TableField> Fields { get; } = fields;
}
