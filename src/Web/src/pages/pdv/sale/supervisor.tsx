import { ShieldCheck } from 'lucide-react'
import { useEffect, useState } from 'react'
import { ApiError } from '@/api/http'
import { FloorButton, FloorSheet, NumericKeypad } from '@/components/shopfloor/kit'
import { isPinComplete, PIN_LENGTH } from '@/pages/shopfloor/device'
import { centsToDecimal, type Cents } from '@/pdv/money'
import { cn } from '@/lib/utils'
import { OfflineAccessError, PRICE_NEEDS_NETWORK } from '@/pdv/offline/offline-access'
import { REASONS } from './discount-input'

/** Ação do caixa que pede supervisor (PDV-04); o código da ação no catálogo é `pos.{código}`. */
export type SupervisorAction = 'Discount' | 'ChangePrice' | 'CancelItem' | 'CancelSale' | 'Return'

const ACTION_CODE: Record<SupervisorAction, string> = { Discount: 'discount', ChangePrice: 'change_price', CancelItem: 'cancel_item', CancelSale: 'cancel_sale', Return: 'return' }

export interface SupervisorOption {
  userId: string
  displayName: string
  actions: readonly string[]
}

export interface SupervisorApprovalRequest {
  action: SupervisorAction
  supervisorId: string
  pin: string
  reason: string
  saleId: string
  lineId: string | null
  beforeAmount: number
  afterAmount: number
}

export interface SupervisorApproval {
  approvalId: string
  supervisorId: string
  supervisorName: string
}

/**
 * O que o caixa precisa para chamar o supervisor: com rede, o servidor confere o PIN (PDV-04); sem rede (`offline`, OFF-03), o aparelho confere o
 * código sem internet do supervisor contra o verificador cifrado e os limites da loja, e a aprovação sobe na fila.
 */
export interface SupervisorApi {
  list: () => Promise<readonly SupervisorOption[]>
  approve: (input: SupervisorApprovalRequest) => Promise<SupervisorApproval>
  offline?: boolean
}

/** O pedido ao supervisor: a ação, o resumo (item, de quanto para quanto), a venda, a linha, os valores e o motivo já escolhido. */
export interface SupervisorRequest {
  action: SupervisorAction
  title: string
  summary: string
  saleId: string
  lineId: string | null
  beforeCents: Cents
  afterCents: Cents
  reason: string
}

const OTHER = 'Outro'
const field = 'block min-h-[56px] w-full rounded-md border-2 border-input bg-card px-4 text-[20px] text-foreground'

/** O motivo da lista ou "Outro" com o texto; devolve o texto final (vazio enquanto não escolheu). */
export function ReasonPicker({ action, value, onChange }: { action: SupervisorAction; value: string; onChange: (reason: string) => void }) {
  const options = REASONS[action]
  const [other, setOther] = useState(value !== '' && !options.includes(value))
  const text = other ? value.replace(/^Outro: /, '') : ''
  return (
    <fieldset className="space-y-2">
      <legend className="font-semibold">Motivo</legend>
      <div className="flex flex-wrap gap-[8px]">
        {[...options, OTHER].map((o) => {
          const active = o === OTHER ? other : !other && value === o
          return (
            <FloorButton key={o} tone={active ? 'primary' : 'secondary'} aria-pressed={active} className="text-[17px]"
              onClick={() => { if (o === OTHER) { setOther(true); onChange('') } else { setOther(false); onChange(o) } }}>{o}</FloorButton>
          )
        })}
      </div>
      {other ? (
        <input aria-label="Qual o motivo" autoFocus maxLength={120} className={field} value={text} placeholder="Escreva o motivo"
          onChange={(e) => onChange(e.target.value.trim() ? `${OTHER}: ${e.target.value}` : '')} />
      ) : null}
    </fieldset>
  )
}

/**
 * "Chamar supervisor" (PDV-04 CA 10 a 12): o resumo da ação, os supervisores da loja que podem aprovar e o PIN dele no mesmo aparelho. O servidor
 * confere a permissão, que não é o operador, e grava a trilha; a aprovação libera só aquela ação e a sessão do operador continua a mesma.
 */
