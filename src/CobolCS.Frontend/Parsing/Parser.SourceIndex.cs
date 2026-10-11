using static CobolCS.Frontend.Parsing.Values;

namespace CobolCS.Frontend.Parsing;

/// <summary>原文範囲による診断の結び付けを、文の順序と同じ最小範囲規則で索引化します。</summary>
internal sealed partial class Parser : V
{
    /// <summary>元fileと包含範囲で最短の文を選び、同幅では元の列挙順を優先します。</summary>
    internal sealed class StatementSourceIndex
    {
        private readonly Dictionary<string, FileRanges> files;
        private readonly Dictionary<(string File, int Start, int End), V> matches = [];

        /// <summary>Initializes a new instance of the <see cref="StatementSourceIndex"/> class.</summary>
        /// <param name="statements">元の列挙順を保持した文。</param>
        internal StatementSourceIndex(V statements)
        {
            files = Items(statements).Select((statement, order) => new SourceRange(statement, order, Number(statement[S("source")][S("start")][S("byteOffset")]), Number(statement[S("source")][S("end")][S("byteOffset")]))).GroupBy(s => Text(s.Statement[S("source")][S("file")]), StringComparer.Ordinal).ToDictionary(g => g.Key, g => new FileRanges(g.OrderBy(s => s.Start).ThenBy(s => s.Order).ToArray()), StringComparer.Ordinal);
        }

        /// <summary>位置を包含する最短の文を探します。</summary>
        /// <param name="source">片方の位置がない場合は従来どおり-1として扱います。</param>
        /// <returns>該当する文、またはNil。</returns>
        internal V Find(V source)
        {
            var file = Call(source, "get", S("file"));
            if (file.Data is not string name || !files.TryGetValue(name, out var ranges))
            {
                return Nil;
            }

            var start = Number(Call(Call(source, "get", S("start"), Dict()), "get", S("byteOffset"), I(-1)));
            var end = Number(Call(Call(source, "get", S("end"), Dict()), "get", S("byteOffset"), I(-1)));
            var key = (name, start, end);
            if (!matches.TryGetValue(key, out var match))
            {
                match = ranges.Find(start, end);
                matches.Add(key, match);
            }

            return match;
        }

        private readonly record struct SourceRange(V Statement, int Order, int Start, int End);

        private sealed class FileRanges
        {
            private readonly SourceRange[] ranges;
            private readonly int[] maximumEnds;

            internal FileRanges(SourceRange[] ranges)
            {
                this.ranges = ranges;
                maximumEnds = new int[ranges.Length * 4];
                Build(1, 0, ranges.Length - 1);
            }

            internal V Find(int start, int end)
            {
                var found = -1;
                Visit(1, 0, ranges.Length - 1);
                return found < 0 ? Nil : ranges[found].Statement;

                void Visit(int node, int first, int last)
                {
                    if (ranges[first].Start > start || maximumEnds[node] < end)
                    {
                        return;
                    }

                    if (first == last)
                    {
                        var candidate = ranges[first];
                        if (found < 0 || (long)candidate.End - candidate.Start < (long)ranges[found].End - ranges[found].Start || (candidate.End - candidate.Start == ranges[found].End - ranges[found].Start && candidate.Order < ranges[found].Order))
                        {
                            found = first;
                        }

                        return;
                    }

                    var middle = first + ((last - first) / 2);
                    Visit(node * 2, first, middle);
                    Visit((node * 2) + 1, middle + 1, last);
                }
            }

            private int Build(int node, int first, int last)
            {
                if (first == last)
                {
                    return maximumEnds[node] = ranges[first].End;
                }

                var middle = first + ((last - first) / 2);
                return maximumEnds[node] = Math.Max(Build(node * 2, first, middle), Build((node * 2) + 1, middle + 1, last));
            }
        }
    }
}
