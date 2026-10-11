using System.Text.Json.Nodes;
using CobolCS.Runtime;

namespace CobolCS.Tools;

/// <summary>開発用の取得、測定、合成デモを.NETだけで実行します。</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h")
            {
                Console.WriteLine("CobolCS.Tools: prepare-workflow|prepare-readiness --out DIR; fetch|evaluate --lock FILE --root DIR [--out DIR --build --verify-expectations --include-gpl]; import-archive ZIP --out DIR; verify SUMMARY; report [--input DIR --before SUFFIX --after SUFFIX --out DIR --markdown FILE]; verify-remaining18 BEFORE AFTER");
                Console.WriteLine("Deep corpus: import-deep --root DIR --out LOCK; evaluate-deep --lock LOCK --root DIR --out DIR --stage analyze|generate|build [--cli DLL --compiler-root DIR --offset N --count N --timeout SECONDS]; summarize-deep --lock LOCK --out DIR");
                return args.Length == 0 ? 2 : 0;
            }

            string[] allowed = args[0] switch
            {
                "import-deep" => ["--root", "--out"],
                "evaluate-deep" => ["--lock", "--root", "--out", "--stage", "--cli", "--compiler-root", "--offset", "--count", "--timeout"],
                "summarize-deep" => ["--lock", "--out"],
                "prepare-workflow" or "prepare-readiness" => ["--out"],
                "fetch" => ["--lock", "--root", "--include-gpl"],
                "evaluate" => ["--lock", "--root", "--include-gpl", "--out", "--build", "--verify-expectations"],
                "import-archive" => ["--out"],
                "verify" or "verify-remaining18" => [],
                "report" => ["--input", "--before", "--after", "--out", "--markdown"],
                _ => throw new ArgumentException("Unknown command: " + args[0]),
            };
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            var positions = new List<string>();
            for (var index = 1; index < args.Length; index++)
            {
                var argument = args[index];
                if (!argument.StartsWith("--", StringComparison.Ordinal))
                {
                    positions.Add(argument);
                    continue;
                }

                if (!allowed.Contains(argument))
                {
                    throw new ArgumentException("Unknown option: " + argument);
                }

                if (argument is "--include-gpl" or "--build" or "--verify-expectations")
                {
                    options[argument] = "true";
                }
                else if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    options[argument] = args[++index];
                }
                else
                {
                    throw new ArgumentException("Missing value for " + argument);
                }
            }

            var expectedPositions = args[0] switch { "verify-remaining18" => 2, "verify" or "import-archive" => 1, _ => 0 };
            if (positions.Count != expectedPositions)
            {
                throw new ArgumentException("Unexpected or missing positional argument");
            }

            string Option(string name, string? fallback = null) => options.TryGetValue(name, out var value) ? value : fallback ?? throw new ArgumentException("Missing " + name);
            string Position(int index) => positions[index - 1];
            var includeGpl = args.Contains("--include-gpl");
            switch (args[0])
            {
                case "import-deep":
                    Console.WriteLine(DeepCorpus.Import(Path.GetFullPath(Option("--root")), Path.GetFullPath(Option("--out"))).I("expectedCases"));
                    break;
                case "evaluate-deep":
                    DeepCorpus.Evaluate(Path.GetFullPath(Option("--lock")), Path.GetFullPath(Option("--root")), Path.GetFullPath(Option("--out")), Option("--stage"), Option("--cli", Workspace.Cli[0]), Option("--compiler-root", Workspace.Root), int.Parse(Option("--offset", "0"), System.Globalization.CultureInfo.InvariantCulture), int.Parse(Option("--count", int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)), System.Globalization.CultureInfo.InvariantCulture), int.Parse(Option("--timeout", "120"), System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case "summarize-deep":
                    Console.WriteLine(DeepCorpus.Summarize(Path.GetFullPath(Option("--lock")), Path.GetFullPath(Option("--out")))["counts"]!.ToJsonString());
                    break;
                case "prepare-workflow":
                case "prepare-readiness":
                    DemoPreparations.Prepare(Option("--out"), args[0] == "prepare-readiness");
                    break;
                case "fetch":
                    Corpus.Fetch(Path.GetFullPath(Option("--lock", "tests/public-corpus/course.lock.json")), Path.GetFullPath(Option("--root")), includeGpl);
                    break;
                case "evaluate":
                    var result = Corpus.Evaluate(Path.GetFullPath(Option("--lock", "tests/public-corpus/course.lock.json")), Path.GetFullPath(Option("--root")), Path.GetFullPath(Option("--out")), includeGpl, args.Contains("--build"));
                    Console.WriteLine(result["counts"]!.ToJsonString());
                    if (args.Contains("--verify-expectations"))
                    {
                        var expected = result.A("expectedExecutionIds").Select(x => x.GetValue<string>()).ToHashSet();
                        if (expected.Count == 0 || result.A("cases").Where(x => expected.Contains(x.S("id"))).Any(x => !x.B("matchedIndependentExpectation")))
                        {
                            Console.Error.WriteLine("Independent expectations were not all matched");
                            return 1;
                        }
                    }

                    break;
                case "import-archive":
                    Console.WriteLine(Corpus.ImportArchive(Path.GetFullPath(Position(1)), Path.GetFullPath(Option("--out"))).ToJsonString());
                    break;
                case "verify":
                    if (!Corpus.Verify(Workspace.Read(Position(1))))
                    {
                        Console.Error.WriteLine("Corpus build/expectation regression; inspect summary and phase logs");
                        return 1;
                    }

                    Console.WriteLine("Corpus generation floor, builds and reviewed expectations passed");
                    break;
                case "report":
                    Reports.Write(Option("--input", "out"), Option("--before", "-pr3"), Option("--after", "-final"), Option("--out", "tests/public-corpus/results"), Option("--markdown", "docs/public-corpus-results.md"));
                    break;
                case "verify-remaining18":
                    Console.WriteLine(Reports.VerifyRecovery(Position(1), Position(2)).ToJsonString());
                    break;
                default:
                    throw new ArgumentException("Unknown command: " + args[0]);
            }

            return 0;
        }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException or System.Text.Json.JsonException or KeyNotFoundException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine(error.Message);
            return 2;
        }
    }
}
