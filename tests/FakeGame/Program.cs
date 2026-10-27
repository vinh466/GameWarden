// Test double for a dedicated game server and for SteamCMD (docs/10-build-test-release.md §2).
//   FakeGame [--world <file>] [--ready-line <text>] [--ready-after-ms <n>] [--hold-mb <n>]
//     stdin commands: save · exit · crash · hang · spawn-child · spam <n> · echo <text>
//     CTRL+C saves the world and exits 0, unless "hang" was sent.
//   FakeGame --child                      a process started by "spawn-child" (like a launcher's real server)
//   FakeGame ... +app_update <id> ...     behaves like SteamCMD installing app <id> into +force_install_dir
//   FakeGame +quit                        behaves like a SteamCMD self-update run
using System.Diagnostics;
using System.Globalization;

if (args.Contains("+quit"))
{
    return SteamCmd();
}

if (args.Contains("--child"))
{
    return Child();
}

var worldPath = Option("--world");
var readyLine = Option("--ready-line") ?? "FakeGame server ready";
var readyAfterMs = int.Parse(Option("--ready-after-ms") ?? "200", CultureInfo.InvariantCulture);
var holdMb = int.Parse(Option("--hold-mb") ?? "0", CultureInfo.InvariantCulture);
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
        case "spawn-child":
            // Like a launcher: start the "real" server, then exit.
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            info.ArgumentList.Add("--child");
            using (var child = Process.Start(info)!)
            {
                Console.WriteLine($"spawned child {child.Id}");
            }

            return 0;
        case "spam":
            var count = int.Parse(argument, CultureInfo.InvariantCulture);
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

int Child()
{
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        Console.WriteLine("child: CTRL+C received");
        Environment.Exit(0);
    };
    Console.WriteLine($"FakeGame child running (pid {Environment.ProcessId})");
    Thread.Sleep(Timeout.Infinite);
    return 0;
}

int SteamCmd()
{
    Console.WriteLine("Steam Console Client (c) Valve Corporation - version 1 (FakeGame)");
    Console.WriteLine("Loading Steam API...OK");
    var installDir = Option("+force_install_dir");
    var appId = Option("+app_update");
    if (installDir is null || appId is null)
    {
        return 0; // a "+quit" self-update run
    }

    if (Environment.GetEnvironmentVariable("FAKESTEAMCMD_FAIL") == "1")
    {
        Console.WriteLine($"ERROR! Failed to install app '{appId}' (No subscription)");
        return 8;
    }

    foreach (var progress in new[] { 0.0, 25.5, 50.25, 75.0, 100.0 })
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $" Update state (0x61) downloading, progress: {progress:0.00} ({progress * 10:0} / 1000)"));
        Thread.Sleep(50);
    }

    // "Install" a game server: a copy of FakeGame itself.
    Directory.CreateDirectory(installDir);
    foreach (var file in new[] { "FakeGame.exe", "FakeGame.dll", "FakeGame.runtimeconfig.json", "FakeGame.deps.json" })
    {
        var source = Path.Combine(AppContext.BaseDirectory, file);
        if (File.Exists(source))
        {
            File.Copy(source, Path.Combine(installDir, file), overwrite: true);
        }
    }

    Console.WriteLine($"Success! App '{appId}' fully installed.");
    return 0;
}

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
