import { apiBlobUrl } from '@/api/http'

/** Abre o PDF numa aba nova para imprimir (a aba abre no clique, antes do servidor, para não cair no bloqueio de pop-up). */
export async function printPdf(url: string) {
  const tab = window.open('about:blank', '_blank')
  try {
    const href = await apiBlobUrl(url)
    if (tab) {
      tab.opener = null
      tab.location.href = href
    }
    setTimeout(() => URL.revokeObjectURL(href), 60_000)
  } catch {
    tab?.close()
  }
}
