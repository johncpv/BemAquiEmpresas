# SPK-03 — Resultado: núcleo local-first sob carga

> Spike da [story SPK-03](../stories/SPK-03-nucleo-local-first-carga.md). Código em [`spikes/spk-03-local-first/`](../../../spikes/spk-03-local-first/README.md), fora de `src/` e fora do build do produto. Medições de 16 e 17/11/2026. Os JSON brutos estão em `spikes/spk-03-local-first/results/`.

## 1. Resposta em uma frase

**IndexedDB (Dexie), com o catálogo guardado em blocos e consultado em memória, e não SQLite-WASM**: com 50 mil itens, essa forma carrega a foto em 0,4 s (até 2 s nas simulações de máquina fraca), acha o código de barras em menos de 0,1 ms e reabre o caixa em 0,2 a 0,6 s, enquanto o SQLite-WASM em OPFS levou de 1,5 a 7,5 s só para abrir no Windows e não ganhou em nada que o caixa usa.

## 2. Ambiente e o que é simulação

| Item | Valor |
|---|---|
| Máquina | Notebook Intel Core i7-8565U (4 núcleos, 8 lógicos, 1,8 GHz), 16 GB, SSD NVMe, Windows 11 Pro 10.0.26200 |
| Navegador | Chromium 156.0.8078.4 do Playwright 1.64.1, headless novo, perfil novo por rodada |
| Bibliotecas | Dexie 4.4.7; `@sqlite.org/sqlite-wasm` 3.54.1-build1 (VFS `opfs-sahpool`, `locking_mode=exclusive`, WAL) |
| Servidor | Node 24 com `node:sqlite` (WAL, `synchronous=FULL`), **não** PostgreSQL nem o backend .NET |
| Ruído | Outras sessões rodavam testes (Vitest) na mesma máquina em parte das rodadas. Os números têm dispersão; olhe a ordem de grandeza |

**Máquina fraca é simulação, não aparelho real.** Dois métodos, porque o primeiro se mostrou incompleto:

1. **Throttling de CPU do Chromium (CDP `Emulation.setCPUThrottlingRate`, 4x e 6x)**, como pedido. Medido: ele desacelera só a thread da página. Em worker o CDP responde *"Operation is only supported for pages, not workers"* (`probe-worker-throttle.mjs`), e o processo do Chromium que grava o IndexedDB também não é desacelerado. Resultado: com CDP, o SQLite-WASM (que roda num worker) aparece **sem** lentidão nenhuma, o que o favorece injustamente.
2. **Job Object do Windows** (`lib/job-throttle.ps1`): o navegador inteiro (página, workers e o processo do IndexedDB) roda preso a 2 núcleos lógicos com teto de CPU de 1/4 ("job 4x") ou 1/6 ("job 6x") desses núcleos. É o método que vale para comparar as tecnologias. O teto do Windows age em rajadas, então a lentidão efetiva variou entre 1,5x e 9x no laço de calibração de cada rodada (está no JSON). Disco lento (eMMC de tablet barato) **não** é simulado em nenhum dos dois.

## 3. As quatro formas medidas

| Forma | Como guarda os 50 mil itens | Busca por nome |
|---|---|---|
| `idb` | IndexedDB (Dexie), um registro por item, índice por código (`*barcodes`) e índice por palavra (`*words`) | índice de palavra, varredura com cursor ou memória |
| `idb-lean` | igual, sem o índice por palavra | memória |
| `idb-chunks` | IndexedDB (Dexie) com o catálogo em **50 blocos de ~1.000 itens**; ao abrir, monta `Map` por código e por id na memória. Venda, carrinho e fila: um registro por linha, como nas outras | memória |
| `sqlite` | SQLite-WASM em OPFS (`opfs-sahpool`) num worker; tabela `items`, `barcodes` e FTS5 com trigramas | `LIKE`, varredura ou FTS5 |

