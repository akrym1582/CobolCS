using System.Globalization;
using System.Text;

namespace CobolCS.Frontend.Parsing;

/// <summary>IRのコレクションと原文に対する操作です。</summary>
internal static partial class Values
{
    /// <summary>解析値の Call 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="receiver">receiver として渡す操作対象。</param>
    /// <param name="method">method として渡す操作対象。</param>
    /// <param name="arguments">arguments として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static V Call(V receiver, string method, params V[] arguments)
    {
        if (receiver is Parser parser && method == "declarations")
        {
            return parser.M_declarations();
        }

        if (receiver is SourceFrontend front && method == "diagnostic")
        {
            return front.M_diagnostic(arguments[0], arguments[1], arguments[2], arguments.Length > 3 ? arguments[3] : null);
        }

        if (receiver.Data is RegexPattern pattern)
        {
            return RegexFinditer(pattern.Pattern, arguments[0], arguments.Length > 1 ? arguments[1] : null, arguments.Length > 2 ? arguments[2] : null);
        }

        if (receiver.Data is LocatedMatch match)
        {
            var group = arguments.Length == 0 ? 0 : Number(arguments[0]);
            return method switch
            {
                "group" => S(match.OriginalGroup(group)),
                "start" => I(match.Match.Groups[group].Index),
                "end" => I(match.Match.Groups[group].Index + match.Match.Groups[group].Length),
                _ => throw new InvalidOperationException(method),
            };
        }

        if (receiver.Data is FilePath path)
        {
            return method switch
            {
                "resolve" => PathValue(RealPath(path.Path)),
                "read_bytes" => new V(File.ReadAllBytes(path.Path)),
                "is_dir" => B(Directory.Exists(path.Path)),
                "is_file" => B(File.Exists(path.Path)),
                "iterdir" => ListOf(Directory.EnumerateFileSystemEntries(path.Path).Select(PathValue)),
                "is_relative_to" => B(IsRelative(path.Path, Text(arguments[0]))),
                "relative_to" => PathValue(System.IO.Path.GetRelativePath(Text(arguments[0]), path.Path)),
                "as_posix" => S(path.Path.Replace('\\', '/')),
                _ => throw new InvalidOperationException(method),
            };
        }

        if (receiver.Data is byte[] bytes)
        {
            return method switch
            {
                "decode" => S(Utf8.GetString(bytes)),
                "hex" => S(Convert.ToHexStringLower(bytes)),
                _ => throw new InvalidOperationException(method),
            };
        }

        if (receiver.Data is string text)
        {
            return CallText(text, method, arguments);
        }

        if (receiver.Data is Dictionary<V, V> map)
        {
            var key = arguments.Length == 0 ? Nil : arguments[0];
            var fallback = arguments.Length < 2 ? Nil : arguments[1];
            switch (method)
            {
                case "get":
                    return map.GetValueOrDefault(key, fallback);
                case "setdefault":
                    if (!map.ContainsKey(key))
                    {
                        map[key] = fallback;
                    }

                    return map[key];
                case "items":
                    return ListOf(map.Select(p => ListOf(p.Key, p.Value)));
                case "values":
                    return ListOf(map.Values);
                case "pop":
                    if (map.Remove(key, out var popped))
                    {
                        return popped;
                    }

                    return fallback;
                case "update":
                    return Merge(receiver, key);
            }
        }

        if (receiver.Data is HashSet<V> set)
        {
            if (method == "add")
            {
                set.Add(arguments[0]);
            }
            else if (method == "update")
            {
                set.UnionWith(Items(arguments[0]));
            }
            else
            {
                throw new InvalidOperationException(method);
            }

            return Nil;
        }

        if (receiver.Data is List<V> list)
        {
            switch (method)
            {
                case "append":
                    list.Add(arguments[0]);
                    return Nil;
                case "extend":
                    list.AddRange(Items(arguments[0]));
                    return Nil;
                case "pop":
                    var index = arguments.Length == 0 ? list.Count - 1 : Number(arguments[0]);
                    var value = list[index];
                    list.RemoveAt(index);
                    return value;
                case "index":
                    var start = arguments.Length > 1 ? Number(arguments[1]) : 0;
                    for (var i = start; i < list.Count; i++)
                    {
                        if (Equal(list[i], arguments[0]))
                        {
                            return I(i);
                        }
                    }

                    throw new ArgumentException("Value is not in list");
                case "count":
                    return I(list.Count(x => Equal(x, arguments[0])));
            }
        }

        throw new InvalidOperationException("Unknown member " + method + " on " + receiver.Data?.GetType().Name);
    }

