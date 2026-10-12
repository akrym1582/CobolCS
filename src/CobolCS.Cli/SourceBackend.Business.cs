namespace CobolCS.Cli;

/// <summary>業務名と独立期待値を確定せず、比較基盤と未記入参考ファイルを生成します。</summary>
internal sealed partial class SourceBackend
{
    /// <summary>編集するアダプターと管理する比較器を分離します。</summary>
    /// <param name="files">生成ソリューション。</param>
    private static void BusinessWorkflow(SortedDictionary<string, string> files)
    {
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
                public static void Register(IDictionary<string, IBusinessCaseAdapter> adapters)
                {
                    // adapters.Add("payment-v1", new PaymentAdapter());
                }
            }
            """ + "\n";
        files["Tests/Templates/Business/README.md"] = """
            # Business cases (unconfigured)

            Templates are references and are not discovered as successful tests. Choose stable business names and independently justified expected values explicitly.
            Put DTOs and IBusinessCaseAdapter implementations in UserCode/Business, register adapters in BusinessRegistration.cs, contracts in Tests/BusinessContracts/*.json and fixed cases in Tests/BusinessCases/*.json.
            Contract input/result/termination use type descriptors; operations map stable names to request/response type descriptors. InitialState is explicit test setup, not formal business input. Use Arguments or ACCEPT for formal inputs. Never copy the entire internal Snapshot into the business result.
            BusinessExternalScript.Invoke bridges typed CALL/FILE/SQL requests and responses, validates request values and operation order, and records stable effects. Feed it through the existing typed fake; never fall back to external Stubs. Missing/unused responses fail. Non-deterministic values, including clocks, must be injected explicitly.
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