export function SupervisorDialog({ request, api, online, onApproved, onCancel }: {
  request: SupervisorRequest | null
  api: SupervisorApi | null
  online: boolean
  onApproved: (approval: SupervisorApproval) => void | Promise<void>
  onCancel: () => void
}) {
  const [supervisors, setSupervisors] = useState<readonly SupervisorOption[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [chosen, setChosen] = useState<SupervisorOption | null>(null)
  const [pin, setPin] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [attempt, setAttempt] = useState(0)
  const open = request !== null

  useEffect(() => {
    if (!open || !api || (!online && !api.offline)) return
    let alive = true
    api.list().then((list) => { if (alive) setSupervisors(list) })
      .catch((e: unknown) => { if (alive) setLoadError(e instanceof ApiError ? e.message : 'Não foi possível buscar os supervisores.') })
    return () => { alive = false; setSupervisors(null); setLoadError(null); setChosen(null); setPin(''); setError(null) }
  }, [open, api, online, attempt])

  if (!request) return null
  const code = ACTION_CODE[request.action]
  const offline = !!api?.offline
  const able = (supervisors ?? []).filter((s) => s.actions.includes(code))

  const approve = async () => {
    if (!api || !chosen) return
    setBusy(true)
    setError(null)
    try {
      const approval = await api.approve({
        action: request.action, supervisorId: chosen.userId, pin, reason: request.reason, saleId: request.saleId, lineId: request.lineId,
        beforeAmount: Number(centsToDecimal(request.beforeCents)), afterAmount: Number(centsToDecimal(request.afterCents)),
      })
      await onApproved(approval)
    } catch (e) {
      setPin('')
      setError(e instanceof ApiError || e instanceof OfflineAccessError ? e.message : 'Não foi possível conferir o PIN. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <FloorSheet role="alertdialog" open={open} onOpenChange={(o) => { if (!o) onCancel() }} title="Chamar supervisor" description={request.title}
      actions={(
        <div className="flex gap-[16px]">
          <FloorButton tone="secondary" className="flex-1" onClick={chosen ? () => { setChosen(null); setPin(''); setError(null) } : onCancel} disabled={busy}>
            {chosen ? 'Trocar supervisor' : 'Voltar'}
          </FloorButton>
          {chosen ? <FloorButton className="flex-1" onClick={() => void approve()} disabled={busy || !isPinComplete(pin)}>{busy ? 'Conferindo…' : 'Aprovar'}</FloorButton> : null}
        </div>
      )}>
      <div className="space-y-1 rounded-md border border-border bg-card p-4">
        <p className="font-semibold">{request.summary}</p>
        <p className="text-muted-foreground">Motivo: {request.reason}</p>
      </div>
      {offline && request.action === 'ChangePrice' ? (
        <p role="alert" className="rounded-md border border-warn-soft bg-warn-soft p-4 text-[18px] font-semibold text-warn-ink">{PRICE_NEEDS_NETWORK}</p>
      ) : !online && !offline ? (
        <p role="alert" className="rounded-md border border-warn-soft bg-warn-soft p-4 text-[18px] font-semibold text-warn-ink">
          Sem conexão: a aprovação do supervisor precisa de rede. Tente de novo quando a conexão voltar.
        </p>
      ) : loadError ? (
        <div className="space-y-2">
          <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft p-4 text-[18px] font-semibold text-crit-ink">{loadError}</p>
          <FloorButton tone="secondary" onClick={() => setAttempt((a) => a + 1)}>Tentar de novo</FloorButton>
        </div>
      ) : supervisors === null ? <p aria-busy="true">Buscando os supervisores da loja…</p> : !chosen ? (
        able.length === 0 ? (
          <p className="text-[18px] text-muted-foreground">
            {offline
              ? 'Nenhum supervisor com o código sem internet e a permissão para esta ação chegou a este caixa. Conecte à internet para aprovar.'
              : 'Nenhum supervisor desta loja tem PIN do caixa e a permissão para esta ação. Peça ao gerente para criar o PIN em Meu perfil → Segurança.'}
          </p>
        ) : (
          <div role="group" aria-label="Supervisores" className="grid grid-cols-2 gap-[8px]">
            {able.map((s) => (
              <FloorButton key={s.userId} tone="secondary" className="justify-start text-[18px]" onClick={() => { setChosen(s); setPin(''); setError(null) }}>
                <ShieldCheck aria-hidden="true" className="size-6" />{s.displayName}
              </FloorButton>
            ))}
          </div>
        )
      ) : (
        <div className="space-y-3">
          <p className="text-[18px]"><b>{chosen.displayName}</b>, digite o seu {offline ? 'código sem internet' : 'PIN'} de {PIN_LENGTH} números.</p>
          <NumericKeypad label={offline ? 'Código sem internet do supervisor' : 'PIN do supervisor'} mask max={PIN_LENGTH} value={pin} onChange={(v) => { setPin(v); setError(null) }} />
        </div>
      )}
      {error ? <p role="alert" className={cn('rounded-md border border-crit-soft bg-crit-soft p-4 text-[18px] font-semibold text-crit-ink')}>{error}</p> : null}
    </FloorSheet>
  )
}
