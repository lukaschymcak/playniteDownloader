import fs from 'node:fs/promises';
import path from 'node:path';
import { safeError, warn, writeTextFileAtomic } from './logger';

export async function readJson<T>(filePath: string, fallback: T): Promise<T> {
  try {
    const raw = await fs.readFile(filePath, 'utf8');
    if (!raw.trim()) {
      return fallback;
    }
    return JSON.parse(raw) as T;
  } catch (err: unknown) {
    if ((err as NodeJS.ErrnoException).code !== 'ENOENT') {
      await warn(`Could not read JSON ${filePath}: ${safeError(err)}`);
    }
    return fallback;
  }
}

export async function writeJson<T>(filePath: string, value: T): Promise<void> {
  await fs.mkdir(path.dirname(filePath), { recursive: true });
  await writeTextFileAtomic(filePath, `${JSON.stringify(value, null, 2)}\n`);
}
