namespace Lux.IR;

internal partial class IRVisitor
{
    public override Node VisitFunctionDecl(LuxParser.FunctionDeclContext context)
    {
        var (namePath, methodName) = VisitFuncNameContent(context.funcName());
        var (parameters, returnType, body, ret) = VisitFuncBodyContent(context.funcBody());
        var isAsync = context.ASYNC() != null;
        return new FunctionDecl(NewNodeID, SpanFromCtx(context), namePath, methodName, parameters, returnType, body, ret, isAsync);
    }

    public override Node VisitLocalFunctionDecl(LuxParser.LocalFunctionDeclContext context)
    {
        var (parameters, returnType, body, ret) = VisitFuncBodyContent(context.funcBody());
        var isAsync = context.ASYNC() != null;
        return new LocalFunctionDecl(NewNodeID, SpanFromCtx(context), NameRefFromTerm(context.NAME()), parameters, returnType, body, ret, isAsync);
    }

    public override Node VisitLocalDecl(LuxParser.LocalDeclContext context)
    {
        var vars = VisitAttribNameListContent(context.attribNameList());
        var values = context.exprList()?.expr().Select(e => (Expr)Visit(e)).ToList() ?? [];
        var isMutable = context.MUT() != null;
        return new LocalDecl(NewNodeID, SpanFromCtx(context), vars, values, isMutable);
    }

    public override Node VisitDeclareStat(LuxParser.DeclareStatContext context)
        => Visit(context.declareBody());

    public override Node VisitDeclareFunction(LuxParser.DeclareFunctionContext context)
    {
        var (namePath, methodName) = VisitFuncNameContent(context.funcName());
        var (parameters, returnType) = VisitFuncSignatureContent(context.funcSignature());
        var isAsync = context.ASYNC() != null;
        return new DeclareFunctionDecl(NewNodeID, SpanFromCtx(context), namePath, methodName, parameters, returnType, isAsync);
    }

    public override Node VisitDeclareVariable(LuxParser.DeclareVariableContext context)
    {
        var typeRef = (TypeRef)Visit(context.typeAnnotation().typeExpr());
        return new DeclareVariableDecl(NewNodeID, SpanFromCtx(context), NameRefFromTerm(context.NAME()), typeRef);
    }

    public override Node VisitDeclareModule(LuxParser.DeclareModuleContext context)
    {
        var moduleName = NameRefFromString(context.str());
        var members = new List<Decl>();

        foreach (var member in context.declareModuleBlock().declareModuleMember())
            members.Add((Decl)Visit(member));

        return new DeclareModuleDecl(NewNodeID, SpanFromCtx(context), moduleName, members);
    }

    public override Node VisitModuleDeclareFunction(LuxParser.ModuleDeclareFunctionContext context)
    {
        var (namePath, methodName) = VisitFuncNameContent(context.funcName());
        var (parameters, returnType) = VisitFuncSignatureContent(context.funcSignature());
        var isAsync = context.ASYNC() != null;
        return new DeclareFunctionDecl(NewNodeID, SpanFromCtx(context), namePath, methodName, parameters, returnType, isAsync);
    }

    public override Node VisitModuleDeclareVariable(LuxParser.ModuleDeclareVariableContext context)
    {
        var typeRef = (TypeRef)Visit(context.typeAnnotation().typeExpr());
        return new DeclareVariableDecl(NewNodeID, SpanFromCtx(context), NameRefFromTerm(context.NAME()), typeRef);
    }

    public override Node VisitEnumDecl(LuxParser.EnumDeclContext context)
    {
        var name = NameRefFromTerm(context.NAME());
        var members = new List<EnumMember>();
        foreach (var memberCtx in context.enumMember())
        {
            var memberName = NameRefFromTerm(memberCtx.NAME());
            Expr? value = memberCtx.expr() != null ? (Expr)Visit(memberCtx.expr()) : null;
            members.Add(new EnumMember(memberName, value, null, SpanFromCtx(memberCtx)));
        }
        return new EnumDecl(NewNodeID, SpanFromCtx(context), name, members, isDeclare: false);
    }

    public override Node VisitDeclareEnum(LuxParser.DeclareEnumContext context)
    {
        var name = NameRefFromTerm(context.NAME());
        var members = new List<EnumMember>();
        foreach (var memberCtx in context.declareEnumMember())
        {
            var memberName = NameRefFromTerm(memberCtx.NAME());
            TypeRef? typeAnn = memberCtx.typeAnnotation() != null
                ? (TypeRef)Visit(memberCtx.typeAnnotation().typeExpr())
                : null;
            members.Add(new EnumMember(memberName, null, typeAnn, SpanFromCtx(memberCtx)));
        }
        return new EnumDecl(NewNodeID, SpanFromCtx(context), name, members, isDeclare: true);
    }

