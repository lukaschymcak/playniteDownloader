import Registry from 'winreg';
import type { RegistryAdapter } from './discoveryService';

export class WinRegAdapter implements RegistryAdapter {
  async listSubKeys(baseKey: string): Promise<string[]> {
    const key = this.makeKey(baseKey);
    const items = await new Promise<Array<{ key: string }>>((resolve, reject) => {
      key.keys((err: Error | null, result: Array<{ key: string }>) => {
        if (err) {
          reject(err);
          return;
        }
        resolve(result as Array<{ key: string }>);
      });
    });

    return items
      .map((item) => item.key.split('\\').filter(Boolean).pop() ?? '')
      .filter((item) => item.length > 0);
  }

  async listValueNames(keyPath: string): Promise<string[]> {
    const key = this.makeKey(keyPath);
    return new Promise((resolve) => {
      key.values((err: Error | null, items: Array<{ name: string }>) => {
        if (err) {
          resolve([]);
          return;
        }
        resolve(items.map((i) => i.name));
      });
    });
  }

  async getValue(keyPath: string, valueName: string): Promise<string | number | null> {
    const key = this.makeKey(keyPath);
    const item = await new Promise<{ value: string } | null>((resolve) => {
      key.get(valueName, (err: Error | null, result: { value: string }) => {
        if (err) {
          // winreg signals missing values via error; treat as null for resilient scan.
          resolve(null);
          return;
        }
        resolve((result as { value: string }) ?? null);
      });
    });

    if (!item) return null;
    const asNumber = Number(item.value);
    return Number.isNaN(asNumber) ? item.value : asNumber;
  }

  private makeKey(keyPath: string) {
    const sanitized = keyPath.startsWith('\\') ? keyPath : `\\${keyPath}`;
    return new Registry({
      hive: Registry.HKCU,
      key: sanitized
    });
  }
}
