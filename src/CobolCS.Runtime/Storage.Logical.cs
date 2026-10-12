using System.Text.Json.Nodes;

namespace CobolCS.Runtime;

/// <summary>外部アダプターの論理値交換を提供します。物理レコード配置とは区別します。</summary>
public sealed partial class Storage
{
    /// <summary>項目または集団項目を、精密な文字列と順序付き配列で読み取ります。</summary>
    /// <param name="reference">一度評価して固定した参照。</param>
    /// <param name="eval">固定された添字を読む評価器。</param>
    /// <returns>文字列、配列、子名をキーにした集団の論理値。</returns>
    public JsonNode ReadLogical(JsonNode reference, Func<JsonNode, object> eval)
    {
        var symbol = Symbols[reference.S("symbolId")];
        if (symbol["pic"] == null)
        {
            var children = Children(symbol).ToArray();
            if (children.Length == 0 || symbol.I("level") == 88 || children.Select(c => c.S("name")).Distinct(StringComparer.Ordinal).Count() != children.Length)
            {
                throw new NotSupportedException("Unknown group layout or condition-name transport: " + symbol.S("id"));
            }

            var group = new JsonObject();
            foreach (var child in children)
            {
                var childReference = reference.DeepClone();
                childReference["symbolId"] = child.S("id");
                group.Add(child.S("name"), ReadLogical(childReference, eval));
            }

            return group;
        }

        if (Dimensions(symbol).Length != 0 && !reference.A("subscripts").Any())
        {
            if (reference["modification"] != null)
            {
                throw new NotSupportedException("Array reference modification needs an explicit subscript");
            }

            return new JsonArray(cells[symbol.S("id")].Select(value => (JsonNode?)JsonValue.Create(value is Number number ? number.Decimal(symbol["pic"]!.I("scale")) : Machine.Text(value))).ToArray());
        }

        var value = Get(reference, eval);
        return JsonValue.Create(value is Number number ? number.Decimal(symbol["pic"]!.I("scale")) : Machine.Text(value))!;
    }

    /// <summary>集団・配列の全要素を検査し、全件が適合した場合だけ格納する処理を返します。</summary>
    /// <param name="reference">更新先の固定参照。</param>
    /// <param name="value">アダプターの設定済み論理値。</param>
    /// <param name="eval">固定添字の評価器。</param>
    /// <returns>検査済みの格納処理。</returns>
    public Action PrepareLogical(JsonNode reference, JsonNode value, Func<JsonNode, object> eval)
    {
        var symbol = Symbols[reference.S("symbolId")];
        var actions = new List<Action>();
        if (symbol["pic"] == null)
        {
            var group = value.AsObject();
            var children = Children(symbol).ToArray();
            if (children.Length == 0 || symbol.I("level") == 88 || children.Select(c => c.S("name")).Distinct(StringComparer.Ordinal).Count() != children.Length)
            {
                throw new NotSupportedException("Unknown group layout or condition-name transport: " + symbol.S("id"));
            }

            if (!group.Select(p => p.Key).ToHashSet(StringComparer.Ordinal).SetEquals(children.Select(c => c.S("name"))))
            {
                throw new InvalidDataException("Group response keys missing or unknown: " + symbol.S("qualifiedName"));
            }

            foreach (var child in children)
            {
                var childReference = reference.DeepClone();
                childReference["symbolId"] = child.S("id");
                actions.Add(PrepareLogical(childReference, group[child.S("name")]!, eval));
            }
        }
        else if (Dimensions(symbol) is var dims && dims.Length != 0 && !reference.A("subscripts").Any())
        {
            var array = value.AsArray();
            var count = dims.Aggregate(1, (a, b) => checked(a * b));
            if (array.Count != count)
            {
                throw new InvalidDataException("Table response count mismatch");
            }

            for (var i = 0; i < count; i++)
            {
                var remaining = i;
                var indices = new JsonNode?[dims.Length];
                for (var d = dims.Length - 1; d >= 0; d--)
                {
                    indices[d] = new JsonObject { ["kind"] = "literal", ["valueType"] = "numeric", ["value"] = ((remaining % dims[d]) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) };
                    remaining /= dims[d];
                }

                var target = reference.DeepClone();
                target["subscripts"] = new JsonArray(indices);
                actions.Add(Prepare(target, array[i]!.GetValue<string>(), eval, sizeError: true));
            }
        }
        else
        {
            actions.Add(Prepare(reference, value.GetValue<string>(), eval, sizeError: true));
        }

        return () => actions.ForEach(a => a());
    }

    private IEnumerable<JsonNode> Children(JsonNode symbol) => Symbols.Values.Where(s => s.S("parentId") == symbol.S("id") && s.I("level") != 88);
}
