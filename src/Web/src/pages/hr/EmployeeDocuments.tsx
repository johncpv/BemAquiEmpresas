import { useRef, useState } from 'react'
import { getDownloadEmployeeDocumentFileUrl, saveEmployeeDocuments, uploadAttachment, useGetEmployeeDocumentsSensitive, useListEmployeeDocumentFiles } from '@/api/generated/bemaqui'
import type { EmployeeDetail, EmployeeDocumentsDetail } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { Field, FormActions, FormError, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput } from '@/components/form/inputs'
import { FormLayout } from '@/components/layout/form-layout'
import { notifySuccess } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDateTime } from '@/lib/format'
import { onlyDigits } from '@/lib/masks'
import { useCan } from '@/pages/use-can'
import { todayIso } from './employee'
import { EmployeeProtected } from './EmployeeProtected'

const OWNER = 'EmployeeDocument'
const CNH_CATEGORIES = ['A', 'B', 'C', 'D', 'E', 'AB', 'AC', 'AD', 'AE', 'ACC']
const primary = 'min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'

/** Aba Documentos (CAD-04c2 CA 11 e 12): CNH e reservista; digitalizados no dono EmployeeDocument (pdf, png e jpg até 10 MB). */
export function EmployeeDocuments({ employee, canEdit }: { employee: EmployeeDetail; canEdit: boolean }) {
  const canSee = useCan('hr.view_sensitive')
  const docs = useGetEmployeeDocumentsSensitive(employee.id, { query: { enabled: canSee, gcTime: 0, staleTime: 0 } })
  if (!canSee) return <EmployeeProtected />
  if (docs.isPending) return <Skeleton className="h-64 max-w-4xl" />
  if (docs.isError || !docs.data) return <ErrorState title="Não foi possível carregar os documentos." onRetry={() => docs.refetch()} />
  return (
    <FormLayout>
      <div className="space-y-5">
      <ExtraDocuments key={docs.data.version} employee={employee} data={docs.data} canEdit={canEdit} onReload={() => docs.refetch()} />
      <DocumentFiles employee={employee} canEdit={canEdit} />
      </div>
    </FormLayout>
  )
}

function ExtraDocuments({ employee, data, canEdit, onReload }: { employee: EmployeeDetail; data: EmployeeDocumentsDetail; canEdit: boolean; onReload: () => Promise<unknown> }) {
  const [cnh, setCnh] = useState(data.cnhNumber ?? '')
  const [category, setCategory] = useState(data.cnhCategory ?? '')
  const [expiry, setExpiry] = useState(data.cnhExpiry ?? '')
  const [military, setMilitary] = useState(data.militaryCertificate ?? '')
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]>; code?: string }>()
  const [version, setVersion] = useState(data.version)
  const field = (k: string) => error?.fields[k]?.[0]
  const expired = !!expiry && expiry < todayIso()
  const save = async () => {
    setError(undefined)
    try {
      const saved = await saveEmployeeDocuments(employee.id, { cnhNumber: onlyDigits(cnh) || null, cnhCategory: category || null, cnhExpiry: expiry || null, militaryCertificate: military.trim() || null, version })
      setVersion(saved.version)
      notifySuccess('Documentos salvos')
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields, code: e.code } : { message: 'Não foi possível salvar os documentos.', fields: {} })
    }
  }

  return (
    // Formulário curto (ADR 0044): uma seção na grade de 12 trilhas (antes, grade dentro da grade) e os botões no fim.
    <form noValidate onSubmit={(e) => { e.preventDefault(); void save() }} className="space-y-3">
      <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
      {error?.code === 'concurrency_conflict' ? <button type="button" onClick={() => void onReload()} className="min-h-11 rounded-md border border-border px-3 py-2 text-sm font-semibold">Recarregar</button> : null}
      <fieldset disabled={!canEdit}>
        {/* CNH número 3 · categoria 2 · validade 3 · reservista 4 */}
        <FormSection title="CNH e reservista" description="Dado pessoal protegido pela LGPD. Cada leitura desta aba fica registrada." grid="form">
          <Field label="CNH: número" size="sm" error={field('cnhNumber')}>{(p) => <input {...p} value={cnh} inputMode="numeric" maxLength={11} autoComplete="off" onChange={(e) => setCnh(onlyDigits(e.target.value))} className={inputClass + ' tabular-nums'} />}</Field>
          <Field label="CNH: categoria" size="xs" error={field('cnhCategory')}>
            {(p) => (
              <select {...p} value={category} onChange={(e) => setCategory(e.target.value)} className={inputClass}>
                <option value="">Sem CNH</option>
                {CNH_CATEGORIES.map((c) => <option key={c} value={c}>{c}</option>)}
              </select>
            )}
          </Field>
          <Field label="CNH: validade" size="sm" error={field('cnhExpiry')} warning={expired ? 'CNH vencida. Peça a renovada ao funcionário.' : undefined}>{(p) => <DateInput {...p} value={expiry} onChange={setExpiry} />}</Field>
          <Field label="Certificado de reservista" size="md" error={field('militaryCertificate')} hint="Opcional.">{(p) => <input {...p} value={military} maxLength={20} autoComplete="off" onChange={(e) => setMilitary(e.target.value)} className={inputClass} />}</Field>
        </FormSection>
      </fieldset>
      {canEdit ? <FormActions><button type="submit" className={primary}>Salvar documentos</button></FormActions> : null}
    </form>
  )
}

