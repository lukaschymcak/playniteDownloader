import path from 'node:path';
import fs from 'node:fs/promises';
import type { UserProfile } from '../../shared/types';
import { userDataRoot } from './paths';

function profilePath(): string {
  return path.join(userDataRoot(), 'profile.json');
}

function normalizeProfile(raw: unknown): UserProfile {
  const obj = (raw && typeof raw === 'object' ? raw : {}) as Record<string, unknown>;
  return {
    name: typeof obj['name'] === 'string' && obj['name'].trim() ? obj['name'].trim() : 'Player',
    avatarPath: typeof obj['avatarPath'] === 'string' ? obj['avatarPath'] : null,
    featuredTrophiesByGame:
      obj['featuredTrophiesByGame'] && typeof obj['featuredTrophiesByGame'] === 'object'
        ? (obj['featuredTrophiesByGame'] as Record<string, string[]>)
        : {}
  };
}

export async function loadProfile(): Promise<UserProfile> {
  try {
    const raw = await fs.readFile(profilePath(), 'utf8');
    return normalizeProfile(JSON.parse(raw));
  } catch {
    return normalizeProfile({});
  }
}

export async function saveProfile(profile: UserProfile): Promise<UserProfile> {
  const normalized = normalizeProfile(profile);
  const dest = profilePath();
  const tmp = `${dest}.tmp`;
  await fs.mkdir(path.dirname(dest), { recursive: true });
  await fs.writeFile(tmp, JSON.stringify(normalized, null, 2), 'utf8');
  await fs.rename(tmp, dest);
  return normalized;
}
