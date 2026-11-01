/** Leitura pela câmera disponível: contexto seguro, câmera no navegador e a API `BarcodeDetector` com QR (sem biblioteca de leitura). */
export async function canScanQr(): Promise<boolean> {
  if (typeof window === 'undefined' || !window.isSecureContext || !navigator.mediaDevices?.getUserMedia) return false
  const ctor = (window as unknown as { BarcodeDetector?: { getSupportedFormats?: () => Promise<string[]> } }).BarcodeDetector
  if (!ctor) return false
  try {
    return !ctor.getSupportedFormats || (await ctor.getSupportedFormats()).includes('qr_code')
  } catch {
    return false
  }
}