Em todas, o carrinho é gravado a cada item, e "concluir venda" grava venda, evento na fila e contador numa transação só. "Sem fsync" = IndexedDB `durability: relaxed` / SQLite `synchronous=NORMAL`; "com fsync" = `strict` / `FULL`.

## 4. Medições (CA 1, 2 e 3)

Valores p50 / p95. Catálogo de 50 mil itens (foto de 6,3 MB em JSON, 1,1 MB com gzip); delta de 500 mudanças (450 alterações e 50 removidos; 72 KB em JSON, 13 KB com gzip). 100 vendas de 3 itens por modo de gravação.

### 4.1 Máquina normal

| Medida | `idb` | `idb-lean` | `idb-chunks` | `sqlite` |
|---|---|---|---|---|
| Abrir o armazenamento (frio) | 0,33 s | 0,38 s | 0,58 s | **1,6 s** (wasm 0,54 + pool OPFS 0,51) |
| Carga inicial (foto de 50 mil) | **31,3 s** | **16,5 s** | **0,38 s** | 5,7 s (FTS5: 0,59 s) |
| Código de barras exato | 0,7 / 1,9 ms | 0,5 / 1,2 ms | 0,0 / 0,0 ms | 0,4 / 7,5 ms |
| Nome, começo de palavra ("arro cami") | índice: 10 / 218 ms; memória: 5,5 / 12 ms | memória: 6,3 / 20 ms | memória: 0,3 / 5,5 ms | FTS5: 1,7 / 2,5 ms; `LIKE`: 3,9 / 40 ms |
| Nome, trecho no meio ("rroz") | varredura: 66 / 179 ms; memória: 4,5 / 10 ms | memória: 3,8 / 6,5 ms | memória: 0,2 / 0,5 ms | FTS5: 1,6 / 2,4 ms |
| Item na venda (busca + carrinho), sem fsync | 1,8 / 4,7 ms | 2,7 / 19 ms | 1,0 / 2,2 ms | 1,1 / 2,4 ms |
| Concluir venda, sem fsync | 4,6 / 9,3 ms | 6,2 / 19 ms | 3,8 / 9,2 ms | 1,9 / 3,2 ms |
| Item na venda, com fsync | 15 / 39 ms | 15 / 48 ms | 14 / 27 ms | 14 / 27 ms |
| Concluir venda, com fsync | 16 / 39 ms | 18 / 53 ms | 18 / 42 ms | 18 / 38 ms |
| Delta de 500 (baixar e aplicar) | 0,55 s | 0,52 s | 0,29 s | **2,4 s** |
| Reabrir o caixa até a 1ª busca | 4 ms + 1,4 s do índice em memória | 1 ms + 0,53 s | **0,21 s** (já com tudo em memória) | 0,63 s |
| Tamanho em disco | 14,7 MB | 8,3 MB | **6,3 MB** | 16,1 MB (21,1 MB no `estimate()`) |
| `navigator.storage.persist()` | negado | negado | negado | negado |

### 4.2 Máquina fraca simulada (Job Object, todos os processos do navegador)

| Medida | `idb` 4x / 6x | `idb-lean` 4x / 6x | `idb-chunks` 4x / 6x | `sqlite` 4x / 6x |
|---|---|---|---|---|
| Abrir o armazenamento (frio) | 1,5 / 2,3 s | 1,2 / 1,9 s | 0,84 / 1,6 s | **7,5 / 7,3 s** |
| Carga inicial (foto) | **98 / 133 s** | **40 / 78 s** | **0,41 / 1,9 s** | 9,2 / 12,8 s |
| Código de barras exato | 1,1 / 88 ms; 0,7 / 1,9 ms | 0,7 / 2,7 ms; 1,9 / 5,3 ms | 0,0 / 0,0 ms (as duas) | 0,4 / 10 ms; 0,7 / 13 ms |
| Nome (memória ou FTS5), pior p95 das duas buscas | 134 / 407 ms | 132 / 450 ms | 7,1 / 9,1 ms | 8,1 / 3,9 ms |
| Item na venda, com fsync (p95) | 119 / 96 ms | 41 / 132 ms | 77 / 47 ms | 34 / 32 ms |
| Concluir venda, com fsync (p95) | 166 / 171 ms | 43 / 185 ms | 75 / 115 ms | 46 / 38 ms |
| Delta de 500 | 7,0 / 1,6 s | 0,18 / 1,1 s | 0,54 / 0,92 s | 2,5 / 3,2 s |
| Reabrir até a 1ª busca (com o índice em memória pronto) | 1,8 / 4,8 s | 1,9 / 2,8 s | **0,44 / 0,63 s** | 1,5 / 2,5 s |

