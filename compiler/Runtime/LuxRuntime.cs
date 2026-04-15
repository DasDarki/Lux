using Lua = KeraLua.Lua;
using LuaStatus = KeraLua.LuaStatus;

namespace Lux.Runtime;

/// <summary>
/// Thin wrapper around an embedded Lua 5.4 interpreter (KeraLua) used to execute
/// Lux-transpiled Lua code directly from the <c>lux run</c> command. Instances of
/// this class own a native Lua state — dispose them to free it.
/// </summary>
public sealed class LuxRuntime : IDisposable
{
    private readonly Lua _state;
    private bool _disposed;

    public LuxRuntime()
    {
        _state = new Lua();
        _state.OpenLibs();
    }

    /// <summary>
    /// Extends Lua's <c>package.path</c> so that <c>require("foo/bar")</c> and
    /// <c>require("foo.bar")</c> find <c>&lt;dir&gt;/foo/bar.lua</c> and
    /// <c>&lt;dir&gt;/foo/bar/init.lua</c>. Accepts any number of root directories
    /// and prepends them to the existing path in order.
    /// </summary>
    public void AddPackagePath(params string[] roots)
    {
        foreach (var root in roots.Reverse())
        {
            if (string.IsNullOrEmpty(root)) continue;
            var normalized = root.Replace('\\', '/').TrimEnd('/');
            var entry = $"{normalized}/?.lua;{normalized}/?/init.lua";

            _state.GetGlobal("package");
            _state.GetField(-1, "path");
            var existing = _state.ToString(-1) ?? "";
            _state.Pop(1);

            _state.PushString(entry + ";" + existing);
            _state.SetField(-2, "path");
            _state.Pop(1);
        }
    }

    /// <summary>
    /// Runs a Lua source string as the entry script. The <paramref name="chunkName"/>
    /// is used in error messages / stack traces. Returns <c>true</c> on success and
    /// prints any runtime error to stderr on failure.
    /// </summary>
    public bool RunChunk(string luaSource, string chunkName)
    {
        var loadStatus = _state.LoadString(luaSource, chunkName);
        if (loadStatus != LuaStatus.OK)
        {
            var err = _state.ToString(-1) ?? "unknown load error";
            Console.Error.WriteLine($"lux run: load error: {err}");
            _state.Pop(1);
            return false;
        }

        var callStatus = _state.PCall(0, 0, 0);
        if (callStatus != LuaStatus.OK)
        {
            var err = _state.ToString(-1) ?? "unknown runtime error";
            Console.Error.WriteLine($"lux run: runtime error: {err}");
            _state.Pop(1);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Loads and runs a Lua file from disk via the native <c>luaL_loadfile</c> +
    /// <c>lua_pcall</c> path. Equivalent to <see cref="RunChunk"/> but lets Lua
    /// itself handle file reading so line numbers map directly to the file.
    /// </summary>
    public bool RunFile(string path)
    {
        var loadStatus = _state.LoadFile(path);
        if (loadStatus != LuaStatus.OK)
        {
            var err = _state.ToString(-1) ?? "unknown load error";
            Console.Error.WriteLine($"lux run: load error: {err}");
            _state.Pop(1);
            return false;
        }

        var callStatus = _state.PCall(0, 0, 0);
        if (callStatus != LuaStatus.OK)
        {
            var err = _state.ToString(-1) ?? "unknown runtime error";
            Console.Error.WriteLine($"lux run: runtime error: {err}");
            _state.Pop(1);
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _state.Dispose();
    }
}
