import {
  getDownloadBankStatementPdfUrl, getDownloadDrePdfUrl, getDownloadReconciliationPdfUrl, getExportBankStatementUrl, getExportDreUrl, getExportReconciliationUrl,
} from '@/api/generated/bemaqui'
import type { ExportFormat } from '@/components/ui/export-menu'

/**
 * Endereço da exportação das telas do financeiro com documento próprio (EXP-02): a planilha pela rota de lista e o PDF pelo documento do
 * período (extrato, relatório de conciliação, DRE gerencial), sempre com os filtros que estão na tela.
 */
export const statementExportUrl = (accountId: string, filters: { from: string; to: string; withoutOrder?: boolean }) => (format: ExportFormat) =>
  format === 'pdf' ? getDownloadBankStatementPdfUrl(accountId, filters) : getExportBankStatementUrl(accountId, { ...filters, format })

export const reconciliationExportUrl = (accountId: string, filters: { from: string; to: string }) => (format: ExportFormat) =>
  format === 'pdf' ? getDownloadReconciliationPdfUrl(accountId, filters) : getExportReconciliationUrl(accountId, { ...filters, format })

export const dreExportUrl = (filters: { from: string; to: string }) => (format: ExportFormat) =>
  format === 'pdf' ? getDownloadDrePdfUrl(filters) : getExportDreUrl({ ...filters, format })
