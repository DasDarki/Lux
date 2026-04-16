using KeraLua;

namespace Lux.Runtime;

/// <summary>
/// Thin wrapper around an embedded Lua 5.4 interpreter (KeraLua) used to execute
/// Lux-transpiled Lua code directly from the <c>lux run</c> command. Instances of
/// this class own a native Lua state — dispose them to free it.
/// </summary>
public sealed class LuxRuntime : IDisposable
{
    private readonly Lua _state;
    private readonly bool _sandboxed;
    private bool _disposed;

    public LuxRuntime() : this(sandboxed: false) { }

    private LuxRuntime(bool sandboxed)
    {
        _state = new Lua();
        _state.OpenLibs();
        _sandboxed = sandboxed;
        if (sandboxed) ApplySandbox();
    }

    /// <summary>
    /// Creates a restricted runtime suitable for executing annotation plugins at compile time.
    /// Disables <c>io</c>, <c>os</c>, <c>package</c>, <c>require</c> and other globals that
    /// could escape the sandbox, while leaving pure Lua (string/math/table/coroutine) intact.
    /// The <c>ir</c> helper module is pre-loaded as a global so annotation scripts can build
    /// new IR nodes ergonomically.
    /// </summary>
    public static LuxRuntime CreateSandboxed()
    {
        var rt = new LuxRuntime(sandboxed: true);
        rt.LoadEmbeddedHelpers();
        return rt;
    }

    private void ApplySandbox()
    {
        foreach (var global in new[] { "io", "os", "package", "require", "dofile", "loadfile", "load", "loadstring", "debug" })
        {
            _state.PushNil();
            _state.SetGlobal(global);
        }
    }