### 4.3 Throttling do CDP (pedido original; só a thread da página)

Rodada de 17/11 (`results/bench-cdp2.json`), **com a máquina carregada por outras sessões**: o fsync dobrou e o SQLite levou 17 s na carga que antes levou 5,7 s. Vale para ver o efeito da CPU na thread da página, não para comparar com as tabelas acima. A calibração confirma: o laço na página ficou 7x a 9x mais lento; no worker, igual (0,21 a 0,28 s em todas as taxas).

| Medida | `idb-chunks` 1x / 4x / 6x | `idb-lean` 1x / 4x / 6x | `sqlite` 1x / 4x / 6x (worker sem throttling) |
|---|---|---|---|
| Carga inicial (foto) | 0,42 / 1,7 / 2,0 s | 39 / 41 / 38 s | 17 / 18 / 17 s |
| Código de barras (p95) | 0,0 / 0,2 / 0,0 ms | 0,4 / 2,6 / 11 ms | 7,0 / 8,4 / 3,0 ms |
| Nome, pior p95 (memória ou FTS5) | 3,1 / 22 / 28 ms | 6,0 / 53 / 52 ms | 1,0 / 2,9 / 9,5 ms |
| Item na venda com fsync (p95) | 23 / 35 / 105 ms | 2,7 s (pico de disco) / 54 / 60 ms | 29 / 37 / 44 ms |
| Delta de 500 | 6,1 / 6,1 / 6,2 s | 0,41 / 0,62 / 0,70 s | 28 / 30 / 28 s |
| Reabrir até a 1ª busca, com índice em memória | 0,12 / 1,1 / 1,7 s | 0,40 / 1,3 / 1,6 s | 0,29 / 1,1 / 0,25 s |
| Heap JS da página com o catálogo | 22 / 19 / 19 MB | 22 / 22 / 19 MB | 3 MB (o worker não entra na conta) |

O delta do `idb-chunks` subiu de 0,3 s para 6 s nesta rodada: as 500 mudanças caem em todos os 50 blocos, então o delta regrava o catálogo inteiro (6 MB com fsync), e o disco estava disputado. Ver riscos.

### 4.4 Leitura dos números

- **A meta do PRD (item em menos de 100 ms)** é atendida por todas as formas na máquina normal. Na simulação fraca, só `idb-chunks` e `sqlite` ficam folgados em todos os p95; `idb` e `idb-lean` passam de 100 ms na busca por nome e na gravação com fsync.
- **O que decide é a carga e a reabertura, não a busca.** Um registro por item no IndexedDB custa de 16 a 133 s para gravar 50 mil itens; em blocos, menos de 2 s. O SQLite fica no meio na carga, mas paga de 1,5 a 7,5 s para abrir, toda vez que o caixa recarrega: é o `installOpfsSAHPoolVfs` abrindo os `SyncAccessHandle` do OPFS. A sonda crua (`public/opfs-probe-worker.js`) mostrou de 85 a 170 ms **por arquivo** para abrir um handle nesta máquina Windows.
- **O fsync custa o mesmo nas duas tecnologias** (14 a 18 ms por gravação na máquina normal). É o disco, não a biblioteca.
- **Busca por trecho no meio do nome** só fica rápida com memória (todas as IDB) ou com FTS5 (SQLite). Varrer o IndexedDB com cursor passa de 170 ms já na máquina normal.
- **Memória:** com os 50 mil itens em memória, o heap JS da página ficou em 19 a 22 MB (`idb-chunks` e `idb-lean`), contra 3 MB da página no SQLite (cujo worker não foi medido). Cabe com folga num tablet de 2 GB, mas precisa ser confirmado no aparelho.
- **`persist()` foi negado em todas as rodadas** (Chromium headless, sem instalar o PWA). Ver CA 3.