/** Documentos digitalizados (CA 12): lista e download só com hr.view_sensitive; cada download fica registrado. */
function DocumentFiles({ employee, canEdit }: { employee: EmployeeDetail; canEdit: boolean }) {
  const files = useListEmployeeDocumentFiles(employee.id, { query: { gcTime: 0, staleTime: 0 } })
  const input = useRef<HTMLInputElement>(null)
  const [error, setError] = useState<string>()
  const [sending, setSending] = useState(false)
  const upload = async (file: File) => {
    setSending(true)
    setError(undefined)
    try {
      await uploadAttachment(OWNER, employee.id, { file })
      notifySuccess('Documento anexado')
      await files.refetch()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível anexar o documento.')
    } finally {
      setSending(false)
      if (input.current) input.current.value = ''
    }
  }

  const attach = canEdit ? (
    <>
      <input ref={input} id="documento-funcionario" type="file" accept=".pdf,.png,.jpg,.jpeg" className="sr-only" onChange={(e) => { const f = e.target.files?.[0]; if (f) void upload(f) }} />
      <label htmlFor="documento-funcionario" className="inline-flex min-h-11 cursor-pointer items-center rounded-md border border-border px-3 py-2 text-sm font-semibold">{sending ? 'Enviando…' : 'Anexar documento'}</label>
    </>
  ) : undefined

  return (
    <FormSection title="Documentos digitalizados" description="RG, CNH, comprovantes e outros, em PDF, PNG ou JPG até 10 MB. Cada download fica registrado." grid="none">
      <div className="space-y-3">
      <FormError message={error} />
      {files.isPending ? <Skeleton className="h-16" /> : files.isError ? <ErrorState title="Não foi possível carregar os documentos." onRetry={() => files.refetch()} />
        : files.data.length === 0 ? <EmptyState title="Nenhum documento anexado." description="Anexe os documentos digitalizados do funcionário." action={attach} />
          : (
            <div className="space-y-2">
              <ul aria-label="Documentos digitalizados" className="divide-y divide-border rounded-md border border-border bg-card text-sm">
                {files.data.map((f) => (
                  <li key={f.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
                    <span className="flex flex-wrap items-center gap-2">
                      <button type="button" onClick={() => void apiDownload(getDownloadEmployeeDocumentFileUrl(employee.id, f.id), f.fileName)} className="break-all font-semibold text-primary hover:underline">{f.fileName}</button>
                      {f.scanStatus === 1 ? null : <StatusChip tone="info">Em verificação</StatusChip>}
                    </span>
                    <span className="text-xs text-muted-foreground tabular-nums">{formatDateTime(f.createdAt)}</span>
                  </li>
                ))}
              </ul>
              {attach}
            </div>
          )}
      </div>
    </FormSection>
  )
}
