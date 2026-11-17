// Gerador determinístico do catálogo de teste (50 mil itens) e do delta.
// Nomes no estilo do varejo brasileiro, código de barras EAN-13 válido e preço em centavos.

const PRODUTOS = ['arroz', 'feijao', 'acucar', 'cafe', 'leite', 'oleo', 'macarrao', 'farinha', 'sal', 'biscoito',
  'refrigerante', 'suco', 'agua mineral', 'cerveja', 'detergente', 'sabao em po', 'amaciante', 'papel higienico',
  'shampoo', 'condicionador', 'sabonete', 'creme dental', 'desodorante', 'iogurte', 'queijo', 'presunto',
  'manteiga', 'margarina', 'chocolate', 'achocolatado', 'molho de tomate', 'extrato de tomate', 'milho verde',
  'ervilha', 'atum', 'sardinha', 'mostarda', 'maionese', 'ketchup', 'vinagre', 'azeite', 'tempero', 'caldo',
  'gelatina', 'pudim', 'leite condensado', 'creme de leite', 'aveia', 'granola', 'cereal', 'pao de forma',
  'torrada', 'bolo', 'salgadinho', 'amendoim', 'bala', 'chiclete', 'pilha', 'lampada', 'esponja', 'alvejante',
  'desinfetante', 'saco de lixo', 'guardanapo', 'copo descartavel', 'racao', 'carvao', 'fosforo', 'vela'];
const MARCAS = ['camil', 'tio joao', 'kicaldo', 'uniao', 'pilao', 'tres coracoes', 'italac', 'piracanjuba',
  'soya', 'liza', 'renata', 'adria', 'dona benta', 'cisne', 'marilan', 'bauducco', 'coca cola', 'guarana',
  'del valle', 'crystal', 'skol', 'brahma', 'ype', 'omo', 'comfort', 'neve', 'seda', 'dove', 'colgate', 'rexona',
  'nestle', 'danone', 'sadia', 'perdigao', 'aviacao', 'qualy', 'garoto', 'lacta', 'toddy', 'quero', 'fugini',
  'gomes da costa', 'coqueiro', 'hellmanns', 'heinz', 'castelo', 'gallo', 'knorr', 'maggi', 'royal', 'moca',
  'quaker', 'kelloggs', 'pullman', 'wickbold', 'elma chips', 'dori', 'trident', 'duracell', 'philips', 'scotch',
  'qboa', 'veja', 'pinho sol', 'dog chow', 'whiskas', 'fiat lux', 'marca propria'];
const VARIANTES = ['tradicional', 'integral', 'light', 'zero', 'diet', 'premium', 'extra', 'tipo 1', 'tipo 2',
  'morango', 'chocolate', 'baunilha', 'limao', 'laranja', 'uva', 'original', 'suave', 'forte', 'refil', 'economico'];
const TAMANHOS = ['1kg', '2kg', '5kg', '500g', '250g', '200g', '1l', '2l', '350ml', '600ml', '1,5l', '90g', '400g',
  'un', 'pct 4un', 'cx 12un', '100ml', '300g', '750ml', '8un'];

// PRNG determinístico (mulberry32): mesma semente, mesmo catálogo.
export function rng(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function ean13(base12) {
  const s = String(base12).padStart(12, '0');
  let sum = 0;
  for (let i = 0; i < 12; i++) sum += Number(s[i]) * (i % 2 === 0 ? 1 : 3);
  return s + ((10 - (sum % 10)) % 10);
}

function pick(r, arr) { return arr[Math.floor(r() * arr.length)]; }

export function makeItem(i, r, seq) {
  const nome = `${pick(r, PRODUTOS)} ${pick(r, MARCAS)} ${pick(r, VARIANTES)} ${pick(r, TAMANHOS)}`.toUpperCase();
  const barcodes = [ean13(789000000000 + i)];
  if (r() < 0.1) barcodes.push(ean13(780000000000 + i)); // 10% com um segundo código (embalagem de fardo)
  return {
    id: `it-${String(i).padStart(6, '0')}`,
    name: nome,
    barcodes,
    price: 99 + Math.floor(r() * 9900), // centavos
    unit: r() < 0.05 ? 'KG' : 'UN',
    cat: Math.floor(r() * 40),
    seq,
  };
}

export function snapshot(n = 50000) {
  const r = rng(42);
  const items = new Array(n);
  for (let i = 0; i < n; i++) items[i] = makeItem(i, r, i + 1);
  return { cursor: n, items };
}

// Delta de 500 mudanças depois da foto: 450 alterados (preço e às vezes nome) e 50 desativados (removidos).
export function delta(n = 50000, size = 500) {
  const r = rng(7);
  const base = snapshot(n).items;
  const changes = [];
  let seq = n;
  const used = new Set();
  while (changes.length < size) {
    const idx = Math.floor(r() * n);
    if (used.has(idx)) continue;
    used.add(idx);
    seq++;
    if (changes.length < size - 50) {
      const it = { ...base[idx], price: base[idx].price + 10 + Math.floor(r() * 500), seq };
      if (r() < 0.2) it.name = it.name + ' NOVA EMB';
      changes.push({ op: 'upsert', item: it });
    } else {
      changes.push({ op: 'delete', id: base[idx].id, seq });
    }
  }
  return { cursor: seq, changes };
}

// Termos de busca: trechos que o operador digitaria. "prefixo" = começo de palavra; "meio" = trecho no meio.
export function searchTerms(n = 50000) {
  const r = rng(99);
  const items = snapshot(n).items;
  const prefix = [];
  const middle = [];
  const barcodes = [];
  for (let k = 0; k < 120; k++) {
    const it = items[Math.floor(r() * n)];
    const words = it.name.toLowerCase().split(' ').filter((w) => w.length >= 3);
    const w1 = words[Math.floor(r() * words.length)];
    const w2 = words[Math.floor(r() * words.length)];
    // prefixo: 1 ou 2 palavras abreviadas ("arro cami")
    prefix.push(k % 2 === 0 ? w1.slice(0, 4) : `${w1.slice(0, 3)} ${w2.slice(0, 3)}`);
    // meio: 4 letras do meio de uma palavra (pior caso para índice de prefixo)
    const start = Math.max(0, Math.min(w1.length - 4, 1));
    middle.push(w1.slice(start, start + 4));
    barcodes.push(it.barcodes[Math.floor(r() * it.barcodes.length)]);
  }
  // 10% de códigos inexistentes (leitura errada)
  for (let k = 0; k < 12; k++) barcodes[k * 10] = '7899999' + String(100000 + k);
  // um código de item que o delta remove (para conferir que a remoção vale na busca)
  const del = delta(n).changes.find((c) => c.op === 'delete');
  const removedBarcode = items[Number(del.id.slice(3))].barcodes[0];
  return { prefix, middle, barcodes, removedBarcode };
}
