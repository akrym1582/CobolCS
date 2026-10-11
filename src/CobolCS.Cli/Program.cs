using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Runtime;

try
{
    var commands = new[] { "generate", "analyze", "build", "run", "compare" };
    var explicitCommand = args.Length > 0 && commands.Contains(args[0]);
    var command = explicitCommand ? args[0] : "generate";
    var argumentsStart = explicitCommand ? 1 : 0;
    if (args.Length == 0 || args.Skip(argumentsStart).FirstOrDefault() is "--help" or "-h")
    {
        Console.WriteLine("""
            CobolCS — COBOL to standalone C#/.NET 10 source

            cobolcs <file.cbl|directory> [options]
            cobolcs generate <file.cbl|directory> [options]
              -o, --output <directory>   Default: ./<input-name>-csharp
              --scaffold                Generate a review scaffold for unsupported COBOL
              --config <json>           Frontend configuration
              -I, --copybook-path <dir>  COPY search path (repeatable)

            cobolcs analyze <input> [-o <dir>] [--config <json>] [-I <dir> ...]
              Reports only. Default: ./<input-name>-analysis
            cobolcs build <generated-directory>
            cobolcs run <generated-directory> [--program <PROGRAM-ID>] [--fixture <json>] [-o <json>]
              --program is optional when the generated project has one program.
            cobolcs compare <actual.json> <baseline.json>
            cobolcs --help | --version

            Example: cobolcs payroll.cbl -o generated/payroll
            Generated source: <output>/Programs/*.cs (with runtime and .csproj).
            Exit codes: 0 success, 1 unsupported COBOL / comparison mismatch, 2 input or execution error.
            """);
        return 0;
    }

    if (args.Length == 1 && args[0] == "--version")
    {
        Console.WriteLine(Generator.Version);
        return 0;
    }

    var positional = new List<string>();
    var options = new Dictionary<string, List<string>>();
    var allowed = command switch
    {
        "analyze" => new[] { "--output", "--config", "--copybook-path" },
        "generate" => new[] { "--output", "--config", "--copybook-path", "--scaffold" },
        "run" => new[] { "--program", "--fixture", "--output" },
        _ => Array.Empty<string>(),
    };
    var positionalOnly = false;
    for (var i = argumentsStart; i < args.Length; i++)
    {
        if (!positionalOnly && args[i] == "--")
        {
            positionalOnly = true;
            continue;
        }

        if (positionalOnly || !args[i].StartsWith('-'))
        {
            positional.Add(args[i]);
            continue;
        }

        var key = args[i] switch
        {
            "-o" => "--output",
            "-I" => "--copybook-path",
            _ => args[i],
        };
        if (!allowed.Contains(key))
        {
            throw new ArgumentException("Unknown option " + args[i] + ". See cobolcs --help.");
        }

        var value = string.Empty;
        if (key != "--scaffold")
        {
            if (++i >= args.Length || args[i].StartsWith('-') || string.IsNullOrWhiteSpace(args[i]))
            {
                throw new ArgumentException("Missing value for " + key);
            }

            value = args[i];
        }

        if (!options.TryGetValue(key, out var values))
        {
            options[key] = values = [];
        }

        if (values.Count != 0 && key != "--copybook-path")
        {
            throw new ArgumentException("Duplicate option " + key);
        }

        values.Add(value);
    }

    string? Option(string key) => options.TryGetValue(key, out var values) ? values.SingleOrDefault() : null;
    if (positional.Count != (command == "compare" ? 2 : 1))
    {
        throw new ArgumentException("Expected " + (command == "compare" ? "<actual.json> <baseline.json>" : "one input path") + ". See cobolcs --help.");
    }

    if (command is "generate" or "analyze")
    {
        var input = Path.GetFullPath(positional[0]);
        if (!File.Exists(input) && !Directory.Exists(input))
        {
            throw new ArgumentException("Input does not exist: " + positional[0]);
        }

        var name = Directory.Exists(input)
            ? new DirectoryInfo(input).Name
            : Path.GetFileNameWithoutExtension(input);
        var output = Path.GetFullPath(Option("--output") ?? (name + (command == "analyze" ? "-analysis" : "-csharp")));
        var mode = command == "analyze" ? "analyze" : options.ContainsKey("--scaffold") ? "scaffold" : "strict";
        var arguments = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "python/frontend/main.py"),
            input,
        };
        if (Option("--config") is string configuration)
        {
            arguments.Add("--config");
            arguments.Add(Path.GetFullPath(configuration));
        }

        if (options.TryGetValue("--copybook-path", out var paths))
        {
            foreach (var path in paths)
            {
                arguments.Add("--copybook-path");
                arguments.Add(Path.GetFullPath(path));
            }
        }

        if (mode == "scaffold")
        {
            arguments.Add("--allow-missing-copy");
        }

        var ir = J.Parse(await ProcessRunner.Run(Environment.GetEnvironmentVariable("COBOLCS_PYTHON") ?? (OperatingSystem.IsWindows() ? "python" : "python3"), arguments));
        var manifest = Generator.Generate(ir, output, mode);
        var generated = manifest["judgments"]!.B("generated");
        Console.WriteLine(generated ? "Generated C# project: " + output : "Reports: " + output);
        Console.WriteLine("Diagnostics: " + Path.Combine(output, "reports/diagnostics.json"));
        if (generated)
        {
            Console.WriteLine("C# source: " + Path.Combine(output, "Programs"));
        }

        return mode == "strict" && !manifest["judgments"]!.B("generated") ? 1 : 0;
    }

    if (command is "build" or "run")
    {
        var output = Path.GetFullPath(positional[0]);
        var manifest = Generator.ReadManifest(output);
        Generator.Verify(output, manifest);
        var judgments = manifest["judgments"]!;
        if (!judgments.B("generated"))
        {
            throw new InvalidOperationException("Project was not generated; inspect reports/diagnostics.json");
        }

        var program = Option("--program");
        if (command == "run")
        {
            var programs = manifest.A("programs").Select(p => p.GetValue<string>()).ToArray();
            program ??= programs.Length == 1 ? programs[0] : throw new ArgumentException("Specify --program. Available programs: " + string.Join(", ", programs));
            if (!programs.Contains(program))
            {
                throw new ArgumentException("Unknown program: " + program + ". Available programs: " + string.Join(", ", programs));
            }
        }

        judgments["built"] = false;
        Generator.SaveManifest(output, manifest);
        var build = await ProcessRunner.Run("dotnet", ["build", "CobolCS.Generated.csproj", "--nologo", "--verbosity", "quiet"], output);
        judgments["built"] = true;
        Generator.SaveManifest(output, manifest);
        if (command == "build")
        {
            Console.WriteLine(build.Trim());
            return 0;
        }

        judgments["executedWithFixtures"] = false;
        judgments["matchedBaseline"] = false;
        Generator.SaveManifest(output, manifest);
        var run = new List<string>
        {
            Path.Combine(output, "bin/Debug/net10.0/CobolCS.Generated.dll"),
            program!,
        };
        if (Option("--fixture") is string fixture)
        {
            run.Add(Path.GetFullPath(fixture));
        }

        var actual = J.Parse(await ProcessRunner.Run("dotnet", run, output));
        if (Option("--output") is string resultPath)
        {
            Generator.SafePath(resultPath);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath))!);
            File.WriteAllText(resultPath, Generator.Json(actual));
        }

        judgments["executedWithFixtures"] = Option("--fixture") != null;
        Generator.SaveManifest(output, manifest);
        Console.WriteLine(Generator.Json(actual));
        return 0;
    }

    if (command == "compare")
    {
        var actual = J.Parse(File.ReadAllText(positional[0]));
        var baseline = J.Parse(File.ReadAllText(positional[1]));
        var matched = JsonNode.DeepEquals(actual, baseline);
        Console.WriteLine(matched ? "matchedBaseline: true" : "matchedBaseline: false");
        return matched ? 0 : 1;
    }

    throw new ArgumentException("Unknown command");
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 2;
}
