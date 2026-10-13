using System.Text;

namespace CobolCS.Frontend;

/// <summary>ソース専用の厳密なデコードと原バイト位置計算を共用し、データ格納文字コードとは区別します。</summary>
internal static class SourceCodec
{
    private static readonly Dictionary<int, int> Aliases = ReadAliases();
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private static readonly Encoding WindowsJapanese = CodePagesEncodingProvider.Instance.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!;

    /// <summary>UTF-8、CP932、JIS X 0201/0208のShift_JISを不正バイト置換なしで読みます。</summary>
    /// <param name="bytes">変更しない原バイト。</param>
    /// <param name="encoding">検証済みのsourceEncoding。</param>
    /// <returns>正規化しないUnicode原文。</returns>
    internal static string Decode(ReadOnlySpan<byte> bytes, string encoding)
    {
        if (encoding == "utf-8")
        {
            return Utf8.GetString(bytes);
        }

        string text;
        try
        {
            text = DecodeWindows(bytes);
        }
        catch (DecoderFallbackException error) when (encoding == "shift_jis")
        {
            // CP932として有効な接頭部を検査し、先行するJIS範囲外の位置を優先します。
            // この接頭部には構造エラーがないため、同じ例外を再帰的に繰り返しません。
            _ = Decode(bytes[..error.Index], encoding);
            throw;
        }

        if (encoding == "cp932")
        {
            return text;
        }

        // .NETの932はWindows拡張なので、Shift_JISでは拡張行と単バイトを拒否します。
        var characters = text.ToCharArray();
        for (int offset = 0, character = 0; offset < bytes.Length; character++)
        {
            var first = bytes[offset];
            if (first <= 0x7f || first is >= 0xa1 and <= 0xdf)
            {
                offset++;
                continue;
            }

            if (!(first is >= 0x81 and <= 0x9f or >= 0xe0 and <= 0xef) || first is 0x87 or 0xed or 0xee)
            {
                throw new DecoderFallbackException("Byte sequence is outside JIS X 0201/0208", [first], offset);
            }

            // 同じ標準バイトに対するWindowsとJISの6文字の対応差だけを復元します。
            var code = (first << 8) | bytes[offset + 1];
            characters[character] = code switch
            {
                0x8160 => '\u301c',
                0x8161 => '\u2016',
                0x817c => '\u2212',
                0x8191 => '\u00a2',
                0x8192 => '\u00a3',
                0x81ca => '\u00ac',
                _ => characters[character],
            };
            offset += 2;
        }

        return new string(characters);
    }

    /// <summary>デコード済み原文の文字境界を原文字コードのバイト位置へ対応させます。</summary>
    /// <param name="text">元文字列またはその接頭部。</param>
    /// <param name="encoding">読み込み時と同じ文字コード。</param>
    /// <returns>元の符号化におけるバイト数。</returns>
    internal static int ByteCount(string text, string encoding)
    {
        if (encoding == "utf-8")
        {
            return Utf8.GetByteCount(text);
        }

        if (encoding == "shift_jis")
        {
            text = text.Replace('\u301c', '\uff5e').Replace('\u2016', '\u2225').Replace('\u2212', '\uff0d').Replace('\u00a2', '\uffe0').Replace('\u00a3', '\uffe1').Replace('\u00ac', '\uffe2');
        }

        return WindowsJapanese.GetByteCount(text);
    }

    private static string DecodeWindows(ReadOnlySpan<byte> bytes)
    {
        // Windowsの厳密デコーダーがbest-fit扱いにするCP932の正規な重複符号だけを復元します。
        var canonical = bytes.ToArray();
        for (var i = 0; i + 1 < canonical.Length; i++)
        {
            var first = canonical[i];
            if (first is >= 0x81 and <= 0x9f or >= 0xe0 and <= 0xfc)
            {
                if (Aliases.TryGetValue((first << 8) | canonical[i + 1], out var replacement))
                {
                    canonical[i] = (byte)(replacement >> 8);
                    canonical[i + 1] = (byte)replacement;
                }

                i++;
            }
        }

        return WindowsJapanese.GetString(canonical);
    }