## 5. Robustez (CA 4 e 5)

`run-crash.mjs`: o caixa simulado vende sem parar (um item a cada 25 ms, carrinho gravado a cada item) e sincroniza com 30% de respostas perdidas. Em momento aleatório (1,5 a 4 s), o roteiro **mata o navegador inteiro** (`taskkill /F /T` no processo principal do Chromium, que leva junto o processo de armazenamento) ou **fecha a aba** sem `beforeunload`. Depois reabre e confere, contra o que a tela já tinha confirmado antes da queda:

- nenhuma venda concluída sumiu (`seq` local ≥ último `seq` confirmado);
- a fila tem `seq` 1..N sem buraco e sem repetição, e o número de vendas bate com ela;
- o carrinho da venda em andamento voltou com pelo menos os itens já confirmados (ou a venda já tinha sido concluída);
- no fim, a fila esvazia e o servidor tem exatamente N eventos daquele caixa, cadeia de hash íntegra.

| Variante | Navegador morto | Aba fechada | Perda ou duplicação | Reabrir depois do kill (página nova até o caixa pronto) | Reabrir depois de fechar a aba |
|---|---|---|---|---|---|
| `idb-chunks`, `durability: strict` | 5 de 5 ok | 3 de 3 ok | nenhuma (55 vendas, servidor = 55) | 2,9 a 18 s | 0,3 a 1,3 s |
| `idb-chunks`, `durability: relaxed` | 5 de 5 ok | 3 de 3 ok | nenhuma (190 vendas, servidor = 190) | 2,2 a 27 s | 0,3 a 0,5 s |
| `sqlite`, `synchronous=FULL` | 5 de 5 ok | 3 de 3 ok | nenhuma (53 vendas, servidor = 53) | 2,5 a 9,8 s | 0,7 a 1,3 s |

No servidor, 136 reenvios foram descartados e 298 eventos aceitos, sem quebra de cadeia. Uma rodada anterior, com o Chromium lançado à mão, também passou 5 de 5 quedas e 3 de 3 abas fechadas.

**Web Locks (CA 5):** nas três variantes, a segunda aba do mesmo caixa ficou sem escrever e mostrou "O caixa já está aberto em outra janela."; quando a primeira fechou, a segunda assumiu em 17 a 177 ms.

**Achados:**

1. **O IndexedDB pode falhar ao abrir logo depois de o navegador morrer.** Numa das 15 quedas, a primeira abertura deu `UnknownError: Internal error opening backing store for indexedDB.open`; recarregar a página resolveu em ~2 s, sem perda. O app tem de tentar de novo antes de mostrar erro, e nunca apagar o banco por causa disso.
2. **Com o Chromium lançado sem os argumentos do Playwright**, a gravação inicial do catálogo falhou algumas vezes com `QuotaExceededError` e não se recuperou recarregando a página. Não se repetiu com o Chromium lançado pelo Playwright. Não deu para isolar a causa no prazo; fica registrado como risco a conferir no aparelho real.
3. **O Chromium restaura a aba do caixa depois de uma queda** e ela pega o Web Lock e volta a vender. É o comportamento que queremos; o roteiro só fecha a aba restaurada para abrir a sua página de verificação.
4. "Reabrir depois do kill" inclui subir o Chromium de novo, que se recupera da queda; por isso a faixa é larga e não serve para comparar tecnologias.

