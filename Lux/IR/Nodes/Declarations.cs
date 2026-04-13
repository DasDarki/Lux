using Lux.Diagnostics;

namespace Lux.IR;

public sealed class FunctionDecl(
    NodeID id, TextSpan span,
    List<NameRef> namePath, NameRef? methodName,
    List<Parameter> parameters, TypeRef? returnType,
    List<Stmt> body, ReturnStmt? returnStmt,
    bool isAsync = false
) : Decl(id, span)
{
    public List<NameRef> NamePath { get; } = namePath;
    public NameRef? MethodName { get; } = methodName;
    public List<Parameter> Parameters { get; } = parameters;
    public TypeRef? ReturnType { get; } = returnType;
    public List<Stmt> Body { get; } = body;
    public ReturnStmt? ReturnStmt { get; } = returnStmt;
    public bool IsAsync { get; } = isAsync;
}

public sealed class LocalFunctionDecl(
    NodeID id, TextSpan span,
    NameRef name,
    List<Parameter> parameters, TypeRef? returnType,
    List<Stmt> body, ReturnStmt? returnStmt,
    bool isAsync = false
) : Decl(id, span)
{
    public NameRef Name { get; } = name;
    public List<Parameter> Parameters { get; } = parameters;
    public TypeRef? ReturnType { get; } = returnType;
    public List<Stmt> Body { get; } = body;
    public ReturnStmt? ReturnStmt { get; } = returnStmt;
    public bool IsAsync { get; } = isAsync;
}

public sealed class LocalDecl(
    NodeID id, TextSpan span,
    List<AttribVar> variables, List<Expr> values,
    bool isMutable = false
) : Decl(id, span)
{
    public List<AttribVar> Variables { get; } = variables;
    public List<Expr> Values { get; } = values;
    public bool IsMutable { get; } = isMutable;
}

public sealed class DeclareFunctionDecl(
    NodeID id, TextSpan span,
    List<NameRef> namePath, NameRef? methodName,
    List<Parameter> parameters, TypeRef? returnType,
    bool isAsync = false
) : Decl(id, span)
{
    public List<NameRef> NamePath { get; } = namePath;
    public NameRef? MethodName { get; } = methodName;
    public List<Parameter> Parameters { get; } = parameters;
    public TypeRef? ReturnType { get; } = returnType;
    public bool IsAsync { get; } = isAsync;
}

public sealed class DeclareVariableDecl(
    NodeID id, TextSpan span,
    NameRef name, TypeRef typeAnnotation
) : Decl(id, span)
{
    public NameRef Name { get; } = name;
    public TypeRef TypeAnnotation { get; } = typeAnnotation;
}

public sealed class DeclareModuleDecl(
    NodeID id, TextSpan span,
    NameRef moduleName, List<Decl> members
) : Decl(id, span)
{
    public NameRef ModuleName { get; } = moduleName;
    public List<Decl> Members { get; } = members;
}

public sealed class EnumDecl(
    NodeID id, TextSpan span,
    NameRef name, List<EnumMember> members, bool isDeclare
) : Decl(id, span)
{
    public NameRef Name { get; } = name;
    public List<EnumMember> Members { get; } = members;
    public bool IsDeclare { get; } = isDeclare;
}

public sealed class EnumMember(NameRef name, Expr? value, TypeRef? typeAnnotation, TextSpan span)
{
    public NameRef Name { get; } = name;
    public Expr? Value { get; set; } = value;
    public TypeRef? TypeAnnotation { get; } = typeAnnotation;
    public TextSpan Span { get; } = span;
}
