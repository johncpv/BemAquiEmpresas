# EXP-01 — Base de PDF e exportação (modelo único, Excel com ClosedXML, contas a pagar e ordem de pagamento)

| Campo | Valor |
|---|---|
| ID | EXP-01 |
| Título | Modelo único de PDF (`PdfDocumentFrame`: cabeçalho da empresa, título com número e data, marcas padrão e rodapé "Emitido por … · Página x de y"), exportação de listas em Excel (ClosedXML, no servidor) e PDF pelo mesmo contrato (`GET …/export?format=xlsx\|pdf` com os filtros da lista, teto, máscara e auditoria), `<DocumentActions>` e `<ExportMenu>` no front, provados em Contas a pagar e no PDF novo da ordem de pagamento |
| Tamanho | M |
| Status | Done |
| Requisitos | Pedido do PO de 02/11/2026 (todo documento com PDF; exportação PDF/Excel nas listas, financeiro primeiro) · PRD §6 (LGPD, auditoria) · R-SEC-5 · R-PRP-1 · ADR 0022 · ADR 0016 |
| Dependências | ADR 0022 (QuestPDF e Archivo), ADR 0044 (layout e `PageHeading`), ADR 0045 (nova) |
| Bloqueia | EXP-02 (financeiro), EXP-03 (compras, romaneio e OS), EXP-04, EXP-05 |
| Ordem de execução | 1 (Pós-go-live: documentos e exportação) |

## User story
Como **usuário de qualquer módulo**, quero **o mesmo botão e o mesmo PDF em todo documento e em toda lista**, para **achar, imprimir e levar os dados para a planilha sem procurar nem copiar a tela**.

## Contexto e referências
- Auditoria de 02/11/2026 (PDF dos documentos e exportação das listas): 12 layouts QuestPDF com cabeçalho, cores e rodapé copiados; nenhuma lista com exportação; recomendação de ClosedXML no servidor atrás de um `ITabularExporter`.
- Decisões do PO (02/11/2026): Excel no servidor com **ClosedXML (MIT)**; exportação com dado sensível **não** pede 2FA (a permissão basta; sem ela, a coluna sai mascarada, como na tela); toda exportação é auditada (quem, quando, o quê, filtros, linhas).
- ADR 0045 (esta story): modelo único, exportadores, contrato, teto, máscara e auditoria.

## Critérios de aceite
1. **Dado** um documento novo, **quando** é gerado, **então** usa o `PdfDocumentFrame`: A4, Archivo com dígitos tabulares, cabeçalho com logo (ou nome), razão social e CNPJ, faixa de título com tipo, número e data, e rodapé "Emitido por {usuário} em {dd/MM/aaaa HH:mm} · Página x de y" no horário de São Paulo; cores só do `PdfTheme`.
2. **Dado** `PdfMark` `Draft`, `Simulation`, `Cancelled` ou `Preview`, **quando** o PDF é gerado, **então** a faixa ("Rascunho", "Simulação", "Cancelado", "Prévia, sem valor fiscal") e a marca d'água diagonal aparecem em **todas** as páginas.
3. **Dado** `GET /api/v1/finance/payables/export?format=xlsx` com os filtros da tela (busca, situação, fornecedor, vencimento, ordem), **quando** chamado com `finance.view`, **então** o arquivo `contas-a-pagar-AAAA-MM-DD.xlsx` traz as mesmas linhas da lista filtrada (todas as páginas), colunas tipadas (dinheiro em reais, data), cabeçalho congelado, autofiltro e a aba "Filtros" com quem emitiu e quando; texto que começa com `= + - @` não vira fórmula.
4. **Dado** `format=pdf`, **então** o PDF de lista sai no modelo único com o título, a linha "Filtros: …", a contagem e as colunas principais (paisagem acima de 6 colunas).
5. **Dado** mais linhas que o teto (10.000 no xlsx, 2.000 no PDF, configuráveis em `Export`), **então** 422 `export_too_large` com o total, o teto e "Refine os filtros…", sem gerar nem auditar; formato desconhecido, 422 `export_format_invalid`.
6. **Dado** coluna sensível e usuário sem a ação que a libera, **então** a coluna sai com a máscara `***` (sem 2FA); regra de linha da tela vale igual (em Contas a pagar, sem `finance.approve` nem `finance.pay`, cada folha sai numa linha "Folha …" com o total, RH-02c CA 7).
7. **Dado** qualquer exportação (lista ou documento), **então** a auditoria registra `Exported` com quem, quando, formato, número de linhas e, na lista, os filtros; nunca o conteúdo.
8. **Dado** a ordem de pagamento, **quando** clico **Baixar PDF** ou **Imprimir** no cabeçalho, **então** recebo `OP-…pdf` com a ordem (situação, total, solicitante, datas, conta de saída), os títulos com favorecido, destino do dinheiro mascarado como na tela e valores, as alçadas, as decisões (quem e quando) e as assinaturas; marca **Rascunho** antes de aprovada (aguardando ou reprovada), **Cancelado** se cancelada ou estornada; mesma permissão da tela (`finance.view`).
9. `<DocumentActions>` e `<ExportMenu>` com carregando (`aria-busy`), erro com a mensagem do servidor (`role="alert"`), teclado (menu Radix) e tokens; slot `toolbar` no `DataTable`.
10. Ajuda das duas telas atualizada no mesmo commit.