## 6. Lote com 20 caixas (CA 6)

`run-sync.mjs`: 20 caixas simulados no mesmo navegador (cada um com o seu banco IndexedDB), 200 vendas cada, lote de até 25 eventos, **30% das respostas descartadas pelo cliente depois de o servidor gravar** (o cliente reenvia o mesmo lote) e **5% de falhas do servidor antes de gravar**.

| Medida | Valor |
|---|---|
| Vendas esperadas / gravadas no servidor / números de venda distintos | 4.000 / 4.000 / 4.000 |
| Reenvios descartados pelo servidor (`ON CONFLICT DO NOTHING`) | 1.819 eventos |
| Lotes enviados / respostas descartadas / falhas do servidor | 5.729 / 1.715 / 251 |
| Cadeia de hash quebrada | 0 |
| `seq` contíguo 1..200 em cada caixa, no servidor e no aparelho | sim |
| Tempo do servidor por lote (p50 / p95 / máx) | 9,9 / 28 / 257 ms |
| Ida e volta no cliente, pior p95 entre os caixas | 259 ms |
| Ocupação do banco | 2,5 MB para 4.000 vendas (~630 bytes por venda, evento + projeção) |
| Duração | 294 s (as vendas eram geradas ao mesmo tempo, com fsync por item) |

**Protocolo do rascunho A confirmado**: chave `event_id` + único `(device_id, epoch, seq)`, `ON CONFLICT DO NOTHING`, resposta com `ack_seq` contíguo, cadeia de hash. Nenhuma duplicação nem perda com 30% de reenvio. Dois ajustes:

1. O cliente deve **avançar a fila só pelo `ack_seq` contíguo** que o servidor devolve, não pela lista de eventos aceitos. Foi o que evitou buraco quando um lote falhou e o seguinte passou.
2. Os lotes ficaram pequenos (média de 1,3 evento) porque o caixa sincroniza logo depois de cada venda. Isso é bom para a fila ficar curta, mas multiplica as transações no servidor; o servidor real deve aceitar lote e fazer **um commit por lote**, como aqui.

## 7. Baixa de estoque: por venda ou agregada (CA 7)

`run-stock.mjs`: as mesmas 4.000 vendas (20 caixas × 200, 3 linhas cada, 12.000 linhas, 2.000 itens com venda concentrada em poucos itens) espalhadas num dia de loja de 8 h. Cada unidade de baixa é uma transação: numera o movimento numa linha única (`MOV`), grava o movimento e atualiza o saldo do item. Banco: `node:sqlite` com fsync; em PostgreSQL, cada gravação na mesma linha é uma espera de lock.

| Forma | Transações | Movimentos (= gravações na linha `MOV`) | Gravações na linha do item mais vendido | Tempo total | Total baixado |
|---|---|---|---|---|---|
| Por venda | 4.000 | 12.000 | 994 | 144 s | 24.006 |
| Agregada por item, caixa e janela de 5 min | 1.920 | 11.734 | 806 | 61 s | 24.006 |
| Agregada por item e janela de 5 min (loja inteira) | 96 | 9.988 | 96 | 6,3 s | 24.006 |

O tempo é quase todo fsync por transação (a máquina estava com outras cargas; a primeira rodada, também com carga, deu 43 / 29 / 1,1 s). Leitura:

- **Agregar por item, caixa e janela quase não ajuda**: um caixa faz uma venda a cada ~2,4 min, então a janela de 5 min raramente junta o mesmo item duas vezes no mesmo caixa.
- **Agregar por loja e janela** corta as transações em 42 vezes e as gravações na linha do item mais vendido em 10 vezes, mas a baixa deixa de apontar para a venda (o estorno "pelo documento de origem" da PDV-05 fica mais difícil).
- Em volume absoluto, 12.000 movimentos por dia por loja é pouco para o PostgreSQL. A disputa só pesa com muitas lojas no mesmo banco.

