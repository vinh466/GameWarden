// Test double for a dedicated game server (docs/10-build-test-release.md §2).
//   FakeGame [--world <file>] [--ready-line <text>] [--ready-after-ms <n>] [--hold-mb <n>]
// stdin commands: save · exit · crash · hang · spam <n> · echo <text>
// CTRL+C saves the world and exits 0, unless "hang" was sent.

var worldPath = Option("--world");
var readyLine = Option("--ready-line") ?? "FakeGame server ready";
var readyAfterMs = int.Parse(Option("--ready-after-ms") ?? "200", System.Globalization.CultureInfo.InvariantCulture);
var holdMb = int.Parse(Option("--hold-mb") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
var hanging = false;

void Save(string reason)
{
    if (worldPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(worldPath))!);
        File.WriteAllText(worldPath, $"saved {DateTimeOffset.UtcNow:O} ({reason})");
    }

    Console.WriteLine($"World saved ({reason})");
}

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    if (hanging)
    {
        Console.WriteLine("CTRL+C ignored (hanging)");
        return;
    }

    Console.WriteLine("CTRL+C received, saving world");
    Save("ctrl-c");
    Environment.Exit(0);
};

var held = holdMb > 0 ? new byte[holdMb * 1024L * 1024L] : Array.Empty<byte>();
Array.Fill(held, (byte)1);

Console.WriteLine($"FakeGame starting (pid {Environment.ProcessId})");
Thread.Sleep(readyAfterMs);
Console.WriteLine(readyLine);

while (Console.ReadLine() is { } line)
{
    var parts = line.Split(' ', 2);
    var argument = parts.Length > 1 ? parts[1] : string.Empty;
    switch (parts[0])
    {
        case "save":
            Save("command");
            break;
        case "exit" when !hanging:
            Save("exit");
            return 0;
        case "crash":
            Console.Error.WriteLine("FakeGame crashing");
            return 3;
        case "hang":
            hanging = true;
            Console.WriteLine("Hanging: CTRL+C and exit are ignored");
            break;
        case "spam":
            var count = int.Parse(argument, System.Globalization.CultureInfo.InvariantCulture);
            for (var i = 0; i < count; i++)
            {
                Console.WriteLine($"spam {i}");
            }

            break;
        case "echo":
            Console.WriteLine(argument);
            break;
        default:
            Console.WriteLine($"ignored: {line}");
            break;
    }
}

GC.KeepAlive(held);
Thread.Sleep(Timeout.Infinite);
return 0;

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
