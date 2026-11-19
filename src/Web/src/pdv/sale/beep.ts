/** Alerta sonoro curto do caixa (PDV-02 CA 3: produto não encontrado). Sem áudio no aparelho, segue em silêncio: a mensagem na tela basta. */
let context: AudioContext | null = null

export function beep(frequency = 880, durationMs = 180): void {
  try {
    context ??= new AudioContext()
    const oscillator = context.createOscillator()
    const gain = context.createGain()
    oscillator.type = 'square'
    oscillator.frequency.value = frequency
    gain.gain.value = 0.15
    oscillator.connect(gain).connect(context.destination)
    oscillator.start()
    oscillator.stop(context.currentTime + durationMs / 1000)
  } catch {
    // sem Web Audio: só a mensagem
  }
}
