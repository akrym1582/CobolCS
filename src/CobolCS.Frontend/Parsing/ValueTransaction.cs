namespace CobolCS.Frontend.Parsing;

/// <summary>解析失敗時に共有IRの変更だけを逆順に戻します。全symbolの複製を文ごとに作ることを避けます。</summary>
internal sealed class ValueTransaction : IDisposable
{
    [ThreadStatic]
    private static ValueTransaction? current;

    private readonly ValueTransaction? parent;
    private readonly List<Action> undo = [];
    private bool completed;

    /// <summary>Initializes a new instance of the <see cref="ValueTransaction"/> class. 同期解析の入れ子単位を開始します。</summary>
    internal ValueTransaction()
    {
        parent = current;
        current = this;
    }

    /// <summary>未確定の変更を逆順に取り消します。共有参照自体を置換しません。</summary>
    public void Dispose()
    {
        if (completed)
        {
            return;
        }

        current = parent;
        for (var index = undo.Count - 1; index >= 0; index--)
        {
            undo[index]();
        }

        completed = true;
    }

    /// <summary>辞書更新前の値を保存します。追加キーは復旧時に削除します。</summary>
    /// <typeparam name="T">辞書キーの型。</typeparam>
    /// <param name="map">更新対象。</param>
    /// <param name="key">更新キー。</param>
    internal static void BeforeMapWrite<T>(Dictionary<T, V> map, T key)
        where T : notnull
    {
        if (current == null)
        {
            return;
        }

        var existed = map.TryGetValue(key, out var old);
        current.undo.Add(() =>
        {
            if (existed)
            {
                map[key] = old!;
            }
            else
            {
                map.Remove(key);
            }
        });
    }

    /// <summary>削除前の辞書順序を保存します。</summary>
    /// <param name="map">削除対象。</param>
    internal static void BeforeMapRemoval(Dictionary<V, V> map)
    {
        if (current == null)
        {
            return;
        }

        var old = map.ToArray();
        current.undo.Add(() =>
        {
            map.Clear();
            foreach (var pair in old)
            {
                map.Add(pair.Key, pair.Value);
            }
        });
    }

    /// <summary>追加前の件数だけを保存します。</summary>
    /// <param name="list">追加対象。</param>
    internal static void BeforeListAppend(List<V> list)
    {
        if (current != null)
        {
            var count = list.Count;
            current.undo.Add(() => list.RemoveRange(count, list.Count - count));
        }
    }

    /// <summary>既存要素の値を保存します。</summary>
    /// <param name="list">更新対象。</param>
    /// <param name="index">更新位置。</param>
    internal static void BeforeListWrite(List<V> list, int index)
    {
        if (current != null)
        {
            var old = list[index];
            current.undo.Add(() => list[index] = old);
        }
    }

    /// <summary>削除要素の値と位置を保存します。</summary>
    /// <param name="list">削除対象。</param>
    /// <param name="index">削除位置。</param>
    internal static void BeforeListRemoval(List<V> list, int index)
    {
        if (current != null)
        {
            var old = list[index];
            current.undo.Add(() => list.Insert(index, old));
        }
    }

    /// <summary>集合更新前の要素を保存します。</summary>
    /// <param name="set">更新対象。</param>
    internal static void BeforeSetWrite(HashSet<V> set)
    {
        if (current == null)
        {
            return;
        }

        var old = set.ToArray();
        current.undo.Add(() =>
        {
            set.Clear();
            set.UnionWith(old);
        });
    }

    /// <summary>成功した子の変更も親の復旧対象として保持します。</summary>
    internal void Commit()
    {
        if (completed)
        {
            return;
        }

        parent?.undo.AddRange(undo);
        completed = true;
        current = parent;
    }
}
