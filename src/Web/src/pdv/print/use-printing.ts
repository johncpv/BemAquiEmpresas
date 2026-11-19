import { useEffect, useMemo, useState } from 'react'
import type { CatalogPrinter } from '@/pdv/local-store/catalog-types'
import type { LocalStore } from '@/pdv/local-store/local-store'
import { BrowserPrintBridge, type HardwareBridge } from './hardware-bridge'
import { INSTALLED_PRINTER_META, printersOf, type ReceiptHeader } from './print-jobs'

/** Impressoras do caixa (o cadastro que desceu e a instalada neste aparelho) e a porta de impressão (CA 1: tudo passa pela `HardwareBridge`). */
export function usePrinting(store: LocalStore, bridge?: HardwareBridge) {
  const port = useMemo(() => bridge ?? new BrowserPrintBridge(), [bridge])
  const [installed, setInstalled] = useState<CatalogPrinter | null>(null)
  useEffect(() => {
    let alive = true
    void store.getMeta<CatalogPrinter>(INSTALLED_PRINTER_META).then((p) => { if (alive) setInstalled(p ?? null) }).catch(() => { /* sem a instalada: só o cadastro */ })
    return () => { alive = false }
  }, [store])
  const printers = printersOf(store.list('printers'), installed)
  return { bridge: port, printers }
}

/** Cabeçalho do cupom: empresa (do catálogo da loja) e o nome da loja. */
export function receiptHeader(store: LocalStore, storeName: string): ReceiptHeader {
  const settings = store.catalogState()?.storeSettings
  return { companyName: settings?.companyName ?? null, companyDocument: settings?.companyDocument ?? null, storeName }
}
