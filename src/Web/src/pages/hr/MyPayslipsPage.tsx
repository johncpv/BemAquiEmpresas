import { useQueryClient } from '@tanstack/react-query'
import { FileDown, Lock } from 'lucide-react'
import { useState } from 'react'
import { getDownloadMyPayslipPdfUrl, getListMyPayslipsQueryKey, useListMyPayslips } from '@/api/generated/bemaqui'
import type { MyPayslip } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { formatInstantDate } from '@/lib/format'
import { useNavigation } from '@/navigation/navigation-context'
import { PageHeading } from '@/shell/PageHeading'
import { ModuleNotReleased } from '../ModulePage'
import { periodLabel, typeLabel } from './payroll/payroll'
import { PAYSLIP_DICAS } from './payroll/payslip-notices'

export const NOT_LINKED_MESSAGE = 'Seu usuário não está ligado a um funcionário ativo. Fale com o RH.'

const capitalize = (s: string) => s.charAt(0).toUpperCase() + s.slice(1)

/** Cartão do holerite (CA 5): competência, tipo, líquido e "Baixar PDF" com 56 px (padrão 8). */
function PayslipCard({ payslip, onDownloaded }: { payslip: MyPayslip; onDownloaded: () => void }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const download = async () => {
    setBusy(true)
    setError(null)
    try {
      await apiDownload(getDownloadMyPayslipPdfUrl(payslip.id), `holerite-${payslip.period.slice(0, 7)}.pdf`)
      onDownloaded()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Não foi possível baixar o holerite. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <li className="flex flex-col gap-3 rounded-md border border-border bg-card p-4 sm:flex-row sm:items-center sm:justify-between">
      <div>
        <p className="font-display text-lg font-bold">{capitalize(periodLabel(payslip.period))}</p>
        <p className="text-sm text-muted-foreground">{typeLabel(payslip.type)}{payslip.firstViewedAt ? ` · Visto em ${formatInstantDate(payslip.firstViewedAt)}` : ''}</p>
        <p className="mt-1 text-sm">Líquido <Money value={payslip.net} className="text-base font-bold tabular-nums" /></p>
        {error ? <p role="alert" className="mt-1 text-sm text-crit-ink">{error}</p> : null}
      </div>
      <button type="button" onClick={() => void download()} disabled={busy}
        className="inline-flex min-h-[56px] items-center justify-center gap-2 rounded-md bg-primary px-4 text-base font-semibold text-primary-foreground">
        <FileDown aria-hidden="true" className="size-5" />
        {busy ? 'Baixando…' : 'Baixar PDF'}
      </button>
    </li>
  )
}

/**
 * "Meus holerites" (RH-02b CA 5 e 6, ADR 0035): o canal do holerite. Só os do funcionário da sessão, das folhas fechadas, do mais recente
 * ao mais antigo; sem vínculo com funcionário ativo, o aviso para falar com o RH. Desenhado para o celular.
 */
export function MyPayslipsPage() {
  const { all, status } = useNavigation()
  const hr = all.find((m) => m.code === 'hr')
  const client = useQueryClient()
  const released = status === 'ready' && !!hr && !hr.locked
  const query = useListMyPayslips({ query: { enabled: released, retry: (count, error) => !(error instanceof ApiError && error.status === 403) && count < 2 } })

  if (status === 'loading') return null
  if (hr?.locked) return <ModuleNotReleased module={hr} />
  const heading = (
    <PageHeading title="Meus holerites">
      <p className="mt-1 inline-flex items-center text-muted-foreground">
        <Lock aria-hidden="true" className="mr-1 size-4" />
        Só você vê, com o seu login.
        <Dica nome="Meus holerites" texto={PAYSLIP_DICAS.canal} />
      </p>
    </PageHeading>
  )

  if (query.isPending) return <>{heading}<div className="space-y-3"><Skeleton className="h-28" /><Skeleton className="h-28" /></div></>
  if (query.isError) {
    const notLinked = query.error instanceof ApiError && query.error.code === 'employee_not_active'
    return (
      <>
        {heading}
        {notLinked
          ? <EmptyState title="Holerite indisponível" description={NOT_LINKED_MESSAGE} />
          : <ErrorState title="Não foi possível carregar os seus holerites." onRetry={() => query.refetch()} />}
      </>
    )
  }

  const items = query.data
  return (
    <>
      {heading}
      {items.length === 0 ? (
        <EmptyState title="Nenhum holerite disponível ainda." description="Ele aparece aqui quando a folha do mês for fechada." />
      ) : (
        <ul aria-label="Meus holerites" className="max-w-2xl space-y-3">
          {items.map((p) => <PayslipCard key={p.id} payslip={p} onDownloaded={() => void client.invalidateQueries({ queryKey: getListMyPayslipsQueryKey() })} />)}
        </ul>
      )}
    </>
  )
}