    /// <summary>解析値の RealPath 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="path">path として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static string RealPath(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        var root = System.IO.Path.GetPathRoot(full)!;
        foreach (var part in full[root.Length..].Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            root = System.IO.Path.Combine(root, part);
            FileSystemInfo info = Directory.Exists(root) ? new DirectoryInfo(root) : new FileInfo(root);
            if (info.LinkTarget is not null)
            {
                root = info.ResolveLinkTarget(true)!.FullName;
            }
        }

        return root;
    }

    /// <summary>解析値の IsRelative 操作を行い、IRの値と順序を維持します。</summary>
    /// <param name="path">path として渡す操作対象。</param>
    /// <param name="parent">parent として渡す操作対象。</param>
    /// <returns>操作の結果。</returns>
    internal static bool IsRelative(string path, string parent)
    {
        var relative = System.IO.Path.GetRelativePath(parent, path);
        return relative != ".." && !relative.StartsWith("../", StringComparison.Ordinal) && !System.IO.Path.IsPathRooted(relative);
    }

    private static V CallText(string text, string method, V[] args)
    {
        var a = args.Length == 0 ? string.Empty : Text(args[0]);
        switch (method)
        {
            case "upper": return S(SourceCasing.Upper(text));
            case "lower": return S(text.ToLowerInvariant());
            case "strip": return S(TrimText(text, args.Length == 0 ? null : a, true, true));
            case "lstrip": return S(TrimText(text, args.Length == 0 ? null : a, true, false));
            case "rstrip": return S(TrimText(text, args.Length == 0 ? null : a, false, true));
            case "startswith": return B(args[0].Data is string ? text.StartsWith(a, StringComparison.Ordinal) : Items(args[0]).Any(v => text.StartsWith(Text(v), StringComparison.Ordinal)));
            case "endswith": return B(args[0].Data is string ? text.EndsWith(a, StringComparison.Ordinal) : Items(args[0]).Any(v => text.EndsWith(Text(v), StringComparison.Ordinal)));
            case "replace": return S(text.Replace(a, Text(args[1]), StringComparison.Ordinal));
            case "join": return S(string.Join(text, Items(args[0]).Select(Text)));
            case "encode": return new V(Utf8.GetBytes(text));
            case "isdigit": return B(text.Length > 0 && text.EnumerateRunes().All(r => CharUnicodeInfo.GetDigitValue(r.ToString(), 0) >= 0));
            case "hexdigest": return S(text);
            case "splitlines": return SplitLines(text);
            case "split":
                if (args.Length == 0)
                {
                    return ListOf(System.Text.RegularExpressions.Regex.Split(text.Trim(), @"\s+").Where(s => s.Length > 0).Select(S));
                }

                return ListOf(text.Split(a, args.Length > 1 ? Number(args[1]) + 1 : int.MaxValue, StringSplitOptions.None).Select(S));
            case "rsplit":
                var at = text.LastIndexOf(a, StringComparison.Ordinal);
                return at < 0 ? ListOf(S(text)) : ListOf(S(text[..at]), S(text[(at + a.Length)..]));
        }

        throw new InvalidOperationException("Unknown string operation " + method);
    }

    private static string TrimText(string text, string? characters, bool left, bool right)
    {
        var start = 0;
        var end = text.Length;
        bool Trim(char ch) => characters is null ? char.IsWhiteSpace(ch) || ch is >= '\u001c' and <= '\u001f' : characters.Contains(ch);
        while (left && start < end && Trim(text[start]))
        {
            start++;
        }

        while (right && end > start && Trim(text[end - 1]))
        {
            end--;
        }

        return text[start..end];
    }

    private static V SplitLines(string text)
    {
        var lines = new List<V>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '\r' or '\n' or '\v' or '\f' or '\u001c' or '\u001d' or '\u001e' or '\u0085' or '\u2028' or '\u2029')
            {
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                lines.Add(S(text[start..(i + 1)]));
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            lines.Add(S(text[start..]));
        }

        return ListOf(lines);
    }
}
