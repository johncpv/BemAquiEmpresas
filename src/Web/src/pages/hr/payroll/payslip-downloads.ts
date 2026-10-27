/** Downloads do holerite (RH-02b CA 1 a 3): o PDF e o ZIP pelo servidor, com o nome sem CPF nem nome do funcionário. */
import { getDownloadPayslipBatchUrl, getDownloadPayslipPdfUrl } from '@/api/generated/bemaqui'
import type { PayrollRunSummary } from '@/api/generated/model'
import { apiDownload } from '@/api/http'
import { notifyError } from '@/components/ui/messages'

const fileName = (run: PayrollRunSummary, row: { registration?: string | null; payslipId: string }) =>
  `holerite-${run.period.slice(0, 7)}-${row.registration?.replace(/[^A-Za-z0-9_-]/g, '') || row.payslipId.replace(/-/g, '').slice(-8)}.pdf`

/** "Baixar holerite": com a folha em rascunho, o PDF vem com a marca d'água. */
export const downloadPayslip = (run: PayrollRunSummary, row: { payslipId: string; registration?: string | null }) =>
  apiDownload(getDownloadPayslipPdfUrl(run.id, row.payslipId), fileName(run, row)).catch((e: unknown) => notifyError(e instanceof Error ? e.message : 'Não foi possível baixar o holerite.'))

export const downloadBatch = (run: PayrollRunSummary, attachmentId: string) =>
  apiDownload(getDownloadPayslipBatchUrl(run.id, attachmentId), `holerites-${run.period.slice(0, 7)}.zip`).catch((e: unknown) => notifyError(e instanceof Error ? e.message : 'Não foi possível baixar os holerites.'))
