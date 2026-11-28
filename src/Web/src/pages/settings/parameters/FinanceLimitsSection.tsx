import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getGetFinanceLimitsQueryKey, updateFinanceLimits, useGetFinanceLimits } from '@/api/generated/bemaqui'
import type { FinanceLimits } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError, FormGrid, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { MoneyInput, PercentInput } from '@/components/form/inputs'
import { TwoFactorCodeField } from '@/components/form/two-factor-code'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { useNavigation } from '@/navigation/navigation-context'
import { useCan } from '@/pages/use-can'
import { labelOf, modeOf, needsCode, ONLY_OWNER_NOTE, SELF_APPROVAL_DICA_NEVER, SELF_APPROVAL_OPTIONS, SELF_APPROVAL_QUESTION, type SelfApprovalModeName } from './self-approval'

/**
 * Limites do Financeiro (FIN-01 CA 13, 14 e 18, D8; FIN-07 CA 15): desconto, cancelamento, data retroativa e o limite do ajustado; só
 * com finance.view. FIN-10a: "Quem solicitou a ordem pode aprová-la?" (três opções), editável só pelo Proprietário e com o código do
 * autenticador quando muda.
 */
export function FinanceLimitsSection({ canEdit }: { canEdit: boolean }) {
  const canSee = useCan('finance.view')
  const query = useGetFinanceLimits({ query: { enabled: canSee } })
  if (!canSee) return null
  if (query.isPending) return <Skeleton className="h-48" />
  if (query.isError || !query.data) return null
  return <LimitsForm key={JSON.stringify(query.data)} limits={query.data} canEdit={canEdit} />
}

