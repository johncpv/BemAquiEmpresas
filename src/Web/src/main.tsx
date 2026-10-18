import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './lib/zod'
import './styles/tokens.css'
import './index.css'
import { AppProviders } from './api/AppProviders'
import { App } from './App'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AppProviders>
      <App />
    </AppProviders>
  </StrictMode>,
)
