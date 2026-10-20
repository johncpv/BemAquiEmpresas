/**
 * Mensagens do sistema (ADR 0021): sucesso e erro fora de diálogo vão para a região do topo do conteúdo. O próximo passo
 * nunca vai aqui: fica no NextSteps da tela.
 */
export interface Message {
  id: number
  tone: 'success' | 'error'
  text: string
  /** Só no erro: repete a ação que falhou. */
  retry?: () => void
}

type Listener = (messages: Message[]) => void

const MAX = 3
let messages: Message[] = []
let next = 1
const listeners = new Set<Listener>()

const emit = () => listeners.forEach((l) => l(messages))

function push(message: Omit<Message, 'id'>) {
  messages = [{ ...message, id: next++ }, ...messages].slice(0, MAX)
  emit()
}

/** Sucesso com o particípio do verbo ("Terceiro salvo"): não bloqueia, fica pelo menos 5 s. */
export function notifySuccess(text: string) {
  push({ tone: 'success', text })
}

/** Erro fora de diálogo (ação direta numa lista, rede): não some sozinho; com retry, oferece "Tentar de novo". */
export function notifyError(text: string, retry?: () => void) {
  push({ tone: 'error', text, retry })
}

export function dismissMessage(id: number) {
  messages = messages.filter((m) => m.id !== id)
  emit()
}

export function subscribeMessages(listener: Listener) {
  listeners.add(listener)
  listener(messages)
  return () => { listeners.delete(listener) }
}

/** Só para testes. */
export function clearMessages() {
  messages = []
  emit()
}
