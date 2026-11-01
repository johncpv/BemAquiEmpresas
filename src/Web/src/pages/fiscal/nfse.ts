/** Textos da NFS-e da instalação (FIS-06): verbos e confirmações da story. */
export const NFSE_TEXTS = {
  saved: 'Rascunho salvo',
  sent: 'NFS-e enviada',
  registered: 'NFS-e registrada',
  cancelled: 'Cancelamento registrado',
  reopened: 'Rascunho reaberto',
  discarded: 'Rascunho descartado',
  resent: 'NFS-e reenviada',
  waiting: 'NFS-e enviada. Aguardando a prefeitura…',
  slow: 'A prefeitura está demorando. A conferência automática segue e avisa quando a nota sair; não emita de novo.',
  emptyQueue: 'Nenhuma instalação esperando NFS-e.',
  cancelInPortal: 'Cancele no portal da prefeitura e registre aqui.',
  registeredOrigin: 'Registrada (portal da prefeitura)',
  issTyped: 'Digitado da nota da prefeitura',
} as const

/** Dicas da story ("Ajuda"): município da prestação, item da LC 116, retenção do ISS e a nota registrada do portal. */
export const NFSE_DICAS = {
  municipio: 'Onde o ISS é devido. Na instalação (item 7.02 da LC 116) é o município da obra; por isso o projeto precisa do endereço da obra com o código IBGE.',
  item: 'O código do serviço na lista da LC 116. A instalação de elevador usa o 7.02 (execução de obra); a contadora pode indicar o 14.06 (instalação e montagem). Fica em Configurações → Fiscal.',
  retencao: 'Quando o tomador retém o ISS, ele paga o imposto à prefeitura e desconta do valor pago à empresa. O valor líquido da nota já sai sem o ISS retido.',
  registrada: 'A nota foi emitida no portal da prefeitura e registrada aqui com os valores dela, enquanto o Emissor Fiscal não emite a NFS-e de Caçapava.',
} as const

/** "NFS-e 9001" ou "Rascunho de NFS-e". */
export function nfseLabel(number: number | null | undefined): string {
  return number ? `NFS-e ${number}` : 'Rascunho de NFS-e'
}

/** Valor com ponto decimal e 2 casas para o formulário do registro (o servidor lê o formato invariável). */
export function decimalField(value: string): string {
  const text = value.trim()
  const n = text.includes(',') ? Number(text.replace(/\./g, '').replace(',', '.')) : Number(text)
  return text !== '' && Number.isFinite(n) ? n.toFixed(2) : ''
}

/** Processando há mais de 30 s: "Demorando" (padrão 6). */
export function isNfseSlow(status: string, processingSince: string | null | undefined, now = Date.now()): boolean {
  return status === 'Processing' && !!processingSince && now - new Date(processingSince).getTime() > 30_000
}
