using System.Diagnostics;
using System.Reflection;
using Lux.Compiler;
using Lux.Configuration;
using Lux.LPS;

namespace Lux;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
#if DEBUG
        if (args.Length > 0 && args[0] == "--test")
        {
            var testFile = Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "test.lux");

            var compiler = new LuxCompiler();
            compiler.AddSource(testFile);

            var sucess = compiler.Compile();
            if (!sucess)
            {
                Console.WriteLine("Compilation FAILED.");

                foreach (var diag in compiler.Diagnostics.Diagnostics)
                {
                    Console.WriteLine(diag.ToString());
                }
            }
            else
            {
                Console.WriteLine("Compilation SUCCESS.");
                Console.WriteLine("--- Generated Lua ---");
                foreach (var pkg in compiler.Packages.Values)
                {
                    foreach (var file in pkg.Files)
                    {
                        Console.WriteLine(file.GeneratedLua);
                    }
                }
            }

            return sucess ? 0 : 1;
        }
#endif

        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

        return command switch
        {
            "build" => await RunBuildAsync(),
            "lps" => await RunLpsAsync(),
            "init" => RunInit(),
            "version" => RunVersion(),
            "help" or "--help" or "-h" => RunHelp(),
            _ => RunUnknown(command)
        };
    }

    private static async Task<int> RunBuildAsync()
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

        var sourceFiles = Directory.GetFiles(srcDir, "*.lux", SearchOption.AllDirectories);
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

        foreach (var pkg in compiler.Packages.Values)
        {
            foreach (var file in pkg.Files)
            {
                if (string.IsNullOrEmpty(file.GeneratedLua)) continue;

                var relativePath = Path.GetRelativePath(srcDir, file.Filename ?? "output.lua");
                var outputPath = Path.Combine(outDir, Path.ChangeExtension(relativePath, ".lua"));

                var outputFileDir = Path.GetDirectoryName(outputPath);
                if (outputFileDir != null) Directory.CreateDirectory(outputFileDir);

                await File.WriteAllTextAsync(outputPath, file.GeneratedLua);
            }
        }

        Console.WriteLine($"Build SUCCESS — {sourceFiles.Length} file(s) compiled to '{config.Output}/'.");

        if (!RunScripts(config.Scripts.PostBuild, "post-build"))
            return 1;

        return 0;
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
        Console.WriteLine("Usage: lux <command>");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  build     Compile the project (reads lux.toml)");
        Console.WriteLine("  init      Create a new Lux project in the current directory");
        Console.WriteLine("  lps       Start the language server (LSP via stdio)");
        Console.WriteLine("  version   Print the Lux version");
        Console.WriteLine("  help      Show this help message");
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
