/**
 * Leitor de código de barras em modo teclado (PDV-02 CA 4, rascunho B item 7): o leitor "digita" o código muito mais rápido que uma pessoa e
 * termina com Enter. O detector olha só o tempo entre as teclas: uma rajada de pelo menos `minLength` caracteres, cada um a no máximo
 * `maxGapMs` do anterior, fechada por Enter, é uma leitura. Assim o app lança o item mesmo com o foco em outro campo.
 *
 * O detector não engole tecla nenhuma no meio da rajada (uma pessoa digitando rápido nunca perde letra): quem usa guarda o valor do campo no
 * começo da rajada (`start`) e, na leitura (`scan`), devolve o campo ao que era, cancela o Enter e lança o código.
 */
export interface ScanDetectorOptions {
  /** Maior intervalo entre duas teclas da mesma leitura (ms). Leitores USB ficam abaixo de 20 ms; gente, acima de 80 ms. */
  maxGapMs?: number
  /** Menor código aceito como leitura (EAN-8 tem 8; códigos internos curtos ficam para o campo de código). */
  minLength?: number
}

export type ScanStep =
  /** Primeira tecla de uma rajada possível: guarde o campo como está agora. */
  | { kind: 'start' }
  /** Tecla dentro da rajada. */
  | { kind: 'continue' }
  /** Leitura completa: cancele o Enter, devolva o campo e lance o código. */
  | { kind: 'scan'; code: string }
  /** Tecla que não interessa (modificador, Enter de gente). */
  | { kind: 'ignore' }

const MODIFIERS = new Set(['Shift', 'Control', 'Alt', 'AltGraph', 'Meta', 'CapsLock'])

export class ScanDetector {
  private readonly maxGapMs: number
  private readonly minLength: number
  private buffer = ''
  private last = Number.NEGATIVE_INFINITY

  constructor(options: ScanDetectorOptions = {}) {
    this.maxGapMs = options.maxGapMs ?? 50
    this.minLength = options.minLength ?? 6
  }

  /** Uma tecla (o `KeyboardEvent.key`) no instante `at` (ms, `event.timeStamp`). */
  push(key: string, at: number): ScanStep {
    if (MODIFIERS.has(key)) return { kind: 'ignore' }
    const fast = at - this.last <= this.maxGapMs
    if (key === 'Enter') {
      const code = this.buffer
      const isScan = fast && code.length >= this.minLength
      this.reset()
      return isScan ? { kind: 'scan', code } : { kind: 'ignore' }
    }

    if (key.length !== 1) {
      this.reset()
      return { kind: 'ignore' }
    }

    this.last = at
    if (fast && this.buffer) {
      this.buffer += key
      return { kind: 'continue' }
    }

    this.buffer = key
    return { kind: 'start' }
  }

  reset(): void {
    this.buffer = ''
    this.last = Number.NEGATIVE_INFINITY
  }
}
