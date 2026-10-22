import { Link } from '@tanstack/react-router'
import type { ReactNode } from 'react'
import { PageHeading } from '@/shell/PageHeading'
import { StageIndicator } from './stage-indicator'
import { StatusChip, type StatusTone } from './status-chip'

interface OrderHeaderProps {
  /** Título da tela: "{número} — {projeto}". */
  title: string
  status: { label: string; tone: StatusTone }
  /** Etapas para o indicador (padrão 3); sem etapas ou com a OS cancelada, o indicador some. */
  stages?: { names: readonly string[]; letters?: readonly (string | null | undefined)[]; current: number } | null
  project?: { id: string; number: string }
  /** Fatos curtos do cabeçalho (cliente, produto, quantidade, tags), na mesma linha. */
  meta?: ReactNode
  actions?: ReactNode
}

/**
 * Cabeçalho da OS (padrão 10 da Fase 3): número e projeto no título, chip de situação, indicador de etapa com o nome, os fatos
 * curtos, o link para o projeto e as ações. Comum à OS de fabricação (PRD-01) e à de instalação (COM-06).
 */
export function OrderHeader({ title, status, stages, project, meta, actions }: OrderHeaderProps) {
  return (
    <PageHeading title={title}>
      <div className="mt-2 flex flex-wrap items-center gap-3">
        <StatusChip tone={status.tone}>{status.label}</StatusChip>
        {stages && stages.names.length > 0 ? <StageIndicator stages={stages.names} letters={stages.letters} current={stages.current} showLabel /> : null}
        {meta}
        {project ? (
          <Link to={'/app/projetos/$id' as never} params={{ id: project.id } as never} search={{ aba: 'itens' } as never} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">
            Ver projeto {project.number}
          </Link>
        ) : null}
        {actions}
      </div>
    </PageHeading>
  )
}