# Rascunho F — Escala, modelo multi-loja e hospedagem

> Rascunho de ADR do [plano do PDV](../README.md). "Conferido" é o que foi lido no código ou nas ADRs; "a validar" é conhecimento geral que precisa de spike ou da contadora antes de virar decisão. Os achados citados por número estão em [02-arquitetura.md](../02-arquitetura.md#1-achados-do-código-que-moldam-o-desenho).

- Status: **Proposto (rascunho)**, 15/11/2026. Ainda não numerado em `docs/adr`; vira [rascunho A](rascunho-A-offline.md) ou seguinte quando o PO aprovar ([como os rascunhos viram ADR](README.md)).
- Contexto: várias lojas, vários caixas, volume de supermercado. Ver achados 2, 3, 8, 10, 11 e 12.

## Decisão proposta

### Desempenho
1. **"Instantâneo" é propriedade do aparelho, não do servidor.** Leitura do código, busca no catálogo local, gravação local e impressão pelo agente não tocam a rede. A latência do servidor só afeta Pix, NFC-e, KDS e painel.
2. **Ingestão em dois tempos.** O lote grava eventos e vendas numa transação curta e responde. Os efeitos em estoque, financeiro e fiscal vêm depois, por consumidores idempotentes.
3. **Estoque agregado.** Baixa por lote: uma perna por item, por caixa e janela de tempo, com origem `PosStockBatch` e rastreio até as vendas. Evita cerca de 400 mil movimentos por dia por loja grande (estimativa) e a disputa pelo número `MOV` e pela linha do item, que guarda o custo médio.
4. **Saldo negativo: adendo à ADR 0023.**
   - Opção A: permitir negativo só em local marcado como loja e só para origem PDV, com relatório de divergências; o custo usa o médio vigente. Exige afrouxar o `CHECK`, o que é migração com ADR.
   - Opção B: manter o `CHECK` e estacionar a falta numa fila de pendência até o ajuste.
   - Recomendo A: o saldo errado fica visível em vez de escondido.
5. **Sem auditoria por linha de venda.** O log de eventos é a trilha, como já é com `stock_movements` (`[NotAudited]`).
6. **Medir antes de particionar.** Guid v7 já dá localidade no índice. Particionar por mês `pos.device_events` e `pos.sale_items` só se o teste de carga pedir (o `LoadTest.Trait` já existe).
7. **Conferir o outbox.** Lote de 20 a cada 5 s pode atrasar com volume; não li o processador inteiro.

### Modelo multi-loja
1. **Estabelecimento (loja)** como conceito novo, exposto por contrato `IEstablishments`: CNPJ e IE próprios quando filial, emitente fiscal, local de estoque padrão, conta "Caixa da loja", lista de preços. O dono é o módulo que hoje guarda os dados da empresa (a conferir).
2. **Fiscal:** emitente por estabelecimento. Adendo à ADR 0027 §2, que hoje fixa um emitente igual ao CNPJ do tenant.
3. **Estoque:** `Location` ganha vínculo opcional com estabelecimento e a marca de saldo negativo. `Item` ganha códigos de barras (vários GTIN por item, com unidade e fator) e marca de pesável.
4. **Preços:** listas de preço versionadas no módulo `Sales` (lista, item, preço, `valid_from`, versão), atribuídas por loja. Promoções depois.
5. **Módulos novos no `ModuleCatalog`:** `sales` e `pos`, com área nova no dock (sugestão: "Loja"). Ações específicas: `pos.manage_devices`, `pos.cancel_sale`, `pos.open_drawer`, `pos.discount_above_limit`.
6. **Caixa e turno:** `pos.registers` (loja, número, série fiscal, aparelho ativo) e `pos.shifts` (abertura, sangrias, fechamento, quebra).

### Hospedagem
1. **VPS nos EUA.** Estimo de 110 a 180 ms de ida e volta até o Brasil (a validar; não medi). Efeitos:
   - cada NFC-e cruza o hemisfério duas vezes (loja → VPS → SEFAZ);
   - Pix, KDS e painel ficam perceptivelmente mais lentos;
   - alguma SEFAZ pode restringir IP estrangeiro (a validar; a ADR 0027 registrava que o Emissor ainda não tinha autorizado nota real);
   - CPF e dados de cliente fora do país pedem base legal na LGPD (a conferir com as ADRs 0018 e 0037).
2. **Recomendação:** antes da versão pública do PDV, levar API, PostgreSQL e EmissorFiscal para um datacenter em São Paulo, numa VPS dedicada ao produto, não compartilhada com Dolibarr e outros projetos. Uma região só; multi-região não se justifica.
3. **Backup:** RPO de 24 h não serve a varejo. Arquivamento contínuo de WAL com restauração a um ponto no tempo.
4. **Emissor:** rever o limite de 4 chamadas por segundo por processo; a NFC-e é uma chamada por venda.
5. **SignalR:** uma instância, escala vertical. Backplane só quando houver segunda instância.

## Consequências
- A migração de hospedagem entra no caminho crítico do PDV fiscal.
- Adendos às ADRs 0023, 0026, 0027, 0008, 0030 e 0032.

## Notas da consolidação do plano
- **Divergência registrada (saldo negativo).** Este rascunho recomenda a opção A (permitir negativo em local de loja). A análise de produto recomenda a opção B (manter o `CHECK` e abrir pendência). O plano não escolhe: [PP-02](../06-pendencias-riscos.md#pp-02--venda-com-saldo-insuficiente). A story [PDV-05](../stories/PDV-05-baixa-estoque-sem-bloquear.md) está escrita para valer nas duas.
- **Preços e códigos de barras:** este rascunho os coloca no `Sales` e no `Item` do Estoque; a análise de produto propõe um módulo `Catalog`. O plano adota o módulo próprio e registra a alternativa na PP-04.
- **Hospedagem:** [INFRA-01](../stories/INFRA-01-hospedagem-brasil-pitr.md) e PP-07.
- **Adendos que este rascunho pede:** ADR 0023 (venda sem bloqueio), ADR 0027 §2 (emitente por estabelecimento) e a regra de backup do `02-arquitetura` §7.
