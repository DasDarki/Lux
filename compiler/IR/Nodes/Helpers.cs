using Lux.Diagnostics;

namespace Lux.IR;

public sealed class Parameter(NodeID id, NameRef name, TypeRef? typeAnnotation, bool isVararg, Expr? defaultValue, TextSpan span) : Node(id, span)
{
    public NameRef Name { get; } = name;
    public TypeRef? TypeAnnotation { get; } = typeAnnotation;
    public bool IsVararg { get; } = isVararg;
    public Expr? DefaultValue { get; } = defaultValue;
}

public sealed class AttribVar(NameRef name, string? attribute, TypeRef? typeAnnotation, TextSpan span)
{
    public NameRef Name { get; } = name;
    public string? Attribute { get; } = attribute;
    public TypeRef? TypeAnnotation { get; } = typeAnnotation;
    public TextSpan Span { get; } = span;
}

public sealed class ImportSpecifier(NodeID id, NameRef name, NameRef? alias, TextSpan span)
{
    public NameRef Name { get; } = name;
    public NameRef? Alias { get; } = alias;
    public TextSpan Span { get; } = span;
}

public sealed class ElseIfClause(Expr condition, List<Stmt> body, TextSpan span)
{
    public Expr Condition { get; } = condition;
    public List<Stmt> Body { get; } = body;
    public TextSpan Span { get; } = span;
}

public sealed class TableField(TableFieldKind kind, Expr? key, NameRef? name, Expr value, TextSpan span)
{
    public TableFieldKind Kind { get; } = kind;
    public Expr? Key { get; } = key;
    public NameRef? Name { get; } = name;
    public Expr Value { get; } = value;
    public TextSpan Span { get; } = span;
}

public sealed class StructTypeField(NameRef name, TypeRef type, bool isMeta, TextSpan span)
{
    public NameRef Name { get; } = name;
    public TypeRef Type { get; } = type;
    public bool IsMeta { get; } = isMeta;
    public TextSpan Span { get; } = span;
}

public enum MatchPatternKind { Value, TypeBinding, Wildcard }

public sealed class MatchPattern(MatchPatternKind kind, Expr? valueExpr, TypeRef? typeRef, NameRef? binding, TextSpan span)
{
    public MatchPatternKind Kind { get; } = kind;
    public Expr? ValueExpr { get; } = valueExpr;
    public TypeRef? TypeRef { get; } = typeRef;
    public NameRef? Binding { get; } = binding;
    public TextSpan Span { get; } = span;
}

public sealed class MatchArm(MatchPattern pattern, Expr? guard, List<Stmt> body, TextSpan span)
{
    public MatchPattern Pattern { get; } = pattern;
    public Expr? Guard { get; } = guard;
    public List<Stmt> Body { get; } = body;
    public TextSpan Span { get; } = span;
}

public sealed class MatchExprArm(MatchPattern pattern, Expr? guard, Expr value, TextSpan span)
{
    public MatchPattern Pattern { get; } = pattern;
    public Expr? Guard { get; } = guard;
    public Expr Value { get; } = value;
    public TextSpan Span { get; } = span;
}

public sealed class ClassFieldNode(
    NameRef name, TypeRef? typeAnnotation, Expr? defaultValue,
    bool isLocal, bool isStatic, bool isProtected, TextSpan span)
{
    public NameRef Name { get; } = name;
    public TypeRef? TypeAnnotation { get; } = typeAnnotation;
    public Expr? DefaultValue { get; } = defaultValue;
    public bool IsLocal { get; } = isLocal;
    public bool IsStatic { get; } = isStatic;
    public bool IsProtected { get; } = isProtected;
    public TextSpan Span { get; } = span;
}

public sealed class ClassMethodNode(
    NameRef name, List<Parameter> parameters, TypeRef? returnType,
    List<Stmt> body, ReturnStmt? returnStmt,
    bool isLocal, bool isStatic, bool isAsync,
    bool isProtected, bool isOverride, bool isAbstract,
    TextSpan span)
{
    public NameRef Name { get; } = name;
    public List<Parameter> Parameters { get; } = parameters;
    public TypeRef? ReturnType { get; } = returnType;
    public List<Stmt> Body { get; } = body;
    public ReturnStmt? ReturnStmt { get; } = returnStmt;
    public bool IsLocal { get; } = isLocal;
    public bool IsStatic { get; } = isStatic;
    public bool IsAsync { get; } = isAsync;
    public bool IsProtected { get; } = isProtected;
    public bool IsOverride { get; } = isOverride;
    public bool IsAbstract { get; } = isAbstract;
    public TextSpan Span { get; } = span;
    public List<TypeParamDef> TypeParams { get; set; } = [];
}

public sealed class ClassConstructorNode(
    List<Parameter> parameters, List<Stmt> body, ReturnStmt? returnStmt, TextSpan span)
{
    public List<Parameter> Parameters { get; } = parameters;
    public List<Stmt> Body { get; } = body;
    public ReturnStmt? ReturnStmt { get; } = returnStmt;
    public TextSpan Span { get; } = span;
}

public enum AccessorKind { Getter, Setter }

public sealed class ClassAccessorNode(
    AccessorKind kind, NameRef name,
    List<Parameter> parameters, TypeRef? returnType,
    List<Stmt> body, ReturnStmt? returnStmt,
    bool isOverride, TextSpan span)
{
    public AccessorKind Kind { get; } = kind;
    public NameRef Name { get; } = name;
    public List<Parameter> Parameters { get; } = parameters;
    public TypeRef? ReturnType { get; } = returnType;
    public List<Stmt> Body { get; } = body;
    public ReturnStmt? ReturnStmt { get; } = returnStmt;
    public bool IsOverride { get; } = isOverride;
    public TextSpan Span { get; } = span;
}

public sealed class InterfaceFieldNode(NameRef name, TypeRef typeAnnotation, TextSpan span)
{
    public NameRef Name { get; } = name;
    public TypeRef TypeAnnotation { get; } = typeAnnotation;
    public TextSpan Span { get; } = span;
}

public sealed class InterfaceMethodNode(
    NameRef name, List<Parameter> parameters, TypeRef? returnType,
    bool isAsync, TextSpan span)
{
    public NameRef Name { get; } = name;
    public List<Parameter> Parameters { get; } = parameters;
    public TypeRef? ReturnType { get; } = returnType;
    public bool IsAsync { get; } = isAsync;
    public TextSpan Span { get; } = span;
    public List<TypeParamDef> TypeParams { get; set; } = [];
}
