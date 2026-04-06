using Lux.IR;

namespace Lux.Compiler.Passes;

/// <summary>
/// The resolve libs pass resolves library dependencies and imports in the source code. It ensures that all library
/// references are correctly resolved and that the necessary libraries are included in the compilation process.
/// It takes care of both standard libraries, user-defined internal libraries, and external libraries.
/// </summary>
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

        return true;
    }
}