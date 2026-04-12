namespace Lux.IR;

internal partial class IRVisitor
{
    public override Node VisitFunctionDecl(LuxParser.FunctionDeclContext context)
    {
        var (namePath, methodName) = VisitFuncNameContent(context.funcName());
        var (parameters, returnType, body, ret) = VisitFuncBodyContent(context.funcBody());
        return new FunctionDecl(NewNodeID, SpanFromCtx(context), namePath, methodName, parameters, returnType, body, ret);
    }

    public override Node VisitLocalFunctionDecl(LuxParser.LocalFunctionDeclContext context)
    {
        var (parameters, returnType, body, ret) = VisitFuncBodyContent(context.funcBody());
        return new LocalFunctionDecl(NewNodeID, SpanFromCtx(context), NameRefFromTerm(context.NAME()), parameters, returnType, body, ret);
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
        return new DeclareFunctionDecl(NewNodeID, SpanFromCtx(context), namePath, methodName, parameters, returnType);
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
        return new DeclareFunctionDecl(NewNodeID, SpanFromCtx(context), namePath, methodName, parameters, returnType);
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
}
