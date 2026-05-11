import fs from 'node:fs/promises';
import path from 'node:path';
import { existsSync } from 'node:fs';
import type { UserProfile } from '../../shared/types.ts';

async function writeTextFileAtomic(filePath: string, content: string): Promise<void> {
  await fs.mkdir(path.dirname(filePath), { recursive: true });
  const tmp = `${filePath}.tmp`;
  await fs.writeFile(tmp, content, 'utf8');
  await fs.rename(tmp, filePath);
}

export function defaultUserProfile(): UserProfile {
  return {
    name: 'Player',
    avatarPath: undefined,
    featuredTrophiesByGame: {}
  };
}

function normalizeFeatured(raw: unknown): Record<string, string[]> {
  const out: Record<string, string[]> = {};
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) {
    return out;
  }
  for (const [k, v] of Object.entries(raw)) {
    if (Array.isArray(v) && v.every((x) => typeof x === 'string')) {
      out[k] = v;
    }
  }
  return out;
}

/** Normalizes persisted shape after JSON parse (partial file safe). */
export function normalizeUserProfile(raw: unknown): UserProfile {
  const base = defaultUserProfile();
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) {
    return base;
  }
  const o = raw as Record<string, unknown>;
  const name =
    typeof o.name === 'string' && o.name.trim().length > 0 ? o.name.trim() : base.name;
  let avatarPath: string | null | undefined = base.avatarPath;
  if (typeof o.avatarPath === 'string') {
    avatarPath = o.avatarPath;
  } else if (o.avatarPath === null) {
    avatarPath = null;
  }
  return {
    name,
    avatarPath,
    featuredTrophiesByGame: normalizeFeatured(o.featuredTrophiesByGame)
  };
}

export class ProfileService {
  private readonly profilePath: string;

  constructor(profilePath: string) {
    this.profilePath = profilePath;
  }

  async load(): Promise<UserProfile> {
    try {
      if (!existsSync(this.profilePath)) {
        return defaultUserProfile();
      }
      const rawText = await fs.readFile(this.profilePath, 'utf8');
      if (!rawText.trim()) {
        return defaultUserProfile();
      }
      const parsed: unknown = JSON.parse(rawText);
      return normalizeUserProfile(parsed);
    } catch {
      return defaultUserProfile();
    }
  }

  async save(profile: UserProfile | null | undefined): Promise<void> {
    const safe = normalizeUserProfile(profile ?? defaultUserProfile());
    await writeTextFileAtomic(this.profilePath, `${JSON.stringify(safe, null, 2)}\n`);
  }
}
