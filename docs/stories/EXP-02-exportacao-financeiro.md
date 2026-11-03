# EXP-02 — Onda 1: exportação do financeiro (extrato, conciliação, ordens, a receber, DRE, fluxo de caixa e inadimplência)

| Campo | Valor |
|---|---|
| ID | EXP-02 |
| Título | Exportação em Excel (xlsx) e PDF das listas e relatórios do financeiro (extrato da conta, extrato do banco, conciliação, ordens de pagamento, a receber, DRE, fluxo de caixa e inadimplência), com os filtros da tela, a mesma permissão e a mesma máscara; PDF próprio do extrato do período e do DRE do período |
| Tamanho | G |
| Status | Done (03/11/2026) |
| Requisitos | Pedido do PO de 02/11/2026 (auditoria de PDF e exportação, seções B, D4 e E) · R-FIN-13 (DRE) · R-FIN-12 (inadimplência) · R-FIN-5 (ordens) · R-COM-3 · PRD §1.1 (visibilidade financeira) · PRD §6 (LGPD, Desempenho) · R-SEC-5 |
| Dependências | EXP-01 (`PdfDocumentFrame`, `PdfMark`, `ListPdfDocument`, `ITabularExporter` com ClosedXML, `ExportLimits`, `<ExportMenu>`, slot `toolbar` do `DataTable`, auditoria de exportação); FIN-04, FIN-05, FIN-06a/06b, FIN-07, FIN-08, FIN-03a, BNK-03a, BNK-03b (telas e consultas já entregues) |
| Bloqueia | — |
| Ordem de execução | 2 (Pós-go-live: documentos e exportação) |

## User story
Como **financeiro ou proprietário**, quero **exportar em Excel e em PDF o extrato, a conciliação, as ordens, o a receber, o DRE, o fluxo de caixa e a inadimplência, com os mesmos filtros que estou vendo**, para **fechar o mês e mandar à contadora sem copiar tela nem montar planilha à mão**.

## Contexto e referências
- Auditoria de 02/11/2026 (`pdf-export-auditoria.md`, seções A, B, D4 "EXP-02" e E). Hoje o financeiro não tem nenhuma exportação, só o CSV da conferência da implantação montado no front (`ReconciliationStep`).
- **EXP-01 (base, em andamento):** `GET .../export?format=xlsx|pdf` reaproveita a query da lista sem paginação e a mesma `RequirePermission`; teto de **10.000 linhas (xlsx)** e **2.000 (PDF)** com 422 `export_too_large`; coluna com `Sensitivity` sai mascarada sem a ação (**sem 2FA**, decisão do PO de 02/11/2026); auditoria `RecordExportAsync` com formato, linhas e filtros; aba "Filtros" no xlsx; `<ExportMenu>` com "Planilha (Excel)" e "PDF da lista". O piloto da EXP-01 é **A pagar** (lista) e o **PDF da ordem de pagamento**: esta story **não** repete os dois.
- Consultas existentes (Finance): `GetBankStatement` (`StatementResult`: saldo inicial/anterior, linhas, saldo final), `GetBankStatementLines` (`BankStatementView`, contraparte com `CounterpartyMasked`), `GetReconciliation` (`ReconciliationView`: pares, só no banco, só no sistema, conciliados, totais, `ReconciledUntil`, `ClosedUntil`, `CounterpartyMasked`), `ListPaymentOrders`, `ListReceivables` (com `ReceivableTotals`), `GetDre` (`DreReport`: linhas com `Level`, meses, total, % da receita líquida, Δ), `GetCashFlow` (`CashFlowResult` + `CashFlowAdjusted`, cenário `base|adjusted|both`), `GetAging` e `GetAgingByProject`.
- Regras de exibição que a exportação herda: folha como **uma linha só** no DRE sem `hr.view_sensitive` (FIN-08); folha prevista só pelo total para quem não aprova nem paga (FIN-07, RH-02c); pontualidade de cliente pessoa física com `finance.view` e `Cache-Control: no-store` (FIN-06b CA 7); contraparte mascarada nas linhas do banco (BNK-03a).

