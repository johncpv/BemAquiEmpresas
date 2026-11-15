# Stories do plano do PDV — Fases 10 e 11

Stories detalhadas das duas primeiras fases do [backlog do PDV](../05-backlog.md): a fundação (perfil de negócio, catálogo, pagamentos e spikes) e o PDV balcão com cupom não fiscal e offline, que fecha o **marco MVP**. As Fases 12 a 17 ficam no backlog com critérios curtos e ganham story quando a fase for aberta.

Seguem o modelo de `docs/stories/` (cabeçalho, user story, contexto, critérios de aceite em Dado/quando/então, regras, permissões, telas e estados, plano de testes, fora do escopo, pontos em aberto, Definition of Done, Change Log). Na aprovação do plano, os arquivos passam para `docs/stories/`.

## Definition of Ready

Vale a regra do repositório: **story sem CA testável não entra em execução.** Todas estão em `Draft`. Uma story só passa a `Ready` quando:
1. O plano foi aprovado pelo PO.
2. As pendências (PP-nn) e os spikes citados em "Dependências" e em "Pontos em aberto" estão decididos e registrados ([06-pendencias-riscos.md](../06-pendencias-riscos.md)).
3. As dependências estão concluídas, ou a story explica como testa sem elas.

## Legenda

| Item | Significado |
|---|---|
| Tamanho | P = até 1 sessão de trabalho · M = 2 a 3 · G = 4 ou mais |
| **[Segurança]** | Critério de acesso, de isolamento ou de dado pessoal próprio da story (as três camadas por endpoint já são verificadas por varredura) |
| Spike | Prova técnica com prazo fechado; entrega relatório e decisão, não produto |
| Ajuda | Toda tela nomeia o arquivo `Ajuda/<chave>.md` que escreve ou atualiza |

## Fase 10 — Fundação: perfil de negócio, catálogo, pagamentos e spikes

| Ordem | ID | Título | Tam. | Depende de | Arquivo |
|---|---|---|---|---|---|
| paralelo | SPK-01 | Spike: periféricos em hardware real | M | Kit de hardware comprado (PP-19) | [SPK-01](SPK-01-perifericos-hardware-real.md) |
| paralelo | SPK-02 | Spike: NFC-e de ponta a ponta em homologação | M | CSC e idCSC de homologação do emitente de teste (tenant `demo`, ADR 0027 §6); acesso ao repositório do EmissorFiscal | [SPK-02](SPK-02-nfce-ponta-a-ponta-homologacao.md) |
| paralelo | SPK-03 | Spike: núcleo local-first sob carga | M | Um tablet Android de entrada e um PC antigo para teste | [SPK-03](SPK-03-nucleo-local-first-carga.md) |
| 1 | PERF-01 | Perfis de negócio no catálogo e liberação efetiva | G | PP-03 decidida; FND-06 (existente) | [PERF-01](PERF-01-perfis-liberacao-efetiva.md) |
| 2 | PERF-02 | Provisionar com perfil e adicionar perfil a tenant existente | M | PERF-01 | [PERF-02](PERF-02-provisionar-com-perfil.md) |
| 3 | PERF-03 | Área "Loja", rótulos de área e vocabulário por perfil | M | PERF-01; PP-25 | [PERF-03](PERF-03-area-loja-vocabulario.md) |
| 4 | PERF-04 | Pacotes de dados iniciais por perfil e segmento e checklist "Primeiros passos" | M | PERF-02, CAT-01, PAG-01 | [PERF-04](PERF-04-seeds-primeiros-passos.md) |
| 5 | CAT-01 | Módulo Catálogo: produto vendável, códigos de barras e preço | G | PERF-01, PERF-03; PP-04 decidida | [CAT-01](CAT-01-produto-vendavel-barras-preco.md) |
| 6 | CAT-02 | Importação de produtos por planilha | M | CAT-01 | [CAT-02](CAT-02-importacao-produtos-planilha.md) |
| 7 | PAG-01 | Formas de pagamento e adquirentes, com taxa e prazo D+N | M | — | [PAG-01](PAG-01-formas-pagamento-adquirentes.md) |
| paralelo | INFRA-01 | Hospedagem no Brasil, VPS dedicada e restauração a um ponto no tempo | M | PP-07 decidida, com orçamento; medição de latência do SPK-02 | [INFRA-01](INFRA-01-hospedagem-brasil-pitr.md) |

