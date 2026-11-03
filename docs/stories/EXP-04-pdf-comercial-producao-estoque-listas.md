# EXP-04 — Onda 2: PDFs do comercial, da produção, das compras e do estoque, Excel das listas operacionais, holerite e boleto à mão

| Campo | Valor |
|---|---|
| ID | EXP-04 |
| Título | PDFs do contrato, do aditivo, da OS de instalação com o termo de aceite técnico em branco, do recebimento de mercadoria, da requisição de material, do ajuste de estoque, da folha de contagem e do resultado do inventário e da não conformidade; exportação xlsx das listas de compras, estoque, comercial e produção; holerite com o rótulo "Baixar holerite" e atalho na ficha do funcionário; boleto no cabeçalho do título e na lista de A receber |
| Tamanho | G |
| Status | Ready |
| Requisitos | Pedido do PO de 02/11/2026 (auditoria, seções A, B, D4 "EXP-04" e E) · R-PRJ-3 e R-PRJ-6 (OS de instalação e aceite técnico) · R-CMP-5 (recebimento) · R-EST-6 e R-EST-9 (ajuste e inventário) · R-PRD-1 · R-RH-5 (holerite) · R-COM-3 · PRD §6 LGPD · R-SEC-5 |
| Dependências | EXP-01 (frame, marcas, `ITabularExporter`, `<DocumentActions>`, `<ExportMenu>`); EXP-03 (`PdfStageIndicator`, `PdfQr`); COM-04a, COM-07, COM-06, CMP-01 a CMP-04, EST-01a, EST-01b, EST-03, EST-05, PRD-01, PRD-05, RH-02b, BNK-01b (telas e consultas entregues) |
| Bloqueia | — |
| Ordem de execução | 4 (Pós-go-live: documentos e exportação) |

## User story
Como **gestor comercial, PCP, almoxarife, comprador e RH**, quero **imprimir os documentos do dia a dia (contrato, aditivo, OS de instalação e termo de aceite, recebimento, requisição, ajuste, inventário e NC), exportar as listas em Excel e achar holerite e boleto sem procurar**, para **assinar, conferir e arquivar sem copiar tela**.

## Contexto e referências
- Auditoria de 02/11/2026 (`pdf-export-auditoria.md`): os 9 documentos desta story **não têm PDF**; o termo de aceite assinado hoje só entra por upload (`InstallationDocuments`); holerite fica a 3 níveis (Folha → competência → aba Holerites) com o rótulo "Baixar" e não aparece na ficha do funcionário; o boleto só se baixa na aba Boleto do título.
- **EXP-01 (base):** `GET .../{id}/pdf`, `GET .../export?format=xlsx|pdf`, tetos 10.000/2.000 com 422 `export_too_large`, `Sensitivity` por coluna com máscara igual à tela (**sem 2FA**, PO 02/11/2026), auditoria de exportação, `<DocumentActions>` e `<ExportMenu>`.
- Regras de exibição herdadas: custo e margem do orçamento só com `quotes.view_cost` e do projeto com `projects.view_cost` (D7); custo estimado da NC só com `projects.view_cost` (PRD-05); contagem do inventário **cega** (EST-05); holerite só com `hr.view_sensitive` e CPF mascarado no PDF (RH-02b); custo de material da OS só com `projects.view_cost` (EST-03).

## Critérios de aceite
**PDFs de documento (comum)**
1. **Dado** a permissão `view` do documento e um dos documentos da tabela "PDFs", **quando** clico "Baixar PDF" no cabeçalho (`<DocumentActions>`), **então** baixa `{número}.pdf` no `PdfDocumentFrame`, com o conteúdo e a marca da tabela; acima de 300 linhas, 422 antes de gerar; dados da empresa incompletos, 422 com o link (regra do frame).
2. **[Segurança]** **Dado** um usuário sem a permissão `view` do documento, **quando** chama o `/pdf`, **então** 403 (varredura); id de outro documento, ou aditivo de outro contrato, dá 404 (IDOR, teste próprio no aditivo); cada download entra na auditoria de exportação.
3. **[Segurança]** **Dado** um PDF desta story com custo ou margem na tela (contrato, aditivo, NC), **quando** o usuário não tem a ação de custo (`projects.view_cost`), **então** o PDF não traz custo, margem nem custo estimado (teste de extração de texto); o recebimento, a requisição e o termo de aceite **nunca** trazem custo, para ninguém.

