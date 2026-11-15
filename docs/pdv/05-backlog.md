# 05 — Backlog do PDV e dos perfis de negócio (proposta)

Formato do `docs/05-backlog.md`: `ID [ ] Título (tamanho)` + requisitos, dependências e critérios de aceite (CA) resumidos.
Tamanho relativo: P (até 1 sessão de trabalho), M (2 a 3), G (4 ou mais).
A linha `Paridade:` cita os requisitos mínimos do [benchmark](00-benchmark-mercado.md#4-requisitos-mínimos-para-não-ficar-abaixo-do-mercado) (1 a 38) que a story cobre.
Requisito de origem de todas: pedido do PO de 15/11/2026 ([README](README.md#2-o-que-o-po-pediu)).

As Fases 10 e 11 têm stories detalhadas em [`stories/`](stories/README.md). As Fases 12 a 17 ficam aqui com CA curto e ganham story detalhada quando a fase for aberta.

**Nada disto começa antes do fechamento do go-live da Konebras** (GOL-01b está `InProgress`) **e das decisões da seção 1 de [06-pendencias-riscos.md](06-pendencias-riscos.md).**

## Visão das fases

| Fase | Nome | Stories | Marco |
|---|---|---|---|
| 10 | Fundação: perfil de negócio, catálogo, pagamentos e spikes | 11 | Parte do MVP |
| 11 | PDV balcão com cupom não fiscal e offline | 15 | **MVP** |
| 12 | CPD e retaguarda do varejo | 12 | |
| 13 | NFC-e | 8 | Produto vendável ao mercado |
| 14 | E-commerce (trilha paralela) | 7 | |
| 15 | Alimentação: lanchonete, hamburgueria, trailer e restaurante | 17 | |
| 16 | Supermercado, padaria e pagamentos integrados | 12 | |
| 17 | Oficina e prestação de serviços | 5 | |
| | **Total** | **87** | |

Épicos (prefixo do id):

| Épico | Tema | Stories |
|---|---|---|
| SPK | Spikes | SPK-01 a SPK-03 |
| PERF | Perfil de negócio | PERF-01 a PERF-04 |
| CAT | Catálogo e preços | CAT-01 a CAT-07 |
| PAG | Pagamentos | PAG-01 a PAG-07 |
| PDV | Venda no balcão e retaguarda de vendas | PDV-01 a PDV-13 |
| CX | Caixa | CX-01 a CX-03 |
| IMP | Impressão, periféricos e ponte | IMP-01 a IMP-06 |
| OFF | Offline e sincronização | OFF-01 a OFF-03 |
| CPD | Central de distribuição e estoque do varejo | CPD-01 a CPD-05 |
| NFCE | NFC-e | NFCE-00 a NFCE-07 |
| ECM | E-commerce | ECM-01 a ECM-07 |
| KDS | Cozinha e painel | KDS-01 a KDS-03 |
| MESA | Mesas e comandas | MESA-01 a MESA-05 |
| DLV | Delivery | DLV-01 a DLV-05 |
| FID | Fidelidade | FID-01, FID-02 |
| OFI | Oficina | OFI-01 a OFI-05 |
| INFRA | Hospedagem | INFRA-01 |

## Dependências e paralelismo

```
Decisões do PO (seção 1 do 06) ─┐
                                ▼
Fase 10 ─┬─ SPK-01, SPK-02, SPK-03 (em paralelo, 1 a 2 semanas cada)
         ├─ PERF-01 → PERF-02 → PERF-03 → PERF-04
         ├─ CAT-01 → CAT-02        PAG-01
         └─ INFRA-01 (em paralelo; precisa estar pronta antes de o MVP ir a cliente)
                │
                ├─► Fase 11 (MVP) ─┬─► Fase 12 (CPD e retaguarda)
                │                  ├─► Fase 13 (NFC-e) ◄── NFCE-00 no EmissorFiscal (externo; começar já)
                │                  ├─► Fase 15 (alimentação) ─► Fase 16 (supermercado, padaria, integrados)
                │                  └─► Fase 17 (oficina)
                └─► Fase 14 (e-commerce): só depende da Fase 10
```

O que pode correr em paralelo:

| Trilha | Conteúdo | Observação |
|---|---|---|
| Spikes | SPK-01, SPK-02, SPK-03 | Independentes entre si e das stories de produto |
| EmissorFiscal | NFCE-00 (fases 0 a 2 do [07](07-emissor-fiscal.md#41-fases)) | Outro repositório. Pode começar hoje; as duas correções da NF-e não dependem de nada |
| Infra | INFRA-01 | Independente do código de produto |
| Produto A | Fase 10 → 11 → 12 e 13 → 15 → 16 | Trilha principal |
| Produto B | Fase 14 (depois da Fase 10) → Fase 17 | E-commerce não depende da NFC-e: emite NF-e modelo 55, que já existe |
| Dentro da Fase 11 | CX-01 a CX-03 (caixa) ‖ IMP-01 e IMP-02 (impressão) ‖ OFF-02 e OFF-03 (sincronização), depois de PDV-01, OFF-01 e PDV-02 | Três frentes |
| Fora do código | Kit de hardware (PP-19), perguntas à contadora (QC-01 a QC-16), contato comercial com adquirentes e TEF (PP-18), lojista piloto (PP-35) | Prazos longos; começar na aprovação do plano |

---

## Fase 10 — Fundação: perfil de negócio, catálogo, pagamentos e spikes

**Ordem de execução:** SPK-01, SPK-02 e SPK-03 em paralelo desde o início → PERF-01 → PERF-02 → PERF-03 → PERF-04 → CAT-01 → CAT-02 → PAG-01. INFRA-01 em paralelo.

**SPK-01 [ ] Spike: periféricos em hardware real (M)** — em paralelo — [story](stories/SPK-01-perifericos-hardware-real.md)
- Depende de: kit de hardware (PP-19).
- Paridade: prepara 2, 21, 23.
- CA: três impressoras (USB, rede, Bluetooth), gaveta, leitor e balança testados a partir do PWA em Windows e Android; WebUSB e WebSerial comparados com um agente mínimo em loopback; resposta escrita sobre a ponte entrar ou não no MVP.

**SPK-02 [ ] Spike: NFC-e de ponta a ponta em homologação (M)** — em paralelo — [story](stories/SPK-02-nfce-ponta-a-ponta-homologacao.md)
- Depende de: CSC de homologação do emitente de teste; acesso ao EmissorFiscal.
- Paridade: prepara 2, 3.
- CA: uma NFC-e modelo 65 autorizada em homologação pelo Emissor, com QR Code lido pelo aplicativo da SEFAZ; latência medida da VPS atual e de São Paulo; restrição de IP conferida; perguntas QC entregues à contadora.

**SPK-03 [ ] Spike: núcleo local-first sob carga (M)** — em paralelo — [story](stories/SPK-03-nucleo-local-first-carga.md)
- Depende de: —
- Paridade: prepara 1, 22.
- CA: busca em 50 mil itens medida em tablet barato e PC antigo; lote com 20 caixas simulados sem duplicar; queda de energia e aba fechada no meio da venda sem perda; IndexedDB ou SQLite-WASM decidido.

**PERF-01 [ ] Perfis de negócio no catálogo e liberação efetiva (G)** — ordem 1 — [story](stories/PERF-01-perfis-liberacao-efetiva.md)
- Depende de: PP-03.
- CA: `BusinessProfileCatalog` com 4 perfis e 7 segmentos; `tenant_profiles` com backfill `factory`; liberado = (plano E perfil) OU exceção; módulo fora do perfil vem `hidden` em `/me/permissions` e some do menu e da busca; rota direta responde 403 `module_not_released`; navegação da Konebras idêntica (teste de regressão); ADR dos perfis.

**PERF-02 [ ] Provisionar com perfil e adicionar perfil a tenant existente (M)** — ordem 2 — [story](stories/PERF-02-provisionar-com-perfil.md)
- Depende de: PERF-01.
- CA: passos "Perfil de negócio" e "Segmento" no `ProvisionDialog`; aba Perfis no tenant; adicionar é idempotente e não toca em exceção; remover só oculta; provisionamento continua abaixo de 1 min.

**PERF-03 [ ] Área "Loja", rótulos de área e vocabulário por perfil (M)** — ordem 3 — [story](stories/PERF-03-area-loja-vocabulario.md)
- Depende de: PERF-01; PP-25.
- CA: área "Loja" no dock; tenant sem `factory` vê "Retaguarda" no lugar de "Fábrica"; dicionário com teto de chaves servido no `tenant-context`; a ajuda respeita o termo; a Konebras não muda.

**PERF-04 [ ] Pacotes de dados iniciais por perfil e segmento e checklist "Primeiros passos" (M)** — ordem 4 — [story](stories/PERF-04-seeds-primeiros-passos.md)
- Depende de: PERF-02, CAT-01, PAG-01 (os seeds desses módulos).
- CA: seed do Comércio (locais, contas Caixa e Cofre, formas de pagamento, tabela de preço, grupos, regras do Simples "A confirmar"); idempotente por `seed_key`; checklist no Início com 5 passos e progresso; cada passo leva à tela certa.

**CAT-01 [ ] Módulo Catálogo: produto vendável, códigos de barras e preço (G)** — ordem 5 — [story](stories/CAT-01-produto-vendavel-barras-preco.md)
- Depende de: PERF-01, PERF-03; PP-04.
- Paridade: 22, 25 (tabela padrão), 33.
- CA: projeto `Catalog` nas 5 camadas; `ItemType.Merchandise`; vários GTIN por produto, únicos e com dígito verificador validado; tabela de preço padrão versionada; `ICatalogLookup` por código, PLU e busca em menos de 50 ms com 20 mil produtos (teste de carga); `catalog.edit_price` separada; margem só com `view_cost`.

**CAT-02 [ ] Importação de produtos por planilha (M)** — ordem 6 — [story](stories/CAT-02-importacao-produtos-planilha.md)
- Depende de: CAT-01.
- CA: modelo xlsx para baixar; prévia com erros por linha; cria item + produto + código de barras + preço + saldo inicial opcional; reexecutável sem duplicar; relatório final.

**PAG-01 [ ] Formas de pagamento e adquirentes, com taxa e prazo D+N (M)** — ordem 7 — [story](stories/PAG-01-formas-pagamento-adquirentes.md)
- Depende de: —
- Paridade: 28 (base).
- CA: `finance.payment_methods` e tabela de taxas por adquirente, bandeira, forma e parcelas; adquirente é terceiro; `IPaymentMethods` em `Finance.Contracts`; valores do seed com o chip "A confirmar"; histórico de alteração de taxa.

**INFRA-01 [ ] Hospedagem no Brasil, VPS dedicada e restauração a um ponto no tempo (M)** — em paralelo — [story](stories/INFRA-01-hospedagem-brasil-pitr.md)
- Depende de: PP-07 (decisão e orçamento); medição do SPK-02.
- CA: API, PostgreSQL e EmissorFiscal num datacenter em São Paulo, em VPS só do produto; arquivamento contínuo de WAL; restauração a um ponto no tempo ensaiada; monitor externo; plano de corte da Konebras sem perda.

---

## Fase 11 — PDV balcão com cupom não fiscal e offline — MARCO MVP

**MVP = Fase 10 (PERF-01 a PERF-04, CAT-01, CAT-02, PAG-01, INFRA-01) + Fase 11 inteira.** Os três spikes precisam estar concluídos antes do meio da Fase 11, porque decidem armazenamento, ponte e hospedagem.

O MVP entrega **balcão com cupom não fiscal**: é um piloto com lojista parceiro, não o lançamento comercial ([benchmark, 7.6](00-benchmark-mercado.md#76-resultado); PP-16).

**Ordem de execução:** PDV-01 → OFF-01 → PDV-02 → PDV-03 → CX-01 → IMP-01 → OFF-02 → CX-02 → CX-03 → PDV-05 → PDV-04 → IMP-02 → OFF-03 → PDV-06 → PDV-07. No fechamento do marco: um E2E por fluxo principal e a rodada completa do CI.

**PDV-01 [ ] Lojas, caixas e aparelho de PDV registrado (M)** — ordem 1 — [story](stories/PDV-01-lojas-caixas-aparelho.md)
- Depende de: PERF-01, CAT-01, PAG-01, PERF-04; PP-04, PP-11, PP-12.
- Paridade: 27 (lojas).
- CA: módulos `sales` e `pos` no `ModuleCatalog`; loja ligada a local de estoque e tabela de preço; caixa ligado a aparelho registrado (aparelho ganha tipo, loja e caixa), a conta Caixa e a série; registro por quem tem `pos.manage_devices`; esquema `Device` restrito a `/api/v1/pos/sync/*` por teste de arquitetura.

**OFF-01 [ ] Fundação offline: app `/pdv`, armazenamento local e descida do catálogo (G)** — ordem 2 — [story](stories/OFF-01-fundacao-offline.md)
- Depende de: PDV-01, CAT-01, PAG-01, SPK-03.
- Paridade: 1.
- CA: app `/pdv` com build e service worker próprios, sem afetar `/fab` nem `/tv`; `LocalStore` com armazenamento persistente; foto e delta do catálogo da loja; a tela abre sem rede; atualização só na troca de turno; faixa "Sem conexão: N vendas a enviar".

**PDV-02 [ ] Tela de venda: toque e teclado/leitor (G)** — ordem 3 — [story](stories/PDV-02-tela-de-venda.md)
- Depende de: OFF-01.
- Paridade: 22.
- CA: item por leitor, busca ou grade em menos de 100 ms; quantidade, remover e observação; venda em espera; cliente opcional e CPF; os dois modos por caixa; atalhos na ajuda; funciona igual sem rede.

**PDV-03 [ ] Pagamento com várias formas e troco, com maquininha externa (M)** — ordem 4 — [story](stories/PDV-03-pagamento-varias-formas.md)
- Depende de: PDV-02, PAG-01.
- Paridade: 10 (formas diferentes na mesma venda).
- CA: 1 a n pagamentos por venda; troco só no dinheiro; cartão pede bandeira e, opcionalmente, NSU; `integration = Manual`; campos de TEF e Pix já existem; porta `PaymentTerminal` com `ManualTerminal`; venda `Completed` só com soma = total.

**CX-01 [ ] Abertura de caixa, sangria e suprimento (M)** — ordem 5 — [story](stories/CX-01-abertura-sangria-suprimento.md)
- Depende de: PDV-01, PDV-02.
- Paridade: 5.
- CA: uma sessão aberta por caixa; fundo de troco como transferência do Cofre; sangria e suprimento com motivo e comprovante; alerta e teto de dinheiro em caixa; sem sessão aberta não se vende.

**IMP-01 [ ] Cupom não fiscal em bobina de 58 e 80 mm e cadastro de impressora (M)** — ordem 6 — [story](stories/IMP-01-cupom-nao-fiscal-impressora.md)
- Depende de: PDV-03; PP-05.
- Paridade: 2 (cupom não fiscal).
- CA: porta `HardwareBridge`; assistente "Instalar impressora" com página de teste; recibo com cabeçalho, itens, pagamentos, troco e o texto de "sem valor fiscal"; reimpressão marca "2ª VIA"; falha na impressora não trava a venda.

**OFF-02 [ ] Sincronização idempotente e Pendências do PDV (G)** — ordem 7 — [story](stories/OFF-02-sincronizacao-pendencias.md)
- Depende de: OFF-01, PDV-03, SPK-03.
- Paridade: 1.
- CA: lote de eventos com `event_id`, `seq` e cadeia de hash; reenvio não duplica (teste com 50 reenvios em paralelo); venda concluída nunca é recusada; divergência de preço, relógio e sequência vira pendência; tela "Pendências do PDV".

**CX-02 [ ] Fechamento com conferência cega e quebra (G)** — ordem 8 — [story](stories/CX-02-fechamento-conferencia-cega.md)
- Depende de: CX-01, PDV-03, IMP-01; PP-22.
- Paridade: 5.
- CA: o operador não vê o esperado; contado por forma; até a tolerância fecha e lança a quebra; acima vai para `PendingReview` com `sales.approve` e motivo; relatório de fechamento em PDF e em bobina; reabrir só a última, com aprovação.

**CX-03 [ ] Fechamento de caixa no financeiro e recebíveis de cartão (G)** — ordem 9 — [story](stories/CX-03-fechamento-financeiro-recebiveis.md)
- Depende de: CX-02, PAG-01, OFF-02.
- Paridade: 28.
- CA: `CashSessionClosed` gera resumos por forma, adquirente, bandeira e vencimento; título contra a adquirente com bruto, taxa prevista e D+N; `ISalesRevenue` alimenta DRE e fluxo; baixa pela conciliação existente; idempotente por sessão; teste de integração com Testcontainers.

**PDV-05 [ ] Baixa de estoque da venda sem bloquear (M)** — ordem 10 — [story](stories/PDV-05-baixa-estoque-sem-bloquear.md)
- Depende de: OFF-02, SPK-03; **PP-02**.
- CA: a falta de saldo nunca impede nem recusa a venda; a baixa acontece fora do caminho do caixa; a falta fica visível na retaguarda com a ação de ajuste; estorno pelo documento de origem; custo gravado na linha para a margem.

**PDV-04 [ ] Desconto, troca de preço, cancelamento e PIN de supervisor (M)** — ordem 11 — [story](stories/PDV-04-desconto-cancelamento-supervisor.md)
- Depende de: PDV-03, CX-01; PP-11.
- Paridade: 6.
- CA: limite de desconto por grupo; acima, "Chamar supervisor"; supervisor diferente do operador; motivo obrigatório; `supervisor_approvals` gravado; aviso ao Proprietário no cancelamento de venda paga; relatório por operador.

**IMP-02 [ ] Impressão direta ESC/POS e gaveta (M)** — ordem 12 — [story](stories/IMP-02-impressao-direta-escpos-gaveta.md)
- Depende de: IMP-01, SPK-01.
- CA: gerador ESC/POS único em TypeScript; `WebDeviceBridge` por WebUSB, WebSerial ou Bluetooth onde o navegador permite (corte, gaveta, QR); queda automática para a impressão do navegador; `pos.open_drawer` registrada; lista de impressoras homologadas na ajuda.

**OFF-03 [ ] Caixa e supervisor sem rede (M)** — ordem 13 — [story](stories/OFF-03-caixa-supervisor-sem-rede.md)
- Depende de: OFF-02, CX-02, PDV-04; PP-10.
- Paridade: 1, 6.
- CA: abrir turno, sangria, suprimento e fechamento funcionam sem rede e sincronizam; PIN de operador e de supervisor conferido no aparelho, marcado como offline; janela máxima sem sincronizar; o que não funciona sem rede aparece desabilitado com explicação.

**PDV-06 [ ] Devolução, troca e vale-troca (M)** — ordem 14 — [story](stories/PDV-06-devolucao-troca-vale.md)
- Depende de: PDV-05, CX-03, PDV-04.
- CA: localizar a venda por número ou CPF; devolução parcial; entrada no estoque ou perda; reembolso em dinheiro ou vale; troca ligada à venda nova; prazo configurável; `pos.return`.

**PDV-07 [ ] Retaguarda de vendas e cards do Início (M)** — ordem 15 — [story](stories/PDV-07-retaguarda-vendas-cards.md)
- Depende de: CX-03, PDV-05, OFF-02.
- Paridade: 8 (parte).
- CA: lista de vendas com filtros e exportação; detalhe com trilha (aprovações, sincronização, estoque); cards "Vendas de hoje", "Caixas abertos" e "Ticket médio"; E2E do fluxo principal no fechamento do marco.

---

## Fase 12 — CPD e retaguarda do varejo

**CPD-01 [ ] Transferência CPD → loja e pedido de reposição (M)**
- Paridade: 27. Depende de: PDV-01; PP-28.
- CA: pedido da loja → separação → em trânsito → recebido com conferência; divergência registrada; sugestão por mínimo e máximo por local.

**CPD-02 [ ] Validade, perdas e quebras (M)**
- Paridade: 26.
- CA: alerta de vencimento por lote (os lotes com validade já existem); baixa de perda com motivo e aprovação; relatório de perdas.

**CPD-03 [ ] Sugestão de compra por giro e curva ABC (M)**
- CA: giro, cobertura em dias e ABC a partir das vendas; gera solicitação no módulo Compras, que já tem cotação.

**CPD-04 [ ] Inventário rotativo por loja (P)**
- Paridade: 26.
- CA: reusa o inventário cego existente, por local e categoria, sem fechar a loja.

**CPD-05 [ ] Entrada pela NF do fornecedor criando produto vendável (M)**
- CA: a importação de XML (FIS-04a) sugere produto, código de barras e preço por markup; de-para pelos códigos do fornecedor.

**CAT-03 [ ] Tabelas de preço por loja e canal, atacado, promoções e remarcação em lote (M)**
- Paridade: 25.
- CA: vigência com `valid_from`; preço de atacado por faixa de quantidade; promoção por regra; prévia do impacto na margem; `PriceChanged` chega aos caixas em menos de 1 min com rede.

**CAT-04 [ ] Etiquetas de gôndola e de produto (P)**
- Paridade: 24.
- CA: modelos por tamanho; fila de etiquetas ao mudar preço; reusa a base de etiqueta do Patrimônio.

**PDV-08 [ ] Fiado com limite e venda por vendedor (M)**
- CA: limite por cliente; título na hora; bloqueio com inadimplência (reusa `ICustomerPaymentStats`); vendedor na venda e relatório de comissão. Só com rede.

**PDV-09 [ ] Relatórios de vendas por período, canal e unidade e painel do dono (M)**
- Paridade: 8.
- CA: relatórios com exportação no padrão da ADR 0045; filtros por período, canal, loja, operador e forma; painel do dono no celular com vendas do dia, caixas abertos e alertas.

**PDV-10 [ ] Margem real por venda no fechamento do caixa (M)**
- Superar: item 1. Depende de: PDV-05, CX-03.
- CA: margem por venda e por sessão com custo real de compra, impostos estimados e taxa da adquirente; só com `sales.view_cost`; relatório "vendas abaixo do custo".

**PDV-11 [ ] Modo treinamento do PDV (P)**
- CA: sessão marcada como treinamento, com faixa visível; não gera estoque, financeiro nem fiscal; nunca sincroniza como venda real.

**IMP-03 [ ] Agente "BemAqui Ponte": impressão, gaveta, balança e base do TEF (G)**
- Depende de: SPK-01; PP-08. **Sobe para a Fase 11 se o SPK-01 mostrar que a impressora USB no Windows não funciona de forma simples pelo navegador.**
- CA: serviço Windows só em loopback, pareado ao aparelho por código de uso único, com conferência de `Origin` e assinatura de código; lista o spooler e varre a porta 9100; impressora de rede da cozinha; cópia do registro de eventos em disco; instalação assistida e atualização própria.

---

## Fase 13 — NFC-e (em paralelo com a 12; depende do EmissorFiscal)

**NFCE-00 [ ] (externa) NFC-e no EmissorFiscal (G)** — repositório do PO, como foi a P-27
- Paridade: 2, 3. Depende de: SPK-02; PP-13.
- CA: fases 0 a 2 de [07-emissor-fiscal.md](07-emissor-fiscal.md#41-fases): correção das lacunas 25 e 5 (afetam a NF-e de hoje), modelo 65, CSC, QR Code, contrato `NfceRequest`, reconciliação própria sem reenvio, cancelamento com prazo por UF, impressão como dados estruturados; homologação em SP. A dívida E14 resolvida antes do segundo cliente.

**NFCE-01 [ ] Configuração fiscal do varejo (M)**
- Depende de: NFCE-00; PP-06.
- CA: regime Simples ou MEI; CSC cadastrado no Emissor e série por caixa; regras por NCM com "A confirmar com a contadora"; item apto (`fiscal_ready`) validado no cadastro; `fiscal_environment_allowed` respeitado.

**NFCE-02 [ ] Emissão de NFC-e pela venda (G)**
- Paridade: 2. Depende de: NFCE-01; PP-05, PP-14.
- CA: modo por loja (sempre NFC-e, nunca, perguntar); `pos.skip_fiscal`; `InvoiceType.NFCe` e `IssueNfceAsync`; a venda nunca espera a SEFAZ além do teto e segue `Pending`; DANFE NFC-e na bobina a partir dos dados do Emissor; rejeição vai para a fila de correção; pagamentos informados como não integrados.

**NFCE-03 [ ] Cancelamento e devolução fiscal (M)**
- Depende de: NFCE-02; PP-11.
- CA: cancelamento dentro do prazo com supervisor; fora do prazo, NF-e de devolução; converter cupom não fiscal em NFC-e depois, se a contadora aprovar (QC-13).

**NFCE-04 [ ] Venda sem internet em loja fiscal (G)**
- Paridade: 2. Depende de: **PP-01**.
- CA: conforme a decisão da PP-01. Opção "cupom agora, NFC-e depois": política por loja, janela máxima, relatório "NFC-e emitidas depois da venda". Opção "agente de loja": agente com A1 dedicado assina `tpEmis=9`, série de contingência por caixa, envio ao Emissor e alerta de prazo (lacunas 13 a 16).

**NFCE-05 [ ] IBS/CBS na NFC-e e Simples pronto para janeiro de 2027 (M)**
- Paridade: 3. Depende de: NFCE-02; QC-04.
- CA: campos de IBS/CBS preenchidos conforme a Nota Técnica vigente; regras por regime com "A confirmar com a contadora"; data de obrigatoriedade para o Simples confirmada antes de ligar.

**NFCE-06 [ ] Tributos aproximados no cupom (P)**
- Depende de: NFCE-02; QC-10.
- CA: valor por item e total (Lei 12.741/2012) na NFC-e e no texto do cupom; fonte da alíquota configurável.

**NFCE-07 [ ] Emitente por loja: filial com CNPJ próprio (M)**
- Depende de: PP-15.
- CA: loja ligada a um estabelecimento com CNPJ e IE próprios; emissão pelo emitente da loja; adendo à ADR 0027 §2; A1 por loja ou da matriz conforme a decisão.

---

## Fase 14 — E-commerce (trilha paralela, depois da Fase 10)

**ECM-01 [ ] Loja virtual: host próprio, vitrine e busca (G)**
- CA: `tenant_domains.kind = Store`; identidade do comprador separada do backoffice (padrão da ADR 0040); páginas públicas com cache; SEO básico; tema do tenant; rotas anônimas na lista do teste de arquitetura.

**ECM-02 [ ] Carrinho, checkout e reserva no estoque único (G)**
- Paridade: 35.
- CA: reserva com validade no mesmo estoque da loja física; frete por retirada ou faixa de CEP; consentimento e dados mínimos (LGPD).

**ECM-03 [ ] Pagamento online: Pix dinâmico e cartão (G)**
- Depende de: PP-21; adendo à ADR 0032.
- CA: Pix com webhook e reconsulta; cartão pelo gateway escolhido; pedido pago vira venda de canal `Online`; taxa e prazo no financeiro.

**ECM-04 [ ] Gestão de pedidos, separação, NF-e automática, frete e expedição (M)**
- Paridade: 37.
- CA: estados Pago → Em separação → Faturado → Enviado → Entregue; NF-e 55 pelo fluxo existente; frete integrado e etiqueta de envio; e-mail a cada estado.

**ECM-05 [ ] Cancelamento, devolução e estorno online (M)**
- CA: direito de arrependimento em 7 dias; estorno pelo meio original; NF-e de devolução.

**ECM-06 [ ] Marketplaces: Mercado Livre, Shopee, Amazon e Magalu (G)**
- Paridade: 35, 36. Depende de: PP-21 (direto ou por hub).
- CA: anúncio e estoque sincronizados; pedido do marketplace vira venda de canal próprio; NF-e por pedido.

**ECM-07 [ ] Integração com Nuvemshop, Tray e Loja Integrada (G)**
- Paridade: 38. Depende de: PP-21.
- CA: produtos, estoque e pedidos sincronizados com a plataforma que o cliente já usa; a loja própria deixa de ser obrigatória.

---

## Fase 15 — Alimentação: lanchonete, hamburgueria, trailer e restaurante

**CAT-05 [ ] Complementos, combos, observações, meio a meio e preço por horário (M)**
- Paridade: 16.
- CA: grupos com mínimo e máximo; preço do complemento; combo com troca de item; regra de preço para dois sabores; preço por horário; funciona sem rede.

**CAT-06 [ ] Ficha técnica, baixa de insumos e CMV teórico × real (G)**
- Paridade: 19.
- CA: receita por produto com perda e rendimento; `ConsumeOnSale` explode na venda; custo teórico e margem; relatório CMV teórico × real.

**KDS-01 [ ] Estações de preparo e comanda de produção impressa (M)**
- Paridade: 12.
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
- Paridade: 9, 11, 21. Depende de: MESA-01; PP-23.
- CA: comanda por mesa, cartão ou nome; lançar por rodada; enviar à cozinha; transferir e juntar com permissão; PWA do garçom por PIN em celular; pesar abre a comanda no self-service.

**MESA-03 [ ] Pré-conta, taxa de serviço e couvert (M)**
- Paridade: 9. Depende de: QC-05.
- CA: pré-conta impressa sem valor fiscal; taxa opcional, com retirada por `restaurant.waive_service`; couvert por pessoa.

**MESA-04 [ ] Split por pessoa e por item, com formas diferentes (G)**
- Paridade: 10. Depende de: PDV-03, MESA-02; PP-20.
- CA: partes iguais, por valor ou por itens; parte paga é preservada; rateio da taxa; arredondamento na última; NFC-e conforme a PP-20; teste de integração com 4 pagantes e 6 formas.

**MESA-05 [ ] Cardápio digital com QR na mesa (M)**
- Paridade: 13.
- CA: QR por mesa abre o cardápio do catálogo; o cliente monta o pedido; o pedido entra na comanda depois da confirmação do garçom ou direto, por configuração.

**DLV-01 [ ] Pedido para viagem, retirada e delivery próprio com taxa de entrega (M)**
- Paridade: 15.
- CA: tipo do pedido (local, viagem, retirada, entrega); taxa por bairro ou raio; endereço e troco para o entregador.

**DLV-02 [ ] Site de delivery sem comissão (M)**
- Paridade: 13. Depende de: ECM-01 (mesma base da loja virtual).
- CA: cardápio online no host do tenant; pedido cai no gestor de pedidos e na cozinha; pagamento na entrega ou por Pix.

**DLV-03 [ ] Entregadores: acerto e roteirização (M)**
- Paridade: 15.
- CA: cadastro de entregadores; acerto por entrega ou diária; agrupamento de pedidos por rota.

**DLV-04 [ ] iFood no gestor de pedidos; 99Food e Keeta pelo Open Delivery (G)**
- Paridade: 14. Depende de: PP-36.
- CA: pedido da plataforma entra no mesmo gestor, vai à cozinha e vira venda de canal próprio; cardápio e disponibilidade sincronizados.

**DLV-05 [ ] Robô de pedidos no WhatsApp (G)**
- Paridade: 18. Depende de: PP-36; ADR 0041.
- CA: o cliente monta o pedido pela conversa; o pedido cai no gestor; consentimento e descadastro conforme a ADR 0041.

**FID-01 [ ] Cupom de desconto (P)**
- Paridade: 17.
- CA: código com vigência, limite de uso e regra; aplicado no PDV e no site; relatório de uso.

**FID-02 [ ] Fidelidade por pontos e cashback (M)**
- Paridade: 17.
- CA: acúmulo por venda identificada; resgate como forma de pagamento; saldo como passivo no financeiro; só com rede.

---

## Fase 16 — Supermercado, padaria e pagamentos integrados

**IMP-04 [ ] Etiqueta de balança: EAN-13 de peso ou preço (P)**
- Paridade: 23.
- CA: prefixo e formato configuráveis por loja; PLU; testes com as máscaras comuns.

**IMP-05 [ ] Balança no caixa (M)**
- Paridade: 21, 23. Depende de: IMP-03 ou WebSerial; PP-19.
- CA: leitura de peso por serial; protocolos por perfil (Toledo, Filizola; a validar).

**IMP-06 [ ] Carga de balança: exportar produtos para Toledo, Filizola e Urano (M)**
- Paridade: 23. Depende de: PP-19.
- CA: arquivo de carga nos formatos dos fabricantes escolhidos; setores de balança; reexportação ao mudar preço.

**PDV-12 [ ] PDV de alto volume (M)**
- Paridade: 22.
- CA: 200 itens por venda sem degradar; consulta de preço; cancelamento com supervisor por atalho; teste de carga marcado como `LoadTest`.

**PDV-13 [ ] Totem de autoatendimento com pagamento integrado (G)**
- Paridade: 20. Depende de: PAG-02, PAG-03 ou PAG-04.
- CA: modo totem do app `/pdv`; pedido e pagamento sem operador; envio à cozinha e senha no painel.

**PAG-02 [ ] Pix QR dinâmico no PDV (M)**
- Paridade: 4. Depende de: adendo à ADR 0032.
- CA: `IPixChargeProvider`; QR na tela do caixa; confirmação por webhook + reconsulta, levada ao caixa por SignalR; tempo limite com a opção "Conferir manualmente". Só com rede.

**PAG-03 [ ] TEF: primeiro provedor (G)**
- Paridade: 4. Depende de: IMP-03; PP-18.
- CA: `TefTerminal` pelo agente; valor enviado à máquina, retorno com NSU e bandeira; estado `Unknown` tratado; estorno; NFC-e como pagamento integrado; queda para o modo manual.

**PAG-04 [ ] SmartPOS Android: PDV na maquininha (G)**
- Paridade: 7, 11. Depende de: PP-18.
- CA: casca Android fina (Capacitor) com o SDK do fabricante; `SmartPosTerminal`; venda e app do garçom rodam na maquininha.

**PAG-05 [ ] Conciliação de cartões por arquivo ou API da adquirente (M)**
- Paridade: 28. Depende de: CX-03.
- CA: `IAcquirerStatementProvider`; confere taxa prevista × cobrada; aponta divergência; baixa os títulos da adquirente.

**PAG-06 [ ] Link de pagamento Pix ou cartão (M)**
- Paridade: 34. Depende de: PAG-02; PP-21.
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
- Paridade: 30, 31.
- CA: OS com placa, km e modelo; status por etapa; checklist de entrada com fotos visíveis ao cliente.

**OFI-02 [ ] Orçamento da OS e aprovação do cliente por link (M)**
- Paridade: 32.
- CA: envio por WhatsApp ou e-mail; aprovação online registrada; a OS só executa o aprovado.

**OFI-03 [ ] Execução: mão de obra por técnico, peças reservadas e baixadas (M)**
- Paridade: 33.
- CA: horas por técnico; peças com código de barras reservadas e baixadas pelo Estoque; comissão em relatório.

**OFI-04 [ ] Fechamento: pagamento pelo PDV, NFS-e + NFC-e ou NF-e, garantia (G)**
- Paridade: 33. Depende de: QC-06.
- CA: pagamento pelo `IOrderPayments`; documento fiscal misto conforme a contadora; garantia reusa `warranty_defaults`.

**OFI-05 [ ] Histórico por veículo, portal do cliente e lembrete de revisão (M)**
- Paridade: 30, 34. Superar: item 3.
- CA: histórico por veículo ou cliente, visível ao cliente pelo portal; lembrete de revisão por data ou km.

---

## Esforço

**Leia antes dos números.** As três análises estimaram em bases diferentes e **não foram reconciliadas**:

| Fonte | Base | O que disse |
|---|---|---|
| Produto | Ritmo histórico do repositório (cerca de 150 stories em 3 semanas de calendário, entre 16/10 e 03/11/2026). Vale para **código** | MVP em 4,5 a 7 semanas de calendário; conjunto completo em 4 a 6 meses com duas trilhas |
| Arquitetura | Semanas-pessoa, ordem de grandeza | Fazer local-first custa de 30% a 40% a mais no núcleo do PDV do que um PDV online simples. A segunda etapa do offline (abrir turno e PIN sem rede, delta, pendências) soma de 5 a 8 semanas-pessoa |
| Emissor | Um desenvolvedor | 12 a 16 semanas com uma pessoa, 8 a 10 com duas, sem contar o agente de loja, o PDV nem a espera por credenciamento, CSC e CSRT nas SEFAZ |

A estimativa de produto para o offline (parte das 2 a 3 semanas da Fase 11) e a de arquitetura (5 a 8 semanas-pessoa só para a segunda etapa) **não fecham**. O SPK-03 existe para tirar essa dúvida antes de assumir prazo.

O que **não** comprime com o ritmo do repositório: teste com hardware real, homologação na SEFAZ, trabalho no EmissorFiscal, credenciamento de TEF e SmartPOS, credenciamento em plataformas de delivery e marketplaces, e piloto em loja.

Faixas em semanas de calendário, com um PO. A coluna "Original" é a da análise de produto. A coluna "Ajuste" é desta consolidação: as Fases 12, 14, 15 e 16 ganharam stories de paridade com o mercado (delivery, fidelidade, totem, SmartPOS, marketplaces, plataformas) que não estavam na conta original. O ajuste é proporcional ao que entrou e tem confiança baixa.

| Fase | Stories | Construção (original) | Construção (ajuste) | Validação (hardware, piloto, externo) | Confiança |
|---|---|---|---|---|---|
| 10 — Fundação e spikes | 11 | 1 a 1,5 | 2 a 3 (com os spikes) | 0,5 | Média-alta |
| 11 — PDV, caixa, cupom, offline | 15 | 2 a 3 | 3 a 6 (ver nota do offline) | 1 a 2 (piloto em loja; offline em campo) | Média: offline e impressão são os pontos incertos |
| **MVP (10 + 11)** | **26** | **3 a 4,5** | **5 a 9** | **1,5 a 2,5 → total de 6,5 a 11,5 semanas** | Média |
| 12 — CPD e retaguarda | 12 | 1,5 a 2,5 | 2,5 a 4 (com a ponte) | 0,5 a 1 | Média |
| 13 — NFC-e | 8 | 1 a 2 no BemAqui | 2 a 3 no BemAqui | 8 a 16 no EmissorFiscal + SEFAZ (estimativa do Emissor) | Baixa: depende de trabalho externo e da UF |
| 14 — E-commerce | 7 | 2 a 4 | 4 a 7 (com marketplaces e plataformas) | 1 a 2 (gateway, antifraude, frete); credenciamento em marketplace sem prazo | Baixa |
| 15 — Alimentação | 17 | 3 a 4 | 5 a 8 (com delivery, fidelidade e robô) | 1 a 2 (cozinha real); credenciamento em plataformas sem prazo | Baixa |
| 16 — Supermercado e integrados | 12 | 3 a 5 | 4 a 7 | O credenciamento de TEF e SmartPOS pode levar meses | Baixa |
| 17 — Oficina | 5 | 1,5 a 2,5 | 1,5 a 2,5 | 0,5 | Alta |

Leitura:
- **MVP (piloto de balcão com cupom não fiscal):** de 1,5 a 3 meses depois do início, se os spikes não trouxerem surpresa.
- **Produto vendável ao mercado (com NFC-e):** depende mais do EmissorFiscal e da SEFAZ do que deste repositório. Com o Emissor começando já, não antes de 3 a 4 meses.
- **Conjunto completo, com duas trilhas:** a análise de produto falava em 4 a 6 meses; com as stories de paridade, **6 a 9 meses** é mais honesto, e as integrações com terceiros (TEF, iFood, marketplaces) podem passar disso.
- Nenhum destes números é medição.
