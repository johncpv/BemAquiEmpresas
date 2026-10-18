import { createContext, useContext } from 'react'

export interface NotificationItem {
  id: string
  title: string
  link: string
  createdAt: string
  readAt: string | null
}

/** Fonte das notificações do sino (FND-07). A FND-09c liga em /api/v1/notifications pelo cliente gerado. */
export interface NotificationsSource {
  list: () => Promise<{ items: NotificationItem[]; unread: number }>
  markRead: (id: string) => Promise<void>
}

const empty: NotificationsSource = {
  list: async () => ({ items: [], unread: 0 }),
  markRead: async () => {},
}

export const NotificationsContext = createContext<NotificationsSource>(empty)

export const useNotificationsSource = () => useContext(NotificationsContext)
