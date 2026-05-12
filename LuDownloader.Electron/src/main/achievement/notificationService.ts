import { BrowserWindow, screen } from 'electron';
import path from 'node:path';
import { is } from '@electron-toolkit/utils';
import type { Achievement, AchievementDiff, AppSettings } from '../../shared/types';
import { safeError } from '../ipc/logger';

const ANIMATE_IN_MS = 300;
const ANIMATE_OUT_MS = 300;
const GAP_MS = 600;

export interface NotificationItem {
  achievement: Achievement;
  type: 'unlock' | 'progress';
  progressCurrent: number;
  progressMax: number;
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

class NotificationQueue {
  private items: NotificationItem[] = [];
  private isProcessing = false;

  constructor(private readonly presenter: NotificationPresenter) {}

  enqueue(item: NotificationItem): void {
    this.items.push(item);
    void this.drain();
  }

  private async drain(): Promise<void> {
    if (this.isProcessing) return;
    this.isProcessing = true;
    try {
      while (this.items.length > 0) {
        const item = this.items.shift()!;
        try {
          await this.presenter.show(item);
        } catch (err) {
          try {
            const { warn } = await import('../ipc/logger');
            await warn(`[NotificationQueue] ${safeError(err)}`);
          } catch {
            /* Logger unavailable. */
          }
        }
        if (this.items.length > 0) {
          await delay(GAP_MS);
        }
      }
    } finally {
      this.isProcessing = false;
    }
  }

  clear(): void {
    this.items.length = 0;
  }
}

class NotificationPresenter {
  private win: BrowserWindow | null = null;

  constructor(private readonly loadSettings: () => Promise<AppSettings>) {}

  async show(item: NotificationItem): Promise<void> {
    const win = await this.ensureWindow();
    if (!win || win.isDestroyed()) return;
    const settings = await this.loadSettings();
    const height = item.progressMax > 0 ? 110 : 90;
    win.setSize(440, height);
    const pos = this.computePosition(settings, win);
    win.setPosition(pos.x, pos.y);
    win.webContents.send('notification:show', { ...item, notificationPosition: settings.notificationPosition });
    win.show();
    const { info } = await import('../ipc/logger');
    await info(`[Notification] shown apiName=${item.achievement.apiName}`);
    await delay(ANIMATE_IN_MS);
    await delay(settings.notificationDurationSeconds * 1000);
    win.webContents.send('notification:hide');
    await delay(ANIMATE_OUT_MS);
    if (!win.isDestroyed()) {
      win.hide();
    }
  }

  async warmUp(): Promise<void> {
    await this.ensureWindow();
  }

  destroy(): void {
    if (this.win && !this.win.isDestroyed()) {
      this.win.destroy();
    }
    this.win = null;
  }

  private async ensureWindow(): Promise<BrowserWindow | null> {
    if (this.win && !this.win.isDestroyed()) {
      return this.win;
    }
    const win = new BrowserWindow({
      width: 440,
      height: 110,
      frame: false,
      transparent: true,
      alwaysOnTop: true,
      skipTaskbar: true,
      resizable: false,
      // prevents notification from stealing focus mid-game
      focusable: false,
      show: false,
      webPreferences: {
        preload: path.join(__dirname, '../preload/index.mjs'),
        contextIsolation: true,
        nodeIntegration: false,
        sandbox: false
      }
    });
    this.win = win;
    try {
      if (is.dev && process.env.ELECTRON_RENDERER_URL) {
        await win.loadURL(process.env.ELECTRON_RENDERER_URL + '/notification/index.html');
      } else {
        await win.loadFile(path.join(__dirname, '../renderer/notification/index.html'));
      }
    } catch (err) {
      try {
        const { warn } = await import('../ipc/logger');
        await warn(`[NotificationPresenter] load failed: ${safeError(err)}`);
      } catch {
        /* Logger unavailable. */
      }
      if (!win.isDestroyed()) {
        win.destroy();
      }
      this.win = null;
      return null;
    }
    return win;
  }

  private computePosition(settings: AppSettings, win: BrowserWindow): { x: number; y: number } {
    if (!screen) {
      return { x: 20, y: 20 };
    }
    const primary = screen.getPrimaryDisplay();
    const { width: w, height: h } = primary.workAreaSize;
    const [fw, fh] = win.getSize();
    const p = settings.notificationPosition;
    switch (p) {
      case 'bottom-left':
        return { x: 20, y: h - fh - 20 };
      case 'top-right':
        return { x: w - fw - 20, y: 20 };
      case 'top-left':
        return { x: 20, y: 20 };
      case 'bottom-right':
      default:
        return { x: w - fw - 20, y: h - fh - 20 };
    }
  }
}

let queue: NotificationQueue | null = null;
let presenter: NotificationPresenter | null = null;
let loadSettingsRef: (() => Promise<AppSettings>) | null = null;

function diffToItem(diff: AchievementDiff): NotificationItem {
  const type: NotificationItem['type'] = diff.isNewUnlock ? 'unlock' : 'progress';
  return {
    achievement: diff.achievement,
    type,
    progressCurrent: diff.achievement.curProgress,
    progressMax: diff.achievement.maxProgress
  };
}

export function enqueueNotificationDiff(diff: AchievementDiff): void {
  void (async () => {
    if (!queue || !loadSettingsRef) return;
    const settings = await loadSettingsRef();
    if (!settings.notificationEnabled) return;
    if (!diff.isNewUnlock && !diff.isProgressMilestone) return;
    const { info } = await import('../ipc/logger');
    await info(`[Notification] enqueue apiName=${diff.achievement.apiName} type=${diff.isNewUnlock ? 'unlock' : 'progress'}`);
    queue.enqueue(diffToItem(diff));
  })();
}

export function startNotificationService(loadSettingsFn: () => Promise<AppSettings>): void {
  loadSettingsRef = loadSettingsFn;
  presenter = new NotificationPresenter(loadSettingsFn);
  queue = new NotificationQueue(presenter);
  void presenter.warmUp();
}

export function stopNotificationService(): void {
  queue?.clear();
  presenter?.destroy();
  queue = null;
  presenter = null;
  loadSettingsRef = null;
}
