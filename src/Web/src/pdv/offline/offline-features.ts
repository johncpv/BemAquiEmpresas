/**
 * OFF-03 CA 11 (rascunho A, item 11): o que fica desabilitado sem rede, cada um com "Disponível com internet.". Fiado e vale-troca já aparecem
 * desabilitados na tela de pagamento (`methodAvailability`) e a busca de cliente na ficha do cliente; o menu do caixa sem rede mostra a lista
 * inteira, para o operador saber de antemão.
 */
export const NEEDS_NETWORK = 'Disponível com internet.'

export const OFFLINE_DISABLED = [
  'Fiado',
  'Vale-troca',
  'Devolução',
  'Busca de cliente cadastrado',
  'Consulta de saldo',
  'Registro de aparelho',
  'Instalação de impressora nova',
] as const
