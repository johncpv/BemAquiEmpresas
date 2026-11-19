import { CloudOff, Search } from 'lucide-react'
import { type ReactNode, useState } from 'react'
import { FloorButton, FloorSheet } from '@/components/shopfloor/kit'
import { formatCents } from '@/pdv/money'
import type { CatalogItem } from '@/pdv/local-store/catalog-types'
import { checkCustomerDocument, formatDocument } from '@/pdv/sale/customer-document'
import { formatQuantity, parseQuantity } from '@/pdv/sale/quantity'
import { type Sale, type SaleLine, saleTotal } from '@/pdv/sale/sale-model'

/**
 * Folhas da tela de venda (PDV-02): quantidade, observação, remoção, espera, descarte, cliente, pagamento e fechamento do caixa. As que têm
 * campo são montadas só enquanto abertas (a tela as cria de novo a cada abertura), então o campo sempre começa do valor atual.
 */

const field = 'block min-h-[56px] w-full rounded-md border-2 border-input bg-card px-4 text-[22px] tabular-nums text-foreground'

function Actions({ children }: { children: ReactNode }) {
  return <div className="flex gap-[16px]">{children}</div>
}

function Problem({ message }: { message: string | null }) {
  return message ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft p-4 text-[18px] font-semibold text-crit-ink">{message}</p> : null
}

/** CA 9: quantidade da linha (ou do pesável que vai entrar). */
export function QuantitySheet({ open, title, fractional, initial, onCancel, onSave }: {
  open: boolean
  title: string
  fractional: boolean
  initial: string
  onCancel: () => void
  onSave: (value: string) => Promise<string | null>
}) {
  const [value, setValue] = useState(initial)
  const [error, setError] = useState<string | null>(null)
  const save = async () => {
    const parsed = parseQuantity(value, fractional)
    if (!parsed.ok) { setError(parsed.message); return }
    const problem = await onSave(parsed.value)
    if (problem) setError(problem)
  }

  return (
    <FloorSheet open={open} onOpenChange={(o) => { if (!o) onCancel() }} title={title}
      description={fractional ? 'Produto vendido por peso ou medida: até 3 casas depois da vírgula (ex.: 0,350).' : 'Produto vendido por unidade: só número inteiro.'}
      actions={<Actions><FloorButton tone="secondary" className="flex-1" onClick={onCancel}>Voltar</FloorButton><FloorButton className="flex-1" onClick={() => void save()}>Confirmar</FloorButton></Actions>}>
      <label className="block space-y-2">
        <span className="font-semibold">Quantidade</span>
        <input autoFocus inputMode={fractional ? 'decimal' : 'numeric'} value={value} onChange={(e) => { setValue(e.target.value); setError(null) }}
          onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); void save() } }} className={field} aria-invalid={!!error} />
      </label>
      <Problem message={error} />
    </FloorSheet>
  )
}

/** CA 11: observação da linha (sai no cupom). */
export function NoteSheet({ open, line, onCancel, onSave }: { open: boolean; line: SaleLine | null; onCancel: () => void; onSave: (note: string) => Promise<void> }) {
  const [value, setValue] = useState(line?.note ?? '')
  return (
    <FloorSheet open={open} onOpenChange={(o) => { if (!o) onCancel() }} title="Observação do item" description={line?.name}
      actions={<Actions><FloorButton tone="secondary" className="flex-1" onClick={onCancel}>Voltar</FloorButton><FloorButton className="flex-1" onClick={() => void onSave(value)}>Gravar</FloorButton></Actions>}>
      <label className="block space-y-2">
        <span className="font-semibold">Observação (sai no cupom)</span>
        <textarea autoFocus rows={3} maxLength={140} value={value} onChange={(e) => setValue(e.target.value)} placeholder="Ex.: sem cebola, embalar para presente"
          className="block w-full rounded-md border-2 border-input bg-card px-4 py-3 text-[20px] text-foreground" />
      </label>
      <p className="text-muted-foreground">Deixe em branco para tirar a observação.</p>
    </FloorSheet>
  )
}

