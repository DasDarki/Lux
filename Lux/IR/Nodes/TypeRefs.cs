using Lux.Diagnostics;

namespace Lux.IR;

public abstract class TypeRef(NodeID id, TextSpan span, TypeKind kind) : Node(id, span)
{
    public TypeKind Kind { get; } = kind;
    
    public TypID ResolvedType { get; set; } = TypID.Invalid;
}

public sealed class PrimitiveTypeRef(NodeID id, TextSpan span, TypeKind kind) : TypeRef(id, span, kind);

public sealed class NamedTypeRef(NodeID id, TextSpan span, NameRef name) : TypeRef(id, span, TypeKind.Enum)
{
    public NameRef Name { get; } = name;
}

public sealed class ArrayTypeRef(NodeID id, TextSpan span, TypeRef elementType) : TypeRef(id, span, TypeKind.TableArray)
{
    public TypeRef ElementType { get; } = elementType;
}

public sealed class NullableTypeRef(NodeID id, TextSpan span, TypeRef innerType) : TypeRef(id, span, innerType.Kind)
{
    public TypeRef InnerType { get; } = innerType;
}

public sealed class UnionTypeRef(NodeID id, TextSpan span, List<TypeRef> types) : TypeRef(id, span, TypeKind.Union)
{
    public List<TypeRef> Types { get; } = types;
}

public sealed class FunctionTypeRef(NodeID id, TextSpan span, List<TypeRef> paramTypes, TypeRef returnType) : TypeRef(id, span, TypeKind.Function)
{
    public List<TypeRef> ParamTypes { get; } = paramTypes;
    public TypeRef ReturnType { get; } = returnType;
}

public sealed class MapTypeRef(NodeID id, TextSpan span, TypeRef keyType, TypeRef valueType) : TypeRef(id, span, TypeKind.TableMap)
{
    public TypeRef KeyType { get; } = keyType;
    public TypeRef ValueType { get; } = valueType;
}

public sealed class StructTypeRef(NodeID id, TextSpan span, List<StructTypeField> fields) : TypeRef(id, span, TypeKind.Struct)
{
    public List<StructTypeField> Fields { get; } = fields;
}

public sealed class TupleTypeRef(NodeID id, TextSpan span, List<TypeRef> elementTypes) : TypeRef(id, span, TypeKind.Tuple)
{
    public List<TypeRef> ElementTypes { get; } = elementTypes;
}
