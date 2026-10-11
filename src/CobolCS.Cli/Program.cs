using System.Text.Json.Nodes;
using CobolCS.Cli;
using CobolCS.Runtime;

try
{
    if (args.Length == 0 || args[0] is "--help" or "-h")
    {
        Console.WriteLine("""
            CobolCS — COBOL subset to standalone C#/.NET 10 projects
            analyze   <file|directory> --out <directory> [--config <json>] [--copybook-path <directory>]
            translate <file|directory> --out <directory> [--backend source|interpreter] [--mode strict|scaffold] [--config <json>] [--copybook-path <directory>]
            check     <generated-directory>
            run       <generated-directory> --program <PROGRAM-ID> [--fixture <json> | --scenario <name>] [--call-stubs throw|noop] [--out <json>]
            adopt     <generated-directory> --out <new-directory>
            compare   --actual <json> --baseline <json>
            --version
            Exit codes: 0 success, 1 strict refusal / comparison mismatch, 2 input or execution error.
            """);
        return 0;
    }

    if (args[0] == "--version")
    {
        Console.WriteLine(Generator.Version);
        return 0;
    }

    var command = args[0];
    var positional = new List<string>();
    var options = new Dictionary<string, List<string>>();
    var allowed = command switch
    {
        "analyze" => new[]
        {
            "--out",
            "--config",
            "--copybook-path",
        },
        "translate" => new[]
        {
            "--out",
            "--config",
            "--copybook-path",
            "--mode",
            "--backend",
        },
        "check" => [],
        "adopt" => ["--out"],
        "run" => new[]
        {
            "--program",
            "--fixture",
            "--scenario",
            "--call-stubs",
            "--out",
        },
        "compare" => new[]
        {
            "--actual",
            "--baseline",
        },
        _ => throw new ArgumentException("Unknown command: " + command),
    };
    for (var i = 1; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--"))
        {
            positional.Add(args[i]);
            continue;
        }

        var key = args[i];
        if (!allowed.Contains(key) || ++i >= args.Length || args[i].StartsWith("--"))
        {
            throw new ArgumentException("Invalid option " + key);
        }

        if (!options.TryGetValue(key, out var values))
        {
            options[key] = values = [];
        }

        if (values.Count != 0 && key != "--copybook-path")
        {
            throw new ArgumentException("Duplicate option " + key);
        }

        values.Add(args[i]);
    }

    string? Option(string key) => options.TryGetValue(key, out var values) ? values.SingleOrDefault() : null;
    string Required(string key) => Option(key) ?? throw new ArgumentException("Required " + key);
    if (positional.Count != (command == "compare" ? 0 : 1))
    {
        throw new ArgumentException("Invalid positional arguments");
    }

    if (command is "translate" or "analyze")
    {
        var backend = Option("--backend") ?? "source";
        if (backend is not ("source" or "interpreter"))
        {
            throw new ArgumentException("Invalid backend");
        }

        var mode = command == "analyze" ? "analyze" : Option("--mode") ?? (backend == "source" ? "scaffold" : "strict");
        if (mode is not ("strict" or "scaffold" or "analyze"))
        {
            throw new ArgumentException("Invalid mode");
        }

        var arguments = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "python/frontend/main.py"),
            Path.GetFullPath(positional[0]),
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
        var manifest = Generator.Generate(ir, Required("--out"), mode, backend);
        if (manifest.B("contractChanged"))
        {
            Console.Error.WriteLine("Dependency contracts changed. Existing Stubs/*.cs were preserved; review reports/contract-changes.json and reports/stubs.json.");
        }

        Console.WriteLine(Generator.Json(manifest["judgments"]!));
        return mode == "strict" && manifest["judgments"]!.B("strictRefused") ? 1 : 0;
    }

    if (command == "adopt")
    {
        return await Adoption.Run(positional[0], Required("--out"));
    }

    if (command is "check" or "run")
    {
        var output = Path.GetFullPath(positional[0]);
        var manifest = Generator.ReadManifest(output);
        Generator.Verify(output, manifest);
        var judgments = manifest["judgments"]!;
        if (!judgments.B("generated"))
        {
            throw new InvalidOperationException("Project was not generated; inspect reports/diagnostics.json");
        }

        judgments["built"] = false;
        Generator.SaveManifest(output, manifest);
        var build = await ProcessRunner.Run("dotnet", ["build", "CobolCS.Generated.csproj", "--nologo", "--verbosity", "quiet"], output);
        judgments["built"] = true;
        Generator.SaveManifest(output, manifest);
        if (command == "check")
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
            Required("--program"),
        };
        if (Option("--fixture") != null && Option("--scenario") != null)
        {
            throw new ArgumentException("Fixture and scenario are mutually exclusive");
        }

        if (Option("--scenario") is string scenario)
        {
            run.AddRange(["--scenario", scenario]);
        }

        if (Option("--call-stubs") is string callStubs)
        {
            if (callStubs is not ("throw" or "noop"))
            {
                throw new ArgumentException("Invalid CALL stub mode");
            }

            run.AddRange(["--call-stubs", callStubs]);
            if (callStubs == "noop")
            {
                Console.Error.WriteLine("WARNING: CALL no-op exploration is not business success.");
            }
        }

        if (Option("--fixture") is string fixture)
        {
            run.Add(Path.GetFullPath(fixture));
        }

        var actual = J.Parse(await ProcessRunner.Run("dotnet", run, output));
        if (Option("--out") is string resultPath)
        {
            Generator.SafePath(resultPath);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath))!);
            File.WriteAllText(resultPath, Generator.Json(actual));
        }

        judgments["executedWithFixtures"] = Option("--fixture") != null;
        var usedNoOp = actual["metadata"]?.B("hasNoOpCalls") == true;
        judgments["executionKind"] = usedNoOp ? "call-no-op-exploration" : Option("--scenario") != null ? "csharp-scenario" : Option("--fixture") != null ? "fixture-input" : "generated-code";
        judgments["externalImplementation"] = usedNoOp ? "generated-call-no-op" : actual["dependencies"] != null ? "user-csharp-adapter-unverified" : "none";
        judgments["callStubMode"] = actual["metadata"]?.S("callStubMode") ?? "Throw";
        judgments["hasNoOpCalls"] = usedNoOp;
        Generator.SaveManifest(output, manifest);
        Console.WriteLine(Generator.Json(actual));
        return 0;
    }

    if (command == "compare")
    {
        var actual = J.Parse(File.ReadAllText(Required("--actual")));
        var baseline = J.Parse(File.ReadAllText(Required("--baseline")));
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
