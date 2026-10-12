namespace CobolCS.ProcessProbe;

/// <summary>プロセス境界のテスト専用エントリ。PythonやOS固有のシェルに依存しません。</summary>
public static class Marker
{
    private static async Task<int> Main(string[] args)
    {
        if (args.FirstOrDefault() == "--worker")
        {
            while (Console.ReadLine() is { } line)
            {
                var request = System.Text.Json.Nodes.JsonNode.Parse(line)!;
                switch (request["programId"]!.GetValue<string>())
                {
                    case "delay":
                        await Task.Delay(TimeSpan.FromSeconds(10));
                        break;
                    case "exit":
                        return 13;
                    case "malformed":
                        Console.WriteLine("invalid-json");
                        Console.Out.Flush();
                        continue;
                    case "descendant":
                        using (var child = System.Diagnostics.Process.Start("dotnet", [typeof(Marker).Assembly.Location, "delay"])!)
                        {
                            File.WriteAllText(request["candidate"]!["pidPath"]!.GetValue<string>(), child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                            await Task.Delay(TimeSpan.FromSeconds(10));
                        }

                        break;
                    case "large":
                        Console.Out.Write(new string('a', 2000000));
                        Console.Out.Flush();
                        continue;
                    case "stderr":
                        Console.Error.Write(new string('b', 2000000));
                        Console.Error.Flush();
                        continue;
                    case "unicode":
                        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new string('あ', 400000), new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                        continue;
                }

                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { pid = Environment.ProcessId, reuse = Environment.GetEnvironmentVariable("MSBUILDDISABLENODEREUSE") }));
                Console.Out.Flush();
            }

            return 0;
        }

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
