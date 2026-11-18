import { AlertTriangle, Info } from 'lucide-react'
import type { ReactNode } from 'react'
import type { ProductImportView } from '@/api/generated/model'
import { Kpi } from '@/components/ui/kpi'

const plural = (n: number, one: string, many: string) => `${n.toLocaleString('pt-BR')} ${n === 1 ? one : many}`

function Notice({ tone, children }: { tone: 'warn' | 'info'; children: ReactNode }) {
  const Icon = tone === 'warn' ? AlertTriangle : Info
  return (
    <p role={tone === 'warn' ? 'alert' : 'status'} className={tone === 'warn'
      ? 'flex gap-2 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink'
      : 'flex gap-2 rounded-md border border-info-soft bg-info-soft px-3 py-2 text-sm text-info-ink'}>
      <Icon aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
      <span>{children}</span>
    </p>
  )
}

/** Lista de erros por linha (CA 2), com a ação de baixar as linhas com erro (CA 3). */
export function ImportErrors({ view, onDownload }: { view: ProductImportView; onDownload: () => void }) {
  if (view.errorCount === 0) return null
  return (
    <section aria-label="Linhas com erro" className="space-y-2 rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-crit-ink">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="font-semibold">
          {plural(view.errorCount, 'linha com erro', 'linhas com erro')}
          {view.errorCount > view.errors.length ? ` (as primeiras ${view.errors.length.toLocaleString('pt-BR')})` : ''}: {view.errorCount === 1 ? 'ela fica de fora' : 'elas ficam de fora'}.
        </p>
        <button type="button" onClick={onDownload} className="min-h-11 rounded-md border border-border bg-card px-3 py-1.5 text-sm font-semibold text-foreground">
          Baixar linhas com erro
        </button>
      </div>
      <ul className="max-h-64 list-disc space-y-0.5 overflow-y-auto pl-5 text-sm">
        {view.errors.map((e) => <li key={e}>{e}</li>)}
      </ul>
    </section>
  )
}

/**
 * Prévia da importação (CAT-02 CA 2, 5 e 6): total de linhas, quantas criam, quantas atualizam e quantas têm erro; o aviso de preços ignorados
 * sem "Alterar preço", as categorias que serão criadas e o saldo inicial (que pede a confirmação "Lançar saldo inicial").
 */
export function ImportPreview({ view, onDownloadErrors }: { view: ProductImportView; onDownloadErrors: () => void }) {
  return (
    <section aria-label="Prévia da importação" className="space-y-3">
      <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
        <Kpi label="Linhas na planilha" value={view.totalRows.toLocaleString('pt-BR')} />
        <Kpi label="Criam produto" value={view.toCreate.toLocaleString('pt-BR')} />
        <Kpi label="Atualizam produto" value={view.toUpdate.toLocaleString('pt-BR')} />
        <Kpi label="Com erro" value={view.errorCount.toLocaleString('pt-BR')} />
      </div>
      {view.pricesIgnored ? (
        <Notice tone="warn">Você não tem a permissão "Alterar preço": os preços da planilha serão ignorados e a importação segue sem eles.</Notice>
      ) : null}
      {view.newCategories.length > 0 ? (
        <Notice tone="info">{view.newCategories.length === 1 ? 'Categoria nova, criada na importação' : 'Categorias novas, criadas na importação'}: {view.newCategories.join(', ')}.</Notice>
      ) : null}
      {view.stockRows > 0 ? (
        view.canLaunchStock
          ? <Notice tone="info">{plural(view.stockRows, 'linha tem', 'linhas têm')} saldo inicial. Ao importar, você confirma o local e o saldo entra uma vez só.</Notice>
          : <Notice tone="warn">{plural(view.stockRows, 'linha tem', 'linhas têm')} saldo inicial, que não será lançado: falta a permissão "Ajustar estoque". Os produtos entram sem o saldo.</Notice>
      ) : null}
      <ImportErrors view={view} onDownload={onDownloadErrors} />
    </section>
  )
}

/** Relatório final (CA 8): criados, atualizados e ignorados, o saldo lançado e o que já tinha sido lançado antes. */
export function ImportReport({ view, onDownloadErrors }: { view: ProductImportView; onDownloadErrors: () => void }) {
  return (
    <section aria-label="Resultado da importação" className="space-y-3">
      <div className="grid grid-cols-3 gap-2">
        <Kpi label="Criados" value={view.created.toLocaleString('pt-BR')} />
        <Kpi label="Atualizados" value={view.updated.toLocaleString('pt-BR')} />
        <Kpi label="Ignorados" value={view.skipped.toLocaleString('pt-BR')} context={view.skipped > 0 ? 'Linhas com erro' : undefined} />
      </div>
      {view.launchStock ? (
        <Notice tone="info">
          Saldo inicial lançado em {plural(view.stockPosted, 'produto', 'produtos')}{view.locationName ? `, no local ${view.locationName}` : ''}.
          {view.stockAlreadyPosted > 0 ? ` ${plural(view.stockAlreadyPosted, 'produto já tinha', 'produtos já tinham')} o saldo de uma importação anterior e não ${view.stockAlreadyPosted === 1 ? 'recebeu' : 'receberam'} de novo.` : ''}
        </Notice>
      ) : null}
      {view.failureMessage ? <Notice tone="warn">{view.failureMessage}</Notice> : null}
      <ImportErrors view={view} onDownload={onDownloadErrors} />
    </section>
  )
}
