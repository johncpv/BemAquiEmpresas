# Stories do plano do PDV — Fases 10 e 11 e o começo do e-commerce

Stories detalhadas das duas primeiras fases do [backlog do PDV](../05-backlog.md): a fundação (perfil de negócio, catálogo, pagamentos, núcleo de vendas e spikes) e o PDV balcão com cupom não fiscal e offline, que fecha o **marco MVP**. Trazem também as quatro primeiras stories do e-commerce (ECM-01 a ECM-04), escritas a pedido da revisão independente de 15/11/2026 para que a trilha seguinte possa abrir sem replanejar. O resto fica no backlog com critérios curtos e ganha story quando a fase for aberta.

Seguem o modelo de `docs/stories/` (cabeçalho, user story, contexto, critérios de aceite em Dado/quando/então, regras, permissões, telas e estados, plano de testes, fora do escopo, pontos em aberto, Definition of Done, Change Log). Na aprovação do plano, os arquivos passam para `docs/stories/`.

## Definition of Ready

Vale a regra do repositório: **story sem CA testável não entra em execução.** Todas estão em `Draft`. Uma story só passa a `Ready` quando:
1. O plano foi aprovado pelo PO e o go-live da Konebras fechou (salvo o que roda no EmissorFiscal, PP-41).
2. As pendências (PP-nn) e os spikes citados em "Dependências" e em "Pontos em aberto" estão decididos e registrados ([06-pendencias-riscos.md](../06-pendencias-riscos.md)).
3. As dependências estão concluídas, ou a story explica como testa sem elas.

## Legenda

| Item | Significado |
|---|---|
| Tamanho | P = até 1 sessão de trabalho · M = 2 a 3 · G = 4 ou mais |
| **[Segurança]** | Critério de acesso, de isolamento ou de dado pessoal próprio da story (as três camadas por endpoint já são verificadas por varredura) |
| Spike | Prova técnica com prazo fechado; entrega relatório e decisão, não produto |
| Ajuda | Toda tela nomeia o arquivo `Ajuda/<chave>.md` que escreve ou atualiza |
| Re-estimada | Tamanho mudado pela revisão independente, com o motivo no contexto da story |

## Fase 10 — Fundação: perfil de negócio, catálogo, pagamentos, núcleo de vendas e spikes

| Ordem | ID | Título | Tam. | Depende de | Arquivo |
|---|---|---|---|---|---|
| paralelo | SPK-01 | Spike: periféricos em hardware real | M | Kit de hardware comprado (PP-19) | [SPK-01](SPK-01-perifericos-hardware-real.md) |
| paralelo | SPK-02 | Spike: NFC-e de ponta a ponta em homologação | M | CSC e idCSC de homologação do emitente de teste (tenant `demo`, ADR 0027 §6); acesso ao repositório do EmissorFiscal | [SPK-02](SPK-02-nfce-ponta-a-ponta-homologacao.md) |
| paralelo | SPK-03 | Spike: núcleo local-first sob carga | M | Um tablet Android de entrada e um PC antigo para teste | [SPK-03](SPK-03-nucleo-local-first-carga.md) |
| 1 | PERF-01 | Perfis de negócio no catálogo e liberação efetiva | G | PP-03 decidida; FND-06 (existente) | [PERF-01](PERF-01-perfis-liberacao-efetiva.md) |
| 2 | PERF-02 | Provisionar com perfil e adicionar perfil a tenant existente | M | PERF-01 | [PERF-02](PERF-02-provisionar-com-perfil.md) |
| 3 | PERF-03 | Área "Loja", rótulos de área e vocabulário por perfil | M | PERF-01; PP-25 | [PERF-03](PERF-03-area-loja-vocabulario.md) |
| 4 | CAT-01 | Módulo Catálogo: produto vendável, categoria, variação, códigos de barras e preço | G | PERF-01, PERF-03; PP-04 decidida | [CAT-01](CAT-01-produto-vendavel-barras-preco.md) |
| 5 | CAT-02 | Importação de produtos por planilha | M | CAT-01 | [CAT-02](CAT-02-importacao-produtos-planilha.md) |
| 6 | PAG-01 | Formas de pagamento e adquirentes, com taxa e prazo D+N | M | — | [PAG-01](PAG-01-formas-pagamento-adquirentes.md) |
| 7 | VND-01 | Núcleo de vendas sem caixa: venda, pagamento, estoque e financeiro por canal | G | CAT-01, PAG-01; PP-04 decidida | [VND-01](VND-01-nucleo-vendas-sem-caixa.md) |
| 8 | PERF-04 | Pacotes de dados iniciais por perfil e segmento e checklist "Primeiros passos" | M | PERF-02, CAT-01, PAG-01 | [PERF-04](PERF-04-seeds-primeiros-passos.md) |
| paralelo | INFRA-02 | Backup mais frequente na VPS atual | P | — | [INFRA-02](INFRA-02-backup-frequente-vps-atual.md) |
| condicionada | INFRA-01 | Hospedagem no Brasil, VPS dedicada e restauração a um ponto no tempo | M | Um dos gatilhos da PP-07 disparar; orçamento; medição do SPK-02 | [INFRA-01](INFRA-01-hospedagem-brasil-pitr.md) |

## Fase 11 — PDV balcão com cupom não fiscal e offline (marco MVP)

