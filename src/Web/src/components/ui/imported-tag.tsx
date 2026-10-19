import { Tag } from './tag'

/**
 * Registro trazido do Dolibarr (MIG-01b CA 17): a Tag e a referência de origem (ex.: llx_societe:42), para conferir no
 * Dolibarr enquanto ele estiver no ar.
 */
export function ImportedTag({ source }: { source?: string | null }) {
  if (!source) return null
  const reference = source.replace(/^dolibarr:/, '')
  return (
    <span className="inline-flex items-center gap-1">
      <Tag>Importado do Dolibarr</Tag>
      <span className="text-xs text-muted-foreground tabular-nums">{reference}</span>
    </span>
  )
}
