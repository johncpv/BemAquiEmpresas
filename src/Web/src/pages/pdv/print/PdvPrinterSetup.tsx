import { CircleHelp, Printer } from 'lucide-react'
import { useEffect, useState } from 'react'
import { createPrinter } from '@/api/generated/bemaqui'
import type { PosHandshake } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { getAccessToken, isPosPinSession } from '@/api/session'
import { ensureSession } from '@/auth/session-guard'
import { FloorButton, FloorDica, FloorSkeleton, FloorState } from '@/components/shopfloor/kit'
import { cn } from '@/lib/utils'
import type { CatalogPrinter } from '@/pdv/local-store/catalog-types'
import { usePdv } from '@/pdv/pdv-context'
import { BrowserPrintBridge, PRINT_FAILED_MESSAGE } from '@/pdv/print/hardware-bridge'
import { INSTALLED_PRINTER_META, printDocument, targetOf } from '@/pdv/print/print-jobs'
import { PRINTER_MODELS, type PrinterModel } from '@/pdv/print/printer-models'
import { buildTestPage, type ReceiptColumns } from '@/pdv/print/receipt-model'
import { usePrinting } from '@/pdv/print/use-printing'

type Step = 'list' | 'connection' | 'model' | 'test' | 'saved'

const CONNECTIONS = [
  { key: 'Browser', label: 'Pelo navegador', detail: 'A impressora instalada neste computador ou aparelho (USB, rede ou Bluetooth pelo sistema). Funciona sem internet.', enabled: true },
  { key: 'Usb', label: 'USB direto', detail: 'Chega numa próxima versão, com corte e gaveta automáticos.', enabled: false },
  { key: 'Network', label: 'Rede (IP)', detail: 'Chega numa próxima versão.', enabled: false },
  { key: 'Bluetooth', label: 'Bluetooth direto', detail: 'Chega numa próxima versão.', enabled: false },
] as const

const COLUMNS_DICA = 'Colunas são quantas letras cabem numa linha da bobina. 58 mm: 32. 80 mm: 48 com a fonte pequena (a maioria) ou 42 com a fonte normal. Se o teste sair cortado à direita, troque 48 por 42.'

type SavedRegister = { register: PosHandshake }

/**
 * Instalar impressora (/pdv/config/impressora, IMP-01 CA 6 e 7): três passos, sem técnico. 1) a conexão; 2) a largura e o modelo, de uma lista
 * curta; 3) "Imprimir teste" com régua de colunas, acentos, QR Code e corte, e a pergunta "Saiu certo?". Salvar pede o login de quem registra
 * aparelhos (`pos.manage_devices`); a impressora é do caixa e chega aos outros aparelhos dele na sincronização.
 */