/** CA 10: remover a linha (o último item sai sem supervisor; outra linha pede quem tem `pos.cancel_item`). */
export function RemoveSheet({ open, line, free, canCancelItem, onCancel, onConfirm }: {
  open: boolean
  line: SaleLine | null
  free: boolean
  canCancelItem: boolean
  onCancel: () => void
  onConfirm: () => Promise<void>
}) {
  const blocked = !free && !canCancelItem
  return (
    <FloorSheet role="alertdialog" open={open} onOpenChange={(o) => { if (!o) onCancel() }} title={blocked ? 'Remover este item pede autorização' : 'Remover o item da venda?'}
      actions={blocked
        ? <FloorButton className="w-full" onClick={onCancel}>Entendi</FloorButton>
        : <Actions><FloorButton tone="secondary" className="flex-1" onClick={onCancel}>Voltar</FloorButton><FloorButton tone="danger" className="flex-1" onClick={() => void onConfirm()}>Remover</FloorButton></Actions>}>
      {line ? (
        <div className="space-y-1 rounded-md border border-border bg-card p-4 tabular-nums">
          <p className="font-semibold">{line.name}</p>
          <p className="text-muted-foreground">{formatQuantity(line.quantity, line.fractional)} {line.unit.toLowerCase()} × {formatCents(line.unitCents)}</p>
        </div>
      ) : null}
      {blocked ? (
        <p>Só o último item lançado sai direto. Para tirar outro item, chame quem tem a permissão de remover itens no caixa. O PIN do supervisor na própria tela chega na próxima versão.</p>
      ) : free ? <p className="text-muted-foreground">É o último item lançado: sai sem supervisor. A remoção fica registrada.</p> : <p className="text-muted-foreground">A remoção fica registrada com o seu nome.</p>}
    </FloorSheet>
  )
}

const time = (iso: string) => new Date(iso).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })

/** CA 12: vendas em espera do caixa, com hora, itens e total. */
export function ParkedSheet({ open, sales, onClose, onResume, onDiscard }: {
  open: boolean
  sales: Sale[]
  onClose: () => void
  onResume: (saleId: string) => Promise<void>
  onDiscard: (saleId: string) => void
}) {
  return (
    <FloorSheet open={open} onOpenChange={(o) => { if (!o) onClose() }} title="Vendas em espera"
      description="A venda em espera fica guardada neste caixa até você retomar ou descartar."
      actions={<FloorButton tone="secondary" className="w-full" onClick={onClose}>Voltar</FloorButton>}>
      {sales.length === 0 ? <p className="text-muted-foreground">Nenhuma venda em espera.</p> : (
        <ul aria-label="Vendas em espera" className="space-y-3">
          {sales.map((s) => (
            <li key={s.id} className="flex flex-wrap items-center gap-3 rounded-md border border-border bg-card p-4">
              <div className="min-w-0 flex-1 tabular-nums">
                <p className="font-semibold">Desde {time(s.parkedAt ?? s.startedAt)} · {s.lines.length} {s.lines.length === 1 ? 'item' : 'itens'}</p>
                <p className="truncate text-muted-foreground">{s.lines.map((l) => l.name).join(', ')}</p>
              </div>
              <p className="text-[22px] font-bold tabular-nums">{formatCents(saleTotal(s))}</p>
              <div className="flex w-full gap-2 sm:w-auto">
                <FloorButton tone="secondary" className="flex-1" onClick={() => onDiscard(s.id)}>Descartar</FloorButton>
                <FloorButton className="flex-1" onClick={() => void onResume(s.id)}>Retomar</FloorButton>
              </div>
            </li>
          ))}
        </ul>
      )}
    </FloorSheet>
  )
}

const REASONS = ['Cliente desistiu', 'Cliente sem dinheiro suficiente', 'Venda lançada errada', 'Produto com defeito']

/** CA 13: descartar a venda pede o motivo (fica registrado). */
export function DiscardSheet({ open, sale, onCancel, onConfirm }: { open: boolean; sale: Sale | null; onCancel: () => void; onConfirm: (reason: string) => Promise<string | null> }) {
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const confirm = async () => {
    if (!reason.trim()) { setError('Informe o motivo do descarte.'); return }
    const problem = await onConfirm(reason)
    if (problem) setError(problem)
  }

  return (
    <FloorSheet role="alertdialog" open={open} onOpenChange={(o) => { if (!o) onCancel() }} title="Descartar a venda?"
      description={sale ? `${sale.lines.length} ${sale.lines.length === 1 ? 'item' : 'itens'} · ${formatCents(saleTotal(sale))}. O descarte fica registrado com o motivo e o seu nome.` : undefined}
      actions={<Actions><FloorButton tone="secondary" className="flex-1" onClick={onCancel}>Voltar</FloorButton><FloorButton tone="danger" className="flex-1" onClick={() => void confirm()}>Descartar venda</FloorButton></Actions>}>
      <div role="group" aria-label="Motivos comuns" className="flex flex-wrap gap-2">
        {REASONS.map((r) => <FloorButton key={r} tone="secondary" aria-pressed={reason === r} className={reason === r ? 'border-primary' : ''} onClick={() => { setReason(r); setError(null) }}>{r}</FloorButton>)}
      </div>
      <label className="block space-y-2">
        <span className="font-semibold">Motivo</span>
        <input value={reason} maxLength={140} onChange={(e) => { setReason(e.target.value); setError(null) }} className={field} aria-invalid={!!error} />
      </label>
      <Problem message={error} />
    </FloorSheet>
  )
}