**Contrato e aditivo (COM-04a, COM-07)**
4. **Dado** `projects.view` e um contrato, **quando** baixo o PDF, **então** sai o **resumo do contrato**: número, situação, cliente (razão social, CNPJ/CPF como a tela mostra, endereço), projeto, orçamento de origem e revisão, data de assinatura, valor, itens (descrição, quantidade, preço), **marcos de pagamento** (marco, %, valor, prazo, situação da parcela: prevista, em aberto, paga), data prometida, endereço da obra, aditivos aprovados (número, data, efeito no valor e no prazo) e as assinaturas "Contratante" e "Contratada"; o contrato de implantação (COM-04c) leva a faixa "Contrato em andamento antes do BemAqui" e as parcelas "Recebido antes da implantação".
5. **Dado** `projects.view` e um aditivo, **quando** baixo o PDF, **então** sai o aditivo com o contrato de origem, o motivo, o **antes → depois** de valor, escopo (itens incluídos e retirados) e prazo, os recebíveis futuros ajustados, a aprovação (quem e quando) e as assinaturas; `Draft` e `PendingApproval` saem com **"Rascunho"**, `Rejected` com **"Cancelado"**, `Approved` sem marca.
6. **Dado** o contrato `Cancelled`, **quando** baixo, **então** sai com **"Cancelado"** e o motivo; `Active` e `Completed` sem marca.

**OS de instalação e termo de aceite técnico (COM-06)**
7. **Dado** `production.view` e uma OS de instalação, **quando** baixo o PDF, **então** sai a **OS de instalação**: número, situação, projeto, cliente, endereço da obra, instalador (terceiro), previsão de início e fim, o indicador de etapa, as etapas (nome, situação, data de conclusão) e o checklist da entrega técnica com caixas em branco (ou marcadas, quando concluído); o valor combinado com o instalador **não** sai (é dado do CP); `Cancelled` com **"Cancelado"**.
8. **Dado** a OS de instalação `Open` ou `InProgress`, **quando** clico "Baixar termo de aceite" (no cabeçalho e na aba Documentos, ao lado do upload do termo assinado), **então** baixa o **termo de aceite técnico em branco** (`GET /production/installations/{id}/acceptance-term/pdf`): identificação da empresa, do cliente, do projeto e da obra, o equipamento (itens e números de série expedidos no romaneio do projeto), o checklist da entrega técnica com "Conforme / Não conforme / Observação" em branco, campos de data, nome, documento e assinatura de quem aceita pelo cliente e do instalador, e o texto "Declaro que recebi o equipamento instalado e em funcionamento, conforme o checklist acima."; com a OS `Completed`, o termo sai preenchido com a data e quem aceitou (do registro do aceite) e a faixa "Aceite registrado em {data}"; o upload do termo assinado continua como hoje.

**Compras e estoque**
9. **Dado** `purchasing.view` e um recebimento, **quando** baixo o PDF, **então** sai o **comprovante de recebimento**: número, data, pedido de origem, fornecedor, nota do fornecedor quando houver, itens (código, descrição, quantidade recebida, unidade, lote, local), quem recebeu, a marca "Recebido pelo Proprietário único" quando houver, e as assinaturas "Recebido por" e "Conferido por"; recebimento estornado sai com **"Cancelado"** e o motivo do estorno; sem custo de entrada (CA 3).
10. **Dado** `inventory.view` e uma requisição de material, **quando** baixo o PDF, **então** sai a **folha de separação**: número, OS e etapa, projeto, quem pediu, itens (código, descrição, quantidade, unidade, local, lote sugerido, reservado) com a coluna "Separado" em branco e as assinaturas "Separado por" e "Recebido por"; `PendingApproval` sai com a faixa "Aguardando aprovação: não separe."; `Rejected` e `Reversed` com **"Cancelado"**; `Requested` e `Completed` sem marca.
11. **Dado** `inventory.view` e um ajuste de estoque, **quando** baixo o PDF, **então** sai o ajuste: número, modo (Quantidade contada ou Diferença), local, motivo, quem lançou, itens (saldo no envio, contado ou diferença, valor pelo custo médio, como a tela) e totais de sobras, faltas e valor; `Pending` sai com **"Prévia"** e a faixa "Aguardando aprovação"; `Approved` sem marca (estornado: faixa "Estornado em {data}: {motivo}"); `Rejected` e `Cancelled` com **"Cancelado"**.
12. **[Segurança]** **Dado** um inventário `Counting`, **quando** baixo a **folha de contagem** (`GET /inventory/inventory-counts/{id}/count-sheet/pdf`), **então** ela lista local, itens e lotes do escopo (código, descrição, unidade de estoque) com "Quantidade contada" e "Contado por" em branco, **sem o saldo do sistema** (contagem cega; teste de extração de texto), ordenada por código; com recontagem pendente, as linhas de recontagem vêm primeiro com "Recontagem".
13. **Dado** um inventário `Closed`, **quando** baixo o **resultado** (`GET /inventory/inventory-counts/{id}/pdf`), **então** sai cada linha com saldo da foto, contado, diferença, valor e quem contou, os totais de sobras, faltas e valor líquido, a escolha para os não contados ("Considerar como zero" ou "Manter o saldo do sistema") e o ajuste gerado com a situação; `Counting` recusa o resultado com 409 "Feche o inventário para ver o resultado."; `Cancelled` sai com **"Cancelado"**.

