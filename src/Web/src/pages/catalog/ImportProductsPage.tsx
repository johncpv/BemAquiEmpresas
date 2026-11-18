import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { Download, FileSpreadsheet } from 'lucide-react'
import { useState } from 'react'
import {
  confirmProductImport,
  getDownloadProductImportErrorsUrl,
  getDownloadProductImportTemplateUrl,
  getGetProductImportQueryKey,
  getListProductsQueryKey,
  previewProductImport,
  useGetProductImport,
} from '@/api/generated/bemaqui'
import type { ProductImportView } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifyError } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { PRODUCTS, progressPercent, validRows } from './catalog'
import { ImportPreview, ImportReport } from './ImportPreview'

const IMPORT = '/app/catalogo/produtos/importar'

const button = 'inline-flex min-h-11 items-center justify-center gap-2 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold'
const primary = 'inline-flex min-h-11 items-center justify-center gap-2 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-50'

const download = async (url: string, name: string) => {
  try {
    await apiDownload(url, name)
  } catch (e) {
    notifyError(e instanceof ApiError ? e.message : 'Não foi possível baixar o arquivo. Tente de novo.')
  }
}

/**
 * Importar produtos (CAT-02): enviar → prévia → confirmar → relatório. O modelo .xlsx explica cada coluna na primeira aba (CA 1); a prévia mostra
 * o que cria, atualiza e as linhas com erro (CA 2), que podem ser baixadas para corrigir (CA 3). Com saldo inicial, a confirmação usa o verbo
 * "Lançar saldo inicial" e o local (CA 6). Importação grande roda em segundo plano, com o progresso aqui e o aviso no sino. O id da importação
 * fica na URL (?id=), então o link do sino e o recarregar voltam ao mesmo ponto.
 */
