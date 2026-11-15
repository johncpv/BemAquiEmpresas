# Rascunhos de ADR do plano do PDV

Status de todos: **Proposto (rascunho)**, 15/11/2026. **Não estão numerados em `docs/adr`.** A última ADR aceita é a 0045.

## Os seis rascunhos

| Rascunho | Tema | Depende de | Pendências |
|---|---|---|---|
| [A](rascunho-A-offline.md) | PDV offline-first: onde roda, armazenamento local e sincronização | SPK-01, SPK-03 | PP-08, PP-09, PP-10, PP-11, PP-12 |
| [B](rascunho-B-impressao-perifericos.md) | Impressão e periféricos | SPK-01 | PP-08, PP-19 |
| [C](rascunho-C-pagamentos.md) | Pagamentos: da maquininha externa ao TEF, SmartPOS e Pix | — | PP-18, QC-03 |
| [D](rascunho-D-tempo-real-kds-painel.md) | Pedidos em tempo real: KDS e painel do cliente | — | PP-23, PP-24 |
| [E](rascunho-E-acoplamento-fiscal.md) | Acoplamento fiscal: NFC-e pelo EmissorFiscal ou recibo não fiscal | SPK-02; contadora | **PP-01**, PP-05, PP-14 |
| [F](rascunho-F-escala-hospedagem.md) | Escala, modelo multi-loja e hospedagem | SPK-02, SPK-03 | **PP-02**, PP-07, PP-15 |

A síntese dos seis está em [02-arquitetura.md](../02-arquitetura.md).

## Como um rascunho vira ADR

1. Os spikes de que ele depende terminam e o texto é corrigido com o que foi medido.
2. O PO decide as pendências ligadas a ele ([06-pendencias-riscos.md](../06-pendencias-riscos.md)).
3. O arquivo vai para `docs/adr/NNNN-titulo.md`, com o próximo número livre (**0046 em diante**, na ordem em que forem aprovados, que pode não ser a ordem das letras), status "Aceito" e a data da decisão.
4. As "Notas da consolidação do plano" do fim de cada rascunho saem do texto: ou viraram decisão, ou viraram adendo.

Os rascunhos E e F **não podem ser aceitos antes** das decisões PP-01 e PP-02, em que as análises discordam.

## ADRs que ainda precisam ser escritas

Não fazem parte dos seis rascunhos, mas o plano depende delas:

| Tema | Por quê | Story que a escreve |
|---|---|---|
| Perfis de negócio: estado "oculto", `tenant_profiles`, áreas e vocabulário | Muda a regra do `CLAUDE.md` de que módulo não liberado aparece com cadeado (PP-03) | [PERF-01](../stories/PERF-01-perfis-liberacao-efetiva.md) |
| Fronteiras Catalog / Sales / Pos / Restaurant / ServiceOrders / Storefront e o contrato `IOrderPayments` | Seis módulos novos; quem é dono de preço, de código de barras e de pagamento (PP-04) | [CAT-01](../stories/CAT-01-produto-vendavel-barras-preco.md) e [PDV-01](../stories/PDV-01-lojas-caixas-aparelho.md) |
| Usuário de operação sem e-mail | A P-24 do `docs/06` já previa ADR nova (PP-12) | [PDV-01](../stories/PDV-01-lojas-caixas-aparelho.md) |

## Adendos a ADRs existentes

| ADR | O que muda | Origem |
|---|---|---|
| 0003 (permissões `modulo.acao`) | Ações específicas novas: as do `pos`, `sales.view_cost`, `catalog.edit_price`, `catalog.view_cost`, as do `restaurant` | PRD, seção 6.3 |
| 0008 (delegação e 2FA) | Cancelamento de venda e de NFC-e no balcão com PIN de supervisor + motivo + aviso ao Proprietário, no lugar do TOTP recente. Hoje `fiscal.approve` é reservada ao Proprietário e pede 2FA de novo | Rascunho E; PP-11 |
| 0019 (idempotência) | A sincronização do PDV não usa o `IdempotencyFilter`: usa `event_id`. O filtro continua para comandos que exigem rede | Rascunho A |
| 0023 (motor de estoque) | Venda sem bloqueio por saldo: saldo negativo em local de loja **ou** fila de pendência; origem de movimento de venda agregada | Rascunho F; PP-02 |
| 0026 (aparelho e PIN) | Aparelho com tipo, loja e caixa; teto por tipo e plano; chave do aparelho e esquema `Device`; segredo que gira por sincronização; PIN conferido no aparelho sem rede; exceção nomeada para PIN de supervisor | Rascunho A; PP-11 |
| 0027 (integração com o EmissorFiscal) | NFC-e modelo 65; série por caixa; emitente por estabelecimento (§2); limite de chamadas revisto; lista nova de lacunas ([07](../07-emissor-fiscal.md)) | Rascunhos E e F; PP-15 |
| 0030 (quiosque e TV) | Visão "pedidos"; escopo por loja; grupo `{tenantId}:store:{storeId}:orders`; criar o token da visão exige `pos.view` | Rascunho D |
| 0032 (Banco Inter) | §4: aceitar escopo de **cobrança** Pix (que não é envio de Pix). Nomes de escopo a validar na documentação do Inter | Rascunho C |

Também muda, sem ser ADR: a regra de backup do `docs/02-arquitetura.md` §7 (RPO de 24 h) e duas frases do `CLAUDE.md` ([README](../README.md#6-o-que-acontece-quando-o-plano-for-aprovado)).
