import { useNavigate } from '@tanstack/react-router'
import { linkParts } from './dashboard'

/** Navegação para um link com busca (o "Ver origem" e o clique no Kpi). */
export function useGo() {
  const navigate = useNavigate()
  return (href: string) => void navigate(linkParts(href) as never)
}