## 8. Decisão

| Pergunta (CA 8) | Resposta |
|---|---|
| (a) IndexedDB ou SQLite-WASM | **IndexedDB com Dexie**, atrás da porta `LocalStore`. O catálogo vai **em blocos** (~1.000 itens por registro) e é indexado em memória (`Map` por código, lista de nomes para trecho). Venda, carrinho, fila e metadados ficam um registro por linha, com `durability: 'strict'` em tudo que é dinheiro (carrinho, venda concluída, fila) |
| (b) Protocolo de sincronização | **Confirmado**, com os dois ajustes da seção 6 |
| (c) Baixa agregada ou por venda no MVP | **Por venda**, fora do caminho do caixa (como já diz a PDV-05), com **um commit por lote de sincronização** e não por venda. A agregação por item, caixa e janela proposta não reduz quase nada; se o volume pedir no futuro, agregar por item e janela na loja inteira, guardando a lista de vendas de origem |
| (d) Estimativa de OFF-01 a OFF-03 | Ver seção 10 |

**Por que não SQLite-WASM:**

- Abrir custa de 1,5 a 7,5 s em cada recarga do caixa no Windows (wasm de 860 KB mais os handles do OPFS), contra 0,2 a 0,6 s do `idb-chunks` já com o catálogo em memória.
- Não ganha na busca que importa: com o catálogo em memória, o código de barras sai em menos de 0,1 ms e o nome em menos de 10 ms (p95) mesmo na simulação 6x. O FTS5 só seria vantagem se o catálogo não coubesse na memória.
- O delta é 4 a 8 vezes mais lento (mantém o FTS5 em dia).
- Exige worker, WAL só com `locking_mode=exclusive` (no `opfs-sahpool` não há memória compartilhada) e um modo de journal que **só pega depois de o arquivo ter esquema** (achado do spike: o `PRAGMA journal_mode` dado antes do `CREATE TABLE` é ignorado em silêncio). Mais peças para dar errado num caixa.

**Por que não IndexedDB com um registro por item (o desenho original do rascunho A):** 16 a 133 s para gravar a foto de 50 mil itens é inaceitável na primeira abertura de um aparelho fraco, e a busca por nome com índice do IndexedDB passa de 200 ms.

## 9. Riscos

| Risco | O que foi visto | Mitigação |
|---|---|---|
| Catálogo inteiro na memória | 19 a 22 MB de heap para 50 mil itens | Medir no tablet real (SPK-01); se o catálogo de algum cliente passar de ~200 mil itens, rever (aí o FTS5 do SQLite volta a ser candidato, atrás da mesma porta `LocalStore`) |
| `persist()` negado | Negado em todas as rodadas (headless, sem PWA instalado). No Chrome, sem persistência o navegador pode apagar os dados **sob pressão de disco** (não por tempo) | Instalar o PWA (o Chrome concede `persist()` a app instalado, a validar no SPK-01), aviso do OFF-01 CA 5, fila curta (sincroniza logo) e contador de vendas não enviadas |
| Falha ao abrir o IndexedDB logo depois de o navegador morrer | Uma vez em 15 quedas (`UnknownError … backing store`), resolvida recarregando. Com o Chromium lançado à mão, `QuotaExceededError` na gravação inicial, sem recuperação; não se repetiu com o Chromium do Playwright | O app deve tratar erro na abertura do armazenamento: tentar de novo e só então mostrar erro; nunca descartar o banco |
| fsync lento em eMMC | 14 a 18 ms por gravação em SSD NVMe; em eMMC barato pode ser bem mais | Medir no aparelho (SPK-01). Se passar do orçamento de 100 ms, gravar o carrinho com `relaxed` e só a venda concluída com `strict` (o carrinho perdido num corte de energia é refeito pelo operador; a venda concluída não pode sumir) |
| Delta regrava blocos inteiros | 500 mudanças espalhadas tocam os 50 blocos: o delta regrava o catálogo todo (0,3 s com disco livre, 6 s com disco disputado). Não trava a venda, mas gasta disco | Gravar o delta como registros à parte (sobreposição sobre os blocos) e compactar na troca de turno; ou blocos menores. Decidir no OFF-01 |
| Várias abas do mesmo caixa | Web Locks funcionou: a segunda aba vê "O caixa já está aberto em outra janela." e assume em ~0,1 s quando a primeira fecha | Já está no OFF-01 CA 6 |
| Corte de energia | Não simulado (ver seção 11) | SPK-01 |

