// Checks that relative Markdown links resolve and that every Excalidraw diagram parses
// with unique element ids and resolvable text containers and arrow bindings.
// Usage: node build/check-docs.mjs [repo-root]
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';

const root = resolve(process.argv[2] ?? '.');
const skipped = new Set(['.git', '.claude', 'node_modules', 'bin', 'obj', 'TestResults']);
const failures = [];
let links = 0;
let diagrams = 0;

function walk(dir, visit) {
  for (const name of readdirSync(dir)) {
    if (skipped.has(name)) continue;
    const path = join(dir, name);
    if (statSync(path).isDirectory()) walk(path, visit);
    else visit(path);
  }
}

function checkMarkdown(file) {
  const text = readFileSync(file, 'utf8');
  for (const match of text.matchAll(/\]\(([^)#\s]+)(#[^)]*)?\)/g)) {
    const target = match[1];
    if (/^[a-z][a-z0-9+.-]*:/i.test(target)) continue; // http:, https:, mailto: ...
    links++;
    if (!existsSync(resolve(dirname(file), target))) {
      failures.push(`broken link: ${relative(root, file)} -> ${target}`);
    }
  }
}

function checkDiagram(file) {
  diagrams++;
  const name = relative(root, file);
  let doc;
  try {
    doc = JSON.parse(readFileSync(file, 'utf8'));
  } catch (error) {
    failures.push(`${name}: not valid JSON (${error.message})`);
    return;
  }
  if (doc.type !== 'excalidraw' || !Array.isArray(doc.elements)) {
    failures.push(`${name}: not an Excalidraw scene`);
    return;
  }
  const byId = new Map(doc.elements.map((element) => [element.id, element]));
  if (byId.size !== doc.elements.length) failures.push(`${name}: duplicate element ids`);
  for (const element of doc.elements) {
    if (element.containerId) {
      const container = byId.get(element.containerId);
      if (!container?.boundElements?.some((bound) => bound.id === element.id)) {
        failures.push(`${name}: text ${element.id} is not bound to its container`);
      }
    }
    for (const bound of element.boundElements ?? []) {
      if (!byId.has(bound.id)) failures.push(`${name}: ${element.id} references missing ${bound.id}`);
    }
    for (const key of ['startBinding', 'endBinding']) {
      if (element[key] && !byId.has(element[key].elementId)) {
        failures.push(`${name}: ${element.id}.${key} points to a missing element`);
      }
    }
  }
}

walk(root, (file) => {
  if (file.endsWith('.md')) checkMarkdown(file);
  else if (file.endsWith('.excalidraw')) checkDiagram(file);
});

for (const failure of failures) console.error(failure);
console.log(`checked ${links} relative links and ${diagrams} diagrams: ${failures.length} problem(s)`);
process.exit(failures.length === 0 ? 0 : 1);