## Fora do escopo
- Migrar os 12 layouts antigos para o frame (EXP-03 e EXP-05) e as demais listas e documentos (EXP-02 a EXP-05).
- Exportação acima do teto por job com aviso no sino.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade (exportador: tipos, anti-fórmula, teto, máscara, auditoria; frame: cabeçalho, rodapé, marcas por página, paisagem), integração com Testcontainers (Contas a pagar xlsx/PDF, 422, 403, auditoria; PDF da OP com Rascunho/sem marca/Cancelado, 404, 403, auditoria) e Vitest dos dois componentes.
- [x] Sem permissão nova (as rotas usam `finance.view`); `EndpointAuthorizationTests` cobre pela varredura.
- [x] Ajuda `financeiro-pagar.md` e `financeiro-ordem-de-pagamento.md` atualizadas.
- [x] Auditoria ligada (`Exported`).
- [x] OpenAPI e cliente Orval regenerados; ADR 0045; story marcada no backlog.

## Tarefas técnicas
- [x] `BuildingBlocks/Pdf`: `PdfTheme`, `PdfMark`/`PdfMarks`, `PdfDocumentFrame`, `PdfFrameFactory`, `IPdfCompanySource` (implementado pelo `CompanySettings` do Identity).
- [x] `BuildingBlocks/Export`: `ExportSpec<T>`, `ExportTable`, `ITabularExporter` (`XlsxTabularExporter` com ClosedXML 0.105.1, `PdfTabularExporter` com `TabularPdfDocument`), `ExportOptions`, `ListExportService`, `AddBemAquiExport`; `IAuditWriter.RecordExportAsync` com os filtros.
- [x] Finance: `PayableService.ExportRowsAsync`/`ExportFiltersAsync` (mesmo filtro da lista), `PayableExport` (colunas), rota `ExportPayables`; `PaymentOrderPdfDocument` e `PaymentOrderDocumentService`, rota `DownloadPaymentOrderPdf`.
- [x] Front: `DocumentActions`, `ExportMenu`, `toolbar` do `DataTable`; Contas a pagar e Ordem de pagamento.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 02/11/2026 | 0.1 | Story a partir da auditoria de PDF e exportação, com as decisões do PO do mesmo dia | @sm |
| 02/11/2026 | 1.0 | Implementada (@dev). Base de PDF e exportação, ADR 0045, pilotos em Contas a pagar e na ordem de pagamento | @dev |