## Critérios de aceite
**Comum a todas as telas**
1. **Dado** `finance.view` numa das telas da tabela "Telas e conteúdo", **quando** abro "Exportar" na barra da lista (slot `toolbar` do `DataTable`, à direita da busca) e escolho "Planilha (Excel)" ou "PDF", **então** o arquivo traz **as mesmas linhas da tela com os filtros e a busca atuais** (não só a página), na ordem da tela, com o nome `{base}-{AAAA-MM-DD}.{xlsx|pdf}` (sem nome de pessoa nem CPF), cabeçalho congelado e autofiltro no xlsx, e a aba "Filtros" com os filtros, "Emitido por {usuário} em {data hora} (São Paulo)"; o PDF usa o `PdfDocumentFrame` (paisagem quando passa de 6 colunas) com a linha "Filtros: …".
2. **Dado** a lista filtrada, **quando** exporto, **então** a contagem de linhas do arquivo é igual ao `total` do `GET` da lista com os mesmos filtros, e os totais do rodapé (quando a tela tem) são iguais aos da tela.
3. **Dado** mais linhas que o teto (10.000 xlsx, 2.000 PDF), **quando** exporto, **então** 422 `export_too_large` e a mensagem da EXP-01 "A lista tem {n} linhas; o limite é {teto}. Filtre o período." aparece abaixo do botão, sem gerar.
4. **[Segurança]** **Dado** um usuário sem `finance.view`, **quando** chama qualquer `/export` ou `/pdf` desta story, **então** 403 `forbidden`; com o módulo `finance` não liberado, 403 `module_not_released` (coberto pela varredura `EndpointAuthorizationTests`, sem teste repetido); o botão "Exportar" não aparece sem a permissão da tela.
5. **[Segurança]** **Dado** qualquer exportação desta story, **quando** termina, **então** a auditoria registra usuário, data, entidade, formato, linhas e filtros (`RecordExportAsync`), e a resposta leva `Cache-Control: no-store`.
6. **[Segurança]** **Dado** uma coluna que a tela mascara para o usuário (contraparte das linhas do banco, conta do favorecido, folha por pessoa), **quando** exporto, **então** sai mascarada **igual à tela** (mesmo serviço de leitura, sem regra nova), sem pedir 2FA (decisão do PO de 02/11/2026).

**Extrato da conta (FIN-04)**
7. **Dado** uma conta e o período **De/Até** (e "Só movimentos sem OP", se marcado), **quando** exporto em xlsx, **então** a primeira linha é o **Saldo inicial** ou **Saldo anterior** (o rótulo da tela), seguem os movimentos com o saldo depois de cada um e a última linha é o **Saldo final**; estornos e lançamentos estornados vêm marcados na coluna "Situação".
8. **Dado** o mesmo filtro, **quando** escolho "PDF" no menu, **então** baixa o **extrato do período** (`GET /api/v1/finance/bank-accounts/{id}/statement/pdf?from&to&withoutOrder`), documento próprio no frame: título "Extrato da conta", conta (nome, banco, agência e número **como a tela mostra**), período, saldo inicial/anterior, tabela de movimentos (data, descrição, origem com número, entrada, saída, saldo), totais de entradas e saídas, saldo final e a nota "Movimentos conciliados até {data}" quando houver; sem saldo inicial informado (`OpeningMissing`), a faixa "Saldo inicial da conta não informado: os saldos podem estar errados." no topo.

**Extrato do banco (BNK-03a, aba `extrato-banco`)**
9. **Dado** a aba Extrato do banco com período e situação, **quando** exporto (xlsx ou PDF da lista), **então** saem as linhas importadas (API ou OFX) com a situação (Pendente, Conciliada, Ignorada com o motivo, Desfeita), a origem e "Verificada pelo banco"; o CPF/CNPJ da contraparte sai **mascarado quando `CounterpartyMasked`**.