    public override Node VisitModuleDeclareEnum(LuxParser.ModuleDeclareEnumContext context)
    {
        var name = NameRefFromTerm(context.NAME());
        var members = new List<EnumMember>();
        foreach (var memberCtx in context.declareEnumMember())
        {
            var memberName = NameRefFromTerm(memberCtx.NAME());
            TypeRef? typeAnn = memberCtx.typeAnnotation() != null
                ? (TypeRef)Visit(memberCtx.typeAnnotation().typeExpr())
                : null;
            members.Add(new EnumMember(memberName, null, typeAnn, SpanFromCtx(memberCtx)));
        }
        return new EnumDecl(NewNodeID, SpanFromCtx(context), name, members, isDeclare: true);
    }

    public override Node VisitClassDecl(LuxParser.ClassDeclContext context)
    {
        var names = context.NAME();
        var name = NameRefFromTerm(names[0]);

        NameRef? baseClass = null;
        var interfaces = new List<NameRef>();

        var nameIdx = 1;
        if (context.EXTENDS() != null)
        {
            baseClass = NameRefFromTerm(names[nameIdx]);
            nameIdx++;
        }

        if (context.IMPLEMENTS() != null)
        {
            for (var i = nameIdx; i < names.Length; i++)
                interfaces.Add(NameRefFromTerm(names[i]));
        }

        var fields = new List<ClassFieldNode>();
        var methods = new List<ClassMethodNode>();
        ClassConstructorNode? constructor = null;
        var accessors = new List<ClassAccessorNode>();

        foreach (var member in context.classMember())
        {
            switch (member)
            {
                case LuxParser.ClassFieldMemberContext field:
                {
                    var isLocal = field.LOCAL() != null;
                    var isStatic = field.STATIC() != null;
                    var fieldName = NameRefFromTerm(field.NAME());
                    TypeRef? typeAnn = field.typeAnnotation() != null
                        ? (TypeRef)Visit(field.typeAnnotation().typeExpr())
                        : null;
                    Expr? defaultValue = field.expr() != null ? (Expr)Visit(field.expr()) : null;
                    fields.Add(new ClassFieldNode(fieldName, typeAnn, defaultValue, isLocal, isStatic, SpanFromCtx(field)));
                    break;
                }
                case LuxParser.ClassMethodMemberContext method:
                {
                    var isLocal = method.LOCAL() != null;
                    var isStatic = method.STATIC() != null;
                    var isAsync = method.ASYNC() != null;
                    var methodName = NameRefFromTerm(method.NAME());
                    var (parameters, returnType, body, ret) = VisitFuncBodyContent(method.funcBody());
                    methods.Add(new ClassMethodNode(methodName, parameters, returnType, body, ret, isLocal, isStatic, isAsync, SpanFromCtx(method)));
                    break;
                }
                case LuxParser.ClassConstructorMemberContext ctor:
                {
                    var (parameters, _, body, ret) = VisitFuncBodyContent(ctor.funcBody());
                    constructor = new ClassConstructorNode(parameters, body, ret, SpanFromCtx(ctor));
                    break;
                }
                case LuxParser.ClassAccessorMemberContext accessor:
                {
                    var kindName = accessor.NAME(0).GetText();
                    var propName = NameRefFromTerm(accessor.NAME(1));
                    var kind = kindName == "get" ? AccessorKind.Getter : AccessorKind.Setter;
                    var (parameters, returnType, body, ret) = VisitFuncBodyContent(accessor.funcBody());
                    accessors.Add(new ClassAccessorNode(kind, propName, parameters, returnType, body, ret, SpanFromCtx(accessor)));
                    break;
                }
            }
        }

        return new ClassDecl(NewNodeID, SpanFromCtx(context), name, baseClass, interfaces, fields, methods, constructor, accessors);
    }

    public override Node VisitInterfaceDecl(LuxParser.InterfaceDeclContext context)
    {
        var names = context.NAME();
        var name = NameRefFromTerm(names[0]);

        var baseInterfaces = new List<NameRef>();
        if (context.EXTENDS() != null)
        {
            for (var i = 1; i < names.Length; i++)
                baseInterfaces.Add(NameRefFromTerm(names[i]));
        }

        var fields = new List<InterfaceFieldNode>();
        var methods = new List<InterfaceMethodNode>();

        foreach (var member in context.interfaceMember())
        {
            switch (member)
            {
                case LuxParser.InterfaceFieldMemberContext field:
                {
                    var fieldName = NameRefFromTerm(field.NAME());
                    var typeAnn = (TypeRef)Visit(field.typeAnnotation().typeExpr());
                    fields.Add(new InterfaceFieldNode(fieldName, typeAnn, SpanFromCtx(field)));
                    break;
                }
                case LuxParser.InterfaceMethodMemberContext method:
                {
                    var isAsync = method.ASYNC() != null;
                    var methodName = NameRefFromTerm(method.NAME());
                    var (parameters, returnType) = VisitFuncSignatureContent(method.funcSignature());
                    methods.Add(new InterfaceMethodNode(methodName, parameters, returnType, isAsync, SpanFromCtx(method)));
                    break;
                }
            }
        }

        return new InterfaceDecl(NewNodeID, SpanFromCtx(context), name, baseInterfaces, fields, methods);
    }
}