    private static Dictionary<int, int> ReadAliases()
    {
        // JIS/Windowsの重複符号。各8桁は原2バイトと同じ文字を持つ標準デコード可能な2バイトです。
        const string pairs = "879081e0879181df879281e7879581e3879681db879781da879a81e6879b81bf879c81beed40fa5ced41fa5ded42fa5eed43fa5fed44fa60ed45fa61ed46fa62ed47fa63ed48fa64ed49fa65ed4afa66ed4bfa67ed4cfa68ed4dfa69ed4efa6aed4ffa6bed50fa6ced51fa6ded52fa6eed53fa6fed54fa70ed55fa71ed56fa72ed57fa73ed58fa74ed59fa75ed5afa76ed5bfa77ed5cfa78ed5dfa79ed5efa7aed5ffa7bed60fa7ced61fa7ded62fa7eed63fa80ed64fa81ed65fa82ed66fa83ed67fa84ed68fa85ed69fa86ed6afa87ed6bfa88ed6cfa89ed6dfa8aed6efa8bed6ffa8ced70fa8ded71fa8eed72fa8fed73fa90ed74fa91ed75fa92ed76fa93ed77fa94ed78fa95ed79fa96ed7afa97ed7bfa98ed7cfa99ed7dfa9aed7efa9bed80fa9ced81fa9ded82fa9eed83fa9fed84faa0ed85faa1ed86faa2ed87faa3ed88faa4ed89faa5ed8afaa6ed8bfaa7ed8cfaa8ed8dfaa9ed8efaaaed8ffaabed90faaced91faaded92faaeed93faafed94fab0ed95fab1ed96fab2ed97fab3ed98fab4ed99fab5ed9afab6ed9bfab7ed9cfab8ed9dfab9ed9efabaed9ffabbeda0fabceda1fabdeda2fabeeda3fabfeda4fac0eda5fac1eda6fac2eda7fac3eda8fac4eda9fac5edaafac6edabfac7edacfac8edadfac9edaefacaedaffacbedb0faccedb1facdedb2faceedb3facfedb4fad0edb5fad1edb6fad2edb7fad3edb8fad4edb9fad5edbafad6edbbfad7edbcfad8edbdfad9edbefadaedbffadbedc0fadcedc1faddedc2fadeedc3fadfedc4fae0edc5fae1edc6fae2edc7fae3edc8fae4edc9fae5edcafae6edcbfae7edccfae8edcdfae9edcefaeaedcffaebedd0faecedd1faededd2faeeedd3faefedd4faf0edd5faf1edd6faf2edd7faf3edd8faf4edd9faf5eddafaf6eddbfaf7eddcfaf8edddfaf9eddefafaeddffafbede0fafcede1fb40ede2fb41ede3fb42ede4fb43ede5fb44ede6fb45ede7fb46ede8fb47ede9fb48edeafb49edebfb4aedecfb4bededfb4cedeefb4dedeffb4eedf0fb4fedf1fb50edf2fb51edf3fb52edf4fb53edf5fb54edf6fb55edf7fb56edf8fb57edf9fb58edfafb59edfbfb5aedfcfb5bee40fb5cee41fb5dee42fb5eee43fb5fee44fb60ee45fb61ee46fb62ee47fb63ee48fb64ee49fb65ee4afb66ee4bfb67ee4cfb68ee4dfb69ee4efb6aee4ffb6bee50fb6cee51fb6dee52fb6eee53fb6fee54fb70ee55fb71ee56fb72ee57fb73ee58fb74ee59fb75ee5afb76ee5bfb77ee5cfb78ee5dfb79ee5efb7aee5ffb7bee60fb7cee61fb7dee62fb7eee63fb80ee64fb81ee65fb82ee66fb83ee67fb84ee68fb85ee69fb86ee6afb87ee6bfb88ee6cfb89ee6dfb8aee6efb8bee6ffb8cee70fb8dee71fb8eee72fb8fee73fb90ee74fb91ee75fb92ee76fb93ee77fb94ee78fb95ee79fb96ee7afb97ee7bfb98ee7cfb99ee7dfb9aee7efb9bee80fb9cee81fb9dee82fb9eee83fb9fee84fba0ee85fba1ee86fba2ee87fba3ee88fba4ee89fba5ee8afba6ee8bfba7ee8cfba8ee8dfba9ee8efbaaee8ffbabee90fbacee91fbadee92fbaeee93fbafee94fbb0ee95fbb1ee96fbb2ee97fbb3ee98fbb4ee99fbb5ee9afbb6ee9bfbb7ee9cfbb8ee9dfbb9ee9efbbaee9ffbbbeea0fbbceea1fbbdeea2fbbeeea3fbbfeea4fbc0eea5fbc1eea6fbc2eea7fbc3eea8fbc4eea9fbc5eeaafbc6eeabfbc7eeacfbc8eeadfbc9eeaefbcaeeaffbcbeeb0fbcceeb1fbcdeeb2fbceeeb3fbcfeeb4fbd0eeb5fbd1eeb6fbd2eeb7fbd3eeb8fbd4eeb9fbd5eebafbd6eebbfbd7eebcfbd8eebdfbd9eebefbdaeebffbdbeec0fbdceec1fbddeec2fbdeeec3fbdfeec4fbe0eec5fbe1eec6fbe2eec7fbe3eec8fbe4eec9fbe5eecafbe6eecbfbe7eeccfbe8eecdfbe9eecefbeaeecffbebeed0fbeceed1fbedeed2fbeeeed3fbefeed4fbf0eed5fbf1eed6fbf2eed7fbf3eed8fbf4eed9fbf5eedafbf6eedbfbf7eedcfbf8eeddfbf9eedefbfaeedffbfbeee0fbfceee1fc40eee2fc41eee3fc42eee4fc43eee5fc44eee6fc45eee7fc46eee8fc47eee9fc48eeeafc49eeebfc4aeeecfc4beeeffa40eef0fa41eef1fa42eef2fa43eef3fa44eef4fa45eef5fa46eef6fa47eef7fa48eef8fa49eef981caeefafa55eefbfa56eefcfa57fa4a8754fa4b8755fa4c8756fa4d8757fa4e8758fa4f8759fa50875afa51875bfa52875cfa53875dfa5481cafa58878afa598782fa5a8784fa5b81e6";
        var result = new Dictionary<int, int>();
        for (var i = 0; i < pairs.Length; i += 8)
        {
            result.Add(Convert.ToInt32(pairs.Substring(i, 4), 16), Convert.ToInt32(pairs.Substring(i + 4, 4), 16));
        }

        return result;
    }
}
