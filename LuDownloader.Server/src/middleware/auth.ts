import type { MiddlewareHandler } from 'hono'

export const apiKeyAuth: MiddlewareHandler = async (c, next) => {
  const key = c.req.header('X-Api-Key')
  if (!process.env.API_KEY || key !== process.env.API_KEY) {
    return c.json({ error: 'Unauthorized' }, 401)
  }
  await next()
}
