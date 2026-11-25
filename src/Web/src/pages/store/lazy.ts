import { lazy } from 'react'

// PDV-07: carregadas sob demanda (o bundle principal está no limite do precache do PWA).
export const SalesPage = lazy(() => import('./SalesPage').then((m) => ({ default: m.SalesPage })))
export const SalePage = lazy(() => import('./SalePage').then((m) => ({ default: m.SalePage })))
