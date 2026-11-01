import { useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { ArrowLeft, CheckCircle2 } from 'lucide-react'
import { useState } from 'react'
import { confirmRetentionRule, getListRetentionRulesQueryKey, saveRetentionRule, useListRetentionRules } from '@/api/generated/bemaqui'
import type { RetentionRuleKind, RetentionRuleRow } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatInstantDate } from '@/lib/format'
import { useNavigation } from '@/navigation/navigation-context'
import { PageHeading } from '@/shell/PageHeading'
import { SettingsTabs } from '../SettingsTabs'
import { ANONYMIZATION_DICAS } from './anonymization'
import { OwnerOnlyNotice } from './PrivacyRequestsPage'
import { RETENTION_SUBJECTS, ruleText } from './retention'

const button = 'inline-flex min-h-11 items-center gap-1.5 rounded-md border border-border px-3 py-2 text-sm font-semibold disabled:opacity-50'

/**
 * Configurações → Privacidade → Retenção (LGPD-01b CA 1, /app/configuracoes/privacidade/retencao): a tabela por tipo de titular e grupo de campos,
 * pré-configurada pela ADR 0018 e editável; cada linha com "A confirmar com a contadora e com o jurídico" até "Marcar como conferido". Só o
 * Proprietário. A 400 px, cada linha vira um cartão.
 */
export function RetentionPage() {
  const { isOwner } = useNavigation()
  const client = useQueryClient()
  const query = useListRetentionRules({ query: { enabled: isOwner, retry: false } })

  if (!isOwner || (query.error instanceof ApiError && query.error.code === 'forbidden')) {
    return (
      <>
        <PageHeading title="Retenção dos dados pessoais" />
        <SettingsTabs />
        <OwnerOnlyNotice />
      </>
    )
  }

  const reload = () => client.invalidateQueries({ queryKey: getListRetentionRulesQueryKey() })
  const back = <Link to="/app/configuracoes/privacidade" className="inline-flex min-h-11 items-center gap-1 text-sm font-semibold text-muted-foreground hover:text-foreground"><ArrowLeft aria-hidden="true" className="size-4" /> Pedidos do titular</Link>
  return (
    <>
      <PageHeading title="Retenção dos dados pessoais" />
      <SettingsTabs />
      {back}
      <p className="mt-2 max-w-3xl text-sm text-muted-foreground">
        Por quanto tempo cada dado pessoal fica guardado antes de poder ser anonimizado a pedido do titular. Os valores vêm pré-configurados;
        confira cada linha com a contadora e com o jurídico e marque como conferida.
      </p>
      {query.isPending ? <Skeleton className="mt-4 h-64" /> : query.isError ? (
        <ErrorState title="Não foi possível carregar a tabela de retenção." message={query.error instanceof ApiError ? query.error.message : undefined} onRetry={() => void query.refetch()} />
      ) : query.data.length === 0 ? (
        <EmptyState title="A tabela de retenção ainda não foi criada." description="Recarregue a tela: os valores sugeridos são criados na primeira abertura." action={<button type="button" onClick={() => void query.refetch()} className={button}>Recarregar</button>} />
      ) : (
        <div className="mt-4 space-y-5">
          {RETENTION_SUBJECTS.map((subject) => {
            const rows = query.data.filter((r) => r.subjectType === subject.value)
            return rows.length === 0 ? null : (
              <section key={subject.value} aria-labelledby={`retencao-${subject.value}`} className="rounded-md border border-border bg-card">
                <h2 id={`retencao-${subject.value}`} className="border-b border-border px-4 py-3 font-semibold">{subject.label}</h2>
                <ul className="divide-y divide-border">
                  {rows.map((r) => <RetentionRow key={r.id} row={r} onSaved={reload} />)}
                </ul>
              </section>
            )
          })}
        </div>
      )}
    </>
  )
}

function RetentionRow({ row, onSaved }: { row: RetentionRuleRow; onSaved: () => Promise<unknown> }) {
  const [rule, setRule] = useState<RetentionRuleKind>(row.rule)
  const [years, setYears] = useState(String(row.years))
  const hasBase = row.baseDate !== 'None'
  return (
    <li className="flex flex-col gap-2 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
      <div className="min-w-0">
        <p className="font-medium">{row.label}</p>
        <p className="flex items-center text-sm text-muted-foreground">
          {ruleText(row)}
          {row.rule !== 'Anytime' ? <Dica nome="Retido" texto={ANONYMIZATION_DICAS.retido} /> : null}
        </p>
        <div className="mt-1">
          {row.confirmed ? (
            <StatusChip tone="ok">Conferido{row.confirmedByName ? ` por ${row.confirmedByName}` : ''}{row.confirmedAt ? ` em ${formatInstantDate(row.confirmedAt)}` : ''}</StatusChip>
          ) : <StatusChip tone="warn">A confirmar com a contadora e com o jurídico</StatusChip>}
        </div>
      </div>
      <div className="flex flex-wrap gap-2">
        <ConfirmDialog verb="Salvar regra" done="Regra salva" title={`Regra de retenção: ${row.label}`}
          description="A mudança fica no histórico, com o valor de antes e o de depois. A linha volta para &quot;A confirmar&quot;."
          onOpen={() => { setRule(row.rule); setYears(String(row.years)) }}
          preconditions={[{ ok: rule === 'Anytime' || (/^\d{1,2}$/.test(years) && Number(years) <= 30), text: 'Informe de 0 a 30 anos.' }]}
          onConfirm={async () => { await saveRetentionRule(row.id, { rule, years: rule === 'Anytime' ? 0 : Number(years), version: row.version }); await onSaved() }}
          trigger={<button type="button" className={button}>Editar</button>}>
          <Field label="Regra" required>
            {(p) => (
              <select {...p} value={rule} onChange={(e) => setRule(e.target.value as RetentionRuleKind)} className={inputClass}>
                <option value="Anytime">A qualquer momento</option>
                {hasBase ? <option value="AfterYears">Depois de um prazo</option> : null}
              </select>
            )}
          </Field>
          {rule === 'AfterYears' ? (
            <Field label="Anos depois da data base" required hint={ruleText({ rule: 'AfterYears', years: Number(years) || 0, baseDate: row.baseDate })}>
              {(p) => <input {...p} inputMode="numeric" value={years} maxLength={2} onChange={(e) => setYears(e.target.value.replace(/\D/g, ''))} className={inputClass + ' tabular-nums'} />}
            </Field>
          ) : null}
        </ConfirmDialog>
        {!row.confirmed ? (
          <ConfirmDialog verb="Marcar como conferido" done="Conferido" title="Marcar a regra como conferida?"
            description="Confirme que a contadora e o jurídico conferiram este prazo. Fica registrado quem conferiu e quando."
            onConfirm={async () => { await confirmRetentionRule(row.id, { version: row.version }); await onSaved() }}
            trigger={<button type="button" className={button}><CheckCircle2 aria-hidden="true" className="size-4" /> Marcar como conferido</button>} />
        ) : null}
      </div>
    </li>
  )
}
