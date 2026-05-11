import { existsSync, readFileSync } from 'node:fs';
import type { RawAchievement } from '../../../shared/types.ts';

const RECORD_SIZE = 24;

export function parseSmartSteamEmuAchievements(filePath: string): Record<string, RawAchievement> {
  const result: Record<string, RawAchievement> = {};
  if (!existsSync(filePath)) {
    return result;
  }

  try {
    const fileBytes = readFileSync(filePath);
    if (fileBytes.length < 4) {
      return result;
    }

    const expectedCount = fileBytes.readInt32LE(0);
    const dataStart = 4;
    const actualCount = Math.floor((fileBytes.length - dataStart) / RECORD_SIZE);

    if (actualCount !== expectedCount) {
      return result;
    }

    for (let i = 0; i < actualCount; i++) {
      const offset = dataStart + i * RECORD_SIZE;
      try {
        const stateValue = fileBytes.readInt32LE(offset + 20);
        if (stateValue > 1) {
          continue;
        }

        const crc = fileBytes.readUInt32LE(offset);
        const crcHex = crc.toString(16);

        const unlockTime = fileBytes.readInt32LE(offset + 8);

        result[crcHex] = {
          achieved: stateValue === 1,
          unlockTime: stateValue === 1 ? unlockTime : 0,
          curProgress: 0,
          maxProgress: 0
        };
      } catch {
        /* skip record */
      }
    }
  } catch {
    return result;
  }

  return result;
}

export function computeSteamEmuCrc32Utf8(input: string): number {
  let crc = 0xffffffff;
  const buf = Buffer.from(input, 'utf8');
  for (let i = 0; i < buf.length; i++) {
    crc ^= buf[i]!;
    for (let j = 0; j < 8; j++) {
      crc = (crc & 1) === 1 ? (crc >>> 1) ^ 0xedb88320 : crc >>> 1;
    }
  }
  return (crc ^ 0xffffffff) >>> 0;
}
