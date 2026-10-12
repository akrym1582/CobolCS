using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

/// <summary>業務 DTO と Executor または採用後のサービスを接続する利用者所有の境界です。</summary>
public interface IBusinessCaseAdapter
{
    /// <summary>Gets 版を含む安定した業務契約 ID。</summary>
    string ContractId { get; }

    /// <summary>正式入力を変換して実行し、業務観測へ投影します。時刻などは明示入力で注入してください。</summary>
    /// <param name="input">業務値と固定応答。</param>
    /// <returns>正常完了した観測。必要な応答が欠ければ失敗します。</returns>
    BusinessObservation Execute(BusinessCaseInput input);
}
