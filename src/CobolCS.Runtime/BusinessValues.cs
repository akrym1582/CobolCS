using System.Numerics;
using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

/// <summary>業務投影で固定配列・精密数値・明示応答を保持します。</summary>
public static class BusinessValues
{
    /// <summary>表の省略を許さず、宣言順の全要素を検査します。</summary>
    /// <param name="value">固定 JSON 表。</param>
    /// <param name="count">宣言された全要素数。</param>
    /// <returns>元の順の JSON 表。</returns>
    public static JsonArray Array(JsonNode? value, int count) => value is JsonArray a && a.Count == count ? a : throw new ArgumentException("Incomplete business table; expected " + count + " elements");

    /// <summary>平坦な全表を、元の多次元宣言順の1始まり添字にします。</summary>
    /// <param name="index">0 始まりの平坦位置。</param>
    /// <param name="dimensions">固定次元。</param>
    /// <returns>既存 TableEntry に使う添字。</returns>
    public static int[] Subscripts(int index, int[] dimensions)
    {
        var result = new int[dimensions.Length];
        for (var i = dimensions.Length - 1; i >= 0; i--)
        {
            result[i] = (index % dimensions[i]) + 1;
            index /= dimensions[i];
        }

        if (index != 0)
        {
            throw new ArgumentException("Business table index exceeds dimensions");
        }

        return result;
    }

    /// <summary>格納済み scale と一致しない分数を、暗黙に丸めません。</summary>
    /// <param name="value">精密な値。</param>
    /// <param name="scale">固定 PIC の小数桁。</param>
    /// <returns>完全に表せる十進表記。</returns>
    public static string Decimal(Number value, int scale)
    {
        if (value.Denominator.IsZero || value != new Number(value.Units(scale), BigInteger.Pow(10, scale)))
        {
            throw new ArgumentException("Business projection requires explicit rounding policy");
        }

        return value.Decimal(scale);
    }

    /// <summary>fake の終了種別を、未記入時の正常値で補いません。</summary>
    /// <typeparam name="T">生成された応答 enum。</typeparam>
    /// <param name="value">明示された enum 名。</param>
    /// <returns>既存の終了種別。</returns>
    public static T Outcome<T>(JsonNode? value)
        where T : struct, Enum
        => value != null && Enum.GetNames<T>().Contains(value.GetValue<string>(), StringComparer.Ordinal) && Enum.TryParse<T>(value.GetValue<string>(), out var outcome) && Enum.IsDefined(outcome) ? outcome : throw new ArgumentException("Explicit dependency outcome required");
}