## 10. O que muda no rascunho A e na estimativa

**Rascunho A, item 4 (armazenamento):** troca "IndexedDB (Dexie) … SQLite-WASM em OPFS fica como alternativa" por: IndexedDB (Dexie) atrás da porta `LocalStore`; catálogo em blocos com índice em memória; venda, carrinho e fila um registro por linha com `durability: 'strict'`; SQLite-WASM descartado para o MVP (abrir lento no Windows; volta a ser candidato só se o catálogo não couber na memória). Item 6 (subida): o cliente avança pela confirmação contígua (`ack_seq`); um commit por lote.

**Estimativa (Fase 11, OFF-01 a OFF-03):**

- **OFF-01 (G) continua G, sem risco extra de armazenamento.** O núcleo que o spike construiu (porta `LocalStore`, foto e delta, carrinho persistido, Web Locks, fila com hash) cabe em ~600 linhas de TypeScript; o que sobra da story é app `/pdv`, service worker, faixa de estado e o lado do servidor (modelo de leitura e endpoints). O ponto em aberto "se o SPK-03 mostrar busca lenta, entra o SQLite-WASM e a story é reestimada" fica **resolvido sem reestimar**.
- **OFF-02 (G) continua G.** O protocolo foi exercitado ponta a ponta com 20 caixas; o trabalho é o servidor real (`pos.device_events`, projeção pelo `ISaleIngestion`, tela de Pendências), não o protocolo.
- **OFF-03 (G)**: o spike não mediu PIN nem turno sem rede (fora do escopo); fica como estava.
- **A divergência do backlog (RP-23):** o "30% a 40% a mais no núcleo" da arquitetura é coerente com o que se viu: o custo do local-first está no servidor (ingestão idempotente, pendências) e no tratamento de erro do armazenamento, não no armazenamento em si. A "segunda etapa de 5 a 8 semanas-pessoa" **não** é tirada por este spike, porque ela é quase toda OFF-03 (PIN, turno e supervisor sem rede), que não foi medido. Sugestão: manter a faixa de construção da Fase 11 (3,5 a 7 semanas) e subir a confiança de "média-baixa" para "média" no que toca ao offline; a parte de impressão continua dependendo do SPK-01.

## 11. O que NÃO foi verificado

- **Aparelho real.** Nenhum tablet Android nem PC antigo foi usado; tudo é simulação num notebook com SSD NVMe (fica para o SPK-01, com o kit de hardware da PP-19).
- **Corte de energia.** `taskkill /F` mata o navegador, mas o que já foi entregue ao sistema operacional sobrevive. Só um corte real prova o `fsync` (`durability: 'strict'`).
- **Android, Edge e Chrome instalado como PWA.** O `persist()` só foi testado no Chromium headless.
- **Disco lento.** Nenhum dos dois métodos de simulação desacelera o disco.
- **PostgreSQL.** O servidor do spike usa SQLite; a disputa por lock da seção 7 é contada em gravações na mesma linha, não medida em espera de lock.
- **Uso de memória do worker do SQLite** (o heap medido é só o da página).