**Não conformidade (PRD-05)**
14. **Dado** `production.view` e uma NC, **quando** baixo o PDF, **então** sai: número, situação, OS, projeto, etapa de qualidade, tentativa, idade, responsável, etapa de retorno, descrição, causa, ação corretiva, resultado, o checklist reprovado da inspeção, a lista de fotos e anexos (nome e data, sem embutir) e o **custo estimado só com `projects.view_cost`**; `Open` e `InAction` saem com a faixa "Em aberto"; `Closed` sem marca.

**Listas em Excel**
15. **Dado** a permissão da lista e uma das listas da tabela "Listas", **quando** escolho "Exportar" → "Planilha (Excel)" na barra da lista, **então** o xlsx traz as linhas com os filtros, a busca e o "Mostrar" atuais (inclusive "Aguardando você"), com a contagem igual ao `total` da lista e as colunas da tabela; essas listas **não** têm "PDF da lista" (só xlsx), salvo Saldos e Movimentos de estoque, que têm os dois.
16. **[Segurança]** **Dado** a coluna de custo ou margem nas listas de orçamentos e projetos, **quando** o usuário não tem `quotes.view_cost` ou `projects.view_cost`, **então** as colunas **saem do arquivo** (não aparecem vazias), igual à tela; "% recebido" dos projetos só com `finance.view`.

**Holerite (RH-02b)**
17. **Dado** `hr.view_sensitive` e a aba Holerites da folha, **quando** vejo a linha do funcionário, **então** o botão se chama **"Baixar holerite"** (o mesmo `GET /hr/payroll-runs/{id}/payslips/{pid}/pdf`), e "Baixar todos os holerites" não muda.
18. **Dado** `hr.view_sensitive` e a ficha do funcionário, **quando** abro a aba Geral, **então** o bloco **"Holerites"** mostra os 6 últimos (competência, tipo: Mensal, Férias, 13º, Rescisão; líquido; situação da folha) com "Baixar holerite" em cada um e "Ver todos" para a folha filtrada pelo funcionário, lidos de `GET /hr/employees/{id}/payslips` (novo, `hr.view_sensitive`, `Cache-Control: no-store`); sem a ação, o bloco não aparece e a chamada não é feita; holerite de folha em rascunho sai com a marca d'água de hoje (RH-02b).

**Boleto (BNK-01b)**
19. **Dado** `finance.view` e um título a receber com boleto `Issued` ou `Paid`, **quando** abro o título, **então** o cabeçalho tem **"Baixar boleto"** (o mesmo arquivo da aba Boleto, `boleto-{número}.pdf`); na lista A receber, a linha com boleto emitido tem "Baixar boleto" no menu da linha; boleto `Cancelled` ou `Expired` não mostra o atalho.

**Ajuda**
20. **Dado** cada tela desta story, **quando** abro a Ajuda, **então** a seção "Imprimir e exportar" diz o que sai, as marcas por situação e, nas listas, o que sai mascarado ou some.

## PDFs (por tela)
Botão no **cabeçalho do documento** (`<DocumentActions>`: "Baixar PDF" e "Imprimir"), com a **permissão da tela**.