export interface CustomerHit { id: string; name: string; document: string | null }

/** CA 14 e 15: CPF/CNPJ avulso (cifrado no aparelho) e, com rede, o cliente cadastrado. */
export function CustomerSheet({ open, online, current, searchCustomers, onCancel, onSave }: {
  open: boolean
  online: boolean
  current: { name: string | null; document: string | null }
  searchCustomers: ((term: string) => Promise<CustomerHit[]>) | null
  onCancel: () => void
  onSave: (input: { customerId?: string | null; customerName?: string | null; document?: string | null }) => Promise<string | null>
}) {
  const [document, setDocument] = useState(current.document ?? '')
  const [term, setTerm] = useState('')
  const [hits, setHits] = useState<CustomerHit[] | null>(null)
  const [searching, setSearching] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const checked = document.trim() ? checkCustomerDocument(document) : null
  const canSearch = online && !!searchCustomers

  const save = async (input: Parameters<typeof onSave>[0]) => {
    const problem = await onSave(input)
    if (problem) setError(problem)
  }

  const search = async () => {
    if (!searchCustomers || term.trim().length < 3) return
    setSearching(true)
    setError(null)
    try {
      setHits(await searchCustomers(term.trim()))
    } catch {
      setError('Não foi possível buscar agora. O CPF na nota continua funcionando.')
    } finally {
      setSearching(false)
    }
  }

  return (
    <FloorSheet open={open} onOpenChange={(o) => { if (!o) onCancel() }} title="Cliente da venda"
      description="Não é obrigatório. O CPF ou CNPJ serve para a nota e para achar a compra numa troca; fica guardado cifrado neste caixa."
      actions={(
        <Actions>
          <FloorButton tone="secondary" className="flex-1" onClick={onCancel}>Voltar</FloorButton>
          {current.document || current.name ? <FloorButton tone="secondary" className="flex-1" onClick={() => void save({ customerId: null, customerName: null, document: null })}>Tirar cliente</FloorButton> : null}
          <FloorButton className="flex-1" disabled={!!checked && !checked.ok} onClick={() => void save({ document: checked?.ok ? checked.value : null })}>Gravar</FloorButton>
        </Actions>
      )}>
      <label className="block space-y-2">
        <span className="font-semibold">CPF ou CNPJ na nota</span>
        <input autoFocus inputMode="numeric" autoComplete="off" value={document} maxLength={18} onChange={(e) => { setDocument(e.target.value); setError(null) }}
          onKeyDown={(e) => { if (e.key === 'Enter' && checked?.ok) { e.preventDefault(); void save({ document: checked.value }) } }}
          className={field} aria-invalid={!!checked && !checked.ok} aria-describedby="doc-check" />
      </label>
      <p id="doc-check" aria-live="polite" className={checked && !checked.ok ? 'font-semibold text-crit-ink' : 'text-muted-foreground'}>
        {checked ? (checked.ok ? `${checked.kind === 'cpf' ? 'CPF' : 'CNPJ'} válido: ${formatDocument(checked.value, checked.kind)}` : checked.message) : 'Só os números, com ou sem pontos.'}
      </p>
      <section className="space-y-2 border-t border-border pt-4">
        <h3 className="font-semibold">Cliente cadastrado</h3>
        {current.name ? <p className="text-muted-foreground">Ligado a esta venda: {current.name}</p> : null}
        {canSearch ? (
          <>
            <div className="flex gap-2">
              <input value={term} onChange={(e) => setTerm(e.target.value)} onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); void search() } }}
                placeholder="Nome, CPF ou CNPJ (3 letras ou mais)" aria-label="Buscar cliente cadastrado" className={field} />
              <FloorButton tone="secondary" aria-label="Buscar cliente" onClick={() => void search()} disabled={searching || term.trim().length < 3}><Search aria-hidden="true" className="size-6" /></FloorButton>
            </div>
            {hits ? hits.length === 0 ? <p className="text-muted-foreground">Nenhum cliente encontrado.</p> : (
              <ul aria-label="Clientes encontrados" className="space-y-2">
                {hits.map((h) => (
                  <li key={h.id}>
                    <FloorButton tone="secondary" className="w-full justify-between" onClick={() => void save({ customerId: h.id, customerName: h.name })}>
                      <span className="truncate">{h.name}</span>
                      {h.document ? <span className="text-[16px] text-muted-foreground tabular-nums">{h.document}</span> : null}
                    </FloorButton>
                  </li>
                ))}
              </ul>
            ) : null}
          </>
        ) : (
          <p className="flex items-center gap-2 text-muted-foreground"><CloudOff aria-hidden="true" className="size-5" />Disponível com internet.</p>
        )}
      </section>
      <Problem message={error} />
    </FloorSheet>
  )
}

