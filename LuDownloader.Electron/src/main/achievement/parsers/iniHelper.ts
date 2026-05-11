import { existsSync, readFileSync } from 'node:fs';

type InnerBucket = Map<string, { key: string; val: string }>;

function finishInner(inner: InnerBucket): Map<string, string> {
  const m = new Map<string, string>();
  for (const { key, val } of inner.values()) {
    m.set(key, val);
  }
  return m;
}

/**
 * INI parse matching LuiAchieve IniHelper: sections, `;`/`#` comments, case-insensitive section and key (last value wins).
 * Iteration order follows first occurrence of each section header.
 */
export function parseIniFile(filePath: string): Map<string, Map<string, string>> {
  const result = new Map<string, Map<string, string>>();
  if (!existsSync(filePath)) {
    return result;
  }

  let content: string;
  try {
    content = readFileSync(filePath, 'utf8');
  } catch {
    return result;
  }

  const bySecLower = new Map<string, { canonSec: string; inner: InnerBucket }>();
  const orderedSecLower: string[] = [];

  let current: { canonSec: string; inner: InnerBucket } | null = null;

  for (const rawLine of content.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line.startsWith(';') || line.startsWith('#')) {
      continue;
    }

    if (line.startsWith('[') && line.endsWith(']')) {
      const rawSec = line.slice(1, -1).trim();
      const sl = rawSec.toLowerCase();
      let bucket = bySecLower.get(sl);
      if (!bucket) {
        bucket = { canonSec: rawSec, inner: new Map() };
        bySecLower.set(sl, bucket);
        orderedSecLower.push(sl);
      }
      current = bucket;
      continue;
    }

    if (!current) {
      continue;
    }

    const eq = line.indexOf('=');
    if (eq <= 0) {
      continue;
    }

    const rawKey = line.slice(0, eq).trim();
    const rawVal = line.slice(eq + 1).trim();
    const kl = rawKey.toLowerCase();
    const prev = current.inner.get(kl);
    if (prev) {
      prev.val = rawVal;
    } else {
      current.inner.set(kl, { key: rawKey, val: rawVal });
    }
  }

  for (const sl of orderedSecLower) {
    const b = bySecLower.get(sl)!;
    result.set(b.canonSec, finishInner(b.inner));
  }

  return result;
}

export function getIniSection(ini: Map<string, Map<string, string>>, section: string): Map<string, string> | undefined {
  const sl = section.toLowerCase();
  for (const [name, keys] of ini) {
    if (name.toLowerCase() === sl) {
      return keys;
    }
  }
  return undefined;
}

export function iniKeyGet(keys: Map<string, string>, key: string): string | undefined {
  const kl = key.toLowerCase();
  for (const [k, v] of keys) {
    if (k.toLowerCase() === kl) {
      return v;
    }
  }
  return undefined;
}

export function hasIniSections(ini: Map<string, Map<string, string>>, ...sectionNames: string[]): boolean {
  return sectionNames.every((n) => getIniSection(ini, n) !== undefined);
}
