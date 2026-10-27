import { useState } from 'react'
import { importPayrollEntries, previewPayrollImport } from '@/api/generated/bemaqui'
import type { PayrollImportPreview, PayrollRunSummary } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { Skeleton } from '@/components/ui/states'
import { CSV_HEADER, KIND_LABELS, periodLabel } from './payroll'

/**
 * "Importar lançamentos" (RH-02a CA 8 a 10): o arquivo CSV no layout padrão v1 vai para a prévia (nada é gravado), com os
 * funcionários encontrados, as linhas por rubrica, os totais e os erros por linha; só com zero erro "Importar {n} lançamentos" fica
 * habilitado, e a confirmação avisa quantos holerites são substituídos. O arquivo não fica guardado.
 */
export function ImportDialog({ run, onClose, onImported }: { run: PayrollRunSummary; onClose: () => void; onImported: () => void }) {
  const [file, setFile] = useState<File | null>(null)
  const [preview, setPreview] = useState<PayrollImportPreview | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const choose = async (next: File | null) => {
    setFile(next)
    setPreview(null)
    setError(null)
    if (!next) return
    setLoading(true)
    try {
      setPreview(await previewPayrollImport(run.id, { file: next }))
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível ler o arquivo.')
    } finally {
      setLoading(false)
    }
  }

  const lines = preview?.lines ?? 0
  return (
    <ConfirmDialog open onOpenChange={(o) => !o && onClose()} size="lg" title={`Importar lançamentos de ${periodLabel(run.period)}`}
      verb={preview?.canImport ? `Importar ${lines} ${lines === 1 ? 'lançamento' : 'lançamentos'}` : 'Importar lançamentos'}
      done={`${lines} ${lines === 1 ? 'lançamento importado' : 'lançamentos importados'}`}
      description={preview?.canImport ? `Substitui os lançamentos de ${preview.employees} ${preview.employees === 1 ? 'funcionário' : 'funcionários'}. Os holerites que não estão no arquivo ficam como estão.` : 'Envie o arquivo CSV no layout padrão do sistema. Antes de gravar, o sistema mostra a prévia.'}
      preconditions={[{ ok: !!preview?.canImport, text: preview ? 'Corrija os erros do arquivo e envie de novo.' : 'Escolha o arquivo e confira a prévia.' }]}
      onConfirm={async () => { if (file) { await importPayrollEntries(run.id, { file }); onImported() } }}>
      <div className="space-y-3 text-sm">
        <p className="text-muted-foreground">Layout v1: <code className="break-all rounded bg-muted px-1">{CSV_HEADER}</code>, separador ponto e vírgula, decimal com vírgula, UTF-8. Até 2 MB e 5 mil linhas.</p>
        <label className="block space-y-1">
          <span className="font-medium">Arquivo CSV</span>
          <input type="file" accept=".csv,text/csv" onChange={(e) => void choose(e.target.files?.[0] ?? null)} className="block w-full text-sm" />
        </label>
        {loading ? <Skeleton className="h-24" /> : null}
        {error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-crit-ink">{error}</p> : null}
        {preview ? (
          <section aria-label="Prévia da importação" className="space-y-2">
            <p className="tabular-nums">{preview.employees} {preview.employees === 1 ? 'funcionário encontrado' : 'funcionários encontrados'} · {preview.lines} {preview.lines === 1 ? 'linha' : 'linhas'}</p>
            {preview.byItem.length > 0 ? (
              <ul className="divide-y divide-border rounded-md border border-border">
                {preview.byItem.map((i) => (
                  <li key={i.code} className="flex flex-wrap justify-between gap-2 px-3 py-1.5">
                    <span>{i.code} · {i.name} <span className="text-muted-foreground">({KIND_LABELS[i.kind]}, {i.lines} {i.lines === 1 ? 'linha' : 'linhas'})</span></span>
                    <Money value={i.total} />
                  </li>
                ))}
              </ul>
            ) : null}
            <p className="tabular-nums">Proventos <Money value={preview.gross} /> · Descontos <Money value={preview.deductions} /> · <b>Líquido <Money value={preview.net} /></b></p>
            {preview.errorCount > 0 ? (
              <div role="alert" className="space-y-1 rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-crit-ink">
                <p className="font-semibold">{preview.errorCount === 1 ? '1 erro' : `${preview.errorCount} erros`} no arquivo{preview.errorCount > preview.errors.length ? ` (os primeiros ${preview.errors.length})` : ''}:</p>
                <ul className="max-h-48 list-disc space-y-0.5 overflow-y-auto pl-5">
                  {preview.errors.map((e) => <li key={e}>{e}</li>)}
                </ul>
              </div>
            ) : null}
          </section>
        ) : null}
      </div>
    </ConfirmDialog>
  )
}
