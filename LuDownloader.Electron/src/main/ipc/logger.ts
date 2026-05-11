import fs from 'node:fs/promises';
import path from 'node:path';
import { logPath, userDataRoot } from './paths';

export async function getDirectorySize(dir: string): Promise<number> {
  let total = 0;
  try {
    for (const entry of await fs.readdir(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) total += await getDirectorySize(full);
      else if (entry.isFile()) total += (await fs.stat(full)).size;
    }
  } catch {
    return total;
  }
  return total;
}

export async function ensureLogRoot(): Promise<void> {
  await fs.mkdir(userDataRoot(), { recursive: true });
}

export async function log(level: 'INFO' | 'WARN' | 'ERROR' | 'DEBUG', message: string): Promise<void> {
  try {
    await ensureLogRoot();
    const line = `[${new Date().toISOString()}] [${level}] ${message}\n`;
    await fs.appendFile(logPath(), line, 'utf8');
  } catch {
    // Logging must never crash the app.
  }
}

export async function info(message: string): Promise<void> {
  await log('INFO', message);
}

export async function warn(message: string): Promise<void> {
  await log('WARN', message);
}

export async function error(message: string): Promise<void> {
  await log('ERROR', message);
}

export function safeError(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}

export function normalizeForLog(value: string): string {
  return value.replace(/\r?\n/g, ' ').trim();
}

export async function writeTextFileAtomic(filePath: string, content: string): Promise<void> {
  await fs.mkdir(path.dirname(filePath), { recursive: true });
  const tmp = `${filePath}.tmp`;
  await fs.writeFile(tmp, content, 'utf8');
  await fs.rename(tmp, filePath);
}