| Documento (rota) | Endpoint | Permissão | Seções do PDF | Marca por situação |
|---|---|---|---|---|
| Contrato (`/app/contratos/$id`) | `GET /commercial/contracts/{id}/pdf` | `projects.view` | CA 4 | `Cancelled`: **Cancelado**; implantação: faixa |
| Aditivo (`/app/contratos/$id/aditivos/$aditivoId`) | `GET /commercial/contracts/{id}/amendments/{amendmentId}/pdf` | `projects.view` | CA 5 | `Draft`/`PendingApproval`: **Rascunho**; `Rejected`: **Cancelado** |
| OS de instalação (`/app/producao/instalacoes/$id`) | `GET /production/installations/{id}/pdf` | `production.view` | CA 7 | `Cancelled`: **Cancelado** |
| Termo de aceite técnico (mesma tela, cabeçalho e aba `documentos`) | `GET /production/installations/{id}/acceptance-term/pdf` | `production.view` | CA 8 | Em branco até o aceite; `Completed`: preenchido + faixa; `Cancelled`: 409 |
| Recebimento (`/app/compras/recebimentos/$id`) | `GET /purchasing/goods-receipts/{id}/pdf` | `purchasing.view` | CA 9 | Estornado: **Cancelado** |
| Requisição de material (`/app/estoque/requisicoes/$id`) | `GET /inventory/requisitions/{id}/pdf` | `inventory.view` | CA 10 | `PendingApproval`: faixa; `Rejected`/`Reversed`: **Cancelado** |
| Ajuste de estoque (`/app/estoque/ajustes/$id`) | `GET /inventory/adjustments/{id}/pdf` | `inventory.view` | CA 11 | `Pending`: **Prévia**; estornado: faixa; `Rejected`/`Cancelled`: **Cancelado** |
| Inventário: folha de contagem (`/app/estoque/inventarios/$id`, aba `contagem`) | `GET /inventory/inventory-counts/{id}/count-sheet/pdf` | `inventory.view` | CA 12 (sem saldo) | Só em `Counting`; demais: 409 |
| Inventário: resultado (mesma tela) | `GET /inventory/inventory-counts/{id}/pdf` | `inventory.view` | CA 13 | `Counting`: 409; `Cancelled`: **Cancelado** |
| Não conformidade (`/app/producao/nao-conformidades/$id`) | `GET /production/non-conformities/{id}/pdf` | `production.view` | CA 14 | `Open`/`InAction`: faixa "Em aberto" |

## Listas (xlsx)
"Exportar" na **barra da lista** (`<ExportMenu formats={['xlsx']}>`, ou `['xlsx','pdf']` onde indicado), com a **permissão da lista**. Endpoint `GET .../export?format=…` com os mesmos filtros do `GET` da lista. Tipos: texto, data, moeda, número (quantidade com 4 casas), %, sim/não.