**Conciliação (BNK-03b)**
10. **Dado** conta e período na Conciliação, **quando** exporto em xlsx, **então** o arquivo tem uma aba por bloco da tela, "Conciliados", "Sugestões" (pares ainda não aceitos, com motivo e confiança), "Sem par no banco" e "No sistema, não no banco" (com a coluna "Há mais de 5 dias"), mais a aba "Resumo" (saldo do banco e do sistema no fim do período, diferença, "Conciliada até", "Fechada até") e a aba "Filtros"; usa a leitura de `GetReconciliation` (`finance.view`), não as de `finance.edit`.
11. **Dado** o mesmo filtro, **quando** escolho "PDF", **então** baixa o **relatório de conciliação** (`GET /api/v1/finance/bank-accounts/{id}/reconciliation/pdf?from&to`) com o resumo no topo e os blocos em seções; com o período **não fechado** (`ClosedUntil` antes de "Até" ou nulo), o PDF leva a marca **"Prévia"** (`PdfMark.Preview`) em todas as páginas; fechado até "Até" ou depois, sem marca.

**Ordens de pagamento (FIN-03a)**
12. **Dado** a lista com os filtros da tela (situação, "Aguardando a minha aprovação", "Autoaprovadas"), **quando** exporto, **então** saem número, situação, data prevista, total, quantidade de títulos, solicitante, enviada em e "Autoaprovada"; o rodapé do PDF e a última linha do xlsx somam o total do filtro. O PDF de cada ordem é o da EXP-01 (não muda aqui).

**A receber (FIN-01)**
13. **Dado** a aba A receber com a busca e os filtros (situação, cliente, vencimento de/até, contrato, "Com boleto"), **quando** exporto, **então** saem os títulos da lista com as colunas da tabela "Telas e conteúdo" e as linhas de total "Em aberto" e "Vencido" iguais ao rodapé da tela; o filtro "Pagamentos a conferir" também exporta.

**DRE (FIN-08)**
14. **Dado** período e "até o mês" do DRE, **quando** exporto em xlsx, **então** a planilha tem as linhas na ordem da tela **com a hierarquia**: coluna "Código", coluna "Linha" com recuo por nível (`Level`, recuo da célula, sem espaços no texto), subtotais e resultados (`= …`) em negrito, os meses em colunas (`MMM/AA`), "Total do período", "% da receita líquida" e "Δ mês anterior" (valor e %); o **drill-down não vai** (sem documentos por célula); os meses provisórios têm "(provisório)" no cabeçalho; os avisos "A classificar" vão na aba "Avisos".
15. **Dado** o mesmo filtro, **quando** escolho "PDF", **então** baixa o **DRE do período** (`GET /api/v1/finance/dre/pdf?from&to`) em paisagem, com a mesma estrutura do xlsx (grupos abertos até o nível da conta), o sinal no rótulo como na tela, resultado negativo em vermelho pelo token semântico do `PdfTheme`, a faixa "Provisório: até a emissão de notas no sistema, a receita é a data de emissão dos títulos." quando `IsProvisional`, e a nota "Gerencial, por competência; não substitui a contabilidade."; acima de 24 meses (o maior período da tela) a API recusa com 422.
16. **[Segurança]** **Dado** um usuário sem `hr.view_sensitive`, **quando** exporta o DRE, **então** a folha sai como **uma linha só**, igual à tela; com a ação, as contas da folha saem como a tela mostra para ele (nunca nome nem valor por funcionário).

