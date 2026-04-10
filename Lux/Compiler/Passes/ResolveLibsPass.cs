using Antlr4.Runtime;
using Lux.IR;

namespace Lux.Compiler.Passes;

public sealed class ResolveLibsPass() : Pass(PassName, PassScope.PerBuild)
{
    public const string PassName = "ResolveLibs";

    private static readonly string[] LuaBuiltinFunctions =
    [
        "print", "type", "tostring", "tonumber", "ipairs", "pairs", "next",
        "select", "error", "assert", "pcall", "xpcall", "unpack", "rawget",
        "rawset", "rawequal", "rawlen", "setmetatable", "getmetatable",
        "require", "dofile", "loadfile", "load", "loadstring", "collectgarbage",
    ];

    private static readonly string[] LuaBuiltinVariables =
    [
        "_G", "_ENV", "_VERSION",
        "string", "table", "math", "io", "os", "coroutine", "debug", "package",
    ];

    public override bool Run(PassContext context)
    {
        foreach (var pkg in context.Pkgs)
        {
            foreach (var name in LuaBuiltinFunctions)
            {
                var sym = pkg.Syms.NewSymbol(SymbolKind.Function, name, pkg.Root, TypID.Invalid, NodeID.Invalid);
                pkg.Scopes.DeclareSymbol(pkg.Root, name, sym, pkg.Syms);
            }

            foreach (var name in LuaBuiltinVariables)
            {
                var sym = pkg.Syms.NewSymbol(SymbolKind.Variable, name, pkg.Root, TypID.Invalid, NodeID.Invalid);
                pkg.Scopes.DeclareSymbol(pkg.Root, name, sym, pkg.Syms);
            }
        }

        LoadDeclarationFiles(context);

        return true;
    }

    private void LoadDeclarationFiles(PassContext context)
    {
        var globals = context.Config.Globals;
        if (globals.Count == 0) return;

        var baseDir = Environment.CurrentDirectory;

        foreach (var globPath in globals)
        {
            var fullPath = Path.IsPathRooted(globPath)
                ? globPath
                : Path.Combine(baseDir, globPath);

            if (Directory.Exists(fullPath))
            {
                foreach (var file in Directory.GetFiles(fullPath, "*.d.lux", SearchOption.AllDirectories))
                    LoadDeclFile(context, file);
            }
            else if (File.Exists(fullPath) && fullPath.EndsWith(".d.lux", StringComparison.OrdinalIgnoreCase))
            {
                LoadDeclFile(context, fullPath);
            }
        }
    }

    private static void LoadDeclFile(PassContext context, string filePath)
    {
        if (context.Pkgs.Any(p => p.Files.Any(f => f.Filename == filePath)))
            return;

        string source;
        try
        {
            source = File.ReadAllText(filePath);
        }
        catch
        {
            return;
        }

        var diag = context.Diag;
        var nodeAlloc = new IDAlloc<NodeID>();
        var inputStream = new AntlrInputStream(source);
        var lexer = new LuxLexer(inputStream);
        lexer.RemoveErrorListeners();
        var tokenStream = new CommonTokenStream(lexer);
        var parser = new LuxParser(tokenStream);
        parser.RemoveErrorListeners();
        var visitor = new IRVisitor(filePath, nodeAlloc, diag, context.Config);
        var ir = visitor.Visit(parser.script());
        if (ir is not IRScript script) return;

        var file = new PreparsedFile(filePath, source) { Hir = script };
        var targetPkg = context.Pkgs.FirstOrDefault();
        targetPkg?.Files.Add(file);
    }
}
