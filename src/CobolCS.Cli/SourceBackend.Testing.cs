using System.Text;
using CobolCS.Runtime;

namespace CobolCS.Cli;

/// <summary>外部契約だけを差し替える router と順序付きメモリ応答を生成します。</summary>
internal sealed partial class SourceBackend
{
    /// <summary>内部の編集済み partial 契約を公開せず接続します。</summary>
    private void RouterAndFake()
    {
        var router = new StringBuilder(Header());
        router.AppendLine($"public interface I{name}ExternalDependencies : I{name}Calls, I{name}Files, I{name}Sql {{ }}\ninternal sealed class {name}Router : I{name}Dependencies, IDependencyCompletion, IInternalStubTrace\n{{\nprivate readonly List<InternalStubEvent> internalTrace = [];\npublic IReadOnlyList<InternalStubEvent> InternalTrace => Array.AsReadOnly(internalTrace.ToArray());\npublic bool InternalTraceTruncated {{ get; private set; }}\nprivate void RecordInternal(string statement, string method, bool completed)\n{{\nif (internalTrace.Count >= options.MaxBranchEvents) {{ InternalTraceTruncated = true; return; }}\ninternalTrace.Add(new(statement, method, completed));\n}}\nprivate readonly ExecutionOptions options;\nprivate readonly I{name}ExternalDependencies? external;\nprivate readonly {name}Dependencies fallback;\ninternal {name}Router(I{name}ExternalDependencies? external, ExecutionOptions options) {{ this.external = external; this.options = options; fallback = new() {{ Options = options }}; }}\npublic void VerifyComplete() {{ if (external is IDependencyCompletion completion) completion.VerifyComplete(); }}");
        var fake = new StringBuilder(Header());
        fake.AppendLine($"public sealed class {name}ScriptedDependencies : I{name}ExternalDependencies, IDependencyCompletion\n{{\nprivate interface IStep {{ string Method {{ get; }} }}\nprivate sealed record Step<TRequest, TResponse>(string Method, Func<TRequest, bool> Matcher, TResponse Response) : IStep;\nprivate readonly Queue<IStep> steps = new();\npublic void VerifyComplete() {{ if (steps.Count != 0) throw new InvalidOperationException(\"Unused dependency responses: \" + steps.Peek().Method); }}");
        foreach (var (category, signatures) in contracts)
        {
            foreach (var signature in signatures)
            {
                var open = signature.IndexOf('(');
                var method = signature[..open].Split(' ')[1];
                var returnType = signature[..open].Split(' ')[0];
                var parameters = signature[(open + 1)..signature.LastIndexOf(')')].Split(", ", StringSplitOptions.RemoveEmptyEntries);
                var arguments = string.Join(", ", parameters.Select(p => p[(p.LastIndexOf(' ') + 1)..]));
                if (category == "Unsupported")
                {
                    var stub = stubs.Single(s => s!.S("programId") == program.S("id") && s!.S("method") == method)!;
                    var statementId = Quote(stub.S("statementId"));
                    var guard = stub.S("executionRole") == "internal-logic" ? string.Empty : "if (options.IsExploration) throw new NotSupportedException(\"Exploration blocked: " + stub.S("executionRole") + " " + stub.S("statementId") + "\");\n";
                    router.AppendLine($"public {signature.TrimEnd(';')}\n{{\n{guard}RecordInternal({statementId}, {Quote(method)}, false);\nvar result = fallback.{method}({arguments});\nRecordInternal({statementId}, {Quote(method)}, true);\nreturn result;\n}}");
                }
                else
                {
                    router.AppendLine($"public {signature.TrimEnd(';')} => (external ?? (I{name}{category})fallback).{method}({arguments});");
                }

                if (category == "Unsupported")
                {
                    continue;
                }

                var requestType = parameters[0].Split(' ')[0];
                fake.AppendLine($"private readonly List<{requestType}> captured{method} = [];\npublic IReadOnlyList<{requestType}> Captured{method} => Array.AsReadOnly(captured{method}.ToArray());\npublic void Expect{method}(Func<{requestType}, bool> matcher, {returnType} response)\n{{\nArgumentNullException.ThrowIfNull(matcher);\nArgumentNullException.ThrowIfNull(response);\nsteps.Enqueue(new Step<{requestType}, {returnType}>({Quote(method)}, matcher, response.Copy()));\n}}\npublic {signature.TrimEnd(';')}\n{{\nvar copy = request.Copy();\ncaptured{method}.Add(copy);\nif (steps.Count == 0 || steps.Peek() is not Step<{requestType}, {returnType}> step || step.Method != {Quote(method)}) throw new InvalidOperationException(request.Source.ToString() + \": Missing or out-of-order response\");\nif (!step.Matcher(copy)) throw new InvalidOperationException(request.Source.ToString() + \": Request mismatch\");\nsteps.Dequeue();\nreturn step.Response.Copy();\n}}");
            }
        }

        router.AppendLine("}");
        fake.AppendLine("}");
        files[$"Contracts/{name}Router.g.cs"] = router.ToString();
        files[$"Testing/{name}ScriptedDependencies.g.cs"] = fake.ToString();
    }
}
