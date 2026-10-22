import { OrderDocuments } from '../OrderDocuments'

/** Fotos e termo assinado (COM-06 CA 8): png, jpg ou pdf até 10 MB; no celular, a câmera pelo "Escolher arquivo". */
export function InstallationDocuments({ orderId }: { orderId: string }) {
  return <OrderDocuments orderId={orderId} inputId="foto-os" accept=".pdf,.png,.jpg,.jpeg,image/*"
    empty={{ title: 'Nenhuma foto ou termo anexado.', description: 'Anexe as fotos da instalação e o termo de entrega assinado.' }} />
}