| Ordem | ID | Título | Tam. | Depende de | Arquivo |
|---|---|---|---|---|---|
| 1 | PDV-01 | Lojas, caixas e aparelho de PDV registrado | G | PERF-01, CAT-01, PAG-01, VND-01, PERF-04; PP-04, PP-11 e PP-12 decididas | [PDV-01](PDV-01-lojas-caixas-aparelho.md) |
| 2 | OFF-01 | Fundação offline: app `/pdv`, armazenamento local e descida do catálogo | G | PDV-01, CAT-01, PAG-01; SPK-03 concluído (escolha do armazenamento); PP-09 decidida | [OFF-01](OFF-01-fundacao-offline.md) |
| 3 | PDV-02 | Tela de venda: toque e teclado/leitor | G | OFF-01; PP-26 (CPF na venda) | [PDV-02](PDV-02-tela-de-venda.md) |
| 4 | PDV-03 | Pagamento com várias formas e troco, com maquininha externa | M | PDV-02, PAG-01, VND-01 | [PDV-03](PDV-03-pagamento-varias-formas.md) |
| 5 | CX-01 | Abertura de caixa, sangria e suprimento | M | PDV-01, PDV-02; PP-22 (alerta e teto) | [CX-01](CX-01-abertura-sangria-suprimento.md) |
| 6 | IMP-01 | Cupom não fiscal em bobina de 58 e 80 mm e cadastro de impressora | M | PDV-03 (a PP-05 foi decidida pelo PO em 15/11/2026) | [IMP-01](IMP-01-cupom-nao-fiscal-impressora.md) |
| 7 | OFF-02 | Sincronização idempotente e Pendências do PDV | G | OFF-01, PDV-03, VND-01; SPK-03 concluído; PP-26 (CPF) | [OFF-02](OFF-02-sincronizacao-pendencias.md) |
| 8 | CX-02 | Fechamento com conferência cega, quebra e recolhimento | G | CX-01, PDV-03, IMP-01; PP-22 | [CX-02](CX-02-fechamento-conferencia-cega.md) |
| 9 | CX-03 | Fechamento de caixa no financeiro e recebíveis de cartão | G | CX-02, PAG-01, VND-01, OFF-02 | [CX-03](CX-03-fechamento-financeiro-recebiveis.md) |
| 10 | PDV-05 | Baixa de estoque da venda sem bloquear | M | OFF-02, VND-01; **PP-02 decidida**; SPK-03 (baixa agregada ou por venda) | [PDV-05](PDV-05-baixa-estoque-sem-bloquear.md) |
| 11 | PDV-04 | Desconto, troca de preço, cancelamento e PIN de supervisor | M | PDV-03, CX-01; PP-11 e PP-38 decididas; PP-22 (valores padrão) | [PDV-04](PDV-04-desconto-cancelamento-supervisor.md) |
| 12 | IMP-02 | Impressão direta ESC/POS e gaveta | M | IMP-01; SPK-01 concluído; PP-08 (se a ponte entra) | [IMP-02](IMP-02-impressao-direta-escpos-gaveta.md) |
| 13 | OFF-03 | Caixa e supervisor sem rede | G | OFF-02, CX-02, PDV-04; PP-10 e PP-39 decididas | [OFF-03](OFF-03-caixa-supervisor-sem-rede.md) |
| 14 | PDV-06 | Devolução, troca e vale-troca | M | PDV-05, CX-03, PDV-04 | [PDV-06](PDV-06-devolucao-troca-vale.md) |
| 15 | PDV-07 | Retaguarda de vendas e cards do Início | M | CX-03, PDV-05, OFF-02 | [PDV-07](PDV-07-retaguarda-vendas-cards.md) |

## Fase 14 — E-commerce (trilha seguinte à do balcão)

| Ordem | ID | Título | Tam. | Depende de | Arquivo |
|---|---|---|---|---|---|
| 1 | ECM-01 | Loja virtual: host próprio, vitrine e busca | G | CAT-01, PERF-01; adendo à ADR 0009 aprovado | [ECM-01](ECM-01-loja-virtual-vitrine.md) |
| 2 | ECM-02 | Carrinho, checkout e reserva no estoque único | G | ECM-01, VND-01; PP-26 (finalidade nova para o dado do comprador) | [ECM-02](ECM-02-carrinho-checkout-reserva.md) |
| 3 | ECM-03 | Pagamento online: Pix dinâmico e cartão | G | ECM-02, VND-01, PAG-01, PAG-02 (porta `IPixChargeProvider`); PP-21 (gateway de cartão) decidida; adendo à ADR 0032 | [ECM-03](ECM-03-pagamento-online.md) |
| 4 | ECM-04 | Gestão de pedidos, separação, NF-e automática, frete e expedição | M | ECM-03; **lacuna 25 corrigida e dívida E14 resolvida no EmissorFiscal** | [ECM-04](ECM-04-pedidos-nfe-expedicao.md) |

## Testes: o que as stories não repetem

Como manda o `CLAUDE.md`: as três camadas de acesso por endpoint, as rotas anônimas, a cobertura da ajuda e a arquitetura já são verificadas por varredura. As stories só trazem teste de acesso para regra própria (ação específica, escopo por loja, dado mascarado). E2E é por marco, não por story: os fluxos principais estão na [PDV-07](PDV-07-retaguarda-vendas-cards.md), critério 12.
