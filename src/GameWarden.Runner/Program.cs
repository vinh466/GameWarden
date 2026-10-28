using System.Text.Json;
using GameWarden.Contracts;
using GameWarden.Runner;

// GameWarden.Runner --spec <launchSpec.json>
// One Runner per running game server (ADR-0003, docs/02-architecture.md §4).
var specPath = args.Length == 2 && args[0] == "--spec" ? args[1] : null;
if (specPath is null)
{
    Console.Error.WriteLine("usage: GameWarden.Runner --spec <launchSpec.json>");
    return 2;
}

var spec = JsonSerializer.Deserialize<LaunchSpec>(await File.ReadAllTextAsync(specPath), ContractJson.Options)
    ?? throw new InvalidDataException($"Empty launch spec: {specPath}");
Directory.CreateDirectory(spec.StateDirectory);
Directory.CreateDirectory(spec.LogDirectory);
try
{
    // The rendered arguments may hold secrets (e.g. a server password); keep them off the disk.
    File.Delete(specPath);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    // Not fatal; Core overwrites the file on the next start.
}

using var diagnostics = new DiagnosticLog(Path.Combine(spec.StateDirectory, "runner.log"));
diagnostics.Write($"Runner {RunnerInfo.Version} starting for '{spec.InstanceId}' (pid {Environment.ProcessId}).");

// Order matters: own a console, clear the inherited CTRL+C-ignore flag, then start the game (02 §4.3).
ConsoleControl.Prepare(diagnostics);
JobObject.KillChildrenWhenRunnerDies(diagnostics);

await using var host = new GameHost(spec, diagnostics);
return await host.RunAsync();
