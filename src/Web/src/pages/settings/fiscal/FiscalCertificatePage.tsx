import { useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { getFiscalCertificate, getGetFiscalCertificateQueryKey, uploadFiscalCertificate, useGetFiscalCertificate } from '@/api/generated/bemaqui'
import type { FiscalCertificateView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { TwoFactorCodeField } from '@/components/form/two-factor-code'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime, formatInstantDate } from '@/lib/format'
import { useNavigation } from '@/navigation/navigation-context'
import { ModuleNotReleased } from '@/pages/ModulePage'
import { PageHeading } from '@/shell/PageHeading'
import { SettingsTabs } from '../SettingsTabs'
import { FiscalNav } from './FiscalNav'
import { certificateStatus, DICAS } from './fiscal-settings'

const primary = 'min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60'
const secondary = 'min-h-11 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold disabled:opacity-60'
const MAX_BYTES = 50 * 1024

/**
 * Configurações → Fiscal → Certificado digital (FIS-02a CA 11 a 13, D12): a situação (válido, vencendo, vencido), lida do BemAqui e
 * conferida no Emissor Fiscal; "Enviar certificado" manda o .pfx e a senha direto ao cofre do Emissor, com o código do autenticador. O
 * BemAqui guarda só titular, CNPJ, validade e thumbprint.
 */
export function FiscalCertificatePage() {
  const query = useGetFiscalCertificate(undefined, { query: { retry: false, gcTime: 0 } })
  const { all } = useNavigation()
  const fiscal = all.find((m) => m.code === 'fiscal')
  if (fiscal?.locked || (query.error instanceof ApiError && query.error.code === 'module_not_released')) {
    return fiscal ? <ModuleNotReleased module={fiscal} /> : null
  }

  return (
    <>
      <PageHeading title="Certificado digital">
        <p className="mt-2 flex flex-wrap items-center text-muted-foreground">
          O certificado A1 assina as notas. Ele vai direto ao cofre do Emissor Fiscal; o BemAqui guarda só a validade e o titular.
          <Dica nome="Certificado A1" texto={DICAS.a1} />
        </p>
      </PageHeading>
      <SettingsTabs />
      <FiscalNav />
      {query.isPending ? <Skeleton className="h-64 max-w-3xl" /> : query.isError || !query.data ? (
        <ErrorState className="max-w-3xl" message={query.error instanceof ApiError ? query.error.message : undefined} onRetry={() => void query.refetch()} />
      ) : (
        <Certificate view={query.data} />
      )}
    </>
  )
}

function Certificate({ view }: { view: FiscalCertificateView }) {
  const client = useQueryClient()
  const [open, setOpen] = useState(false)
  const [checking, setChecking] = useState(false)
  const status = certificateStatus(view.status, view.daysLeft)
  const store = (data: FiscalCertificateView) => client.setQueryData(getGetFiscalCertificateQueryKey(), data)
  const check = async () => {
    setChecking(true)
    try {
      store(await getFiscalCertificate({ refresh: true }))
    } finally {
      setChecking(false)
    }
  }

  if (!view.integrationReady) {
    return (
      <EmptyState className="max-w-3xl" title="Emissor Fiscal não conectado." description={view.integrationMessage ?? 'Conecte o Emissor Fiscal antes de enviar o certificado.'}
        action={<Link to="/app/configuracoes/integracoes/emissor-fiscal" className={primary}>Configurar integração</Link>} />
    )
  }

  return (
    <div className="max-w-3xl space-y-4">
      {view.changedOutsideAt ? (
        <p role="status" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm font-semibold text-warn-ink">
          O certificado no Emissor Fiscal mudou em <span className="tabular-nums">{formatDateTime(view.changedOutsideAt)}</span>.
        </p>
      ) : null}
      {view.emitterUnavailable ? <p role="status" className="text-sm text-muted-foreground">Não foi possível conferir no Emissor Fiscal agora. Mostrando os dados guardados no BemAqui.</p> : null}

      <section aria-labelledby="cert-situacao" className="space-y-3 rounded-md border border-border bg-card p-5">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 id="cert-situacao" className="font-semibold">Situação</h2>
          <StatusChip tone={status.tone}>{status.label}</StatusChip>
        </div>
        {view.status === 'None' ? (
          <EmptyState title="Nenhum certificado enviado." description="Sem ele, nenhuma nota é emitida."
            action={view.canEdit ? <button type="button" onClick={() => setOpen(true)} className={primary}>Enviar certificado</button> : undefined} />
        ) : (
          <dl className="grid grid-cols-1 gap-x-4 gap-y-1 text-sm sm:grid-cols-[auto_1fr]">
            <dt className="text-muted-foreground">Titular</dt><dd className="min-w-0 break-words">{view.subject ?? '—'}</dd>
            <dt className="text-muted-foreground">CNPJ</dt><dd className="tabular-nums">{view.document ?? '—'}</dd>
            <dt className="text-muted-foreground">Válido até</dt><dd className="tabular-nums">{view.notAfter ? formatInstantDate(view.notAfter) : '—'}</dd>
            <dt className="text-muted-foreground">Thumbprint</dt><dd className="min-w-0 break-all font-mono text-xs">{view.thumbprint ?? '—'}</dd>
            <dt className="text-muted-foreground">Enviado</dt>
            <dd className="tabular-nums">{view.uploadedAt ? `${formatDateTime(view.uploadedAt)}${view.uploadedByName ? ' por ' + view.uploadedByName : ''}` : 'Fora do BemAqui'}</dd>
          </dl>
        )}
        <div className="flex flex-wrap gap-2 pt-1">
          {view.canEdit && view.status !== 'None' ? <button type="button" onClick={() => setOpen(true)} className={view.status === 'Valid' ? secondary : primary}>Enviar certificado</button> : null}
          <button type="button" onClick={() => void check()} disabled={checking} className={secondary}>{checking ? 'Conferindo…' : 'Conferir no Emissor Fiscal'}</button>
        </div>
        {!view.canEdit ? <p className="text-xs text-muted-foreground">Só o Proprietário envia o certificado.</p> : null}
      </section>

      {view.history.length > 0 ? (
        <section aria-labelledby="cert-historico" className="space-y-2 rounded-md border border-border bg-card p-5 text-sm">
          <h2 id="cert-historico" className="font-semibold">Histórico de trocas</h2>
          <ol className="space-y-2">
            {view.history.map((h) => (
              <li key={h.at + h.thumbprint} className="rounded-md border border-border p-2">
                <span className="tabular-nums">{formatDateTime(h.at)}</span> · {h.source === 'Emissor' ? 'trocado no Emissor Fiscal' : 'enviado pelo BemAqui'}{h.byName ? ' por ' + h.byName : ''}
                {h.notAfter ? <span className="block text-xs text-muted-foreground tabular-nums">Válido até {formatInstantDate(h.notAfter)}</span> : null}
              </li>
            ))}
          </ol>
        </section>
      ) : null}

      <UploadDialog open={open} onOpenChange={setOpen} onDone={store} />
    </div>
  )
}

function UploadDialog({ open, onOpenChange, onDone }: { open: boolean; onOpenChange: (o: boolean) => void; onDone: (v: FiscalCertificateView) => void }) {
  const [file, setFile] = useState<File | null>(null)
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const [errors, setErrors] = useState<Record<string, string>>({})

  const send = async () => {
    setErrors({})
    if (!file) throw new DialogMessage('Escolha o arquivo .pfx do certificado A1.')
    if (file.size > MAX_BYTES) throw new DialogMessage('O arquivo passa de 50 KB. Confira se é o certificado A1 (.pfx).')
    if (!password) throw new DialogMessage('Informe a senha do certificado.')
    if (!/^\d{6}$/.test(code)) throw new DialogMessage('Digite os 6 números do aplicativo autenticador.')
    try {
      const saved = await uploadFiscalCertificate({ pfx: file, password, code })
      onDone(saved)
    } finally {
      setPassword('')
      setCode('')
    }
  }

  return (
    <ConfirmDialog
      open={open}
      onOpenChange={onOpenChange}
      onOpen={() => { setFile(null); setPassword(''); setCode(''); setErrors({}) }}
      verb="Enviar certificado"
      done="Certificado enviado"
      title="Enviar certificado digital"
      size="lg"
      description="O arquivo e a senha vão direto ao cofre do Emissor Fiscal e não ficam guardados no BemAqui. O CNPJ do certificado precisa ser o da empresa."
      onConfirm={send}
      onFieldErrors={(e: ApiError) => { setErrors(Object.fromEntries(Object.entries(e.fields).map(([k, v]) => [k, v[0] ?? '']))); return false }}
    >
      <div className="space-y-3">
        <Field label="Arquivo do certificado (.pfx)" required error={errors.pfx} hint="Certificado A1, até 50 KB.">
          {(p) => <input {...p} type="file" accept=".pfx,.p12,application/x-pkcs12" onChange={(e) => setFile(e.target.files?.[0] ?? null)} className={inputClass} />}
        </Field>
        <Field label="Senha do certificado" required error={errors.password}>
          {(p) => <input {...p} type="password" autoComplete="off" value={password} onChange={(e) => setPassword(e.target.value)} className={inputClass} />}
        </Field>
        <TwoFactorCodeField code={code} onChange={setCode} />
      </div>
    </ConfirmDialog>
  )
}
