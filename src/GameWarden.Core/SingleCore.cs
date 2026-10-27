namespace GameWarden.Core;

/// <summary>One Core per machine (OQ-31): a second Core, e.g. a development console while the service runs, exits.</summary>
internal static class SingleCore
{
    private const string GlobalName = @"Global\GameWarden.Core";
    private const string LocalName = @"Local\GameWarden.Core";

    public static Mutex? TryAcquire()
    {
        try
        {
            var global = new Mutex(initiallyOwned: true, GlobalName, out var created);
            if (created)
            {
                return global;
            }

            global.Dispose();
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // Creating Global\ objects needs SeCreateGlobalPrivilege. Without it, refuse if a service Core
            // already holds the global name, otherwise guard this session only.
            try
            {
                if (Mutex.TryOpenExisting(GlobalName, out var existing))
                {
                    existing.Dispose();
                    return null;
                }
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            var local = new Mutex(initiallyOwned: true, LocalName, out var createdLocal);
            if (createdLocal)
            {
                return local;
            }

            local.Dispose();
            return null;
        }
    }
}