**Fluxo de caixa (FIN-05, FIN-07)**
17. **Dado** a visão (diária, semanal ou mensal), o horizonte (30, 60 ou 90 dias), a conta e o **cenário** (Os dois, Base ou Ajustado), **quando** exporto em xlsx, **então** cada período é uma linha com início, fim, "Realizado/Projetado", entradas, saídas, vencidos em aberto (entradas e saídas) e saldo **por cenário** (com "Os dois": colunas "Saldo base" e "Saldo ajustado" lado a lado); a aba "Destaques" traz o saldo atual, o menor saldo e o primeiro dia negativo de cada cenário, a diferença causada por atrasos, o risco de não receber (total e clientes acima do limite) e "Deslocado para depois de"; a aba "A receber sem data" lista as parcelas que esperam marco.
18. **Dado** o mesmo filtro, **quando** escolho "PDF", **então** baixa a lista em paisagem com os destaques no topo e a tabela de períodos; sem histórico de pagamento, a nota "Ainda não há histórico de pagamento: o ajustado é igual à base."; filtrando por conta, a nota "Projeção só no consolidado." (regra da tela). Sem gráfico no PDF (a tabela é a mesma do "Ver como tabela").
19. **[Segurança]** **Dado** um usuário que não aprova nem paga no Financeiro, **quando** exporta o fluxo, **então** a folha prevista sai só pelo total de cada folha, igual à tela.

**Inadimplência (FIN-06a, FIN-06b)**
20. **Dado** a aba "Por cliente", **quando** exporto, **então** saem cliente, as 4 faixas (até 30, 31 a 60, 61 a 90, mais de 90), total vencido, maior atraso, quantidade de títulos, atraso médio e % pago com atraso ("Sem histórico" como texto), do maior saldo para o menor, com a linha de total igual aos cartões; **dado** a aba "Por projeto", saem projeto, cliente e as faixas, com "Sem projeto" no fim; o CPF/CNPJ do cliente **não** vai (a tela não mostra).

**Ajuda**
21. **Dado** cada tela desta story, **quando** abro a Ajuda, **então** existe a seção "Imprimir e exportar" dizendo o que sai no Excel e no PDF, que os filtros da tela valem, o limite de linhas e o que sai mascarado; o DRE e o fluxo explicam que o drill-down e o gráfico não vão para o arquivo.

## Telas e conteúdo
Todos os botões ficam na **barra da lista** (`<ExportMenu>` no slot `toolbar`), com a **permissão da tela** (`finance.view`). "PDF da lista" = `ListPdfDocument` genérico; "PDF próprio" = documento no `PdfDocumentFrame` com layout desta story. Tipos: **texto**, **data** (`dd/MM/yyyy`, célula de data), **moeda** (`R$ #,##0.00`, `decimal`), **número**, **%** (2 casas), **sim/não**.