function LimitsForm({ limits, canEdit }: { limits: FinanceLimits; canEdit: boolean }) {
  const client = useQueryClient()
  const [pct, setPct] = useState<string | null>(String(limits.discountApprovalPct))
  const [discount, setDiscount] = useState<string | null>(String(limits.discountApprovalAmount))
  const [cancel, setCancel] = useState<string | null>(String(limits.cancelApprovalAmount))
  const [days, setDays] = useState(String(limits.backdateDays))
  const { isOwner } = useNavigation()
  const seen = modeOf(limits.selfApprovalMode)
  const [mode, setMode] = useState<SelfApprovalModeName>(seen)
  const [code, setCode] = useState('')
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [notify, setNotify] = useState<string | null>(String(limits.transferNotifyAmount ?? 10000))
  const [tolerance, setTolerance] = useState<string | null>(String(limits.paymentTolerance ?? 0))
  const [threshold, setThreshold] = useState<string | null>(String(limits.adjustedDefaultThresholdPct ?? 20))
  const [ofxCap, setOfxCap] = useState<string | null>(String(limits.ofxBankEntryCap ?? 500))
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const modeChanged = needsCode(seen, mode)
  // Sem trocar o modo, o PUT não leva o modo nem o código (FIN-10a CA 14): quem não é Proprietário salva os demais limites.
  const send = async (withCode?: string) => {
    const saved = await updateFinanceLimits({
      discountApprovalPct: Number(pct ?? 0), discountApprovalAmount: Number(discount ?? 0), cancelApprovalAmount: Number(cancel ?? 0), backdateDays: Number(days || 0),
      paymentTolerance: Number(tolerance ?? 0), transferNotifyAmount: Number(notify ?? 0), adjustedDefaultThresholdPct: Number(threshold ?? 0), ofxBankEntryCap: Number(ofxCap ?? 0),
      ...(modeChanged ? { selfApprovalMode: mode, selfApprovalModeSeen: seen, code: withCode } : {}),
    })
    client.setQueryData(getGetFinanceLimitsQueryKey(), saved)
  }
  const save = async () => {
    if (modeChanged) {
      setCode('')
      setConfirmOpen(true)
      return
    }

    try {
      await send()
      notifySuccess('Limites do financeiro salvos')
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar os limites.', fields: {} })
    }
  }

  const field = (k: string) => error?.fields[k]?.[0]
  return (
    <FormSection title="Limites do financeiro" description="Acima destes valores, só quem aprova em Caixa e pagamentos confirma. Datas muito antigas pedem motivo e avisam o Proprietário." grid="none">
      <fieldset disabled={!canEdit} className="space-y-3">
        <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
        {/* Grade de 12 trilhas (ADR 0044): valores com 4 trilhas; os dois com texto de ajuda longo, com 6. */}
        <FormGrid>
          <Field label="Desconto: % do saldo" size="md" error={field('discountApprovalPct')}>{(p) => <PercentInput {...p} value={pct} onChange={setPct} />}</Field>
          <Field label="Desconto: valor" size="md" error={field('discountApprovalAmount')}>{(p) => <MoneyInput {...p} value={discount} onChange={setDiscount} />}</Field>
          <Field label="Cancelamento: saldo acima de" size="md" error={field('cancelApprovalAmount')}>{(p) => <MoneyInput {...p} value={cancel} onChange={setCancel} />}</Field>
          <Field label="Data retroativa: dias" size="md" error={field('backdateDays')} hint="Baixa com data mais antiga que isso pede motivo.">
            {(p) => <input {...p} inputMode="numeric" maxLength={3} value={days} onChange={(e) => setDays(e.target.value.replace(/\D/g, ''))} className={inputClass + ' tabular-nums'} />}
          </Field>
          <Field label="Tolerância no pagamento" size="md" error={field('paymentTolerance')} hint="Quanto o pagamento pode passar do aprovado sem nova aprovação.">{(p) => <MoneyInput {...p} value={tolerance} onChange={setTolerance} />}</Field>
          <Field label="Aviso de transferência acima de" size="md" error={field('transferNotifyAmount')} hint="Transferência maior que isso avisa o Proprietário.">{(p) => <MoneyInput {...p} value={notify} onChange={setNotify} />}</Field>
          <Field label="Limite de inadimplência para ponderar o ajustado" size="lg" error={field('adjustedDefaultThresholdPct')} hint="No fluxo de caixa ajustado, cliente com inadimplência acima disso entra só com a parte que provavelmente paga (padrão 20,00%).">
            {(p) => <PercentInput {...p} value={threshold} onChange={setThreshold} />}
          </Field>
          <Field label="Lançamento sem OP a partir de OFX: acima de" size="lg" error={field('ofxBankEntryCap')} hint="Na conciliação, lançar tarifa, IOF, juros ou rendimento a partir de uma linha de arquivo OFX acima disso só com quem aprova em Caixa e pagamentos (padrão R$ 500,00).">
            {(p) => <MoneyInput {...p} value={ofxCap} onChange={setOfxCap} />}
          </Field>
        </FormGrid>
      </fieldset>
      {/* Fora do fieldset de canEdit: só o Proprietário muda a regra (FIN-10a CA 13). */}
      <fieldset disabled={!isOwner} className="mt-4 space-y-2">
        <legend className="text-sm font-semibold">{SELF_APPROVAL_QUESTION}</legend>
        {SELF_APPROVAL_OPTIONS.map((o) => (
          <label key={o.value} className="flex items-start gap-2 text-sm">
            <input type="radio" name="self-approval-mode" value={o.value} checked={mode === o.value} onChange={() => setMode(o.value)} className="mt-0.5 size-4 accent-primary" />
            <span>
              <b>{o.label}</b>{o.value === 'Never' ? <Dica nome="Nunca" texto={SELF_APPROVAL_DICA_NEVER} /> : null}
              <span className="block text-xs text-muted-foreground">{o.description}</span>
            </span>
          </label>
        ))}
        <p className="text-xs text-muted-foreground">{isOwner ? 'Trocar pede o código do autenticador e avisa os outros Proprietários.' : ONLY_OWNER_NOTE}</p>
      </fieldset>
      {canEdit ? <button type="button" onClick={() => void save()} className="mt-3 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar limites</button> : null}
      <ConfirmDialog size="lg" verb="Confirmar" done="Regra de aprovação salva" title="Confirme com o código do autenticador" open={confirmOpen} onOpenChange={setConfirmOpen}
        description={`${SELF_APPROVAL_QUESTION} De "${labelOf(seen)}" para "${labelOf(mode)}". Os outros Proprietários serão avisados.`}
        preconditions={[{ ok: code.length === 6, text: 'Código do autenticador com 6 dígitos.' }]}
        recovery={{ verb: 'Recarregar', when: (c) => c === 'parameters_changed', onSelect: () => client.invalidateQueries({ queryKey: getGetFinanceLimitsQueryKey() }) }}
        onConfirm={() => send(code)}>
        <TwoFactorCodeField code={code} onChange={setCode} />
      </ConfirmDialog>
    </FormSection>
  )
}
