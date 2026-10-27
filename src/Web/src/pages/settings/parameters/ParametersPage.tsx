import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useSearch } from '@tanstack/react-router'
import { getGetTenantParametersQueryKey, saveEmailDispatchLimit, savePayrollCharges, savePricingMode, useGetPositionsSummary, useGetTenantParameters } from '@/api/generated/bemaqui'
import { PricingMode, type TenantParametersDto } from '@/api/generated/model'
import { RecordHistory } from '@/components/history/RecordHistory'
import { ToggleGroup } from '@/components/form/choice'
import { Field, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { PercentInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { PageHeading } from '@/shell/PageHeading'
import { useCan } from '@/pages/use-can'
import { SettingsTabs } from '../SettingsTabs'
import { CommercialPricingSection } from './CommercialPricingSection'
import { DreParametersSection } from './DreParametersSection'
import { FinanceLimitsSection } from './FinanceLimitsSection'
import { HrParametersSection } from './HrParametersSection'
import { ProductionParametersSection } from './ProductionParametersSection'
import { PurchasingParametersSection } from './PurchasingParametersSection'
import { RevenueTargetsSection } from './RevenueTargetsSection'
import { useCanSettings } from '../use-can-settings'

const CHARGES_DICA = 'Custo-hora do cargo = salário × (1 + % de encargos) ÷ horas do mês. Valor sugerido. Confirme com a contadora.'
const MODES = [
  { value: PricingMode.Markup, label: 'Markup sobre o custo' },
  { value: PricingMode.Margin, label: 'Margem sobre o preço' },
]

/** Configurações → Parâmetros (CAD-06 CA 15 a 18, FIN-01, PRD-04): % de encargos, precificação, limites do financeiro e a produção. */
export function ParametersPage() {
  const query = useGetTenantParameters()
  const canEdit = useCanSettings('edit')
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const loaded = !!query.data
  // ?secao=rh (RH-02a CA 3): abre já na seção do módulo dono.
  useEffect(() => {
    if (!loaded || typeof search.secao !== 'string') return
    const t = setTimeout(() => document.getElementById('secao-' + String(search.secao))?.scrollIntoView({ block: 'start' }), 300)
    return () => clearTimeout(t)
  }, [loaded, search.secao])
  return (
    <>
      <PageHeading title="Configurações: Parâmetros" />
      <SettingsTabs />
      {query.isPending ? <Skeleton className="h-96 max-w-3xl" /> : query.isError || !query.data ? <ErrorState onRetry={() => query.refetch()} /> : (
        <div className="max-w-3xl space-y-6">
          <ChargesSection key={`c${query.data.payrollChargesPct}`} params={query.data} canEdit={canEdit} />
          <PricingSection key={`p${query.data.pricingMode}`} params={query.data} canEdit={canEdit} />
          <CommercialPricingSection canEdit={canEdit} />
          <FinanceLimitsSection canEdit={canEdit} />
          <DreParametersSection canEdit={canEdit} />
          <RevenueTargetsSection canEdit={canEdit} />
          <ProductionParametersSection canEdit={canEdit} />
          <PurchasingParametersSection canEdit={canEdit} />
          <HrParametersSection canEdit={canEdit} />
          <EmailDispatchSection key={`e${query.data.emailDispatchMaxPerUserDay}`} params={query.data} canEdit={canEdit} />
        </div>
      )}
    </>
  )
}

function useSaved() {
  const client = useQueryClient()
  return (saved: TenantParametersDto) => {
    client.setQueryData(getGetTenantParametersQueryKey(), saved)
    void client.invalidateQueries({ predicate: (q) => String(q.queryKey[0]).startsWith('/api/v1/audit/TenantSetting/') })
  }
}

function ChargesSection({ params, canEdit }: { params: TenantParametersDto; canEdit: boolean }) {
  const [value, setValue] = useState<string | null>(String(params.payrollChargesPct))
  const saved = useSaved()
  const pct = value === null ? NaN : Number(value)
  const error = value === null ? 'Informe o percentual.' : pct < 0 || pct > 200 ? 'Informe um percentual entre 0 e 200, com até 2 casas.' : undefined
  const dirty = pct !== params.payrollChargesPct
  // Com hr.view, o diálogo diz quantos cargos mudam (CAD-04a CA 7); sem, o texto genérico.
  const canHr = useCan('hr.view')
  const positions = useGetPositionsSummary({ query: { enabled: canHr } })
  const impact = canHr && typeof positions.data?.calculated === 'number' ? (positions.data.calculated === 1 ? '1 cargo será recalculado.' : positions.data.calculated + ' cargos serão recalculados.') : 'Os cargos com custo-hora calculado serão recalculados.'

  return (
    <FormSection title="Encargos" description="Usado no custo-hora dos cargos (RH) e no custo das ordens de serviço.">
      <Field label="% de encargos estimados" required error={error} dica={CHARGES_DICA}>
        {(p) => <PercentInput {...p} value={value} onChange={setValue} disabled={!canEdit} />}
      </Field>
      {canEdit ? (
        <div className="flex justify-end">
          <ConfirmDialog verb="Salvar % de encargos" done="% de encargos salvo" title="Salvar % de encargos?"
            description={impact}
            onConfirm={async () => saved(await savePayrollCharges({ pct }))}
            trigger={<button type="button" disabled={!dirty || !!error} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Salvar % de encargos</button>} />
        </div>
      ) : null}
      {params.chargesSettingId ? <History id={params.chargesSettingId} /> : null}
    </FormSection>
  )
}

function PricingSection({ params, canEdit }: { params: TenantParametersDto; canEdit: boolean }) {
  const [mode, setMode] = useState<PricingMode>(params.pricingMode)
  const saved = useSaved()
  return (
    <FormSection title="Precificação" description="Como o orçamento sugere o preço a partir do custo. O % padrão entra com o módulo Comercial.">
      <div className="space-y-1">
        <p className="text-sm font-semibold">Modo de precificação</p>
        <ToggleGroup label="Modo de precificação" options={MODES} value={mode} onChange={setMode} disabled={!canEdit} />
      </div>
      {canEdit ? (
        <div className="flex justify-end">
          <ConfirmDialog verb="Salvar modo de precificação" done="Modo de precificação salvo" title="Salvar modo de precificação?"
            description="Os orçamentos novos passam a usar este modo. Os já emitidos não mudam."
            onConfirm={async () => saved(await savePricingMode({ mode }))}
            trigger={<button type="button" disabled={mode === params.pricingMode} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Salvar modo de precificação</button>} />
        </div>
      ) : null}
      {params.pricingSettingId ? <History id={params.pricingSettingId} /> : null}
    </FormSection>
  )
}

const DISPATCH_DICA = 'Quantos orçamentos e pedidos de compra cada usuário pode mandar por e-mail por dia, somados. A empresa tem também o teto de 50 por dia. Passou do limite, o usuário baixa o PDF e manda por fora.'

/** Envios por e-mail (CMP-03b CA 5, email.dispatch_max_per_user_day): por usuário por dia, de 1 a 50 (padrão 20). */
function EmailDispatchSection({ params, canEdit }: { params: TenantParametersDto; canEdit: boolean }) {
  const current = params.emailDispatchMaxPerUserDay ?? 20
  const [value, setValue] = useState(String(current))
  const saved = useSaved()
  const max = Number(value)
  const error = value.trim() === '' || !Number.isInteger(max) || max < 1 || max > 50 ? 'Informe de 1 a 50 envios por dia.' : undefined
  return (
    <FormSection title="Envios por e-mail" description="Limite diário de envios de orçamento e de pedido de compra por e-mail. O total da empresa é 50 por dia.">
      <Field label="Envios por usuário por dia" required error={error} dica={DISPATCH_DICA}>
        {(p) => <input {...p} inputMode="numeric" value={value} onChange={(e) => setValue(e.target.value.replace(/[^0-9]/g, ''))} disabled={!canEdit} className={inputClass + ' max-w-32 tabular-nums'} />}
      </Field>
      {canEdit ? (
        <div className="flex justify-end">
          <ConfirmDialog verb="Salvar limite de envios" done="Limite de envios salvo" title="Salvar limite de envios?"
            description={`Cada usuário poderá mandar até ${max} orçamentos e pedidos por e-mail por dia, somados. Vale a partir de agora.`}
            onConfirm={async () => saved(await saveEmailDispatchLimit({ maxPerUserDay: max }))}
            trigger={<button type="button" disabled={max === current || !!error} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Salvar limite de envios</button>} />
        </div>
      ) : null}
      {params.emailDispatchSettingId ? <History id={params.emailDispatchSettingId} /> : null}
    </FormSection>
  )
}

function History({ id }: { id: string }) {
  return (
    <details className="rounded-md border border-border bg-card px-3 py-2 text-sm">
      <summary className="cursor-pointer font-semibold">Histórico de alterações</summary>
      <div className="mt-3"><RecordHistory entity="TenantSetting" id={id} /></div>
    </details>
  )
}
