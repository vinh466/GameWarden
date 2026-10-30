import type { ModEntryRequest, ModSetView, ModsView } from '@/api/generated';

// The Mods tab changes a set the way the CLI does (docs/13 §4.4 item 4): PUT /mods with the whole list of chosen mods,
// each with its version and `enabled`; Core adds the dependencies again. Pure functions, tested on their own.

/** `Namespace-Name`, as Thunderstore and Core's `Packages.IsPackage` take it. */
const packagePattern = /^[A-Za-z0-9_]+-[A-Za-z0-9_]+$/;
const versionPattern = /^\d{1,9}\.\d{1,9}\.\d{1,9}$/;

/** The chosen mods of a set as PUT /mods takes them; dependencies are left to Core. */
export function chosenEntries(set: ModSetView | null | undefined): ModEntryRequest[] {
  return (set?.entries ?? [])
    .filter((entry) => entry.origin === 'chosen')
    .map((entry) => ({ package: entry.package, version: entry.version, enabled: entry.enabled }));
}

/** Adds a mod, or sets the version of one already chosen. */
export function withMod(entries: ModEntryRequest[], add: ModEntryRequest): ModEntryRequest[] {
  return entries.some((entry) => entry.package === add.package)
    ? entries.map((entry) => (entry.package === add.package ? { ...entry, ...add } : entry))
    : [...entries, add];
}

export function withoutMod(entries: ModEntryRequest[], packageName: string): ModEntryRequest[] {
  return entries.filter((entry) => entry.package !== packageName);
}

export function withEnabled(
  entries: ModEntryRequest[],
  packageName: string,
  enabled: boolean
): ModEntryRequest[] {
  return entries.map((entry) => (entry.package === packageName ? { ...entry, enabled } : entry));
}

/** The chosen mods with the newer versions of the last update check; dependencies follow the mods that need them. */
export function withUpdates(
  entries: ModEntryRequest[],
  updates: { package: string; latest: string }[]
): ModEntryRequest[] {
  const latest = new Map(updates.map((update) => [update.package, update.latest]));
  return entries.map((entry) => {
    const version = latest.get(entry.package);
    return version ? { ...entry, version } : entry;
  });
}

/** The newer versions of chosen mods only: what *Update all* changes. */
export function chosenUpdates(mods: ModsView) {
  const chosen = new Set(chosenEntries(mods.active).map((entry) => entry.package));
  return (mods.newerVersions?.updates ?? []).filter((update) => chosen.has(update.package));
}

/** `Namespace-Name` or `Namespace-Name@1.2.3`; null when it is neither. */
export function parsePackage(text: string): { package: string; version: string | null } | null {
  const [name = '', version, ...rest] = text.trim().split('@');
  if (rest.length > 0 || !packagePattern.test(name)) {
    return null;
  }

  if (version === undefined) {
    return { package: name, version: null };
  }

  return versionPattern.test(version) ? { package: name, version } : null;
}

/** The package's page on Thunderstore, in the community when Core names one. */
export function packageUrl(community: string | null | undefined, packageName: string): string {
  const dash = packageName.indexOf('-');
  const owner = packageName.slice(0, dash);
  const name = packageName.slice(dash + 1);
  return community
    ? `https://thunderstore.io/c/${community}/p/${owner}/${name}/`
    : `https://thunderstore.io/package/${owner}/${name}/`;
}

/** The part after the namespace, as Thunderstore shows a package's name. */
export function displayName(packageName: string): string {
  const dash = packageName.indexOf('-');
  return dash < 0 ? packageName : packageName.slice(dash + 1).replaceAll('_', ' ');
}