export function ImportProductsPage() {
  const navigate = useNavigate()
  const client = useQueryClient()
  const { id } = useSearch({ strict: false }) as { id?: string }
  const canCreate = useCan('catalog.create')
  const [reading, setReading] = useState(false)
  const [fileError, setFileError] = useState<string | null>(null)
  const [stockOpen, setStockOpen] = useState(false)
  const [locationId, setLocationId] = useState('')
  const [busy, setBusy] = useState(false)
  const run = useGetProductImport(id ?? '', {
    query: { enabled: !!id, refetchInterval: (q) => (q.state.data?.status === 'Running' ? 1500 : false) },
  })
  const view = run.data

  const open = (next: ProductImportView) => {
    client.setQueryData(getGetProductImportQueryKey(next.id), next)
    void navigate({ to: IMPORT, search: { id: next.id }, replace: true })
  }

  const send = async (file: File | null) => {
    if (!file) return
    setFileError(null)
    setReading(true)
    try {
      open(await previewProductImport({ file }))
    } catch (e) {
      setFileError(e instanceof ApiError ? e.message : 'Não foi possível ler a planilha. Confira a conexão e envie de novo.')
    } finally {
      setReading(false)
    }
  }

  const confirm = async (launchInitialStock: boolean) => {
    if (!view) return
    setBusy(true)
    try {
      const done = await confirmProductImport(view.id, { launchInitialStock, locationId: launchInitialStock ? locationId || view.suggestedLocationId : null })
      open(done)
      void client.invalidateQueries({ queryKey: getListProductsQueryKey() })
    } finally {
      setBusy(false)
    }
  }

  const importNow = async () => {
    try {
      await confirm(false)
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível importar. Tente de novo.')
    }
  }

  const downloadErrors = () => view && void download(getDownloadProductImportErrorsUrl(view.id), 'linhas-com-erro.xlsx')
  const startOver = () => {
    setFileError(null)
    void navigate({ to: IMPORT, search: {}, replace: true })
  }

  const heading = (
    <PageHeading title="Importar produtos" description="Suba a planilha dos seus produtos em vez de cadastrar um a um. Antes de gravar, você confere a prévia."
      actions={<Link to={PRODUCTS} className={button}>Voltar aos produtos</Link>} />
  )

  if (!canCreate) {
    return (
      <>
        {heading}
        <EmptyState title="Importar produtos exige a permissão &quot;Cadastrar&quot; no Catálogo." description="Peça a quem administra os grupos de usuários para liberar a ação, ou cadastre pelo botão Novo produto." />
      </>
    )
  }

  if (id && run.isLoading) {
    return <>{heading}<Skeleton className="h-48" /></>
  }

  if (id && run.isError) {
    return (
      <>
        {heading}
        <ErrorState title="Não foi possível abrir a importação." message={run.error instanceof ApiError && run.error.status === 404 ? 'Ela não existe mais. Envie a planilha de novo.' : undefined}
          onRetry={() => void run.refetch()} />
        <button type="button" onClick={startOver} className={`${button} mt-3`}>Enviar outra planilha</button>
      </>
    )
  }

  // 1. Enviar: o modelo, as regras curtas e o arquivo.
  if (!view) {
    return (
      <>
        {heading}
        <section aria-label="Enviar planilha" className="max-w-3xl space-y-4 rounded-md border border-border bg-card p-4">
          <ol className="list-decimal space-y-1 pl-5 text-sm">
            <li>Baixe o modelo. A primeira aba explica cada coluna.</li>
            <li>Preencha a aba "Produtos": uma linha por produto, até 20.000 linhas.</li>
            <li>
              Envie a planilha e confira a prévia. O código interno, ou o código de barras, é a chave: importar de novo atualiza, não duplica.
              <Dica nome="Chave da importação" texto="O código interno é o código do item no Estoque. Sem ele, o primeiro código de barras acha o produto. Linha sem nenhum dos dois não entra, para a planilha poder ser importada de novo sem duplicar." />
            </li>
          </ol>
          <div className="flex flex-wrap gap-2">
            <button type="button" onClick={() => void download(getDownloadProductImportTemplateUrl(), 'modelo-importacao-produtos.xlsx')} className={button}>
              <Download aria-hidden="true" className="size-4" />Baixar o modelo
            </button>
            <label className={`${primary} cursor-pointer`}>
              <FileSpreadsheet aria-hidden="true" className="size-4" />Enviar planilha
              <input type="file" accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" className="sr-only" disabled={reading}
                onChange={(e) => { void send(e.target.files?.[0] ?? null); e.target.value = '' }} />
            </label>
          </div>
          <p className="text-xs text-muted-foreground">Só .xlsx (Excel), até 10 MB. O arquivo não fica guardado: só o resumo e as linhas para confirmar.</p>
          {reading ? <div role="status" className="space-y-2"><p className="text-sm">Lendo a planilha…</p><Skeleton className="h-24" /></div> : null}
          {fileError ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">{fileError}</p> : null}
        </section>
      </>
    )
  }

  // 3. Importando (job): progresso, atualizado sozinho.
  if (view.status === 'Running') {
    const pct = progressPercent(view)
    return (
      <>
        {heading}
        <section aria-label="Importando" className="max-w-3xl space-y-2 rounded-md border border-border bg-card p-4">
          <p className="font-semibold">Importando {view.fileName}…</p>
          <div role="progressbar" aria-label="Progresso da importação" aria-valuemin={0} aria-valuemax={100} aria-valuenow={pct} className="h-2 overflow-hidden rounded-full bg-muted">
            <div className="h-full rounded-full bg-primary" style={{ width: `${pct}%` }} />
          </div>
          <p className="text-sm tabular-nums text-muted-foreground">
            {view.processed.toLocaleString('pt-BR')} de {view.toProcess.toLocaleString('pt-BR')} linhas. Pode sair desta tela: avisamos no sino quando terminar.
          </p>
        </section>
      </>
    )
  }

  // 4. Relatório final, com o próximo passo (CA 8).
  if (view.status === 'Completed' || view.status === 'Failed') {
    return (
      <>
        {heading}
        <div className="max-w-3xl space-y-4">
          <p className="font-semibold">{view.status === 'Completed' ? `Importação concluída: ${view.fileName}` : `A importação parou: ${view.fileName}`}</p>
          <ImportReport view={view} onDownloadErrors={downloadErrors} />
          <nav aria-label="Próximo passo" className="flex flex-wrap gap-2">
            <Link to={PRODUCTS} search={{ semPreco: 'sim' } as never} className={primary}>Conferir preços</Link>
            <Link to="/app" className={button}>Voltar aos primeiros passos</Link>
            <button type="button" onClick={startOver} className={button}>Importar outra planilha</button>
          </nav>
        </div>
      </>
    )
  }

  // 2. Prévia: confirmar só as válidas; com saldo inicial, "Lançar saldo inicial" com o local.
  const count = validRows(view)
  const withStock = view.stockRows > 0 && view.canLaunchStock
  const chosen = locationId || view.suggestedLocationId || ''
  const locationName = view.locations.find((l) => l.id === chosen)?.name
  return (
    <>
      {heading}
      <div className="max-w-3xl space-y-4">
        <p className="text-sm"><b>{view.fileName}</b>: confira antes de gravar. Só as linhas sem erro entram.</p>
        <ImportPreview view={view} onDownloadErrors={downloadErrors} />
        {count === 0 ? <p className="text-sm">Nenhuma linha válida para importar. Corrija a planilha e envie de novo.</p> : null}
        <div className="flex flex-wrap gap-2">
          {withStock ? (
            <>
              <button type="button" disabled={count === 0 || busy} onClick={() => setStockOpen(true)} className={primary}>Importar {count.toLocaleString('pt-BR')} {count === 1 ? 'produto' : 'produtos'}</button>
              <ConfirmDialog open={stockOpen} onOpenChange={setStockOpen} title="Lançar saldo inicial" verb="Lançar saldo inicial" done="Saldo inicial lançado"
                description={`${view.stockRows.toLocaleString('pt-BR')} ${view.stockRows === 1 ? 'produto entra' : 'produtos entram'} com saldo inicial${locationName ? ` no local ${locationName}` : ''}, pelo custo da planilha. Importar de novo a mesma planilha não lança o saldo outra vez.`}
                preconditions={[{ ok: !!chosen, text: 'Cadastre um local de estoque para receber o saldo inicial.' }]}
                onConfirm={() => confirm(true)}>
                <label className="block space-y-1 text-sm">
                  <span className="font-medium">Local de destino</span>
                  <select value={chosen} onChange={(e) => setLocationId(e.target.value)} className={inputClass}>
                    {view.locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
                  </select>
                </label>
              </ConfirmDialog>
              <button type="button" disabled={count === 0 || busy} onClick={() => void importNow()} className={button}>Importar sem o saldo</button>
            </>
          ) : (
            <button type="button" disabled={count === 0 || busy} onClick={() => void importNow()} className={primary}>
              {busy ? 'Importando…' : `Importar ${count.toLocaleString('pt-BR')} ${count === 1 ? 'produto' : 'produtos'}`}
            </button>
          )}
          <button type="button" onClick={startOver} className={button}>Enviar outra planilha</button>
        </div>
      </div>
    </>
  )
}