| Tela (rota, aba) | Endpoint novo | xlsx: colunas (tipo; máscara) | PDF | Marca |
|---|---|---|---|---|
| Extrato da conta (`/app/financeiro/contas/$id/extrato`) | `GET /finance/bank-accounts/{id}/statement/export?format=xlsx&from&to&withoutOrder` · `GET .../statement/pdf` | Data (data) · Descrição (texto) · Tipo (texto) · Origem (texto, número do documento) · Entrada (moeda) · Saída (moeda) · Saldo (moeda) · Sem OP (sim/não) · Situação (texto: Estornado/Estorno/—); linhas de saldo inicial/anterior e final | Próprio: "Extrato da conta" (CA 8) | Nenhuma; faixa de saldo inicial ausente |
| Extrato do banco (mesma rota, aba `extrato-banco`) | `GET /finance/bank-accounts/{id}/statement-lines/export?format=xlsx\|pdf&from&to&status` | Data (data) · Descrição (texto) · Tipo (texto) · Contraparte (texto) · CPF/CNPJ da contraparte (texto; **mascarado** quando `CounterpartyMasked`) · Valor (moeda, saída negativa) · Origem (texto: API/OFX) · Verificada pelo banco (sim/não) · Situação (texto) · Motivo de ignorar (texto) | Da lista | Nenhuma |
| Conciliação (`/app/financeiro/conciliacao`) | `GET /finance/bank-accounts/{id}/reconciliation/export?format=xlsx&from&to` · `GET .../reconciliation/pdf` | Abas (CA 10). Conciliados e Sugestões: Data (data) · Linha do banco (texto) · Valor (moeda) · Transação do sistema (texto, origem e número) · Valor (moeda) · Motivo (texto) · Confiança (texto) · Conciliado por/em (texto/data). Sem par no banco: Data · Descrição · Tipo · Valor · Contraparte (mascarada como a tela). No sistema, não no banco: Data · Descrição · Origem · Valor · Há mais de 5 dias (sim/não). Resumo: saldos e datas | Próprio: "Relatório de conciliação" (CA 11) | **Prévia** com o período não fechado |
| Ordens de pagamento (`/app/financeiro/ordens-de-pagamento`) | `GET /finance/payment-orders/export?format=xlsx\|pdf&situation&awaitingMe&selfApproved` | Número (texto) · Situação (texto) · Data prevista (data) · Total (moeda) · Títulos (número) · Solicitante (texto) · Enviada em (data e hora, São Paulo) · Autoaprovada (sim/não); linha de total | Da lista | Nenhuma (a marca por situação é do PDF de cada ordem, EXP-01) |
| A receber (`/app/financeiro/receber`, aba `receber`) | `GET /finance/receivables/export?format=xlsx\|pdf&q&situation&partyId&dueFrom&dueTo&contractId&withBillingSlip&sort` | Número (texto) · Cliente (texto) · Contrato/projeto (texto) · Parcela/marco (texto) · Emissão (data) · Vencimento (data; "Aguarda: {marco}" na prevista) · Valor (moeda) · Recebido (moeda) · Saldo (moeda) · Situação (texto) · Boleto (texto: situação) · Implantação (sim/não); linhas "Em aberto" e "Vencido" | Da lista | Nenhuma |
| DRE (`/app/financeiro/dre`) | `GET /finance/dre/export?format=xlsx&from&to` · `GET /finance/dre/pdf?from&to` | Código (texto) · Linha (texto, recuo por nível) · um mês por coluna (moeda) · Total do período (moeda) · % da receita líquida (%) · Δ mês anterior (moeda) · Δ % (%); abas "Avisos" e "Filtros" | Próprio: "DRE gerencial" (CA 15), paisagem | Faixa "Provisório" quando `IsProvisional` (não é marca d'água) |
| Fluxo de caixa (`/app/financeiro/fluxo-de-caixa`) | `GET /finance/cash-flow/export?format=xlsx\|pdf&view&days&bankAccountId&scenario` | Início (data) · Fim (data) · Tipo (texto: Realizado/Projetado/Hoje) · Entradas (moeda) · Saídas (moeda) · Vencidos a receber (moeda) · Vencidos a pagar (moeda) · Saldo base (moeda) · Entradas ajustadas (moeda) · Saldo ajustado (moeda) — as colunas do cenário não escolhido saem; abas "Destaques", "A receber sem data", "Filtros" | Da lista, paisagem, com os destaques no topo | Nenhuma; notas do CA 18 |
| Inadimplência (`/app/financeiro/inadimplencia`, abas `clientes` e `projetos`) | `GET /finance/aging/export?format=xlsx\|pdf&sort` · `GET /finance/aging/projects/export?format=xlsx\|pdf` | Por cliente: Cliente (texto) · Até 30 (moeda) · 31 a 60 (moeda) · 61 a 90 (moeda) · Mais de 90 (moeda) · Total vencido (moeda) · Maior atraso (número, dias) · Títulos (número) · Atraso médio (número, dias, ou "Sem histórico") · % pago com atraso (%); linha de total. Por projeto: Projeto (texto) · Cliente (texto) · faixas e total (moeda) | Da lista | Nenhuma |

## Regras de negócio e técnicas
- **Mesma consulta:** cada `/export` chama o mesmo serviço da tela (`StatementAsync`, `ViewAsync`, `ListAsync`, `BuildAsync`, `ReportAsync`, `ProjectsAsync`) sem paginação (`PageRequest` de exportação da EXP-01) e monta o `ExportTable`; nenhum SQL novo. O endpoint de exportação aceita **exatamente os mesmos filtros** do `GET` da tela (mesmos nomes de parâmetro), para o `<ExportMenu>` repassar o estado da URL.
- **Várias abas no xlsx** (Conciliação, DRE, Fluxo): se o `ITabularExporter` da EXP-01 só montar uma tabela, esta story acrescenta `ExportWorkbook(sheets)` nos BuildingBlocks (cada aba é um `ExportTable`; "Filtros" continua automática). Sem fórmula em nenhuma célula (os totais são valores calculados pelo servidor).
- **Hierarquia do DRE no xlsx:** recuo por `IndentLevel` do ClosedXML pelo `Level`; negrito para `Kind` subtotal/resultado; agrupamento de linhas (outline) por grupo, aberto. No PDF, recuo de 8 pt por nível.
- **PDFs próprios** (extrato, conciliação, DRE): layouts no Finance (`StatementPdfDocument`, `ReconciliationPdfDocument`, `DrePdfDocument`) sobre o `PdfDocumentFrame`; o Finance não referencia QuestPDF fora deles (ADR 0022). Limites: 2.000 linhas (o mesmo teto do PDF de lista); DRE até 24 meses.
- **Datas:** o período é `date`; "Enviada em" e "Emitido em" em `America/Sao_Paulo`. Dinheiro `decimal(18,2)`; nunca `double` ao montar a planilha.
- **Nome do arquivo:** `extrato-{nome curto da conta}-{de}-{ate}`, `extrato-banco-…`, `conciliacao-…`, `ordens-de-pagamento-{data}`, `contas-a-receber-{data}`, `dre-{de}-{ate}`, `fluxo-de-caixa-{data}`, `inadimplencia-{clientes|projetos}-{data}`; só letras, números e hífen.
- **Sem dado novo exposto:** a exportação nunca traz coluna que a tela não mostra para aquele usuário.

## Permissões, eventos e tabelas
- **Permissões:** `finance.view` em todas as rotas desta story (as mesmas das telas); as máscaras seguem `hr.view_sensitive` (DRE), `finance.approve`/`finance.pay` (folha prevista no fluxo) e a regra de contraparte da BNK-03a. Nenhuma ação nova no `ModuleCatalog`.
- **Eventos:** nenhum. **Tabelas:** nenhuma (auditoria de exportação na tabela da EXP-01).
- **Migration:** nenhuma.

## Telas e estados
- **Onde:** "Exportar" na barra da lista de cada tela da tabela; no extrato, a aba ativa decide o que sai (Extrato da conta ou Extrato do banco); na inadimplência, a aba ativa (clientes ou projetos).
- **Estados (do `<ExportMenu>`):** "Gerando…" com `aria-busy`; erro abaixo do botão (`role="alert"`) com a mensagem do ProblemDetails e "Tentar de novo"; 422 de teto com a mensagem do CA 3. Lista vazia: "Exportar" desabilitado com a dica "Nada para exportar com estes filtros.".
- **Propositivo:** o período e a conta vêm os da tela; no fechamento (Conciliação "Conciliada até"), o próximo passo "Baixar relatório de conciliação" aparece ao lado de "Fechar conciliação"; no DRE, "Exportar" fica ao lado do seletor de período.
- **400 px:** "Exportar" vai para "Mais ações" da barra, como o "Exportar CSV" do rastreio.

## Ajuda (mesmo commit)
Seção **"Imprimir e exportar"** em: `Ajuda/financeiro-extrato.md`, `Ajuda/financeiro-contas-extrato-banco.md`, `Ajuda/financeiro-conciliacao.md`, `Ajuda/financeiro-ordens-de-pagamento.md`, `Ajuda/financeiro-receber.md`, `Ajuda/financeiro-dre.md`, `Ajuda/financeiro-fluxo-de-caixa.md`, `Ajuda/financeiro-inadimplencia.md` e `Ajuda/financeiro-inadimplencia-projetos.md`. `<Dica>` em "Prévia" do relatório de conciliação ("Período ainda não fechado: os números podem mudar.").

## Plano de testes
- **Unidade:** montagem do `ExportTable` de cada tela a partir do resultado do serviço (colunas, tipos, linhas de saldo e de total); recuo e negrito do DRE; colunas por cenário do fluxo; marca "Prévia" pela regra do `ClosedUntil`.
- **Integração (Testcontainers, dataset fixo do financeiro):** para cada `/export`, abrir o xlsx com ClosedXML e conferir contagem = `total` da lista com os mesmos filtros, totais iguais aos da tela e tipos das células (CA 1, 2, 7, 10, 12–14, 17, 20); teto com 422 (CA 3, uma tela); auditoria e `no-store` (CA 5); máscara: contraparte mascarada (CA 9), DRE sem `hr.view_sensitive` com a folha numa linha (CA 16) e folha prevista só pelo total (CA 19); PDFs próprios (extrato, conciliação com e sem marca, DRE) com texto extraído esperado na imagem Alpine (CA 8, 11, 15). Acesso por rota fica com a varredura (`EndpointAuthorizationTests`).
- **Vitest:** um teste por tela de que o `<ExportMenu>` recebe os filtros atuais da URL e some sem `finance.view` (o componente em si já é testado na EXP-01).
- **E2E (Playwright, 1 fluxo):** DRE → "Exportar" → "Planilha (Excel)": `page.waitForEvent('download')`, nome `dre-….xlsx` e tipo; conteúdo fica no teste de integração.

## Fora do escopo
- Lista de A pagar e PDF da ordem de pagamento: EXP-01 (piloto).
- Descontos e cancelamentos, plano de contas e centros de custo: EXP-05.
- Exportação acima do teto por job com aviso no sino: evolução (risco "Desempenho" da auditoria).
- Gráficos no PDF; envio do arquivo por e-mail à contadora.

## Pontos em aberto
Nenhum que segure a story. Decisões aplicadas: máscara sem 2FA (PO, 02/11/2026); tetos de 10.000/2.000 (EXP-01).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, xlsx lido com ClosedXML e PDF na imagem Alpine), Vitest e o E2E passando.
- [ ] Nenhuma migration (confirmar); nenhuma permissão nova.
- [ ] Telas com os estados do `<ExportMenu>`, inclusive a 400 px.
- [ ] Ajuda das 9 telas com a seção "Imprimir e exportar"; teste de cobertura passando.
- [ ] Auditoria de exportação ligada em todas as rotas.
- [ ] OpenAPI e cliente Orval regenerados (`getExport…Url`).
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `ExportWorkbook` (várias abas) nos BuildingBlocks, se a EXP-01 não trouxer.
- [ ] Endpoints `/export` e os três PDFs próprios no Finance; `StatementPdfDocument`, `ReconciliationPdfDocument`, `DrePdfDocument`.
- [ ] `<ExportMenu>` nas 8 telas (9 abas); ajudas.

## Quality gates
- Agentes previstos: @dev, cybersecurity (máscara e auditoria), @qa.
- Gate: CA 2, 6, 16 e 19 obrigatórios.

## Checklist de prontidão
Checklist do SM, 02/11/2026, sobre o pedido do PO do mesmo dia.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` rastreáveis | OK (pedido do PO + R- do PRD) |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão | OK (CA 4–6, 16, 19) |
| 6 | Dependências coerentes | OK (EXP-01) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas | OK (auditoria, stories FIN/BNK, código lido) |
| 9 | Telas com estados, ajuda e propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados | OK |

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 02/11/2026 | 0.1 | Story detalhada a partir da auditoria de PDF e exportação (onda 1, financeiro); `Ready` pelo pedido do PO de 02/11/2026 | Claude (SM) |
