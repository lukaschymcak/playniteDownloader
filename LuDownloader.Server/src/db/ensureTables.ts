import { db } from './client.js'
import { sql } from 'drizzle-orm'

export async function ensureTables(): Promise<void> {
  await db.execute(sql`
    CREATE TABLE IF NOT EXISTS tasks (
      id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
      type TEXT NOT NULL,
      payload JSONB NOT NULL,
      status TEXT NOT NULL DEFAULT 'pending',
      progress INTEGER DEFAULT 0,
      log_tail TEXT[] DEFAULT '{}',
      error TEXT,
      created_at TIMESTAMPTZ DEFAULT NOW() NOT NULL,
      started_at TIMESTAMPTZ,
      completed_at TIMESTAMPTZ
    )
  `)

  await db.execute(sql`
    CREATE TABLE IF NOT EXISTS installed_games (
      app_id TEXT PRIMARY KEY,
      game_name TEXT NOT NULL,
      install_path TEXT,
      library_path TEXT,
      install_dir TEXT,
      installed_date TEXT,
      size_on_disk BIGINT,
      selected_depots TEXT[],
      drm_stripped BOOLEAN DEFAULT FALSE,
      registered_with_steam BOOLEAN DEFAULT FALSE,
      header_image_url TEXT,
      executable_path TEXT,
      synced_at TIMESTAMPTZ DEFAULT NOW()
    )
  `)

  await db.execute(sql`
    CREATE TABLE IF NOT EXISTS saved_library (
      app_id TEXT PRIMARY KEY,
      game_name TEXT NOT NULL,
      added_date TEXT,
      header_image_url TEXT
    )
  `)

  console.log('DB tables ensured')
}
