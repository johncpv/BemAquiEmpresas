# SPK-03 — núcleo local-first sob carga

Código do spike [SPK-03](../../docs/pdv/stories/SPK-03-nucleo-local-first-carga.md). Fica **fora de `src/`** e não entra no build do produto. O resultado está em [`docs/pdv/spikes/SPK-03-resultado.md`](../../docs/pdv/spikes/SPK-03-resultado.md).

## O que tem aqui

| Arquivo | Para quê |
|---|---|
| `server.mjs` | Servidor mínimo em Node: estáticos, catálogo (foto e delta) e `POST /api/sync/events` idempotente (`ON CONFLICT DO NOTHING` por `event_id` e por `device_id, epoch, seq`). Banco: `node:sqlite`, **não** PostgreSQL |
| `lib/catalog.mjs` | Catálogo determinístico de 50 mil itens (nome, EAN-13, preço em centavos), delta de 500 mudanças (450 alterações, 50 removidos) e termos de busca |
| `public/store-idb.js` | Porta `LocalStore` sobre IndexedDB com Dexie 4 (thread principal) |
| `public/store-sqlite.js`, `public/sqlite-worker.js` | Porta `LocalStore` sobre SQLite-WASM 3.54 em OPFS (VFS `opfs-sahpool`, num worker; `locking_mode=exclusive`, WAL) |
| `public/register.js` | Caixa: carrinho gravado a cada item, venda concluída como evento imutável com `seq` por aparelho e época e cadeia de hash, fila de saída e lote |
| `public/bench.*` | Bancada: carga, busca, venda, delta, tamanho, reabertura |
| `public/pos.*` | Caixa simulado que vende e sincroniza ao mesmo tempo (usado na carga e na queda) |
| `run-bench.mjs` | CA 1, 2 e 3, por tecnologia (`idb`, `idb-lean`, `sqlite`); `--rates=4` usa o throttling do CDP (só a thread da página) |
| `lib/job-throttle.ps1`, `run-matrix.ps1` | Máquina fraca simulada com Job Object do Windows (2 núcleos lógicos, teto de CPU = 1/4 ou 1/6), que pega em **todos** os processos do Chromium; `run-matrix.ps1` roda normal, 4x e 6x |
| `summarize.mjs` | Tabela Markdown a partir de `results/bench-*.json` |
| `run-sync.mjs` | CA 6: 20 caixas × 200 vendas com respostas perdidas e falhas |
| `run-crash.mjs` | CA 4 e 5: `taskkill /F` no navegador e aba fechada no meio da venda; duas abas no mesmo caixa |
| `run-stock.mjs` | CA 7: baixa de estoque por venda x agregada |
| `probe.mjs`, `probe-worker-throttle.mjs`, `public/opfs-probe-worker.js` | Sondas da exploração: modos de journal do SQLite, throttling do CDP em worker (não pega) e custo cru do OPFS |

## Como rodar

Precisa de Node 24 (usa `node:sqlite`) e do Chromium do Playwright já baixado (o mesmo do `src/Web`; se faltar, `npx playwright-core install chromium`).

```bash
cd spikes/spk-03-local-first
npm ci
powershell -File run-matrix.ps1   # ~30 min: normal, job 4x, job 6x -> results/bench-*.json
node summarize.mjs bench-normal.json bench-job4x.json bench-job6x.json
npm run bench           # avulso; parâmetros: --rates=1,4,6 (CDP) --techs=idb,idb-lean,sqlite --n=50000 --sales=100
npm run sync            # 20 caixas x 200 vendas; --tech=idb --drop=0.3 --fail=0.05
npm run crash           # --variants=idb:strict,idb:relaxed,sqlite:full,sqlite:normal --kills=6 --closes=3
npm run stock
```

Os resultados saem em `results/*.json` (os do relatório estão versionados). O servidor sobe sozinho na porta 5317 (`PORT=` muda).

## Limites (leia antes de citar um número)

- **CPU fraca é simulação.** `Emulation.setCPUThrottlingRate` (CDP) só desacelera a thread da página: medido, não pega em worker (`probe-worker-throttle.mjs`; o CDP responde "Operation is only supported for pages, not workers") nem no processo que guarda o IndexedDB. Por isso a matriz do relatório usa o Job Object, que limita tudo; a lentidão efetiva é medida em cada rodada pelo laço de calibração e varia (o teto do Windows age em rajadas). Disco lento (eMMC de tablet barato) não é simulado.
- **Queda de energia não é simulada.** `taskkill /F` mata o processo, mas o que já foi entregue ao sistema operacional sobrevive. Só aparelho real com corte de energia prova o `fsync` (fica para o SPK-01).
- Windows 11, Chromium do Playwright em headless novo; Android não foi testado.