    private void LoadEmbeddedHelpers()
    {
        var asm = typeof(LuxRuntime).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("ir_helpers.lua", StringComparison.OrdinalIgnoreCase));
        if (resourceName == null) return;
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null) return;
        using var reader = new StreamReader(stream);
        var source = reader.ReadToEnd();
        LoadAndRun(source, "ir_helpers");
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

    /// <summary>
    /// Loads a Lua source chunk and executes it as the module body (so any globals or
    /// functions defined at the top level become visible). Returns <c>null</c> on success
    /// or a string describing the failure.
    /// </summary>
    public string? LoadAndRun(string luaSource, string chunkName)
    {
        var loadStatus = _state.LoadString(luaSource, chunkName);
        if (loadStatus != LuaStatus.OK)
        {
            var err = _state.ToString(-1) ?? "unknown load error";
            _state.Pop(1);
            return err;
        }

        var callStatus = _state.PCall(0, 0, 0);
        if (callStatus != LuaStatus.OK)
        {
            var err = _state.ToString(-1) ?? "unknown runtime error";
            _state.Pop(1);
            return err;
        }

        return null;
    }

    /// <summary>
    /// Calls a global function by name with the given arguments (serialized C# object trees —
    /// <see cref="Dictionary{TKey,TValue}"/> / <see cref="List{T}"/> / primitives). Returns the
    /// first return value converted back to the same shape, or sets <paramref name="error"/>
    /// if the call failed.
    /// </summary>
    public object? CallGlobalFunction(string funcName, object?[] args, out string? error)
    {
        error = null;
        var top = _state.GetTop();
        try
        {
            var t = _state.GetGlobal(funcName);
            if (t != LuaType.Function)
            {
                _state.SetTop(top);
                error = $"global '{funcName}' is not a function (got {t}).";
                return null;
            }

            foreach (var arg in args) PushValue(arg);

            var status = _state.PCall(args.Length, 1, 0);
            if (status != LuaStatus.OK)
            {
                var err = _state.ToString(-1) ?? "unknown error";
                _state.SetTop(top);
                error = err;
                return null;
            }

            var result = ReadValue(-1);
            _state.SetTop(top);
            return result;
        }
        catch (Exception ex)
        {
            _state.SetTop(top);
            error = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Pushes a C# object tree (Dictionary / List / primitives) onto the Lua stack as a nested
    /// table. Dictionaries use string keys; Lists become 1-indexed arrays. Unsupported types
    /// are pushed as <c>nil</c>.
    /// </summary>
    private void PushValue(object? value)
    {
        switch (value)
        {
            case null:
                _state.PushNil();
                break;
            case string s:
                _state.PushString(s);
                break;
            case bool b:
                _state.PushBoolean(b);
                break;
            case int i:
                _state.PushInteger(i);
                break;
            case long l:
                _state.PushInteger(l);
                break;
            case ulong ul:
                _state.PushInteger((long)ul);
                break;
            case double d:
                _state.PushNumber(d);
                break;
            case float f:
                _state.PushNumber(f);
                break;
            case IDictionary<string, object?> dict:
                _state.NewTable();
                foreach (var kv in dict)
                {
                    PushValue(kv.Value);
                    _state.SetField(-2, kv.Key);
                }
                break;
            case System.Collections.IList list:
                _state.NewTable();
                for (var idx = 0; idx < list.Count; idx++)
                {
                    PushValue(list[idx]);
                    _state.RawSetInteger(-2, idx + 1);
                }
                break;
            default:
                _state.PushNil();
                break;
        }
    }

    /// <summary>
    /// Reads a Lua value at the given stack index and converts it to a C# object tree
    /// (<see cref="Dictionary{TKey,TValue}"/> for tables with string keys, <see cref="List{T}"/>
    /// for sequences, plus primitives). Does not mutate the stack.
    /// </summary>
    private object? ReadValue(int index)
    {
        var abs = _state.AbsIndex(index);
        var t = _state.Type(abs);
        switch (t)
        {
            case LuaType.Nil: return null;
            case LuaType.Boolean: return _state.ToBoolean(abs);
            case LuaType.Number:
                if (_state.IsInteger(abs)) return _state.ToInteger(abs);
                return _state.ToNumber(abs);
            case LuaType.String: return _state.ToString(abs);
            case LuaType.Table: return ReadTable(abs);
            case LuaType.None:
            case LuaType.LightUserData:
            case LuaType.Function:
            case LuaType.UserData:
            case LuaType.Thread:
            default: return null;
        }
    }

    private object? ReadTable(int absIndex)
    {
        var hasStringKey = false;
        var maxIntKey = 0;
        var intKeyCount = 0;

        _state.PushNil();
        while (_state.Next(absIndex))
        {
            var keyType = _state.Type(-2);
            if (keyType == LuaType.String)
            {
                hasStringKey = true;
            }
            else if (keyType == LuaType.Number && _state.IsInteger(-2))
            {
                var k = (int)_state.ToInteger(-2);
                if (k > maxIntKey) maxIntKey = k;
                intKeyCount++;
            }
            _state.Pop(1);
        }

        if (!hasStringKey && intKeyCount > 0 && maxIntKey == intKeyCount)
        {
            var list = new List<object?>(intKeyCount);
            for (var i = 1; i <= intKeyCount; i++)
            {
                _state.RawGetInteger(absIndex, i);
                list.Add(ReadValue(-1));
                _state.Pop(1);
            }
            return list;
        }

        var dict = new Dictionary<string, object?>();
        _state.PushNil();
        while (_state.Next(absIndex))
        {
            // key at -2, value at -1
            if (_state.Type(-2) == LuaType.String)
            {
                var key = _state.ToString(-2) ?? "";
                dict[key] = ReadValue(-1);
            }
            else if (_state.Type(-2) == LuaType.Number && _state.IsInteger(-2))
            {
                var key = _state.ToInteger(-2).ToString();
                dict[key] = ReadValue(-1);
            }
            _state.Pop(1);
        }
        return dict;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _state.Dispose();
    }
}
