// Test double for a dedicated game server and for SteamCMD (docs/10-build-test-release.md §2).
//   FakeGame [--world <file>] [--ready-line <text>] [--ready-after-ms <n>] [--hold-mb <n>] [--log-file <file>]
//     --log-file writes the console to a file instead of stdout, like Unity's -logFile.
//     stdin commands: save · exit · crash · hang · spawn-child · spam <n> · echo <text>
//     CTRL+C (SIGINT on Linux) saves the world and exits 0, unless "hang" was sent.
//   FakeGame --child                      a process started by "spawn-child" (like a launcher's real server)
//   FakeGame ... +app_update <id> ...     behaves like SteamCMD installing app <id> into +force_install_dir;
//                                         app 404 (or FAKESTEAMCMD_FAIL=1) fails like a network error
//   FakeGame +quit                        behaves like a SteamCMD self-update run
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

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
var restPort = Option("--rest-port") is { } rest ? int.Parse(rest, CultureInfo.InvariantCulture) : (int?)null;
var restPassword = Option("--rest-password") ?? "test";
var shutdownDelayMs = int.Parse(Option("--shutdown-delay-ms") ?? "100", CultureInfo.InvariantCulture);
var hanging = false;
if (Option("--log-file") is { } logFile)
{
    var log = new FileStream(logFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
    Console.SetOut(new StreamWriter(log) { AutoFlush = true });
}

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

if (args.Contains("--utf16-first-line"))
{
    // Palworld's first console line is UTF-16 text while the rest is ASCII (spike S-P1); the Runner must keep the
    // lines after it readable (docs/14 §4.4).
    Console.Out.Flush();
    var first = Encoding.Unicode.GetPreamble()
        .Concat(Encoding.Unicode.GetBytes("Game version is v0.0.0 (FakeGame)\r\n"))
        .ToArray();
    var stdout = Console.OpenStandardOutput();
    stdout.Write(first);
    stdout.Flush();
}

if (restPort is { } port)
{
    _ = Task.Run(() => RestServer(port, restPassword, shutdownDelayMs));
}

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

void RestServer(int port, string password, int shutdownDelay)
{
    var listener = new TcpListener(IPAddress.Loopback, port);
    listener.Start();
    Console.WriteLine($"REST API started on port {port}");
    while (true)
    {
        using var client = listener.AcceptTcpClient();
        using var stream = client.GetStream();
        var buffer = new byte[8192];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (Encoding.ASCII.GetString(buffer, 0, total).Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                break;
            }
        }

        var request = Encoding.ASCII.GetString(buffer, 0, total);
        var lines = request.Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts.Length < 2)
        {
            continue;
        }

        var method = parts[0];
        var path = parts[1];
        if (!Authorized(lines, password))
        {
            Respond(stream, 401, "{\"error\":\"unauthorized\"}");
            continue;
        }

        switch (method, path)
        {
            case ("GET", "/v1/api/info"):
                Respond(stream, 200, "{\"version\":\"fake\",\"servername\":\"FakeGame\"}");
                break;
            case ("POST", "/v1/api/save"):
                Save("rest-save");
                Respond(stream, 200, "{}");
                break;
            case ("POST", "/v1/api/shutdown"):
                Respond(stream, 200, "{}");
                _ = Task.Run(async () =>
                {
                    await Task.Delay(shutdownDelay);
                    Save("rest-shutdown");
                    Environment.Exit(0);
                });
                break;
            default:
                Respond(stream, 404, "{\"error\":\"not found\"}");
                break;
        }
    }
}

static bool Authorized(string[] lines, string password)
{
    var header = lines.FirstOrDefault(line => line.StartsWith("Authorization: Basic ", StringComparison.OrdinalIgnoreCase));
    if (header is null)
    {
        return false;
    }

    try
    {
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Authorization: Basic ".Length..].Trim()));
        return decoded == $"admin:{password}";
    }
    catch (FormatException)
    {
        return false;
    }
}

static void Respond(NetworkStream stream, int status, string json)
{
    var body = Encoding.UTF8.GetBytes(json);
    var reason = status switch { 200 => "OK", 401 => "Unauthorized", _ => "Not Found" };
    var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
    stream.Write(Encoding.ASCII.GetBytes(head));
    stream.Write(body);
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

    if (appId == "404" || Environment.GetEnvironmentVariable("FAKESTEAMCMD_FAIL") == "1")
    {
        Console.WriteLine($"ERROR! Failed to install app '{appId}' (No connection)");
        return 8;
    }

    foreach (var progress in new[] { 0.0, 25.5, 50.25, 75.0, 100.0 })
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $" Update state (0x61) downloading, progress: {progress:0.00} ({progress * 10:0} / 1000)"));
        Thread.Sleep(50);
    }

    // "Install" a game server: a copy of FakeGame itself, with the apphost's name on this OS.
    Directory.CreateDirectory(installDir);
    var apphost = OperatingSystem.IsWindows() ? "FakeGame.exe" : "FakeGame";
    foreach (var file in new[] { apphost, "FakeGame.dll", "FakeGame.runtimeconfig.json", "FakeGame.deps.json" })
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
