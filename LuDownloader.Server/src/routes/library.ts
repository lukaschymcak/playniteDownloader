import { Hono } from 'hono'
import { db } from '../db/client.js'
import { installedGames, savedLibrary } from '../db/schema.js'
import { notInArray } from 'drizzle-orm'
import type { InstalledGame, SavedLibraryGame } from '../types.js'

export const libraryRouter = new Hono({ strict: false })

libraryRouter.get('/', async (c) => {
  const [installed, saved] = await Promise.all([
    db.select().from(installedGames),
    db.select().from(savedLibrary),
  ])
  return c.json({ installed, saved })
})

libraryRouter.post('/sync', async (c) => {
  const { installed, saved } = await c.req.json<{
    installed: InstalledGame[]
    saved: SavedLibraryGame[]
  }>()

  await db.transaction(async (tx) => {
    if (installed.length > 0) {
      for (const game of installed) {
        await tx.insert(installedGames).values({
          appId: game.appId,
          gameName: game.gameName,
          installPath: game.installPath,
          libraryPath: game.libraryPath,
          installDir: game.installDir,
          installedDate: game.installedDate,
          sizeOnDisk: game.sizeOnDisk,
          selectedDepots: game.selectedDepots,
          drmStripped: game.drmStripped,
          registeredWithSteam: game.registeredWithSteam,
          headerImageUrl: game.headerImageUrl,
          executablePath: game.executablePath,
        }).onConflictDoUpdate({
          target: installedGames.appId,
          set: {
            gameName: game.gameName,
            installPath: game.installPath,
            libraryPath: game.libraryPath,
            installDir: game.installDir,
            installedDate: game.installedDate,
            sizeOnDisk: game.sizeOnDisk,
            selectedDepots: game.selectedDepots,
            drmStripped: game.drmStripped,
            registeredWithSteam: game.registeredWithSteam,
            headerImageUrl: game.headerImageUrl,
            executablePath: game.executablePath,
          },
        })
      }
      const ids = installed.map((g) => g.appId)
      await tx.delete(installedGames).where(notInArray(installedGames.appId, ids))
    } else {
      await tx.delete(installedGames)
    }

    if (saved.length > 0) {
      for (const game of saved) {
        await tx.insert(savedLibrary).values({
          appId: game.appId,
          gameName: game.gameName,
          addedDate: game.addedDate,
          headerImageUrl: game.headerImageUrl,
        }).onConflictDoUpdate({
          target: savedLibrary.appId,
          set: {
            gameName: game.gameName,
            addedDate: game.addedDate,
            headerImageUrl: game.headerImageUrl,
          },
        })
      }
      const ids = saved.map((g) => g.appId)
      await tx.delete(savedLibrary).where(notInArray(savedLibrary.appId, ids))
    } else {
      await tx.delete(savedLibrary)
    }
  })

  return c.json({ ok: true })
})

export default libraryRouter