/** Pagamento: da PDV-03. A venda fica guardada no caixa até lá. */
export function PaySheet({ open, onClose }: { open: boolean; onClose: () => void }) {
  return (
    <FloorSheet open={open} onOpenChange={(o) => { if (!o) onClose() }} title="Pagamento ainda não disponível"
      actions={<FloorButton className="w-full" onClick={onClose}>Voltar para a venda</FloorButton>}>
      <p>O recebimento (dinheiro, Pix, cartão e troco) chega na próxima versão do caixa. A venda continua guardada neste aparelho: você pode deixá-la em espera ou descartá-la.</p>
    </FloorSheet>
  )
}

/** Fechar o caixa (CA 12): com venda em espera ou na tela, pede para retomar ou descartar antes. */
export function CloseRegisterSheet({ open, pending, onCancel, onReview, onConfirm }: { open: boolean; pending: number; onCancel: () => void; onReview: () => void; onConfirm: () => Promise<void> }) {
  return (
    <FloorSheet role="alertdialog" open={open} onOpenChange={(o) => { if (!o) onCancel() }} title={pending ? 'Há vendas guardadas neste caixa' : 'Fechar o caixa?'}
      actions={pending
        ? <Actions><FloorButton tone="secondary" className="flex-1" onClick={onCancel}>Voltar</FloorButton><FloorButton className="flex-1" onClick={onReview}>Ver vendas em espera</FloorButton></Actions>
        : <Actions><FloorButton tone="secondary" className="flex-1" onClick={onCancel}>Voltar</FloorButton><FloorButton className="flex-1" onClick={() => void onConfirm()}>Fechar caixa</FloorButton></Actions>}>
      {pending
        ? <p>{pending === 1 ? '1 venda está' : `${pending} vendas estão`} na tela ou em espera. Retome e conclua, ou descarte, antes de fechar o caixa.</p>
        : <p className="text-muted-foreground">O caixa fica fechado neste aparelho até alguém abrir de novo. A conferência do dinheiro chega com o fechamento completo do caixa.</p>}
    </FloorSheet>
  )
}

/** Tile do produto no modo Toque (CA 7): o "retrato" é a sigla do nome enquanto o catálogo não traz foto. */
export function ProductTile({ item, priceCents, onPick }: { item: CatalogItem; priceCents: number | null; onPick: () => void }) {
  const initials = item.name.split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0]?.toUpperCase()).join('')
  return (
    <button type="button" data-floor-target="" onClick={onPick} disabled={priceCents === null}
      className="flex min-h-[120px] flex-col items-start gap-2 rounded-md border-2 border-input bg-card p-3 text-left disabled:opacity-60">
      <span aria-hidden="true" className="grid size-12 place-items-center rounded-md bg-muted font-display text-[20px] font-bold text-muted-foreground">{initials}</span>
      <span className="line-clamp-2 text-[17px] font-semibold leading-tight">{item.name}</span>
      <span className="mt-auto text-[18px] font-bold tabular-nums">{priceCents === null ? 'Sem preço' : `${formatCents(priceCents)}${item.fractional ? `/${item.unit.toLowerCase()}` : ''}`}</span>
    </button>
  )
}
