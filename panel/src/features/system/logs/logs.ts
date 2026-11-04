/** Level colours for the System log; the word is always shown next to the colour (docs/11 §5). */
export const logLevelColors: Record<string, string> = {
  trace: 'gray',
  debug: 'gray',
  info: 'blue',
  warn: 'yellow',
  error: 'red',
  critical: 'dark',
};

/** Pretty-print structured data when it is JSON; otherwise show it as it came. */
export function prettyData(json: string) {
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
}
