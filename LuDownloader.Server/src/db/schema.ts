import {
  pgTable, text, uuid, integer, jsonb, boolean, bigint, timestamp
} from 'drizzle-orm/pg-core'

export const tasks = pgTable('tasks', {
  id:          uuid('id').primaryKey().defaultRandom(),
  type:        text('type').notNull(),
  payload:     jsonb('payload').notNull(),
  status:      text('status').notNull().default('pending'),
  progress:    integer('progress').default(0),
  logTail:     text('log_tail').array().default([]),
  error:       text('error'),
  createdAt:   timestamp('created_at').defaultNow().notNull(),
  startedAt:   timestamp('started_at'),
  completedAt: timestamp('completed_at'),
})

export const installedGames = pgTable('installed_games', {
  appId:               text('app_id').primaryKey(),
  gameName:            text('game_name').notNull(),
  installPath:         text('install_path'),
  libraryPath:         text('library_path'),
  installDir:          text('install_dir'),
  installedDate:       text('installed_date'),
  sizeOnDisk:          bigint('size_on_disk', { mode: 'number' }),
  selectedDepots:      text('selected_depots').array(),
  drmStripped:         boolean('drm_stripped').default(false),
  registeredWithSteam: boolean('registered_with_steam').default(false),
  headerImageUrl:      text('header_image_url'),
  executablePath:      text('executable_path'),
  syncedAt:            timestamp('synced_at').defaultNow(),
})

export const savedLibrary = pgTable('saved_library', {
  appId:          text('app_id').primaryKey(),
  gameName:       text('game_name').notNull(),
  addedDate:      text('added_date'),
  headerImageUrl: text('header_image_url'),
})
