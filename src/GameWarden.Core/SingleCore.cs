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

    /// <summary>Whether a Core holds the lock; a named mutex lives until its last handle closes.</summary>
    public static bool IsHeld()
    {
        foreach (var name in new[] { GlobalName, LocalName })
        {
            try
            {
                if (Mutex.TryOpenExisting(name, out var existing))
                {
                    existing.Dispose();
                    return true;
                }
            }
            catch (UnauthorizedAccessException)
            {
                return true; // held by the service, which we may not open
            }
        }

        return false;
    }
}