| Lista (rota) | Permissão | Colunas (tipo; máscara) |
|---|---|---|
| Solicitações de compra (`/app/compras/solicitacoes`) | `purchasing.view` | Número (texto) · Origem (texto) · Destino (texto: projeto ou Estoque geral) · OS (texto) · Itens (número) · Necessidade mais próxima (data) · Situação (texto) · Criada em (data) |
| Cotações (`/app/compras/cotacoes`) | `purchasing.view` | Número (texto) · Itens (número) · Fornecedores (número) · Responderam (número) · Criada em (data) · Situação (texto) |
| Pedidos de compra (`/app/compras/pedidos`) | `purchasing.view` | Número (texto) · Fornecedor (texto) · Destino (texto) · Total (moeda) · Previsão de entrega (data) · Situação (texto) · Aprovado em (data) · Enviado em (data); linha de total |
| Recebimentos (`/app/compras/recebimentos`, abas a receber e `recebidos`) | `purchasing.view` | A receber: Pedido (texto) · Fornecedor (texto) · Previsão (data) · Saldo a receber (moeda) · Situação (texto). Recebidos: Número (texto) · Pedido (texto) · Fornecedor (texto) · Data (data) · Itens (número) · Total (moeda) · Recebido por (texto) · Estornado (sim/não) |
| Saldos de estoque (`/app/estoque/saldos`) — **xlsx e PDF** | `inventory.view` | Código (texto) · Item (texto) · Unidade (texto) · Local (texto) · Físico (número) · Reservado (número) · Disponível (número) · Mínimo (número) · Custo médio (moeda, 6 casas) · Valor (moeda) · Situação (texto); linha de total do valor. Custo e valor como a tela mostra (sem ação de custo no Estoque hoje) |
| Movimentos (`/app/estoque/movimentos`) — **xlsx e PDF** | `inventory.view` | Data (data) · Número (texto) · Tipo (texto) · Origem (texto) · Item (texto) · Local (texto) · Lote/série (texto) · Quantidade (número, saída negativa) · Custo unitário (moeda, 6 casas) · Valor (moeda) · Lançado por (texto) |
| Ajustes (`/app/estoque/ajustes`) | `inventory.view` | Número (texto) · Modo (texto) · Local (texto) · Itens (número) · Sobras (moeda) · Faltas (moeda) · Valor líquido (moeda) · Lançado por (texto) · Situação (texto) · Decidido por/em (texto/data) |
| Requisições (`/app/estoque/requisicoes`) | `inventory.view` | Número (texto) · OS (texto) · Etapa (texto) · Projeto (texto) · Itens (número) · Pedida por (texto) · Pedida em (data) · Situação (texto) |
| Inventários (`/app/estoque/inventarios`) | `inventory.view` | Número (texto) · Local (texto) · Foto do saldo (data) · Itens contados (número) · Itens no escopo (número) · Ajuste (texto) · Situação (texto) |
| Orçamentos (`/app/orcamentos`) | `quotes.view` | Número (texto) · Revisão (número) · Cliente (texto) · Situação (texto) · Validade (data) · Preço (moeda) · **Custo (moeda) e Margem (%) só com `quotes.view_cost`** (somem sem a ação) |
| Contratos (`/app/contratos`) | `projects.view` | Número (texto) · Cliente (texto) · Projeto (texto) · Assinatura (data) · Situação (texto) · Valor (moeda) |
| Projetos (`/app/projetos`) | `projects.view` | Número (texto) · Nome (texto) · Cliente (texto) · Etapa (texto: letra e nome) · Entrega prometida (data) · Atraso (número, dias) · Contratado (moeda) · Responsável (texto) · **Margem orçada (%) só com `projects.view_cost`** · **% recebido (%) só com `finance.view`** |
| OS de fabricação (`/app/producao/os`) | `production.view` | Número (texto) · Projeto (texto) · Cliente (texto) · Produto (texto) · Quantidade (número) · Etapa atual (texto) · Data prometida (data) · Fim previsto (data) · Atrasada (sim/não) · Situação (texto); nunca custo |
| OS de instalação (`/app/producao/instalacoes`) | `production.view` | Número (texto) · Projeto (texto) · Instalador (texto) · Previsão (data) · Etapas concluídas (texto "x de y") · Situação (texto); sem valor do instalador |
| Não conformidades (`/app/producao/nao-conformidades`) | `production.view` | Número (texto) · OS (texto) · Projeto (texto) · Etapa de qualidade (texto) · Responsável (texto) · Situação (texto) · Idade (número, dias) · **Custo estimado (moeda) só com `projects.view_cost`** |

## Regras de negócio e técnicas
- **Layouts novos** no módulo dono, sobre o `PdfDocumentFrame`: `ContractPdfDocument` e `AmendmentPdfDocument` (Commercial), `InstallationOrderPdfDocument`, `AcceptanceTermPdfDocument` e `NonConformityPdfDocument` (Production), `GoodsReceiptPdfDocument` (Purchasing), `RequisitionPdfDocument`, `StockAdjustmentPdfDocument`, `InventoryCountSheetPdfDocument` e `InventoryCountResultPdfDocument` (Inventory). Nenhum outro arquivo dos módulos referencia QuestPDF (ADR 0022).
- **Modelos sem custo** (recebimento, requisição, termo de aceite, OS de instalação, folha de contagem): o record do modelo não tem propriedade de custo, saldo do sistema (folha de contagem) nem valor do instalador.
- **Termo de aceite:** os números de série vêm do romaneio do projeto (`Production`, PRD-06b); sem romaneio, a seção "Equipamento" lista os itens do projeto sem série e a linha "Série: ____" em branco. O texto do termo é fixo nesta story (um parâmetro em Configurações fica como evolução).
- **Holerites na ficha:** `GET /hr/employees/{id}/payslips?take=6` lê os holerites das folhas do funcionário (todas as situações), com `hr.view_sensitive`; o download usa o endpoint existente de cada folha (IDOR coberto pelo serviço da RH-02b).
- **Boleto:** o atalho usa o mesmo download da aba Boleto (`BillingSlipTab`); a lista A receber já tem `withBillingSlip` e passa a devolver o id e a situação do boleto em andamento para montar o menu da linha (sem chamada por linha).
- **Exportação das listas:** cada `/export` usa o serviço da lista sem paginação e a mesma `RequirePermission`; colunas condicionadas a uma ação (`quotes.view_cost`, `projects.view_cost`, `finance.view`) são **omitidas** pelo `ExportTable` quando o usuário não tem a ação (`Sensitivity` com `Omit`), não preenchidas com vazio.
- **Nomes de arquivo:** o número do documento (`{CT}.pdf`, `{CT}-aditivo-{n}.pdf`, `{OSI}.pdf`, `{OSI}-termo-de-aceite.pdf`, `{REC}.pdf`, `{REQ}.pdf`, `{AJ}.pdf`, `{INV}-folha-de-contagem.pdf`, `{INV}.pdf`, `{NC}.pdf`); listas `{lista}-{AAAA-MM-DD}.xlsx`.

