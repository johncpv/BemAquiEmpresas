// Utilitários do caixa simulado: normalização, UUID v7, hash, estatística.

export function norm(s) {
  return s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
}

export function words(s) {
  return [...new Set(norm(s).split(/[^a-z0-9,]+/).filter((w) => w.length > 0))];
}

// UUID v7 (48 bits de milissegundos + aleatório). crypto.randomUUID é v4.
export function uuidv7() {
  const b = crypto.getRandomValues(new Uint8Array(16));
  const ms = BigInt(Date.now());
  for (let i = 0; i < 6; i++) b[i] = Number((ms >> BigInt(8 * (5 - i))) & 0xffn);
  b[6] = (b[6] & 0x0f) | 0x70;
  b[8] = (b[8] & 0x3f) | 0x80;
  const h = [...b].map((x) => x.toString(16).padStart(2, '0')).join('');
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
}

export async function sha256(text) {
  const d = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text));
  return [...new Uint8Array(d)].map((x) => x.toString(16).padStart(2, '0')).join('');
}

export function stats(arr) {
  const s = [...arr].sort((a, b) => a - b);
  const q = (p) => (s.length ? s[Math.min(s.length - 1, Math.floor(p * s.length))] : null);
  const r = (x) => (x == null ? null : Math.round(x * 100) / 100);
  return { n: s.length, p50: r(q(0.5)), p95: r(q(0.95)), max: r(s.at(-1)) };
}

export function itemRow(it) {
  return { ...it, nameNorm: norm(it.name), words: words(it.name) };
}

export function log(msg) {
  console.log(msg);
}
