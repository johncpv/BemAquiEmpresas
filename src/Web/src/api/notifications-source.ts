import { listMyNotifications, markNotificationRead } from '@/api/generated/bemaqui'
import type { NotificationsSource } from '@/shell/notifications'

/** Sino ligado em /api/v1/notifications (FND-07) pelo cliente gerado. */
export const apiNotificationsSource: NotificationsSource = {
  list: async () => {
    const result = await listMyNotifications({ page: 1, pageSize: 10 })
    return {
      unread: result.unread,
      items: result.page.items.map((n) => ({ id: n.id, title: n.title, link: n.link, createdAt: n.createdAt, readAt: n.readAt ?? null })),
    }
  },
  markRead: async (id) => {
    await markNotificationRead(id)
  },
}
