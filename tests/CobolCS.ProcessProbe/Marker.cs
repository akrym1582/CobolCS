namespace CobolCS.ProcessProbe;

/// <summary>プロセス境界のテスト専用エントリ。PythonやOS固有のシェルに依存しません。</summary>
public static class Marker
{
    private static async Task<int> Main(string[] args)
    {
        if (args.FirstOrDefault() == "delay")
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            return 0;
        }

        if (args.FirstOrDefault() == "large")
        {
            Console.Out.Write(new string('a', 200000));
            Console.Error.Write(new string('b', 200000));
            return 0;
        }

        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(args));
        Console.Error.WriteLine("probe-error");
        return 7;
    }
}