## Permissões, eventos e tabelas
- **Permissões:** as das telas (`projects.view`, `production.view`, `purchasing.view`, `inventory.view`, `quotes.view`, `hr.view_sensitive`, `finance.view`) e as de custo para as colunas (`quotes.view_cost`, `projects.view_cost`). Nenhuma ação nova.
- **Eventos:** nenhum. **Tabelas:** nenhuma; **migration:** nenhuma.

## Telas e estados
- **Documentos:** `<DocumentActions>` no cabeçalho de cada um, primeiro botão secundário; na OS de instalação, "Baixar termo de aceite" ao lado e também na aba Documentos, acima do "Enviar termo assinado"; no inventário, "Baixar folha de contagem" (em contagem) ou "Baixar resultado" (fechado).
- **Listas:** `<ExportMenu>` no slot `toolbar` do `DataTable`; vazia, desabilitado com "Nada para exportar com estes filtros.".
- **Ficha do funcionário:** bloco "Holerites" com skeleton, vazio ("Nenhum holerite ainda. Os holerites aparecem quando a folha do mês é lançada.") e erro com "Tentar de novo".
- **Estados:** "Gerando PDF…" / "Gerando…" (`aria-busy`); erro abaixo do botão (`role="alert"`) com a mensagem do ProblemDetails e "Tentar de novo"; 409 com a mensagem do CA.
- **Propositivo:** no inventário aberto, o próximo passo "Imprimir a folha de contagem" para quem conta no papel; na OS de instalação iniciada, "Baixar termo de aceite" nos próximos passos; depois de registrar o aceite, "Enviar termo assinado"; na requisição pedida pelo operador, "Imprimir a folha de separação".
- **400 px:** botões no menu "Mais ações".

## Ajuda (mesmo commit)
- Documentos: `Ajuda/comercial-contrato.md`, `Ajuda/comercial-contrato-aditivo.md`, `Ajuda/producao-instalacao.md`, `Ajuda/producao-instalacao-documentos.md` (termo em branco e o upload do assinado), `Ajuda/compras-recebimento.md`, `Ajuda/estoque-requisicao.md`, `Ajuda/estoque-ajuste.md`, `Ajuda/estoque-inventario.md` (folha de contagem cega e resultado), `Ajuda/producao-nao-conformidade.md`.
- Listas: `Ajuda/compras-solicitacoes.md`, `Ajuda/compras-cotacoes.md`, `Ajuda/compras-pedidos.md`, `Ajuda/compras-recebimentos.md`, `Ajuda/compras-recebimentos-recebidos.md`, `Ajuda/estoque-saldos.md`, `Ajuda/estoque-movimentos.md`, `Ajuda/estoque-ajustes.md`, `Ajuda/estoque-requisicoes.md`, `Ajuda/estoque-inventarios.md`, `Ajuda/comercial-orcamentos.md`, `Ajuda/comercial-contratos.md`, `Ajuda/projetos.md`, `Ajuda/producao-os-lista.md`, `Ajuda/producao-instalacoes.md`, `Ajuda/producao-nao-conformidades.md`.
- Holerite e boleto: `Ajuda/rh-folha-detalhe.md` ("Baixar holerite"), `Ajuda/rh-funcionario.md` (bloco Holerites), `Ajuda/financeiro-receber-titulo.md`, `Ajuda/financeiro-receber-titulo-boleto.md` e `Ajuda/financeiro-receber.md` (atalho do boleto).
- Seção "Imprimir e exportar" em cada uma; `<Dica>` em "Folha de contagem" ("Sem o saldo do sistema: conte o que está na prateleira.") e em "Termo de aceite".