export function PdvPrinterSetup() {
  const pdv = usePdv()
  const store = pdv?.store ?? null
  const [register, setRegister] = useState<PosHandshake | null | undefined>(undefined)
  const [step, setStep] = useState<Step>('list')
  const [model, setModel] = useState<PrinterModel>(PRINTER_MODELS[0])
  const [width, setWidth] = useState<58 | 80>(PRINTER_MODELS[0].paperWidthMm)
  const [columns, setColumns] = useState<ReceiptColumns>(PRINTER_MODELS[0].columns)
  const [name, setName] = useState('')
  const [tested, setTested] = useState<'no' | 'printed' | 'failed' | 'wrong'>('no')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!store) return
    let alive = true
    void store.getMeta<SavedRegister>('register').then((s) => { if (alive) setRegister(s?.register ?? null) }).catch(() => { if (alive) setRegister(null) })
    return () => { alive = false }
  }, [store])

  if (store && register === undefined) return <main className="mx-auto max-w-2xl p-4"><FloorSkeleton rows={3} /></main>
  if (!store || !register) {
    return (
      <main id="conteudo" data-surface="floor" className="mx-auto max-w-2xl space-y-4 p-4 sm:p-8">
        <FloorState title="Este aparelho ainda não é um caixa." message="Registre o aparelho antes de instalar a impressora." action={<a href="/pdv/" className="font-semibold text-primary underline">Voltar ao caixa</a>} />
      </main>
    )
  }

  const printerName = name.trim() || `${model.label.split(' (')[0]} · Caixa ${String(register.registerNumber).padStart(2, '0')}`
  const pickModel = (m: PrinterModel) => { setModel(m); setWidth(m.paperWidthMm); setColumns(m.columns); setTested('no') }
  const pickWidth = (w: 58 | 80) => { setWidth(w); setColumns(w === 58 ? 32 : 48); setTested('no') }

  const test = async () => {
    setError(null)
    const doc = buildTestPage({ printerName, columns, paperWidthMm: width, at: new Date().toISOString() })
    const result = await printDocument(new BrowserPrintBridge(), doc, targetOf({ id: 'teste', name: printerName, kind: 'printer', paperWidthMm: width, columns }))
    setTested(result.ok ? 'printed' : 'failed')
  }

  const save = async () => {
    setBusy(true)
    setError(null)
    try {
      const token = getAccessToken()
      if (!(await ensureSession()) || (token && isPosPinSession(token))) {
        setError('Para salvar, entre com o e-mail e a senha de quem registra aparelhos. O teste continua valendo: depois de entrar, volte aqui e salve.')
        return
      }
      const saved = await createPrinter({ registerId: register.registerId, name: printerName, connection: 'Browser', paperWidthMm: width, columns, model: model.key, isDefault: true, testConfirmed: true })
      const record: CatalogPrinter = { id: saved.id, name: saved.name, kind: 'printer', connection: saved.connection, paperWidthMm: saved.paperWidthMm, columns: saved.columns, model: saved.model, isDefault: saved.isDefault }
      await store.setMeta(INSTALLED_PRINTER_META, record).catch(() => { /* a sincronização traz */ })
      pdv?.refreshStatus()
      setStep('saved')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar a impressora. Confira a internet e tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <main id="conteudo" data-surface="floor" className="mx-auto min-h-dvh max-w-2xl space-y-5 bg-background p-4 text-foreground sm:p-8">
      <header className="flex items-start gap-3">
        <div className="min-w-0 flex-1 space-y-1">
          <p className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">Caixa {String(register.registerNumber).padStart(2, '0')} · {register.storeName}</p>
          <h1 className="font-display text-2xl font-bold">Impressora do caixa</h1>
        </div>
        <FloorButton tone="secondary" onClick={() => pdv?.openHelp()} aria-label="Ajuda do caixa" className="px-3"><CircleHelp aria-hidden="true" className="size-6" /><span className="max-sm:sr-only">Ajuda</span></FloorButton>
      </header>

      {step === 'list' ? <PrinterList onInstall={() => setStep('connection')} /> : null}

      {step === 'connection' ? (
        <section aria-labelledby="passo-1" className="space-y-3">
          <h2 id="passo-1" className="text-[20px] font-bold">Passo 1 de 3 · Como a impressora está ligada?</h2>
          <ul className="space-y-[8px]">
            {CONNECTIONS.map((c) => (
              <li key={c.key}>
                <button type="button" disabled={!c.enabled} onClick={() => setStep('model')}
                  className={cn('block min-h-[56px] w-full rounded-md border-2 p-4 text-left', c.enabled ? 'border-primary bg-card' : 'border-border bg-muted text-muted-foreground')}>
                  <b className="block text-[18px]">{c.label}</b><span className="text-[16px]">{c.detail}</span>
                </button>
              </li>
            ))}
          </ul>
          <FloorButton tone="secondary" onClick={() => setStep('list')}>Voltar</FloorButton>
        </section>
      ) : null}

      {step === 'model' ? (
        <section aria-labelledby="passo-2" className="space-y-4">
          <h2 id="passo-2" className="text-[20px] font-bold">Passo 2 de 3 · Largura da bobina e modelo</h2>
          <div role="radiogroup" aria-label="Largura da bobina" className="flex gap-[8px]">
            {([58, 80] as const).map((w) => <FloorButton key={w} role="radio" aria-checked={width === w} tone={width === w ? 'primary' : 'secondary'} className="flex-1" onClick={() => pickWidth(w)}>{w} mm</FloorButton>)}
          </div>
          {width === 80 ? (
            <div className="space-y-1">
              <p className="flex items-center gap-1 font-semibold">Colunas <FloorDica nome="Colunas" texto={COLUMNS_DICA} /></p>
              <div role="radiogroup" aria-label="Colunas" className="flex gap-[8px]">
                {([48, 42] as const).map((c) => <FloorButton key={c} role="radio" aria-checked={columns === c} tone={columns === c ? 'primary' : 'secondary'} className="flex-1" onClick={() => { setColumns(c); setTested('no') }}>{c} colunas</FloorButton>)}
              </div>
            </div>
          ) : <p className="text-[16px] text-muted-foreground">Bobina de 58 mm: 32 colunas.</p>}
          <label className="block space-y-1">
            <span className="font-semibold">Modelo</span>
            <select value={model.key} onChange={(e) => pickModel(PRINTER_MODELS.find((m) => m.key === e.target.value) ?? PRINTER_MODELS[0])}
              className="block min-h-[56px] w-full rounded-md border-2 border-input bg-card px-4 text-[18px]">
              {PRINTER_MODELS.filter((m) => m.paperWidthMm === width || m.key === 'outro').map((m) => <option key={m.key} value={m.key}>{m.label}</option>)}
            </select>
          </label>
          <label className="block space-y-1">
            <span className="font-semibold">Nome da impressora</span>
            <input value={name} maxLength={60} placeholder={printerName} onChange={(e) => setName(e.target.value)}
              className="block min-h-[56px] w-full rounded-md border-2 border-input bg-card px-4 text-[18px]" />
          </label>
          <div className="flex gap-[8px]">
            <FloorButton tone="secondary" className="flex-1" onClick={() => setStep('connection')}>Voltar</FloorButton>
            <FloorButton className="flex-1" onClick={() => setStep('test')}>Continuar</FloorButton>
          </div>
        </section>
      ) : null}

      {step === 'test' ? (
        <section aria-labelledby="passo-3" className="space-y-4">
          <h2 id="passo-3" className="text-[20px] font-bold">Passo 3 de 3 · Imprimir teste</h2>
          <p className="text-[18px]">{printerName}: {width} mm, {columns} colunas. O teste traz a régua de colunas, os acentos, um QR Code e o corte.</p>
          <FloorButton onClick={() => void test()}><Printer aria-hidden="true" className="size-5" />Imprimir teste</FloorButton>
          {tested === 'failed' ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft p-4 font-semibold text-crit-ink">{PRINT_FAILED_MESSAGE} Confira se a impressora está ligada e instalada no aparelho, e tente de novo.</p> : null}
          {tested === 'printed' ? (
            <div className="space-y-3 rounded-md border border-border bg-card p-4">
              <p className="text-[20px] font-semibold">Saiu certo?</p>
              <p className="text-[16px] text-muted-foreground">A régua aparece inteira numa linha só, os acentos saem certos e o celular lê o QR Code.</p>
              <div className="flex flex-wrap gap-[8px]">
                <FloorButton disabled={busy} onClick={() => void save()}>{busy ? 'Salvando…' : 'Sim, salvar a impressora'}</FloorButton>
                <FloorButton tone="secondary" onClick={() => setTested('wrong')}>Não saiu certo</FloorButton>
              </div>
            </div>
          ) : null}
          {tested === 'wrong' ? (
            <div role="status" className="space-y-2 rounded-md border border-warn-soft bg-warn-soft p-4 text-warn-ink">
              <p className="font-semibold">O que fazer:</p>
              <ul className="list-disc space-y-1 pl-6">
                <li>Régua cortada à direita: volte e use 42 colunas (ou confira a largura da bobina).</li>
                <li>Letras pequenas demais ou margem grande: no diálogo de impressão, escala 100% e margens “Nenhuma”.</li>
                <li>Acentos trocados: atualize o driver da impressora no aparelho.</li>
              </ul>
              <FloorButton tone="secondary" onClick={() => { setTested('no'); setStep('model') }}>Voltar e ajustar</FloorButton>
            </div>
          ) : null}
          {error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft p-4 font-semibold text-crit-ink">{error}</p> : null}
          <FloorButton tone="secondary" onClick={() => setStep('model')}>Voltar</FloorButton>
        </section>
      ) : null}

      {step === 'saved' ? (
        <FloorState title="Impressora instalada."
          message="Ela já é a padrão deste caixa. Os outros aparelhos do caixa recebem a configuração na próxima sincronização."
          action={<a href="/pdv/" className="inline-flex min-h-14 items-center rounded-md bg-primary px-5 font-semibold text-primary-foreground">Voltar ao caixa</a>} />
      ) : null}
    </main>
  )
}

function PrinterList({ onInstall }: { onInstall: () => void }) {
  const store = usePdv()!.store
  const { printers } = usePrinting(store)
  return (
    <section className="space-y-3">
      {printers.length === 0 ? (
        <FloorState title="Nenhuma impressora instalada neste caixa." message="Sem impressora, o cupom sai pela impressora padrão do navegador, com 42 colunas." />
      ) : (
        <ul aria-label="Impressoras do caixa" className="divide-y divide-border rounded-md border border-border bg-card">
          {printers.map((p) => (
            <li key={p.id} className="flex items-center justify-between gap-2 p-4 text-[18px]">
              <span className="min-w-0 truncate font-semibold">{p.name}</span>
              <span className="shrink-0 tabular-nums text-muted-foreground">{p.paperWidthMm} mm · {p.columns} col.{p.isDefault ? ' · padrão' : ''}</span>
            </li>
          ))}
        </ul>
      )}
      <div className="flex flex-wrap gap-[8px]">
        <FloorButton onClick={onInstall}><Printer aria-hidden="true" className="size-5" />Instalar impressora</FloorButton>
        <a href="/pdv/" className="inline-flex min-h-14 items-center rounded-md border border-border px-5 font-semibold">Voltar ao caixa</a>
      </div>
    </section>
  )
}
