import { getDownloadAcceptanceTermPdfUrl } from '@/api/generated/bemaqui'
import { DocumentActions } from '@/components/ui/document-actions'
import { Dica } from '@/help/Dica'
import { OrderDocuments } from '../OrderDocuments'
import { INSTALLATION_DICAS } from './installations'

/**
 * Fotos e termo assinado (COM-06 CA 8): png, jpg ou pdf até 10 MB; no celular, a câmera pelo "Escolher arquivo". Acima do envio, o termo de
 * aceite técnico para imprimir (EXP-04 CA 8): em branco até o aceite, preenchido depois; a OS cancelada não tem termo.
 */
export function InstallationDocuments({ orderId, number, status }: { orderId: string; number: string; status: string }) {
  return (
    <>
      {status !== 'Cancelled' ? (
        <div className="mb-4 flex flex-wrap items-start gap-2">
          <span className="inline-flex items-start">
            <DocumentActions url={getDownloadAcceptanceTermPdfUrl(orderId)} fileName={`${number}-termo-de-aceite.pdf`} label="Baixar termo de aceite" print={false} />
            <Dica nome="Termo de aceite" texto={INSTALLATION_DICAS.termo} />
          </span>
          <p className="text-sm text-muted-foreground">Imprima, colha a assinatura na obra e envie o termo assinado abaixo.</p>
        </div>
      ) : null}
      <OrderDocuments orderId={orderId} inputId="foto-os" accept=".pdf,.png,.jpg,.jpeg,image/*"
        empty={{ title: 'Nenhuma foto ou termo anexado.', description: 'Anexe as fotos da instalação e o termo de entrega assinado.' }} />
    </>
  )
}
