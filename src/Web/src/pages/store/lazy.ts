import { lazy } from 'react'

// PDV-07: carregadas sob demanda (o bundle principal está no limite do precache do PWA).
export const SalesPage = lazy(() => import('./SalesPage').then((m) => ({ default: m.SalesPage })))
export const SalePage = lazy(() => import('./SalePage').then((m) => ({ default: m.SalePage })))
// VND-02a: pedidos de venda.
export const OrdersPage = lazy(() => import('./orders/OrdersPage').then((m) => ({ default: m.OrdersPage })))
export const NewOrderPage = lazy(() => import('./orders/NewOrderPage').then((m) => ({ default: m.NewOrderPage })))
export const OrderPage = lazy(() => import('./orders/OrderPage').then((m) => ({ default: m.OrderPage })))