## Plano de testes
- **Unidade:** modelo de cada PDF (seções, marca por situação); modelos sem custo (recebimento, requisição, termo, OS de instalação) e folha de contagem sem saldo; colunas omitidas sem a ação de custo.
- **Integração (Testcontainers, PDF na imagem Alpine com texto extraído; xlsx lido com ClosedXML):** cada PDF com o dataset fixo e a marca da situação (CA 1, 4–14); IDOR do aditivo (CA 2); contrato, aditivo e NC sem custo para quem não tem `projects.view_cost` e com custo para quem tem (CA 3, 14); folha de contagem sem o saldo e resultado recusado em contagem (CA 12, 13); termo de aceite em branco e preenchido (CA 8); listas: contagem = `total`, filtros "Aguardando você", colunas de custo omitidas (CA 15, 16); `GET /hr/employees/{id}/payslips` com `no-store` e 403 sem `hr.view_sensitive` (CA 18). Acesso por rota fica com a varredura.
- **Vitest:** `<DocumentActions>` nas telas dos 10 documentos (URL e nome); bloco Holerites some sem `hr.view_sensitive`; "Baixar boleto" só com boleto `Issued`/`Paid`; `<ExportMenu>` das listas com os filtros da URL.
- **E2E (Playwright, 2 fluxos):** OS de instalação → "Baixar termo de aceite" (download `{OSI}-termo-de-aceite.pdf`); Saldos de estoque → "Exportar" → "Planilha (Excel)" (download `saldos-de-estoque-….xlsx`).

## Fora do escopo
- BOM, roteiro, resumo do projeto, solicitação de compra, títulos CR/CP, OS de manutenção, inspeção de qualidade, painel do dono e prévia do 13º: EXP-05.
- Excel de RH, notas fiscais, patrimônio e cadastros: EXP-05.
- Assinatura eletrônica do termo de aceite; texto do termo configurável.
- Ação nova de custo no Estoque (o valor segue a tela).

## Pontos em aberto
Nenhum que segure a story. Propostas desta story, a confirmar pelo PO na revisão: o comprovante de recebimento sem custo de entrada (CA 9; a tela mostra o custo, o papel vai para o almoxarifado) e o texto fixo do termo de aceite (CA 8).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, PDF na imagem Alpine, xlsx com ClosedXML), Vitest e os 2 E2E passando.
- [ ] Nenhuma migration; nenhuma permissão nova.
- [ ] Telas com os estados, inclusive a 400 px.
- [ ] Ajudas atualizadas; teste de cobertura passando.
- [ ] Auditoria de exportação ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] 10 layouts de PDF nos módulos donos; `Sensitivity.Omit` no `ExportTable` se a EXP-01 não trouxer.
- [ ] `/export` das 15 listas; `GET /hr/employees/{id}/payslips`; id e situação do boleto na lista A receber.
- [ ] `<DocumentActions>`, `<ExportMenu>`, bloco Holerites e atalho do boleto; ajudas.

## Quality gates
- Agentes previstos: @dev, cybersecurity (custo, contagem cega, holerite), @qa.
- Gate: CA 3, 12, 16 e 18 obrigatórios.

## Checklist de prontidão
Checklist do SM, 02/11/2026, sobre o pedido do PO do mesmo dia.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão | OK (CA 2, 3, 12, 16, 18) |
| 6 | Dependências coerentes | OK (EXP-01, EXP-03) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas | OK (auditoria, stories COM/CMP/EST/PRD/RH/BNK, ajudas e código lidos) |
| 9 | Telas com estados, ajuda e propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados | OK (2 propostas marcadas) |

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 02/11/2026 | 0.1 | Story detalhada a partir da auditoria de PDF e exportação (onda 2); `Ready` pelo pedido do PO de 02/11/2026 | Claude (SM) |
