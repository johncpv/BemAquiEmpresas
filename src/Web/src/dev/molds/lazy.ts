import { lazy } from 'react'

/** Moldes da FND-09d carregados sob demanda (o código não entra no bundle principal). */
export const CreateMold = lazy(() => import('./CreateMold'))
export const DetailMold = lazy(() => import('./DetailMold'))
export const PanelMold = lazy(() => import('./PanelMold'))
export const ComponentsMold = lazy(() => import('./ComponentsMold'))
