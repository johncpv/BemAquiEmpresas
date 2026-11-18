import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '../lib/zod'
import '../styles/tokens.css'
import '../index.css'
import { AppProviders } from '@/api/AppProviders'
import { registerPdvServiceWorker } from '@/pdv/app-update'
import { bootPdv } from './boot'
import { PdvApp } from './PdvApp'

/**
 * Entrada do app do caixa (/pdv/, OFF-01 CA 1): build e service worker próprios, separados do backoffice e do /fab. Abre o armazenamento local
 * uma vez, antes do React, e registra o service worker do /pdv (só no build).
 */
const boot = bootPdv()
registerPdvServiceWorker()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AppProviders>
      <PdvApp boot={boot} />
    </AppProviders>
  </StrictMode>,
)