## Fase 11 — PDV balcão com cupom não fiscal e offline (marco MVP)

| Ordem | ID | Título | Tam. | Depende de | Arquivo |
|---|---|---|---|---|---|
| 1 | PDV-01 | Lojas, caixas e aparelho de PDV registrado | M | PERF-01, CAT-01, PAG-01, PERF-04; PP-04, PP-11 e PP-12 decididas | [PDV-01](PDV-01-lojas-caixas-aparelho.md) |
| 2 | OFF-01 | Fundação offline: app `/pdv`, armazenamento local e descida do catálogo | G | PDV-01, CAT-01, PAG-01; SPK-03 concluído (escolha do armazenamento) | [OFF-01](OFF-01-fundacao-offline.md) |
| 3 | PDV-02 | Tela de venda: toque e teclado/leitor | G | OFF-01 | [PDV-02](PDV-02-tela-de-venda.md) |
| 4 | PDV-03 | Pagamento com várias formas e troco, com maquininha externa | M | PDV-02, PAG-01 | [PDV-03](PDV-03-pagamento-varias-formas.md) |
| 5 | CX-01 | Abertura de caixa, sangria e suprimento | M | PDV-01, PDV-02 | [CX-01](CX-01-abertura-sangria-suprimento.md) |
| 6 | IMP-01 | Cupom não fiscal em bobina de 58 e 80 mm e cadastro de impressora | M | PDV-03; PP-05 decidida | [IMP-01](IMP-01-cupom-nao-fiscal-impressora.md) |
| 7 | OFF-02 | Sincronização idempotente e Pendências do PDV | G | OFF-01, PDV-03; SPK-03 concluído | [OFF-02](OFF-02-sincronizacao-pendencias.md) |
| 8 | CX-02 | Fechamento com conferência cega e quebra | G | CX-01, PDV-03, IMP-01; PP-22 | [CX-02](CX-02-fechamento-conferencia-cega.md) |
| 9 | CX-03 | Fechamento de caixa no financeiro e recebíveis de cartão | G | CX-02, PAG-01, OFF-02 | [CX-03](CX-03-fechamento-financeiro-recebiveis.md) |
| 10 | PDV-05 | Baixa de estoque da venda sem bloquear | M | OFF-02; **PP-02 decidida**; SPK-03 (baixa agregada ou por venda) | [PDV-05](PDV-05-baixa-estoque-sem-bloquear.md) |
| 11 | PDV-04 | Desconto, troca de preço, cancelamento e PIN de supervisor | M | PDV-03, CX-01; PP-11 decidida | [PDV-04](PDV-04-desconto-cancelamento-supervisor.md) |
| 12 | IMP-02 | Impressão direta ESC/POS e gaveta | M | IMP-01; SPK-01 concluído | [IMP-02](IMP-02-impressao-direta-escpos-gaveta.md) |
| 13 | OFF-03 | Caixa e supervisor sem rede | M | OFF-02, CX-02, PDV-04; PP-10 decidida | [OFF-03](OFF-03-caixa-supervisor-sem-rede.md) |
| 14 | PDV-06 | Devolução, troca e vale-troca | M | PDV-05, CX-03, PDV-04 | [PDV-06](PDV-06-devolucao-troca-vale.md) |
| 15 | PDV-07 | Retaguarda de vendas e cards do Início | M | CX-03, PDV-05, OFF-02 | [PDV-07](PDV-07-retaguarda-vendas-cards.md) |

## Testes: o que as stories não repetem

Como manda o `CLAUDE.md`: as três camadas de acesso por endpoint, as rotas anônimas, a cobertura da ajuda e a arquitetura já são verificadas por varredura. As stories só trazem teste de acesso para regra própria (ação específica, escopo por loja, dado mascarado). E2E é por marco, não por story: os fluxos principais estão na [PDV-07](PDV-07-retaguarda-vendas-cards.md), critério 12.
