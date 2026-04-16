using System.Diagnostics;
using System.Reflection;
using Lux.Compiler;
using Lux.Configuration;
using Lux.LPS;
using Lux.Runtime;

namespace Lux;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
#if DEBUG
        if (args.Length > 0 && args[0] == "--test")
        {
            var testFile = Path.Combine(Environment.CurrentDirectory, "..", "..", "test.lux");
            return await RunBuildFilesAsync([testFile]);
        }
#endif

        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

        return command switch
        {
            "build" => await RunBuildAsync(args.Skip(1).ToArray()),
            "run" => await RunExecuteAsync(args.Skip(1).ToArray()),
            "lps" => await RunLpsAsync(),
            "init" => RunInit(),
            "version" => RunVersion(),
            "help" or "--help" or "-h" => RunHelp(),
            _ => RunUnknown(command)
        };
    }

    private static async Task<int> RunBuildAsync(string[] fileArgs)
    {
        if (fileArgs.Length > 0)
            return await RunBuildFilesAsync(fileArgs);

        return await RunBuildProjectAsync();
    }

    private static async Task<int> RunBuildProjectAsync()
    {
        var configPath = Path.Combine(Environment.CurrentDirectory, "lux.toml");
        var config = Config.LoadFromFile(configPath) ?? new Config();

        var srcDir = Path.Combine(Environment.CurrentDirectory, config.Source);
        if (!Directory.Exists(srcDir))
        {
            await Console.Error.WriteLineAsync($"Source directory '{config.Source}' not found.");
            return 1;
        }

        var outDir = Path.Combine(Environment.CurrentDirectory, config.Output);

        if (!RunScripts(config.Scripts.PreBuild, "pre-build"))
            return 1;

        var sourceFiles = Directory.GetFiles(srcDir, "*.lux", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".d.lux", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (sourceFiles.Length == 0)
        {
            await Console.Error.WriteLineAsync($"No .lux files found in '{config.Source}'.");
            return 1;
        }

        var compiler = new LuxCompiler { Config = config };
        foreach (var file in sourceFiles)
            compiler.AddSource(file);

        var success = compiler.Compile();
        if (!success)
        {
            foreach (var diag in compiler.Diagnostics.Diagnostics)
                await Console.Error.WriteLineAsync(diag.ToString());
            await Console.Error.WriteLineAsync("Build FAILED.");
            return 1;
        }

        Directory.CreateDirectory(outDir);
        await WriteOutputFilesAsync(compiler, srcDir, outDir, config);

        Console.WriteLine($"Build SUCCESS — {sourceFiles.Length} file(s) compiled to '{config.Output}/'.");

        if (!RunScripts(config.Scripts.PostBuild, "post-build"))
            return 1;

        return 0;
    }

    private static async Task<int> RunBuildFilesAsync(string[] fileArgs)
    {
        Config? config = null;
        var luxFiles = new List<string>();

        foreach (var arg in fileArgs)
        {
            var fullPath = Path.GetFullPath(arg);
            if (fullPath.EndsWith(".toml", StringComparison.OrdinalIgnoreCase) ||
                fullPath.EndsWith("lux.toml", StringComparison.OrdinalIgnoreCase))
            {
                config = Config.LoadFromFile(fullPath);
            }
            else if (fullPath.EndsWith(".lux", StringComparison.OrdinalIgnoreCase) &&
                     !fullPath.EndsWith(".d.lux", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(fullPath))
                    luxFiles.Add(fullPath);
                else
                    await Console.Error.WriteLineAsync($"File not found: {arg}");
            }
            else
            {
                await Console.Error.WriteLineAsync($"Unknown file type: {arg}");
            }
        }

        config ??= new Config();

        if (luxFiles.Count == 0)
        {
            await Console.Error.WriteLineAsync("No .lux files specified.");
            return 1;
        }

        var compiler = new LuxCompiler { Config = config };
        foreach (var file in luxFiles)
            compiler.AddSource(file);

        var success = compiler.Compile();
        if (!success)
        {
            foreach (var diag in compiler.Diagnostics.Diagnostics)
                await Console.Error.WriteLineAsync(diag.ToString());
            await Console.Error.WriteLineAsync("Build FAILED.");
            return 1;
        }

        var outDir = Path.Combine(Environment.CurrentDirectory, config.Output);
        Directory.CreateDirectory(outDir);

        var baseDir = luxFiles.Count == 1
            ? Path.GetDirectoryName(luxFiles[0])!
            : FindCommonParent(luxFiles);

        await WriteOutputFilesAsync(compiler, baseDir, outDir, config);

        Console.WriteLine($"Build SUCCESS — {luxFiles.Count} file(s) compiled to '{config.Output}/'.");
        return 0;
    }

    private static async Task WriteOutputFilesAsync(LuxCompiler compiler, string baseDir, string outDir, Config config)
    {
        foreach (var pkg in compiler.Packages.Values)
        {
            foreach (var file in pkg.Files)
            {
                if (string.IsNullOrEmpty(file.GeneratedLua)) continue;

                var relativePath = Path.GetRelativePath(baseDir, file.Filename ?? "output.lua");
                var outputPath = Path.Combine(outDir, Path.ChangeExtension(relativePath, ".lua"));

                var outputFileDir = Path.GetDirectoryName(outputPath);
                if (outputFileDir != null) Directory.CreateDirectory(outputFileDir);

                await File.WriteAllTextAsync(outputPath, file.GeneratedLua);
            }
        }

        if (compiler.Cache.TryGetValue("GeneratedDeclarations", out var declObj) && declObj is string declContent)
        {
            var declName = config.Name ?? "index";
            var declPath = Path.Combine(outDir, declName + ".d.lux");
            await File.WriteAllTextAsync(declPath, declContent);
        }
    }

    private static string FindCommonParent(List<string> paths)
    {
        if (paths.Count == 0) return Environment.CurrentDirectory;
        var dirs = paths.Select(p => Path.GetDirectoryName(p) ?? "").ToList();
        var common = dirs[0];
        foreach (var dir in dirs.Skip(1))
        {
            while (!dir.StartsWith(common, StringComparison.OrdinalIgnoreCase) && common.Length > 0)
            {
                common = Path.GetDirectoryName(common) ?? "";
            }
        }
        return string.IsNullOrEmpty(common) ? Environment.CurrentDirectory : common;
    }

    private static async Task<int> RunExecuteAsync(string[] fileArgs)
    {
        Config? config = null;
        var luxFiles = new List<string>();
        var passThroughArgs = new List<string>();
        var parsingLuxArgs = true;

        foreach (var arg in fileArgs)
        {
            if (!parsingLuxArgs)
            {
                passThroughArgs.Add(arg);
                continue;
            }

            if (arg == "--")
            {
                parsingLuxArgs = false;
                continue;
            }

            var fullPath = Path.GetFullPath(arg);
            if (fullPath.EndsWith(".toml", StringComparison.OrdinalIgnoreCase) ||
                fullPath.EndsWith("lux.toml", StringComparison.OrdinalIgnoreCase))
            {
                config = Config.LoadFromFile(fullPath);
            }
            else if (fullPath.EndsWith(".lux", StringComparison.OrdinalIgnoreCase) &&
                     !fullPath.EndsWith(".d.lux", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(fullPath)) luxFiles.Add(fullPath);
                else await Console.Error.WriteLineAsync($"File not found: {arg}");
            }
            else
            {
                passThroughArgs.Add(arg);
            }
        }

        var projectMode = luxFiles.Count == 0;
        if (projectMode)
        {
            var configPath = Path.Combine(Environment.CurrentDirectory, "lux.toml");
            config ??= Config.LoadFromFile(configPath) ?? new Config();

            var srcDir = Path.Combine(Environment.CurrentDirectory, config.Source);
            if (!Directory.Exists(srcDir))
            {
                await Console.Error.WriteLineAsync($"Source directory '{config.Source}' not found.");
                return 1;
            }

            luxFiles.AddRange(Directory
                .GetFiles(srcDir, "*.lux", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".d.lux", StringComparison.OrdinalIgnoreCase)));

            if (luxFiles.Count == 0)
            {
                await Console.Error.WriteLineAsync($"No .lux files found in '{config.Source}'.");
                return 1;
            }
        }
        else
        {
            config ??= new Config();
        }

        var compiler = new LuxCompiler { Config = config };
        foreach (var file in luxFiles)
            compiler.AddSource(file);

        var success = compiler.Compile();
        if (!success)
        {
            foreach (var diag in compiler.Diagnostics.Diagnostics)
                await Console.Error.WriteLineAsync(diag.ToString());
            await Console.Error.WriteLineAsync("Build FAILED.");
            return 1;
        }

        var baseDir = projectMode
            ? Path.Combine(Environment.CurrentDirectory, config.Source)
            : (luxFiles.Count == 1 ? Path.GetDirectoryName(luxFiles[0])! : FindCommonParent(luxFiles));

        var outDir = projectMode
            ? Path.Combine(Environment.CurrentDirectory, config.Output)
            : Path.Combine(Path.GetTempPath(), "lux-run-" + Guid.NewGuid().ToString("N")[..8]);

        Directory.CreateDirectory(outDir);
        await WriteOutputFilesAsync(compiler, baseDir, outDir, config);

        string? entryPath = null;
        if (!string.IsNullOrEmpty(config.Entry))
        {
            var rel = config.Entry.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
                ? config.Entry
                : config.Entry + ".lua";
            var candidate = Path.Combine(outDir, rel);
            if (File.Exists(candidate)) entryPath = candidate;
            else
            {
                await Console.Error.WriteLineAsync($"lux run: entry '{config.Entry}' not found in '{outDir}'.");
                return 1;
            }
        }
        else
        {
            entryPath = ResolveDefaultEntry(outDir, baseDir, luxFiles);
            if (entryPath == null)
            {
                await Console.Error.WriteLineAsync(
                    "lux run: could not determine entry file. Set [entry] in lux.toml, " +
                    "or add a 'main.lux' / 'index.lux' to your source root.");
                return 1;
            }
        }

        using var runtime = new LuxRuntime();
        runtime.AddPackagePath(outDir);

        PushCommandLineArgs(runtime, passThroughArgs);

        var ok = runtime.RunFile(entryPath);

        if (!projectMode)
        {
            try { Directory.Delete(outDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }

        return ok ? 0 : 1;
    }

    private static string? ResolveDefaultEntry(string outDir, string baseDir, List<string> luxFiles)
    {
        foreach (var candidate in new[] { "main.lua", "index.lua", "init.lua" })
        {
            var path = Path.Combine(outDir, candidate);
            if (File.Exists(path)) return path;
        }

        if (luxFiles.Count == 1)
        {
            var rel = Path.GetRelativePath(baseDir, luxFiles[0]);
            var lua = Path.Combine(outDir, Path.ChangeExtension(rel, ".lua"));
            if (File.Exists(lua)) return lua;
        }

        return null;
    }

    private static void PushCommandLineArgs(LuxRuntime runtime, List<string> args)
    {
        if (args.Count == 0) return;
        var escaped = string.Join(", ", args.Select(a => "\"" + a.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""));
        runtime.RunChunk($"arg = {{ {escaped} }}", "<lux-run-args>");
    }

    private static async Task<int> RunLpsAsync()
    {
        await LuxLanguageServer.RunAsync();
        return 0;
    }

    private static int RunInit()
    {
        var configPath = Path.Combine(Environment.CurrentDirectory, "lux.toml");
        if (File.Exists(configPath))
        {
            Console.Error.WriteLine("lux.toml already exists in the current directory.");
            return 1;
        }

        var config = new Config();
        Config.SaveToFile(config, configPath);

        Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, config.Source));
        Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, config.Output));

        var gitignorePath = Path.Combine(Environment.CurrentDirectory, ".gitignore");
        if (!File.Exists(gitignorePath))
            File.WriteAllText(gitignorePath, $"{config.Output}/\n");
        else
        {
            var content = File.ReadAllText(gitignorePath);
            if (!content.Contains($"{config.Output}/"))
                File.AppendAllText(gitignorePath, $"\n{config.Output}/\n");
        }

        Console.WriteLine("Initialized Lux project:");
        Console.WriteLine($"  lux.toml");
        Console.WriteLine($"  {config.Source}/");
        Console.WriteLine($"  {config.Output}/");
        Console.WriteLine($"  .gitignore");
        return 0;
    }

    private static int RunVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Console.WriteLine($"lux {version?.ToString(3) ?? "0.0.0"}");
        return 0;
    }

    private static int RunHelp()
    {
        Console.WriteLine("Usage: lux <command> [args]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  build              Compile the project (reads lux.toml)");
        Console.WriteLine("  build <files...>   Compile specific .lux files (optional lux.toml)");
        Console.WriteLine("  run                Compile and execute the project via embedded Lua");
        Console.WriteLine("  run <files...>     Compile and execute specific .lux files");
        Console.WriteLine("  init               Create a new Lux project in the current directory");
        Console.WriteLine("  lps                Start the language server (LSP via stdio)");
        Console.WriteLine("  version            Print the Lux version");
        Console.WriteLine("  help               Show this help message");
        return 0;
    }

    private static int RunUnknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        Console.Error.WriteLine("Run 'lux help' for available commands.");
        return 1;
    }

    private static bool RunScripts(List<string> scripts, string phase)
    {
        foreach (var script in scripts)
        {
            Console.WriteLine($"[{phase}] {script}");
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd" : "sh",
                Arguments = OperatingSystem.IsWindows() ? $"/c {script}" : $"-c \"{script}\"",
                WorkingDirectory = Environment.CurrentDirectory,
                UseShellExecute = false
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                Console.Error.WriteLine($"[{phase}] Failed to start: {script}");
                return false;
            }

            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                Console.Error.WriteLine($"[{phase}] Script exited with code {proc.ExitCode}: {script}");
                return false;
            }
        }
        return true;
    }
}
