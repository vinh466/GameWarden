using GameWarden.Setup;

// GameWarden Setup engine (ADR-0013, docs/10 §5-6). Needs Administrator (the manifest asks for it).
const string Usage = """
    usage: GameWarden.Setup.exe [command] [options]

      install [--from <zip>] [--data-root <path>] [--quiet] [--dry-run]
                                 install the package inside this exe (or <zip>), or upgrade to it; the default
      apply <version> [--snapshot <db file>] [--restore-db <db file>]
                                 switch the service to an installed version; roll back if it does not come up
                                 (--restore-db puts that database in place first; --snapshot is used for the rollback)
      uninstall [--remove-data] [--quiet]
                                 remove GameWarden; the data root is kept unless --remove-data
      status                     installed versions, service state, last result
    """;

var arguments = args.ToList();
var quiet = arguments.Remove("--quiet");
var interactive = !quiet && !Console.IsInputRedirected && arguments.Count == 0; // double-clicked
var command = arguments.Count > 0 && !arguments[0].StartsWith("--", StringComparison.Ordinal) ? arguments[0] : "install";
using var log = new SetupLog(command);

if (command is "help" or "-h" or "--help")
{
    Console.WriteLine(Usage);
    return 0;
}

if (!Machine.IsAdministrator())
{
    log.Error("Run GameWarden Setup as Administrator.");
    return 5;
}

int exitCode;
try
{
    var installer = new Installer(log);
    exitCode = command switch
    {
        "install" => await installer.InstallAsync(new InstallOptions(
            TakeOption(arguments, "--from"),
            TakeOption(arguments, "--data-root"),
            quiet,
            arguments.Remove("--dry-run"))),
        "apply" when arguments.Count >= 2 => await installer.ApplyAsync(arguments[1], TakeOption(arguments, "--snapshot"), TakeOption(arguments, "--restore-db")),
        "uninstall" => await installer.UninstallAsync(arguments.Remove("--remove-data"), quiet),
        "status" => installer.Status(),
        _ => UsageError(),
    };
}
catch (Exception ex)
{
    log.Error(ex.ToString());
    exitCode = 1;
}

if (log.FilePath is not null)
{
    Console.WriteLine($"Log: {log.FilePath}");
}

if (interactive)
{
    Console.WriteLine("Press Enter to close.");
    Console.ReadLine();
}

return exitCode;

static string? TakeOption(List<string> list, string name)
{
    var index = list.IndexOf(name);
    if (index < 0 || index + 1 >= list.Count)
    {
        return null;
    }

    var value = list[index + 1];
    list.RemoveRange(index, 2);
    return value;
}

static int UsageError()
{
    Console.Error.WriteLine(Usage);
    return 2;
}
