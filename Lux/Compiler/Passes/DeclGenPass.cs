using System.Text;
using Lux.IR;
using Type = Lux.IR.Type;

namespace Lux.Compiler.Passes;

public sealed class DeclGenPass() : Pass(PassName, PassScope.PerBuild, true, InferTypesPass.PassName)
{
    public const string PassName = "DeclGen";

    public override bool Run(PassContext context)
    {
        if (!context.Config.GenerateDeclarations) return true;

        var sourceRoot = Path.Combine(Environment.CurrentDirectory, context.Config.Source);
        var sb = new StringBuilder();
        var hasContent = false;

        foreach (var pkg in context.Pkgs)
        {
            foreach (var file in pkg.Files)
            {
                if (file.IsDeclarationFile) continue;

                var exports = CollectExports(file);
                if (exports.Count == 0) continue;

                var modulePath = DeriveModulePath(file, sourceRoot);
                if (hasContent) sb.AppendLine();
                EmitModuleDeclaration(sb, context, pkg, modulePath, exports);
                hasContent = true;
            }
        }

        if (hasContent)
            context.Cache["GeneratedDeclarations"] = sb.ToString();

        return true;
    }

    private static string DeriveModulePath(PreparsedFile file, string sourceRoot)
    {
        if (file.Filename == null) return "unknown";

        var fullPath = Path.GetFullPath(file.Filename);
        var fullRoot = Path.GetFullPath(sourceRoot);

        string relative;
        if (fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            relative = Path.GetRelativePath(fullRoot, fullPath);
        }
        else
        {
            relative = Path.GetFileName(fullPath);
        }

        relative = Path.ChangeExtension(relative, null);
        return relative.Replace('\\', '/');
    }

    private sealed record ExportedSymbol(string Name, NameRef NameRef, Decl Declaration);

    private static List<ExportedSymbol> CollectExports(PreparsedFile file)
    {
        var exports = new List<ExportedSymbol>();

        foreach (var stmt in file.Hir.Body)
        {
            if (stmt is not ExportStmt export) continue;

            switch (export.Declaration)
            {
                case FunctionDecl fd when fd.NamePath.Count > 0:
                    exports.Add(new ExportedSymbol(fd.NamePath[0].Name, fd.NamePath[0], fd));
                    break;
                case LocalFunctionDecl lfd:
                    exports.Add(new ExportedSymbol(lfd.Name.Name, lfd.Name, lfd));
                    break;
                case LocalDecl ld:
                    foreach (var v in ld.Variables)
                        exports.Add(new ExportedSymbol(v.Name.Name, v.Name, ld));
                    break;
            }
        }

        return exports;
    }

    private void EmitModuleDeclaration(StringBuilder sb, PassContext ctx, PackageContext pkg,
        string modulePath, List<ExportedSymbol> exports)
    {
        sb.AppendLine($"declare module \"{modulePath}\"");

        foreach (var export in exports)
        {
            switch (export.Declaration)
            {
                case FunctionDecl fd:
                    EmitFunctionDeclaration(sb, ctx, pkg, export.Name, fd);
                    break;
                case LocalFunctionDecl lfd:
                    EmitLocalFunctionDeclaration(sb, ctx, pkg, lfd);
                    break;
                case LocalDecl:
                    EmitVarDeclaration(sb, ctx, pkg, export.NameRef);
                    break;
            }
        }

        sb.AppendLine("end");
    }

    private void EmitFunctionDeclaration(StringBuilder sb, PassContext ctx, PackageContext pkg,
        string exportName, FunctionDecl fd)
    {
        sb.Append("    declare function ");
        sb.Append(exportName);

        if (fd.MethodName != null)
        {
            sb.Append(':');
            sb.Append(fd.MethodName.Name);
        }

        sb.Append('(');
        EmitParams(sb, ctx, pkg, fd.Parameters);
        sb.Append(')');
        EmitReturnType(sb, ctx, pkg, fd.NamePath[0]);
        sb.AppendLine();
    }

    private void EmitLocalFunctionDeclaration(StringBuilder sb, PassContext ctx, PackageContext pkg,
        LocalFunctionDecl lfd)
    {
        sb.Append("    declare function ");
        sb.Append(lfd.Name.Name);
        sb.Append('(');
        EmitParams(sb, ctx, pkg, lfd.Parameters);
        sb.Append(')');
        EmitReturnType(sb, ctx, pkg, lfd.Name);
        sb.AppendLine();
    }

    private void EmitVarDeclaration(StringBuilder sb, PassContext ctx, PackageContext pkg, NameRef nameRef)
    {
        sb.Append("    declare ");
        sb.Append(nameRef.Name);
        sb.Append(": ");
        sb.AppendLine(FormatSymType(ctx, pkg, nameRef));
    }

    private void EmitParams(StringBuilder sb, PassContext ctx, PackageContext pkg, List<Parameter> parameters)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var p = parameters[i];
            if (p.IsVararg)
            {
                sb.Append("...");
                if (p.TypeAnnotation != null)
                {
                    sb.Append(": ");
                    sb.Append(FormatSymType(ctx, pkg, p.Name));
                }
            }
            else
            {
                sb.Append(p.Name.Name);
                sb.Append(": ");
                sb.Append(FormatSymType(ctx, pkg, p.Name));
            }
        }
    }

    private void EmitReturnType(StringBuilder sb, PassContext ctx, PackageContext pkg, NameRef nameRef)
    {
        if (nameRef.Sym == SymID.Invalid) return;
        if (!pkg.Syms.GetByID(nameRef.Sym, out var sym)) return;
        if (!ctx.Types.GetByID(sym.Type, out var typ) || typ is not FunctionType ft) return;

        sb.Append(": ");
        sb.Append(FormatType(ctx, ft.ReturnType));
    }

    private string FormatSymType(PassContext ctx, PackageContext pkg, NameRef nameRef)
    {
        if (nameRef.Sym == SymID.Invalid) return "any";
        if (!pkg.Syms.GetByID(nameRef.Sym, out var sym)) return "any";
        if (sym.Type == TypID.Invalid) return "any";
        if (!ctx.Types.GetByID(sym.Type, out var typ)) return "any";
        return FormatType(ctx, typ);
    }

    private string FormatType(PassContext ctx, Type typ)
    {
        return typ switch
        {
            FunctionType ft => $"({string.Join(", ", ft.ParamTypes.Select(p => FormatType(ctx, p)))}) -> {FormatType(ctx, ft.ReturnType)}",
            TableArrayType arr => $"{FormatType(ctx, arr.ElementType)}[]",
            TableMapType map => $"{{ [{FormatType(ctx, map.KeyType)}]: {FormatType(ctx, map.ValueType)} }}",
            UnionType union => string.Join(" | ", union.Types.Select(t => FormatType(ctx, t))),
            StructType st => $"{{ {string.Join(", ", st.Fields.Select(f => $"{f.Name.Name}: {FormatType(ctx, f.Type)}"))} }}",
            TupleType tuple => $"({string.Join(", ", tuple.Fields.Select(f => FormatType(ctx, f.Type)))})",
            _ => typ.Kind switch
            {
                TypeKind.PrimitiveNil => "nil",
                TypeKind.PrimitiveAny => "any",
                TypeKind.PrimitiveNumber => "number",
                TypeKind.PrimitiveBool => "boolean",
                TypeKind.PrimitiveString => "string",
                _ => "any"
            }
        };
    }
}
