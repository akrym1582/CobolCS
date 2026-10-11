using System.Numerics;
using System.Text.Json.Nodes;

namespace CobolCS.Frontend.Parsing;

/// <summary>解析中の共有IR値です。JSONの単一親制約を解析状態に持ち込まず、出力時だけJSONへ変換します。</summary>
internal class V
{
    private Dictionary<string, V>? fields;
    private int[]? scalarOffsets;
    private bool scalarOffsetsInitialized;

    /// <summary>Initializes a new instance of the <see cref="V"/> class.</summary>
    /// <param name="data">値の内容。</param>
    internal V(object? data = null)
    {
        Data = data;
    }

    /// <summary>Gets 値の内容。</summary>
    internal object? Data { get; }

    /// <summary>Gets 状態オブジェクトの名前付き要素。</summary>
    internal Dictionary<string, V> Fields => fields ??= new(StringComparer.Ordinal);

    /// <summary>Gets 非BMP文字を含む文字列のスカラー境界。BMPだけならUTF-16添字と一致するためnullです。</summary>
    internal int[]? ScalarOffsets
    {
        get
        {
            if (!scalarOffsetsInitialized)
            {
                var text = (string)Data!;
                if (text.AsSpan().ContainsAnyInRange('\ud800', '\udfff'))
                {
                    var offsets = new List<int>();
                    var offset = 0;
                    foreach (var rune in text.EnumerateRunes())
                    {
                        offsets.Add(offset);
                        offset += rune.Utf16SequenceLength;
                    }

                    offsets.Add(offset);
                    scalarOffsets = offsets.ToArray();
                }

                scalarOffsetsInitialized = true;
            }

            return scalarOffsets;
        }
    }

    /// <summary>Gets or sets 添字またはキーで指定された要素。</summary>
    /// <param name="key">キー。</param>
    /// <returns>選択された値。</returns>
    internal V this[V key]
    {
        get => Values.Index(this, key);
        set => Values.SetIndex(this, key, value);
    }

    /// <summary>共有構造を最終出力の独立したJSONツリーへ変換します。</summary>
    /// <returns>IRのJSON値。</returns>
    internal JsonNode? ToJson() => Data switch
    {
        null => null,
        string text => JsonValue.Create(text),
        bool boolean => JsonValue.Create(boolean),
        BigInteger number => JsonNode.Parse(number.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        JsonValue number => number.DeepClone(),
        double number => JsonValue.Create(number),
        Dictionary<V, V> map => new JsonObject(map.Select(p => KeyValuePair.Create(Values.Text(p.Key), p.Value.ToJson()))),
        IEnumerable<V> sequence => new JsonArray(sequence.Select(x => x.ToJson()).ToArray()),
        _ => throw new InvalidOperationException("Non-IR value: " + Data.GetType().Name),
    };
}
