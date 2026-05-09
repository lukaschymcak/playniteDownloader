import { Hono } from 'hono'
import { db } from '../db/client.js'
import { tasks } from '../db/schema.js'
import { and, eq } from 'drizzle-orm'

export const tasksRouter = new Hono({ strict: false })

tasksRouter.get('/', async (c) => {
  const status = c.req.query('status')
  const rows = status
    ? await db.select().from(tasks).where(eq(tasks.status, status))
    : await db.select().from(tasks)
  return c.json(rows)
})

tasksRouter.post('/', async (c) => {
  const { type, payload } = await c.req.json<{ type: string; payload: unknown }>()
  const [task] = await db
    .insert(tasks)
    .values({ type, payload: payload as Record<string, unknown>, status: 'pending', progress: 0, logTail: [] })
    .returning()
  return c.json(task, 201)
})

tasksRouter.patch('/:id', async (c) => {
  const id = c.req.param('id')
  const body = await c.req.json<{
    status?: string
    progress?: number
    logTail?: string[]
    error?: string
  }>()

  const updates: Partial<typeof tasks.$inferInsert> = {}
  if (body.status !== undefined) {
    updates.status = body.status
    if (body.status === 'running') updates.startedAt = new Date()
    if (body.status === 'completed' || body.status === 'failed') updates.completedAt = new Date()
  }
  if (body.progress !== undefined) updates.progress = body.progress
  if (body.logTail !== undefined) updates.logTail = body.logTail
  if (body.error !== undefined) updates.error = body.error

  const [task] = await db.update(tasks).set(updates).where(eq(tasks.id, id)).returning()
  if (!task) return c.json({ error: 'Not found' }, 404)
  return c.json(task)
})

tasksRouter.delete('/:id', async (c) => {
  const id = c.req.param('id')
  const [task] = await db
    .delete(tasks)
    .where(and(eq(tasks.id, id), eq(tasks.status, 'pending')))
    .returning()
  if (!task) return c.json({ error: 'Task not found or not cancellable' }, 404)
  return c.json({ ok: true })
})

export default tasksRouter
