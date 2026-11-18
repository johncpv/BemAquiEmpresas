# 05 — Backlog do PDV e dos perfis de negócio (proposta)

Formato do `docs/05-backlog.md`: `ID [ ] Título (tamanho)` + requisitos, dependências e critérios de aceite (CA) resumidos.
Tamanho relativo: P (até 1 sessão de trabalho), M (2 a 3), G (4 ou mais). **Épico** = maior que G: é quebrado em stories quando a fase for aberta.
A linha `Paridade:` cita os requisitos mínimos do [benchmark](00-benchmark-mercado.md#4-requisitos-mínimos-para-não-ficar-abaixo-do-mercado) (1 a 38) que a story cobre.
Requisito de origem de todas: pedido do PO de 15/11/2026 ([README](README.md#2-o-que-o-po-pediu)).

As Fases 10 e 11 e as quatro primeiras stories do e-commerce têm arquivo em [`stories/`](stories/README.md). O resto fica aqui com CA curto e ganha story detalhada quando a fase for aberta.

## Regra do go-live

**Revista em 16/11/2026 (John, PP-41):** a construção começa já, **na `main`**, sem branch longa: uma story por PR, com CI e revisão, como no resto do repositório. Os módulos novos entram **desligados por tenant** (a Konebras não vê nada), o banco muda só por acréscimo (esquemas novos) e toda migração passa pelo dump antes do deploy. **Correção do go-live da Konebras tem prioridade** sobre qualquer story do PDV e não espera nada do PDV.

_Regra anterior (15/11/2026), mantida para histórico:_ a construção começava depois do fechamento do go-live da Konebras.

| O que | Pode começar antes do go-live? | Base |
|---|---|---|
| Trabalho no **EmissorFiscal** (NFCE-00, SPK-02, as correções das lacunas 25 e 5, a dívida E14) | **Sim.** Exceção aprovada pelo PO em 15/11/2026, só para o Emissor, em outra frente de trabalho | PP-41 |
| Preparação sem código: perguntas à contadora, compra do kit de hardware, contato com adquirentes, escolha do lojista piloto | Sim: não é construção | PP-19, PP-18, PP-35 |
| SPK-01, SPK-03 e todas as stories deste repositório | **Não**, salvo nova exceção do PO | PP-41 |

## Visão das fases

| Fase | Nome | Stories | Marco |
|---|---|---|---|
| 10 | Fundação: perfil de negócio, catálogo, pagamentos, núcleo de vendas e spikes | 13 | Parte do MVP |
| 11 | PDV balcão com cupom não fiscal e offline | 15 | **MVP (piloto com lojista)** |
| 12 | CPD (retaguarda), multiloja e Pix no caixa | 17 | |
| 13 | NFC-e | 10 | **Versão pública** |
| 14 | E-commerce | 7 | |
| 15 | Alimentação: lanchonete, hamburgueria, trailer e restaurante | 17 | |
| 16 | Supermercado, padaria e pagamentos integrados | 9 | |
| 17 | Oficina e prestação de serviços | 5 | |
| | **Total** | **93** | |

Os termos "piloto interno", "MVP" e "versão pública" estão definidos no [README](README.md#marcos).

Épicos (prefixo do id):

| Prefixo | Tema | Stories |
|---|---|---|
| SPK | Spikes | SPK-01 a SPK-03 |
| PERF | Perfil de negócio | PERF-01 a PERF-04 |
| CAT | Catálogo e preços | CAT-01 a CAT-07 |
| PAG | Pagamentos | PAG-01 a PAG-07 |
| VND | Núcleo de vendas (sem caixa) | VND-01 |
| PDV | Venda no balcão e retaguarda de vendas | PDV-01 a PDV-15 |
| CX | Caixa | CX-01 a CX-03 |
| IMP | Impressão, periféricos e ponte | IMP-01 a IMP-06 |
| OFF | Offline e sincronização | OFF-01 a OFF-03 |
| CPD | CPD: a retaguarda do varejo | CPD-01 a CPD-06 |
| NFCE | NFC-e | NFCE-00 a NFCE-08 |
| ECM | E-commerce | ECM-01 a ECM-07 |
| KDS | Cozinha e painel | KDS-01 a KDS-03 |
| MESA | Mesas e comandas | MESA-01 a MESA-05 |
| DLV | Delivery | DLV-01 a DLV-05 |
| FID | Fidelidade | FID-01, FID-02 |
| OFI | Oficina | OFI-01 a OFI-05 |
| INFRA | Hospedagem e backup | INFRA-01 (condicionada), INFRA-02 |

**CPD** aqui é o **Centro de Processamento de Dados no sentido do varejo: a retaguarda**, onde se faz o controle geral (lançar as notas de entrada, precificar, cadastrar, acompanhar os caixas). Não é centro de distribuição (definição do PO de 15/11/2026). O depósito central que abastece lojas é capacidade de multiloja (CPD-06).

## Dependências e paralelismo

```
Go-live da Konebras + decisões do PO (seção 2 do 06)
                │
Fase 10 ─┬─ SPK-01, SPK-03 (em paralelo)        SPK-02 (no Emissor; pode começar antes, PP-41)
         ├─ PERF-01 → PERF-02 → PERF-03
         ├─ CAT-01 → CAT-02      PAG-01 ──► VND-01 (núcleo de vendas) ──► PERF-04
         └─ INFRA-02 (backup mais frequente; antes do piloto)      INFRA-01 (migração: só se um gatilho da PP-07 disparar)
                │
                ├─► Fase 11 (MVP) ─┬─► Fase 12 (CPD, multiloja, Pix)
                │                  ├─► Fase 13 (NFC-e) ◄── NFCE-00 no EmissorFiscal
                │                  ├─► Fase 15 (alimentação) ─► Fase 16 (supermercado, padaria, integrados)
                │                  └─► Fase 17 (oficina)
                │
                └─► Fase 14 (e-commerce) ◄── VND-01 + EmissorFiscal: dívida E14 e lacuna 25
```

**Do que o e-commerce depende** (corrigido pela revisão de 15/11/2026; a versão anterior dizia "só da Fase 10" e "não depende do Emissor", e as duas afirmações estavam erradas):

| Dependência | Por quê |
|---|---|
| **VND-01**, o núcleo de vendas sem caixa (Fase 10) | O pedido pago vira venda, baixa o estoque e gera o recebível. Antes da revisão isso só nascia na Fase 11, preso à sessão de caixa |
| CAT-01 e PAG-01 (Fase 10) | Produto, preço, forma de pagamento e taxa |
| **Dívida E14 do EmissorFiscal** | Bloqueia o segundo cliente do Emissor, seja ele de balcão ou de loja virtual |
| **Lacuna 25 do EmissorFiscal** (PIS/COFINS com CST 49 e 99) | Sem a correção, a NF-e modelo 55 de um cliente do Simples reprova. Loja virtual pequena costuma ser do Simples |
| PP-21 | Gateway de cartão; hub de marketplaces e plataformas |

Não depende: do app `/pdv`, do caixa, do offline nem da NFC-e (emite NF-e modelo 55, que já existe).

**Ordem entre balcão e e-commerce: decidida pelo PO em 15/11/2026.** O primeiro cliente é de **balcão**, então a trilha do balcão (Fases 10 → 11 → 12 e 13) vem antes. O e-commerce é a trilha seguinte. O núcleo de vendas fica na Fase 10 e as stories ECM-01 a ECM-04 já estão detalhadas, para a trilha poder abrir sem replanejar. Quando abrir, há duas entradas possíveis, e a escolha depende de o cliente já ter loja (PP-16):

| Entrada | Conteúdo | Quando faz sentido |
|---|---|---|
| Loja própria | ECM-01 → ECM-02 → ECM-03 → ECM-04 | O cliente não tem loja virtual |
| ECM-07 reduzida | Pedidos de Nuvemshop ou Tray → estoque → NF-e → financeiro, sem vitrine nem checkout | O cliente já vende numa plataforma. É a entrega mais curta: não precisa de gateway nem de host público |

O que pode correr em paralelo:

| Trilha | Conteúdo | Observação |
|---|---|---|
| EmissorFiscal | NFCE-00 e SPK-02 | Outro repositório, outra frente, do próprio PO. Pode começar antes do go-live (PP-41) |
| Spikes | SPK-01 e SPK-03 | Independentes entre si e das stories de produto |
| Infra | INFRA-02 | Independente do código de produto. A INFRA-01 fica condicionada (PP-07) |
| Balcão | Fase 10 → 11 → 12 e 13 → 15 → 16 | Trilha principal |
| E-commerce | Fase 14 → Fase 17 | Trilha seguinte; pode correr ao lado da 12 e da 13 se houver uma segunda frente |
| Dentro da Fase 11 | CX-01 a CX-03 (caixa) ‖ IMP-01 e IMP-02 (impressão) ‖ OFF-02 e OFF-03 (sincronização), depois de PDV-01, OFF-01 e PDV-02 | Três frentes |

"Em paralelo" quer dizer mais de uma sessão de trabalho ao mesmo tempo. O PO revisa tudo; a capacidade dele é o limite real (ver "Esforço").

---

## Fase 10 — Fundação: perfil de negócio, catálogo, pagamentos, núcleo de vendas e spikes

**Ordem de execução:** PERF-01 → PERF-02 → PERF-03 → CAT-01 → CAT-02 → PAG-01 → VND-01 → PERF-04. Em paralelo: SPK-01, SPK-02, SPK-03 e INFRA-02. Condicionada: INFRA-01.

**SPK-01 [ ] Spike: periféricos em hardware real (M)** — em paralelo — [story](stories/SPK-01-perifericos-hardware-real.md)
- Depende de: kit de hardware (PP-19).
- Paridade: prepara 2, 21, 23.
- CA: três impressoras (USB, rede, Bluetooth), gaveta, leitor e balança testados a partir do PWA em Windows e Android; WebUSB e WebSerial comparados com um agente mínimo em loopback; resposta escrita sobre a ponte entrar ou não no MVP.

**SPK-02 [ ] Spike: NFC-e de ponta a ponta em homologação (M)** — em paralelo, no EmissorFiscal — [story](stories/SPK-02-nfce-ponta-a-ponta-homologacao.md)
- Depende de: CSC de homologação do emitente de teste; acesso ao EmissorFiscal.
- Paridade: prepara 2, 3.
- CA: uma NFC-e modelo 65 autorizada em homologação pelo Emissor, com QR Code lido pelo aplicativo da SEFAZ; contingência central (`tpEmis=9`) exercitada; latência medida da VPS atual e de São Paulo; restrição de IP conferida; perguntas QC entregues à contadora.

**SPK-03 [ ] Spike: núcleo local-first sob carga (M)** — em paralelo — [story](stories/SPK-03-nucleo-local-first-carga.md)
- Depende de: —
- Paridade: prepara 1, 22.
- CA: busca em 50 mil itens medida em tablet barato e PC antigo; lote com 20 caixas simulados sem duplicar; queda de energia e aba fechada no meio da venda sem perda; IndexedDB ou SQLite-WASM decidido.
- **Executado em 16 e 17/11/2026, com ressalvas** ([resultado](spikes/SPK-03-resultado.md)): IndexedDB (Dexie) com catálogo em blocos e índice em memória; 20 caixas × 200 vendas com 30% de reenvio sem duplicar nem perder; navegador morto e aba fechada sem perda. **Falta para marcar:** a busca foi medida em máquina fraca **simulada** (CDP e Job Object), não em tablet nem PC antigo reais, e o corte de energia não foi feito; os dois vão com o SPK-01.

**PERF-01 [x] Perfis de negócio no catálogo e liberação efetiva (G)** — ordem 1 — [story](stories/PERF-01-perfis-liberacao-efetiva.md)
- Depende de: PP-03.
- CA: `BusinessProfileCatalog` com 4 perfis e 7 segmentos; `tenant_profiles` com backfill `factory`; liberado = (plano E perfil) OU exceção; módulo fora do perfil vem `hidden` em `/me/permissions` e some do menu e da busca; rota direta responde 403 `module_not_released`; navegação da Konebras idêntica (teste de regressão); ADR dos perfis.

**PERF-02 [x] Provisionar com perfil e adicionar perfil a tenant existente (M)** — ordem 2 — [story](stories/PERF-02-provisionar-com-perfil.md)
- Depende de: PERF-01.
- CA: passos "Perfil de negócio" e "Segmento" no `ProvisionDialog`; aba Perfis no tenant; adicionar é idempotente e não toca em exceção; remover só oculta; provisionamento continua abaixo de 1 min.

**PERF-03 [x] Área "Loja", rótulos de área e vocabulário por perfil (M)** — ordem 3 — [story](stories/PERF-03-area-loja-vocabulario.md)
- Depende de: PERF-01; PP-25.
- CA: área "Loja" no dock; tenant sem `factory` vê "Retaguarda" no lugar de "Fábrica"; dicionário com teto de chaves servido no contexto do tenant; a ajuda respeita o termo; a Konebras não muda.

**CAT-01 [ ] Módulo Catálogo: produto vendável, categoria, variação, códigos de barras e preço (G)** — ordem 4 — [story](stories/CAT-01-produto-vendavel-barras-preco.md)
- Depende de: PERF-01, PERF-03; PP-04.
- Paridade: 22, 25 (tabela padrão), 33.
- CA: projeto `Catalog` nas 5 camadas; `ItemType.Merchandise`; categoria de produto; produto com variações (grade de cor e tamanho), cada uma com item, código de barras e preço próprios; vários GTIN por produto, únicos e com dígito verificador validado; tabela de preço padrão versionada; `ICatalogLookup` por código, PLU e busca em menos de 50 ms com 20 mil produtos (teste de carga); `catalog.edit_price` separada; margem só com `view_cost`.

**CAT-02 [ ] Importação de produtos por planilha (M)** — ordem 5 — [story](stories/CAT-02-importacao-produtos-planilha.md)
- Depende de: CAT-01.
- CA: modelo xlsx para baixar; prévia com erros por linha; cria item + produto + categoria + código de barras + preço + saldo inicial opcional; reexecutável sem duplicar; relatório final.

**PAG-01 [x] Formas de pagamento e adquirentes, com taxa e prazo D+N (M)** — ordem 6 — [story](stories/PAG-01-formas-pagamento-adquirentes.md)
- Depende de: —
- Paridade: 28 (base).
- CA: `finance.payment_methods` e tabela de taxas por adquirente, bandeira, forma e parcelas; adquirente é terceiro; `IPaymentMethods` em `Finance.Contracts`; valores do seed com o chip "A confirmar"; histórico de alteração de taxa.

**VND-01 [ ] Núcleo de vendas sem caixa: venda, pagamento, estoque e financeiro por canal (G)** — ordem 7 — [story](stories/VND-01-nucleo-vendas-sem-caixa.md)
- Depende de: CAT-01, PAG-01; PP-04.
- Paridade: base de 10, 28, 35 e 37.
- CA: módulo `sales` no `ModuleCatalog`; venda com linhas, partes e pagamentos, por canal; `ISaleIngestion` e `IOrderPayments` em `Sales.Contracts`; venda de canal sem caixa (online, OS) não exige sessão de caixa; baixa de estoque por consumidor idempotente; recebível por canal no Financeiro, sem `cash_session_id`; `ISalesRevenue` para o DRE; nenhum dado de cartão guardado.

**PERF-04 [ ] Pacotes de dados iniciais por perfil e segmento e checklist "Primeiros passos" (M)** — ordem 8 — [story](stories/PERF-04-seeds-primeiros-passos.md)
- Depende de: PERF-02, CAT-01, PAG-01.
- CA: seed do Comércio (locais, contas Caixa e Cofre, formas de pagamento, tabela de preço, categorias, grupos, regras do Simples "A confirmar"); idempotente por `seed_key`; checklist no Início com 5 passos e progresso; cada passo leva à tela certa.
- Parcial (17/11/2026, branch `feat/perf-04-seeds-primeiros-passos`): mecanismo de pacotes por perfil, pacotes do Financeiro, Estoque, Acessos e Terceiros, unidades de alimentação, gancho no provisionamento e na aba Perfis e o cartão "Primeiros passos". Falta, com a CAT-01 e a CAT-02: o pacote do Catálogo (tabela "Balcão" e categoria "Geral", e as categorias de alimentação) e o link do passo "Importar produtos"; com a IMP-01, o passo "Impressora"; com a PDV-02, o passo da venda de teste.

**INFRA-02 [ ] Backup mais frequente na VPS atual (P)** — em paralelo, antes do piloto — [story](stories/INFRA-02-backup-frequente-vps-atual.md)
- Depende de: —
- CA: backup dos bancos de tenant em intervalo menor que o diário, cifrado e fora da VPS; uma semana de medição; restauração ensaiada; alerta de falha; perda máxima de dados medida e registrada na PP-07.

**INFRA-01 [ ] Hospedagem no Brasil, VPS dedicada e restauração a um ponto no tempo (M)** — **condicionada**, fora do caminho do MVP — [story](stories/INFRA-01-hospedagem-brasil-pitr.md)
- Depende de: um gatilho da PP-07 disparar (SEFAZ recusando IP estrangeiro; latência medida acima da meta; primeiro cliente de volume; exigência de LGPD); orçamento.
- CA: API, PostgreSQL e EmissorFiscal num datacenter em São Paulo, em VPS só do produto; arquivamento contínuo de WAL; restauração a um ponto no tempo ensaiada; monitor externo; plano de corte sem perda. **Decisão do PO de 15/11/2026: ficar na VPS atual por enquanto.**

---

## Fase 11 — PDV balcão com cupom não fiscal e offline — MARCO MVP

**MVP = Fase 10 (PERF-01 a PERF-04, CAT-01, CAT-02, PAG-01, VND-01, INFRA-02) + Fase 11 inteira.** A INFRA-01 não faz parte do MVP (PP-07). Os spikes SPK-01 e SPK-03 precisam estar concluídos antes do meio da Fase 11, porque decidem armazenamento e ponte.

O MVP entrega **balcão com cupom não fiscal**: é um piloto com lojista parceiro, não a versão pública ([benchmark, 7.6](00-benchmark-mercado.md#76-resultado)).

**Ordem de execução:** PDV-01 → OFF-01 → PDV-02 → PDV-03 → CX-01 → IMP-01 → OFF-02 → CX-02 → CX-03 → PDV-05 → PDV-04 → IMP-02 → OFF-03 → PDV-06 → PDV-07. No fechamento do marco: um E2E por fluxo principal e a rodada completa do CI.

**PDV-01 [ ] Lojas, caixas e aparelho de PDV registrado (G)** — ordem 1 — [story](stories/PDV-01-lojas-caixas-aparelho.md)
- Depende de: PERF-01, CAT-01, PAG-01, VND-01, PERF-04; PP-04, PP-11, PP-12.
- Paridade: 27 (lojas).
- CA: módulo `pos` no `ModuleCatalog`; loja ligada a local de estoque e tabela de preço; caixa ligado a aparelho registrado (aparelho ganha tipo, loja e caixa), a conta Caixa e a série; registro com TOTP recente e aviso ao Proprietário; época do aparelho no número da venda; esquema `Device` restrito a `/api/v1/pos/sync/*` por teste de arquitetura. Re-estimada de M para G pela revisão.

**OFF-01 [ ] Fundação offline: app `/pdv`, armazenamento local e descida do catálogo (G)** — ordem 2 — [story](stories/OFF-01-fundacao-offline.md)
- Depende de: PDV-01, CAT-01, PAG-01, SPK-03; PP-09.
- Paridade: 1.
- CA: app `/pdv` com build e service worker próprios, sem afetar `/fab` nem `/tv`; `LocalStore` com armazenamento persistente; foto e delta do catálogo da loja; a tela abre sem rede; atualização só na troca de turno; faixa "Sem conexão: N vendas a enviar".

**PDV-02 [ ] Tela de venda: toque e teclado/leitor (G)** — ordem 3 — [story](stories/PDV-02-tela-de-venda.md)
- Depende de: OFF-01; PP-26.
- Paridade: 22.
- CA: item por leitor, busca ou grade por categoria em menos de 100 ms; quantidade, remover e observação; venda em espera; cliente opcional e CPF; os dois modos por caixa; atalhos na ajuda; funciona igual sem rede.

**PDV-03 [ ] Pagamento com várias formas e troco, com maquininha externa (M)** — ordem 4 — [story](stories/PDV-03-pagamento-varias-formas.md)
- Depende de: PDV-02, PAG-01, VND-01.
- Paridade: 10 (formas diferentes na mesma venda).
- CA: 1 a n pagamentos por venda; troco só no dinheiro; cartão pede bandeira e, opcionalmente, NSU; nunca número de cartão; `integration = Manual`; Pix conferido pelo operador marcado para conferência no fechamento; porta `PaymentTerminal` com `ManualTerminal`; venda `Completed` só com soma = total.

**CX-01 [ ] Abertura de caixa, sangria e suprimento (M)** — ordem 5 — [story](stories/CX-01-abertura-sangria-suprimento.md)
- Depende de: PDV-01, PDV-02; PP-22.
- Paridade: 5.
- CA: uma sessão aberta por caixa; fundo de troco como transferência do Cofre; sangria e suprimento com motivo e comprovante, sem revelar o esperado; alerta e teto de dinheiro em caixa; sem sessão aberta não se vende.

**IMP-01 [ ] Cupom não fiscal em bobina de 58 e 80 mm e cadastro de impressora (M)** — ordem 6 — [story](stories/IMP-01-cupom-nao-fiscal-impressora.md)
- Depende de: PDV-03.
- Paridade: 2 (cupom não fiscal).
- CA: modo "só cupom não fiscal" ligado pelo Proprietário, com aceite registrado em auditoria; porta `HardwareBridge`; assistente "Instalar impressora" com página de teste; recibo com "Não é documento fiscal"; reimpressão marca "2ª VIA"; relatório de vendas sem documento fiscal; falha na impressora não trava a venda.

**OFF-02 [ ] Sincronização idempotente e Pendências do PDV (G)** — ordem 7 — [story](stories/OFF-02-sincronizacao-pendencias.md)
- Depende de: OFF-01, PDV-03, VND-01, SPK-03.
- Paridade: 1.
- CA: lote de eventos com `event_id`, época, `seq` e cadeia de hash; reenvio não duplica (teste com 50 reenvios em paralelo); venda entra no `Sales` pelo contrato `ISaleIngestion`; venda concluída nunca é recusada; aparelho revogado ainda consegue subir; CPF fora do evento imutável; tela "Pendências do PDV".

**CX-02 [ ] Fechamento com conferência cega, quebra e recolhimento (G)** — ordem 8 — [story](stories/CX-02-fechamento-conferencia-cega.md)
- Depende de: CX-01, PDV-03, IMP-01; PP-22.
- Paridade: 5.
- CA: o operador não vê o esperado; contado por forma; tolerância **por forma**; acima vai para `PendingReview`; recolhimento do dinheiro ao Cofre no fechamento; Pix conferido pelo operador conferido contra o extrato; relatório em PDF e em bobina; reabrir só a última, por pessoa diferente do operador, com aviso ao Proprietário.

**CX-03 [ ] Fechamento de caixa no financeiro e recebíveis de cartão (G)** — ordem 9 — [story](stories/CX-03-fechamento-financeiro-recebiveis.md)
- Depende de: CX-02, PAG-01, VND-01, OFF-02.
- Paridade: 28.
- CA: `CashSessionClosed` gera resumos por forma, adquirente, bandeira e vencimento; título contra a adquirente com bruto, taxa prevista e D+N; recolhimento como transferência Caixa → Cofre; `ISalesRevenue` alimenta DRE e fluxo; um depósito baixa vários títulos; idempotente por sessão; teste de integração com Testcontainers.

**PDV-05 [ ] Baixa de estoque da venda sem bloquear (M)** — ordem 10 — [story](stories/PDV-05-baixa-estoque-sem-bloquear.md)
- Depende de: OFF-02, VND-01, SPK-03; **PP-02**.
- CA: a falta de saldo nunca impede nem recusa a venda do PDV; a baixa acontece fora do caminho do caixa; a falta fica visível na retaguarda com a ação de ajuste; estorno pelo documento de origem; custo gravado na linha para a margem.

**PDV-04 [ ] Desconto, troca de preço, cancelamento e PIN de supervisor (M)** — ordem 11 — [story](stories/PDV-04-desconto-cancelamento-supervisor.md)
- Depende de: PDV-03, CX-01; PP-11, PP-22, PP-38.
- Paridade: 6.
- CA: limite de desconto (por grupo ou por loja, PP-38); acima, "Chamar supervisor"; supervisor diferente do operador; motivo obrigatório; `supervisor_approvals` gravado; aviso ao Proprietário no cancelamento de venda paga; relatório por operador.

**IMP-02 [ ] Impressão direta ESC/POS e gaveta (M)** — ordem 12 — [story](stories/IMP-02-impressao-direta-escpos-gaveta.md)
- Depende de: IMP-01, SPK-01; PP-08.
- CA: gerador ESC/POS único em TypeScript; `WebDeviceBridge` por WebUSB, WebSerial ou Bluetooth onde o navegador permite (corte, gaveta, QR); queda automática para a impressão do navegador; `pos.open_drawer` registrada; lista de impressoras homologadas na ajuda.

**OFF-03 [ ] Caixa e supervisor sem rede (G)** — ordem 13 — [story](stories/OFF-03-caixa-supervisor-sem-rede.md)
- Depende de: OFF-02, CX-02, PDV-04; PP-10, PP-39.
- Paridade: 1, 6.
- CA: abrir turno, sangria, suprimento e fechamento funcionam sem rede e sincronizam; PIN conferido no aparelho com chave de cifra não extraível; limites de valor e de quantidade para aprovação offline; janela máxima medida a partir do último horário do servidor; o que não funciona sem rede aparece desabilitado com explicação. Re-estimada de M para G pela revisão.

**PDV-06 [ ] Devolução, troca e vale-troca (M)** — ordem 14 — [story](stories/PDV-06-devolucao-troca-vale.md)
- Depende de: PDV-05, CX-03, PDV-04.
- CA: localizar a venda por número ou CPF (busca sem CPF na URL); devolução parcial; entrada no estoque ou perda; reembolso em dinheiro ou vale; troca ligada à venda nova; prazo configurável; `pos.return`. Só com rede.

**PDV-07 [ ] Retaguarda de vendas e cards do Início (M)** — ordem 15 — [story](stories/PDV-07-retaguarda-vendas-cards.md)
- Depende de: CX-03, PDV-05, OFF-02.
- Paridade: 8 (parte).
- CA: lista de vendas com filtros e exportação; detalhe com trilha (aprovações, sincronização, estoque); cards "Vendas de hoje", "Caixas abertos" e "Ticket médio"; E2E do fluxo principal no fechamento do marco.

---

## Fase 12 — CPD (retaguarda), multiloja e Pix no caixa

A retaguarda é onde o pessoal faz o controle geral: lança as notas de entrada, precifica, mantém o cadastro, carrega balança e etiquetas, faz inventário e perdas, e acompanha os caixas. O recebimento (Compras) e a nota de entrada (Fiscal → notas de entrada, FIS-04a e FIS-04b) **já existem**; o que é novo é **precificar no mesmo fluxo** e levar o preço aos caixas e às etiquetas.

**CPD-01 [ ] Lançar a nota de entrada e precificar no mesmo fluxo (G)**
- Depende de: CAT-01; FIS-04a e FIS-04b (existentes). Com a CAT-03, vale para as tabelas além da padrão.
- CA:
  1. Dado uma NF-e de compra importada pelo XML, quando abro o recebimento pela nota, então cada linha mostra o produto do catálogo ligado (de-para pelo código do fornecedor ou pelo GTIN) ou a ação "Criar produto", que já traz nome, NCM, unidade e código de barras do XML.
  2. Dado o recebimento confirmado, quando o custo médio do item muda, então a tela seguinte é "Precificar": por linha, custo anterior, custo novo, preço atual, margem atual e **preço sugerido** pela margem ou pelo markup da categoria (ou do produto).
  3. Dado a sugestão, quando confirmo com "Aplicar preços" (uma linha, as marcadas ou todas), então o preço entra na tabela com nova versão, com histórico de quem e quando, e o `PriceChanged` é publicado. Nada muda sem confirmação.
  4. Dado um preço aplicado, quando os caixas sincronizam, então recebem o preço novo em menos de 1 minuto com rede, e a fila de etiquetas ganha as etiquetas de gôndola dos produtos alterados (CAT-04).
  5. Dado quem não tem `catalog.edit_price`, quando conclui o recebimento, então o passo "Precificar" aparece só para consulta, com "Peça a quem define os preços.", e fica a pendência "Notas recebidas sem precificação".
  6. Dado um item cujo preço sugerido fica abaixo do custo novo, ou cuja variação passa do limite da loja, quando reviso, então a linha vem destacada e exige confirmação individual.
  7. Dado a nota já precificada, quando reabro, então vejo o que foi aplicado e posso reprecificar; a entrada de estoque não é refeita.
  8. Teste de integração (Testcontainers): XML → recebimento → custo médio → sugestão → aplicar → versão da tabela e evento; repetir "Aplicar" não duplica.

**CPD-02 [ ] Validade, perdas e quebras (M)**
- Paridade: 26. Depende de: PP-28.
- CA: alerta de vencimento por lote (os lotes com validade já existem); baixa de perda com motivo e aprovação; relatório de perdas.

**CPD-03 [ ] Sugestão de compra por giro e curva ABC (M)**
- CA: giro, cobertura em dias e ABC a partir das vendas; gera solicitação no módulo Compras, que já tem cotação.

**CPD-04 [ ] Inventário rotativo por loja (P)**
- Paridade: 26.
- CA: reusa o inventário cego existente, por local e categoria, sem fechar a loja.

**CPD-05 [ ] Painel da retaguarda: acompanhar caixas e lojas e conferir fechamentos (M)**
- Paridade: 8. Depende de: CX-02, CX-03, OFF-02.
- CA: uma tela com os caixas de todas as lojas (aberto, fechado, em conferência, sem enviar vendas há N horas); fila "Fechamentos a conferir" com esperado, contado e diferença por forma; atalhos para Pendências do PDV, divergências de estoque, notas sem precificação e vendas sem documento fiscal; restrição por loja se a PP-42 for aprovada.

**CPD-06 [ ] Multiloja: depósito central, transferência entre lojas e pedido de reposição (M)**
- Paridade: 27. Depende de: PDV-01; PP-28.
- CA: pedido da loja → separação → em trânsito → recebido com conferência; divergência registrada; sugestão por mínimo e máximo por local. Usa a transferência entre locais que já existe no Estoque.

**CAT-03 [ ] Tabelas de preço por loja e canal, atacado, promoções e remarcação em lote (M)**
- Paridade: 25.
- CA: vigência com `valid_from`; preço de atacado por faixa de quantidade; promoção por regra; margem ou markup padrão por categoria; prévia do impacto na margem; `PriceChanged` chega aos caixas em menos de 1 min com rede.

**CAT-04 [ ] Etiquetas de gôndola e de produto (P)**
- Paridade: 24.
- CA: modelos por tamanho; fila de etiquetas ao mudar preço; reusa a base de etiqueta do Patrimônio.

**IMP-04 [ ] Etiqueta de balança: EAN-13 de peso ou preço (P)**
- Paridade: 23.
- CA: prefixo e formato configuráveis por loja; PLU; o caixa lê a etiqueta e lança peso ou preço; testes com as máscaras comuns.

**IMP-06 [ ] Carga de balança: exportar produtos para Toledo, Filizola e Urano (M)**
- Paridade: 23. Depende de: PP-19.
- CA: arquivo de carga nos formatos dos fabricantes escolhidos; setores de balança; reexportação ao mudar preço.

**PDV-08 [ ] Fiado com limite e venda por vendedor (M)**
- Depende de: PP-26.
- CA: limite por cliente; título na hora; bloqueio com inadimplência (reusa `ICustomerPaymentStats`); vendedor na venda e relatório de comissão. Só com rede.

**PDV-09 [ ] Relatórios de vendas por período, canal e unidade e painel do dono (M)**
- Paridade: 8.
- CA: relatórios com exportação no padrão da ADR 0045; filtros por período, canal, loja, operador e forma; painel do dono no celular com vendas do dia, caixas abertos e alertas.

**PDV-10 [ ] Margem real por venda no fechamento do caixa (M)**
- Superar: item 1. Depende de: PDV-05, CX-03.
- CA: margem por venda e por sessão com custo real de compra, impostos estimados e taxa da adquirente; só com `sales.view_cost`; relatório "vendas abaixo do custo".

**PDV-11 [ ] Modo treinamento do PDV (P)**
- CA: sessão marcada como treinamento, com faixa visível; não gera estoque, financeiro nem fiscal; nunca sincroniza como venda real.

**PDV-15 [ ] Pré-venda: o vendedor monta, o caixa recebe (M)**
- Depende de: PDV-02, PDV-03.
- CA: pré-venda criada num aparelho de vendedor, com número curto ou código de barras; o caixa a localiza, confere e recebe; a pré-venda não baixa estoque nem gera financeiro até ser paga; validade configurável; vendedor gravado na venda. Só com rede entre os aparelhos.

**PAG-02 [ ] Pix QR dinâmico no PDV, por qualquer PSP (M)**
- Paridade: 4. Depende de: PDV-03; adendo à ADR 0032.
- CA: porta `IPixChargeProvider` sem nada específico de banco; primeiro adaptador pelo Inter e a porta pronta para outro PSP; QR na tela do caixa; confirmação por webhook + reconsulta, levada ao caixa por SignalR; tempo limite com a opção "Conferir manualmente"; pagamento nasce `integration = PixApi`. Só com rede. Antecipada da Fase 16 pela revisão.

**IMP-03 [ ] Agente "BemAqui Ponte": impressão, gaveta, balança e base do TEF (G)**
- Depende de: SPK-01; PP-08. **Sobe para a Fase 11 se o SPK-01 mostrar que a impressora USB no Windows não funciona de forma simples pelo navegador.**
- CA: serviço Windows só em loopback, pareado ao aparelho por código de uso único, com conferência de `Origin` e assinatura de código; lista o spooler e varre a porta 9100; impressora de rede da cozinha; cópia do registro de eventos em disco; instalação assistida e atualização própria.

---

## Fase 13 — NFC-e (depende do EmissorFiscal)

**NFCE-00 [ ] (externa) NFC-e no EmissorFiscal (épico)** — repositório e frente do PO; pode começar antes do go-live (PP-41)
- Paridade: 2, 3. Depende de: SPK-02.
- CA: fases 0 a 2 de [07-emissor-fiscal.md](07-emissor-fiscal.md#41-fases): correção das lacunas 25 e 5 (afetam a NF-e de hoje), modelo 65, CSC, QR Code, contrato `NfceRequest`, reconciliação própria sem reenvio, cancelamento com prazo por UF e por substituição, **emissão central em contingência (lacuna 41)**, impressão como dados estruturados; homologação em SP. A dívida E14 resolvida antes do segundo cliente. Cada fase do `07` vira uma ou mais stories no repositório do Emissor.

**NFCE-01 [ ] Configuração fiscal do varejo (M)**
- Depende de: NFCE-00; PP-06.
- CA: regime Simples ou MEI; CSC cadastrado no Emissor e série por caixa; regras por NCM com "A confirmar com a contadora"; item apto (`fiscal_ready`) validado no cadastro, com a lista de campos confirmada pela contadora; `fiscal_environment_allowed` respeitado.

**NFCE-02 [ ] Emissão de NFC-e pela venda (G)**
- Paridade: 2. Depende de: NFCE-01; PP-14; QC-17.
- CA: modo configurável pelo tenant, loja a loja (NFC-e ou só cupom não fiscal); `pos.skip_fiscal`; `InvoiceType.NFCe` e `IssueNfceAsync`; a venda nunca espera a SEFAZ além do teto e segue `Pending`; DANFE NFC-e na bobina a partir dos dados do Emissor; rejeição vai para a fila de correção; pagamentos informados como não integrados; data de emissão conforme a QC-17.

**NFCE-08 [ ] Contingência com a SEFAZ fora do ar: o caixa segue emitindo (M)**
- Paridade: 2. Depende de: NFCE-02; lacuna 41 do Emissor; QC-01.
- CA: quando a loja tem internet e a SEFAZ não responde, o Emissor devolve a NFC-e em contingência e o caixa imprime o DANFE de contingência; a venda fica `AuthorizedInContingency` e passa a `Authorized` quando o Emissor transmite; relatório "NFC-e em contingência a transmitir", com o prazo; rejeição na transmissão vai para a fila de correção; nota normal que ficou sem resposta é cancelada por substituição. **Esta story é a dona do estado `AuthorizedInContingency`.**

**NFCE-03 [ ] Cancelamento e devolução fiscal (M)**
- Depende de: NFCE-02; PP-11.
- CA: cancelamento dentro do prazo com supervisor; fora do prazo, NF-e de devolução; converter cupom não fiscal em NFC-e depois, se a contadora do cliente orientar assim.

**NFCE-04 [ ] Venda sem internet em loja fiscal: servidor de assinatura na loja (épico)** — escopo firme desde 15/11/2026
- Paridade: 2. Depende de: IMP-03 (o agente em Windows); lacunas 13 a 16 e 21 do Emissor (fase 3 de lá); QC-01 e QC-12 por UF; consulta à certificadora. A PP-01 foi decidida pelo PO em 15/11/2026.
- CA: **um servidor de assinatura por loja** (não por caixa) assina `tpEmis=9` com certificado próprio da loja, de chave não exportável, separado do que fica no cofre central; os caixas da loja pedem a assinatura pela rede local; série própria de contingência; DANFE de contingência impresso sem internet; envio ao Emissor e transmissão quando a rede volta; alerta de prazo; inventário de certificados por loja; alerta de uso fora da série; procedimento de revogação escrito. Modo degradado "cupom agora, NFC-e depois" existe, desligado por padrão. Quebra prevista: servidor de assinatura e certificado; série e numeração; transmissão e prazo; rejeição tardia; inventário e alertas.

**NFCE-05 [ ] IBS/CBS na NFC-e e Simples pronto para janeiro de 2027 (M)**
- Paridade: 3. Depende de: QC-04.
- CA: campos de IBS/CBS preenchidos conforme a Nota Técnica vigente; regras por regime com "A confirmar com a contadora". **Se a NFCE-02 for entregue perto de janeiro de 2027, esta story entra junto com ela**, não depois: a data de obrigatoriedade para o Simples precisa estar confirmada antes de ligar.

**NFCE-06 [ ] Tributos aproximados no cupom (P)**
- Depende de: NFCE-02; QC-10.
- CA: valor por item e total (Lei 12.741/2012) na NFC-e e no texto do cupom; fonte da alíquota configurável.

**NFCE-07 [ ] Emitente por loja: filial com CNPJ próprio (M)**
- Depende de: PP-15.
- CA: loja ligada a um estabelecimento com CNPJ e IE próprios; emissão pelo emitente da loja; adendo à ADR 0027 §2; A1 por loja ou da matriz conforme a decisão.

**PDV-14 [ ] NF-e modelo 55 no balcão para comprador com CNPJ (M)**
- Depende de: PDV-03; FIS-03a (emissão de NF-e, existente).
- CA: na venda de balcão, "Emitir NF-e" para cliente cadastrado com CNPJ e IE; usa a emissão de NF-e que já existe, com os dados da venda; a venda fica ligada à nota; não depende da NFC-e. Só com rede.

---

## Fase 14 — E-commerce

Trilha seguinte à do balcão (decisão do PO de 15/11/2026). Dependências na seção "Dependências e paralelismo".

**ECM-01 [ ] Loja virtual: host próprio, vitrine e busca (G)** — [story](stories/ECM-01-loja-virtual-vitrine.md)
- Depende de: CAT-01; adendo à ADR 0009 (rotas anônimas).
- CA: `tenant_domains.kind = Store`; identidade do comprador separada do backoffice (padrão da ADR 0040); páginas públicas com cache; categorias e variações; SEO básico; tema do tenant.

**ECM-02 [ ] Carrinho, checkout e reserva no estoque único (G)** — [story](stories/ECM-02-carrinho-checkout-reserva.md)
- Paridade: 35. Depende de: ECM-01, VND-01.
- CA: reserva com validade no mesmo estoque da loja física; frete por retirada ou faixa de CEP; consentimento e dados mínimos (LGPD).

**ECM-03 [ ] Pagamento online: Pix dinâmico e cartão (G)** — [story](stories/ECM-03-pagamento-online.md)
- Depende de: ECM-02, VND-01, PAG-02 (porta de Pix); PP-21.
- CA: Pix com webhook e reconsulta; cartão só por campos hospedados ou token do gateway, sem número de cartão passar pelo sistema; pedido pago vira venda de canal `Online`, sem sessão de caixa; taxa e prazo no financeiro.

**ECM-04 [ ] Gestão de pedidos, separação, NF-e automática, frete e expedição (M)** — [story](stories/ECM-04-pedidos-nfe-expedicao.md)
- Paridade: 37. Depende de: ECM-03; lacuna 25 e dívida E14 do Emissor.
- CA: estados Pago → Em separação → Faturado → Enviado → Entregue; NF-e 55 pelo fluxo existente; frete e etiqueta de envio; e-mail a cada estado.

**ECM-05 [ ] Cancelamento, devolução e estorno online (M)**
- CA: direito de arrependimento em 7 dias; estorno pelo meio original; NF-e de devolução.

**ECM-06 [ ] Marketplaces: Mercado Livre, Shopee, Amazon e Magalu (épico)**
- Paridade: 35, 36. Depende de: VND-01; PP-21 (direto ou por hub).
- CA: anúncio e estoque sincronizados; pedido do marketplace vira venda de canal próprio; NF-e por pedido. Quebra prevista: uma story G por marketplace, ou uma só se for por hub.

**ECM-07 [ ] Integração com Nuvemshop, Tray e Loja Integrada (épico)**
- Paridade: 38. Depende de: VND-01, CAT-01; lacuna 25 e dívida E14 do Emissor; PP-21.
- CA: produtos, estoque e pedidos sincronizados com a plataforma que o cliente já usa. Quebra prevista: uma story G por plataforma.
- **Versão reduzida, candidata a primeira entrega da trilha** (para cliente que já tem loja): pedido pago na Nuvemshop ou na Tray entra como venda de canal `Online` → baixa o estoque → emite a NF-e 55 → gera o recebível. Sem vitrine, sem checkout, sem gateway. Tamanho estimado: G para a primeira plataforma.

---

## Fase 15 — Alimentação: lanchonete, hamburgueria, trailer e restaurante

**CAT-05 [ ] Complementos, combos, observações, meio a meio e preço por horário (M)**
- Paridade: 16.
- CA: grupos com mínimo e máximo; preço do complemento; combo com troca de item; regra de preço para dois sabores; preço por horário; funciona sem rede.

**CAT-06 [ ] Ficha técnica, baixa de insumos e CMV teórico × real (G)**
- Paridade: 19.
- CA: receita por produto com perda e rendimento; `ConsumeOnSale` explode na venda; custo teórico e margem; relatório CMV teórico × real.

**KDS-01 [ ] Estações de preparo e comanda de produção impressa (M)**
- Paridade: 12. Depende de: IMP-02; **IMP-03 para impressora de rede** (sem a ponte, só impressora ligada ao próprio caixa).
- CA: produto → estação → impressora; impressão por estação com `print_jobs` idempotentes; reimpressão marcada; sem internet, o caixa que lançou imprime direto.

**KDS-02 [ ] KDS: tela da cozinha com prioridade (G)**
- Paridade: 12. Depende de: KDS-01.
- CA: aparelho registrado do tipo KDS; `Queued → InPreparation → Ready`; cores por tempo e prioridade; tempo real pelo `PosHub`, com rebusca de reserva; desfazer por 10 s; aviso de cancelamento; "Sem conexão" explícito.

**KDS-03 [ ] Painel do cliente "Na cozinha / Pedidos prontos" (M)**
- Superar: item 7. Depende de: KDS-01; PP-24.
- CA: reusa `/tv#token` e o `KioskHub` com a visão "pedidos" e escopo por loja; senha ou primeiro nome; som e destaque; some ao entregar ou após N min; sempre escuro.

**MESA-01 [ ] Mesas e mapa do salão (M)**
- Paridade: 9.
- CA: áreas, mesas e estados; mapa em tablet, em tempo real.

**MESA-02 [ ] Comandas e app do garçom (G)**
- Paridade: 9, 11. Depende de: MESA-01; PP-23.
- CA: comanda por mesa, cartão ou nome; lançar por rodada; enviar à cozinha; transferir e juntar com permissão; PWA do garçom por PIN em celular.

**MESA-03 [ ] Pré-conta, taxa de serviço e couvert (M)**
- Paridade: 9. Depende de: QC-05.
- CA: pré-conta impressa sem valor fiscal; taxa opcional, com retirada por `restaurant.waive_service`; couvert por pessoa.

**MESA-04 [ ] Split por pessoa e por item, com formas diferentes (G)**
- Paridade: 10. Depende de: PDV-03, MESA-02; PP-20.
- CA: partes iguais, por valor ou por itens; parte paga é preservada; rateio da taxa; arredondamento na última; NFC-e conforme a PP-20; teste de integração com 4 pagantes e 6 formas.

**MESA-05 [ ] Cardápio digital com QR na mesa (M)**
- Paridade: 13. Depende de: adendo à ADR 0009 (rota anônima).
- CA: QR por mesa abre o cardápio do catálogo; o cliente monta o pedido; o pedido entra na comanda depois da confirmação do garçom ou direto, por configuração.

**DLV-01 [ ] Pedido para viagem, retirada e delivery próprio com taxa de entrega (M)**
- Paridade: 15.
- CA: tipo do pedido (local, viagem, retirada, entrega); taxa por bairro ou raio; endereço e troco para o entregador.

**DLV-02 [ ] Site de delivery sem comissão (M)**
- Paridade: 13. Depende de: **ECM-01** (mesma base da loja virtual; se a Fase 14 não tiver sido aberta, a ECM-01 entra antes desta); adendo à ADR 0009.
- CA: cardápio online no host do tenant; pedido cai no gestor de pedidos e na cozinha; pagamento na entrega ou por Pix.

**DLV-03 [ ] Entregadores: acerto e roteirização (M)**
- Paridade: 15.
- CA: cadastro de entregadores; acerto por entrega ou diária; agrupamento de pedidos por rota.

**DLV-04 [ ] iFood no gestor de pedidos; 99Food e Keeta pelo Open Delivery (épico)**
- Paridade: 14. Depende de: PP-36.
- CA: pedido da plataforma entra no mesmo gestor, vai à cozinha e vira venda de canal próprio; cardápio e disponibilidade sincronizados. Quebra prevista: iFood (G) e Open Delivery (G).

**DLV-05 [ ] Robô de pedidos no WhatsApp (G)**
- Paridade: 18. Depende de: PP-36; ADR 0041.
- CA: o cliente monta o pedido pela conversa; o pedido cai no gestor; consentimento e descadastro conforme a ADR 0041.

**FID-01 [ ] Cupom de desconto (P)**
- Paridade: 17.
- CA: código com vigência, limite de uso e regra; aplicado no PDV e no site; relatório de uso.

**FID-02 [ ] Fidelidade por pontos e cashback (M)**
- Paridade: 17. Depende de: PP-26 (finalidade nova para o CPF).
- CA: acúmulo por venda identificada, com consentimento; resgate como forma de pagamento; saldo como passivo no financeiro; só com rede.

---

## Fase 16 — Supermercado, padaria e pagamentos integrados

**IMP-05 [ ] Balança no caixa e venda por quilo (M)**
- Paridade: 21, 23. Depende de: IMP-03 ou WebSerial; PP-19.
- CA: leitura de peso por serial; protocolos por perfil (Toledo, Filizola; a validar); no self-service por quilo, pesar lança o item na venda ou abre a comanda (quando o módulo Restaurante existir).

**PDV-12 [ ] PDV de alto volume (M)**
- Paridade: 22.
- CA: 200 itens por venda sem degradar; consulta de preço; cancelamento com supervisor por atalho; teste de carga marcado como `LoadTest`.

**PDV-13 [ ] Totem de autoatendimento com pagamento integrado (G)**
- Paridade: 20. Depende de: PAG-02, PAG-03 ou PAG-04.
- CA: modo totem do app `/pdv`; pedido e pagamento sem operador; envio à cozinha e senha no painel.

**PAG-03 [ ] TEF: primeiro provedor (G)**
- Paridade: 4. Depende de: IMP-03; PP-18.
- CA: `TefTerminal` pelo agente; valor enviado à máquina, retorno com NSU e bandeira; estado `Unknown` tratado; estorno; NFC-e como pagamento integrado; queda para o modo manual.

**PAG-04 [ ] SmartPOS Android: PDV na maquininha (épico)**
- Paridade: 7, 11. Depende de: PP-18.
- CA: casca Android fina (Capacitor) com o SDK do fabricante; `SmartPosTerminal`; venda e app do garçom rodam na maquininha. Quebra prevista: a casca e o primeiro fabricante (G); cada fabricante seguinte (M a G).

**PAG-05 [ ] Conciliação de cartões por arquivo ou API da adquirente (M)**
- Paridade: 28. Depende de: CX-03.
- CA: `IAcquirerStatementProvider`; confere taxa prevista × cobrada; aponta divergência; baixa os títulos da adquirente.

**PAG-06 [ ] Link de pagamento Pix ou cartão (M)**
- Paridade: 34. Depende de: PAG-02; PP-21; adendo à ADR 0009 (rota anônima).
- CA: link gerado da venda, da OS ou do orçamento; baixa automática ao pagar.

**PAG-07 [ ] Conciliação por Open Finance (G)**
- Superar: item 8. Depende de: PAG-05.
- CA: extrato bancário e de cartão por Open Finance, ligado ao caixa do PDV. Só com demanda; provedor a escolher.

**CAT-07 [ ] Padaria: encomendas e ordem de produção por receita (M)**
- Paridade: 29. Depende de: CAT-06.
- CA: encomenda com data, sinal e retirada; receita `ProduceToStock` registra a fornada (consome insumos, entra produto); ordem de produção do dia a partir das encomendas.

---

## Fase 17 — Oficina e prestação de serviços

**OFI-01 [ ] OS de serviço: veículo ou equipamento, status e checklist com fotos (G)**
- Paridade: 30, 31. Depende de: VND-01.
- CA: OS com placa, km e modelo; status por etapa; checklist de entrada com fotos visíveis ao cliente.

**OFI-02 [ ] Orçamento da OS e aprovação do cliente por link (M)**
- Paridade: 32. Depende de: adendo à ADR 0009 (rota anônima).
- CA: envio por WhatsApp ou e-mail; aprovação online registrada; a OS só executa o aprovado.

**OFI-03 [ ] Execução: mão de obra por técnico, peças reservadas e baixadas (M)**
- Paridade: 33.
- CA: horas por técnico; peças com código de barras reservadas e baixadas pelo Estoque; comissão em relatório.

**OFI-04 [ ] Fechamento: pagamento pelo PDV, NFS-e + NFC-e ou NF-e, garantia (G)**
- Paridade: 33. Depende de: QC-06.
- CA: pagamento pelo `IOrderPayments`; documento fiscal misto conforme a contadora; garantia reusa `warranty_defaults`.

**OFI-05 [ ] Histórico por veículo, portal do cliente e lembrete de revisão (M)**
- Paridade: 30, 34. Superar: item 3. Depende de: adendo à ADR 0009, se o histórico for aberto por link.
- CA: histórico por veículo ou cliente, visível ao cliente pelo portal; lembrete de revisão por data ou km.

---

## Esforço

**Nenhum número abaixo é medição.** As três análises estimaram em bases diferentes, e a revisão independente apontou que a versão anterior desta seção somava cenários incompatíveis. Esta versão tem uma tabela só, com dois cenários explícitos.

### De onde vêm os números

| Fonte | Base | O que disse |
|---|---|---|
| Produto | Ritmo histórico do repositório (cerca de 150 stories em 3 semanas de calendário, entre 16/10 e 03/11/2026). Vale para **código** | MVP em 4,5 a 7 semanas |
| Arquitetura | Semanas-pessoa, ordem de grandeza | Local-first custa de 30% a 40% a mais no núcleo do PDV. A segunda etapa do offline (abrir turno e PIN sem rede, delta, pendências) soma de 5 a 8 semanas-pessoa |
| Emissor | Um desenvolvedor | 12 a 16 semanas com uma pessoa, 8 a 10 com duas, sem contar o agente de loja nem a espera por credenciamento, CSC e CSRT nas SEFAZ |
| Revisão | Releitura dos tamanhos | PDV-01 e OFF-03 estavam subestimadas (viraram G). A lacuna 41, o núcleo de vendas e as stories novas somam trabalho |

O que **não** comprime com o ritmo do repositório: teste com hardware real, homologação na SEFAZ, credenciamento de TEF, SmartPOS, plataformas de delivery e marketplaces, e piloto em loja.

### Tabela única

Semanas de calendário. "Construção" é código neste repositório. "Externo" é o que espera terceiro ou campo e pode correr ao lado da construção seguinte.

| Fase | Stories | Construção | Externo (hardware, piloto, terceiros) | Confiança |
|---|---|---|---|---|
| 10 — Fundação e spikes | 13 | 2,5 a 4 (sem a INFRA-01, que é condicionada) | 0,5 | Média |
| 11 — PDV, caixa, cupom, offline | 15 | 3,5 a 7 | 1 a 2 (piloto em loja; offline em campo) | Média-baixa: offline e impressão |
| 12 — CPD, multiloja, Pix | 17 | 3,5 a 6 | 0,5 a 1 | Média |
| 13 — NFC-e (neste repositório) | 10 | 3 a 5 (com o servidor de assinatura da NFCE-04, agora escopo firme) | 2 ou mais por UF (homologação) | Baixa |
| 14 — E-commerce | 7 | 4 a 7 (2 a 3 se for só a ECM-07 reduzida) | 1 a 2; credenciamento em marketplace sem prazo | Baixa |
| 15 — Alimentação | 17 | 5 a 8 | 1 a 2; credenciamento em plataformas sem prazo | Baixa |
| 16 — Supermercado e integrados | 9 | 3 a 6 | TEF e SmartPOS podem levar meses | Baixa |
| 17 — Oficina | 5 | 1,5 a 2,5 | 0,5 | Alta |
| **Soma neste repositório** | **93** | **26 a 45,5** | **6,5 a 12 ou mais** | |
| **EmissorFiscal** (outra frente) | 41 lacunas | **12 a 16** com uma pessoa, já contando a fase 3 (contingência offline), que virou escopo firme | Credenciamento, CSC e CSRT por UF | Baixa |

| Marco | Cenário 1: uma frente de construção, em série (mais o Emissor na frente do PO) | Cenário 2: duas frentes de construção (balcão e e-commerce), mais o Emissor |
|---|---|---|
| **MVP** (piloto de balcão, cupom não fiscal): Fases 10 e 11 | 6 a 11 semanas de construção + 1,5 a 2,5 de piloto = **7,5 a 13,5 semanas** depois do go-live | O mesmo: a segunda frente não acelera o balcão |
| **Versão pública** (com NFC-e online): MVP + Fase 13 | **12 a 20 semanas** depois do go-live, e nunca antes de o Emissor terminar (12 a 16 semanas dele, que podem começar antes do go-live) mais a homologação na UF | O mesmo |
| Venda sem internet em loja fiscal (NFCE-04), **escopo firme** | Depois da versão pública: depende da ponte (IMP-03), das lacunas 13 a 16 e 21 do Emissor e do parecer por UF. Sem prazo honesto antes do SPK-01 e do SPK-02 | O mesmo |
| E-commerce no ar | Depois das Fases 12 e 13: **18 a 30 semanas** (com a ECM-07 reduzida, 2 a 4 semanas a menos) | Em paralelo às Fases 12 e 13: **12 a 20 semanas** |
| **Conjunto completo** (93 stories) | 26 a 45,5 de construção + 6,5 a 12 de externo, em parte sobreposto: **8 a 13 meses** | Trilha do balcão com 20,5 a 36 semanas e a outra ao lado: **6 a 10 meses** |

Leituras:
- A versão anterior falava em "produto vendável em 3 a 4 meses" e "conjunto em 6 a 9 meses" sem dizer que isso supunha duas frentes. Com uma frente só, o conjunto leva **8 a 13 meses**.
- O cenário 2 supõe que o PO consegue revisar duas frentes de construção **e** tocar o Emissor ao mesmo tempo. Ele é o dono do Emissor (PP-13). Se a frente do Emissor disputar o tempo dele com a revisão das stories, o caminho crítico volta a ser o Emissor.
- A estimativa de produto para o offline e a de arquitetura (5 a 8 semanas-pessoa só para a segunda etapa) continuam sem fechar. O SPK-03 existe para tirar essa dúvida antes de assumir data (RP-23). **SPK-03 (17/11/2026):** o armazenamento local deixou de ser risco (OFF-01 continua G, sem reestimar) e o protocolo de subida foi confirmado (OFF-02 continua G). A segunda etapa de 5 a 8 semanas-pessoa é quase toda OFF-03 (PIN, turno e supervisor sem rede), que o spike não mediu; por isso a faixa da Fase 11 fica igual e só a confiança no offline sobe de média-baixa para média ([resultado, seção 10](spikes/SPK-03-resultado.md#10-o-que-muda-no-rascunho-a-e-na-estimativa)).
- Integrações com terceiros (TEF, iFood, marketplaces) podem passar de qualquer faixa acima.
