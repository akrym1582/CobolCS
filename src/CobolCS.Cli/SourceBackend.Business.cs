namespace CobolCS.Cli;

/// <summary>業務名と独立期待値を確定せず、比較基盤と未記入参考ファイルを生成します。</summary>
internal sealed partial class SourceBackend
{
    /// <summary>編集するアダプターと管理する比較器を分離します。</summary>
    /// <param name="files">生成ソリューション。</param>
    private static void BusinessWorkflow(SortedDictionary<string, string> files)
    {
        files["Testing/BusinessMapping.schema.json"] = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Templates/business-mapping.schema.json.txt"));
        files["Tests/Support/BusinessTests.g.cs"] = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Templates/BusinessTests.g.cs.txt"));
        files["Testing/BusinessHistory.json"] = "{\"schemaVersion\":1,\"requiredCaseIds\":[],\"status\":\"business-cases-unconfigured\"}\n";
        files["UserCode/Business/BusinessRegistration.cs"] = """
            using CobolCS.Runtime;

            namespace CobolCS.Generated;

            /// <summary>業務契約 ID と利用者が選んだアダプターを結び付けます。</summary>
            public static class BusinessRegistration
            {
                /// <summary>独立期待値を作成した契約だけを登録します。重複 ID は Add が拒否します。</summary>
                /// <param name="adapters">ケース別の実行境界。</param>
                public static void Register(IDictionary<string, Func<IBusinessCaseAdapter>> adapters)
                {
                    // adapters.Add("payment-v1", () => new PaymentAdapter());
                }
            }
            """ + "\n";
        files["Tests/Templates/Business/README.md"] = """
            # Business cases (unconfigured)

            Templates are references and are not discovered as successful tests. Choose stable business names and independently justified expected values explicitly.
            Register factories with `IDictionary<string, Func<IBusinessCaseAdapter>>` and `adapters.Add("payment-v1", () => new PaymentAdapter());`. Each execution creates a local registry and a fresh adapter/service/fake. Async disposal takes priority over synchronous disposal. Static state, singleton factories, clocks and environment values require explicit injection and must not be shared. Discovery and prepare do not run factories.
            Put DTOs and IBusinessCaseAdapter implementations in UserCode/Business, register adapters in BusinessRegistration.cs, contracts in Tests/BusinessContracts/*.json and fixed cases in Tests/BusinessCases/*.json.
            Contract input/result/termination use type descriptors; operations map stable names to request/response type descriptors. InitialState is explicit test setup, not formal business input. Use Arguments or ACCEPT for formal inputs. Never copy the entire internal Snapshot into the business result.
            Optional mapping acceptMode=case-sequence connects known CONSOLE scalar ACCEPT through Input.AcceptInput (IAcceptInput.Read(SourceLocation)). It cannot be combined with acceptOrder or nonempty AcceptInputs. Contract acceptInputs metadata selects value arrays and the string sequence field. BusinessAcceptScript reads stable keys and rejects missing, out-of-order, unused or unmapped inputs. Each case gets a new consumer; service replacements keep business keys instead of COBOL statement IDs. See generated case-guide.md for unfilled structures and mandatory formal arguments.
            BusinessExternalScript.Invoke bridges typed CALL/FILE/SQL requests and responses, validates request values and operation order, and records stable effects. Feed it through the existing typed fake; never fall back to external Stubs. Missing/unused responses fail. Non-deterministic values, including clocks, must be injected explicitly.
            Save capture bytes in Tests/BusinessCases/Data; provenance.fileHashes maps safe forward-slash relative paths from Data to actual SHA-256. JSON under Data is not a case; C# under Data is not compiled. Bind referenced capture files in review files too. Missing/modified captures are diagnostics during prepare and failures during test/adopt. Hash equality does not authenticate original COBOL execution or correspondence to expectations.
            Individual xUnit Theory rows use the two 32-character halves of caseId SHA-256; original IDs remain in TestResults/business/<executionId>/cases/<full-hash>.json. started.json binds inputs and implementation; summary.json distinguishes complete from passed. A failure does not stop other cases. The integrity Fact rejects diagnostics and missing required IDs. No configured cases means independent validation count zero. Use `--filter Category=Business` or `--filter Category=Characterization`; select a case with both hash halves in DisplayName. Partial selection does not satisfy adopt.
            Legacy instance registrations and business-execution.json are not accepted as isolated execution. Generate into a new directory and port user edits with the factory signature if managed output cannot be updated compatibly.
            Run `cobolcs prepare .` to register required case history; run `dotnet test Tests/CobolCS.Generated.Tests.csproj` for comparison. No expected-value update command exists.
            Review entries use caseKind=business and bind case/contract hashes in files, all non-test C# implementationHashes, sourceHashes, configHash and contractHashes from manifest. Use reviewed=true only after reviewing input, fixed responses, mapping, completion helpers, expectations and provenance. Adopt additionally requires actual per-case execution in the same stage and preserves required IDs. Implementation hashes are adoption history only after adopt; data contract and expectation edits require explicit hash review.
            baselineKind: csharp-characterization=current C# behavior (not independent correctness); specification=independent source/specification/hand calculation; cobol-reference=captured COBOL, with processor/version/conditions/method/fileHashes. A reviewed exploration observation remains csharp-characterization.
            The comparison uses exact Number strings, complete strings including trailing spaces, strict array order/count, key presence, nullability and types. Empty result contracts, unknown or duplicate keys/IDs, unset values and invalid PIC fail. sourceReferences supply original-statement links in differences.
            """ + "\n";
        files["Tests/Templates/Business/contract.json.txt"] = """
            {"schemaVersion":1,"contractId":"CHOOSE-BUSINESS-NAME-v1","version":1,"reason":"UNFILLED: choose boundary and explain why","input":{"type":"object","fields":{}},"result":{"type":"object","fields":{}},"termination":{"type":"string"},"operations":{},"sourceReferences":[]}
            """ + "\n";
        files["Tests/Templates/Business/case.json.txt"] = """
            {"schemaVersion":1,"caseId":"UNFILLED","programId":"UNFILLED","contractId":"UNFILLED","required":true,"baselineKind":"specification","reviewReason":"UNREVIEWED","provenance":{},"input":{},"responses":[],"expected":null}
            """ + "\n";
    }
}
