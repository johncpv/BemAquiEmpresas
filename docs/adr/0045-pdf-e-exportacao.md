# ADR 0045: Modelo único de PDF e exportação de listas (xlsx e PDF)

- Status: Aceito (02/11/2026). Decisões do PO de 02/11/2026: Excel gerado no servidor com **ClosedXML (MIT)**; exportação com dado sensível **não** pede 2FA (a permissão basta e, sem ela, a coluna sai mascarada, como na tela); toda exportação é auditada.
- Contexto: auditoria de 02/11/2026 (PDF dos documentos e exportação das listas). Havia 12 layouts QuestPDF, cada um com o seu record de empresa, o seu cabeçalho, as suas cores literais (`#14202b`, `#9aa7b4`, `#eef2f6`), o seu rodapé e a sua marca (holerite com marca d'água, rescisão "Simulação", DANFE de prévia com faixa). Nenhuma lista tinha exportação; as quatro exportações existentes eram CSV (`SafeCsv`). Pedido do PO: todo documento com PDF para imprimir e um componente de exportação PDF/Excel nas listas, com o financeiro primeiro.
- Complementa a ADR 0022 (PDF com QuestPDF Community e a Archivo embutida), a ADR 0016 (auditoria e dado sensível), a ADR 0003 (ações `modulo.acao`) e a ADR 0044 (layout e `PageHeading` com `actions`).

## Decisão

### 1. Modelo único de PDF (`BuildingBlocks/Pdf`)
- `PdfDocumentFrame` (classe base, `IDocument`): A4 (paisagem quando o documento pede), margem 32, Archivo com dígitos tabulares; **cabeçalho** com o logo (só `Clean`) ou o nome, a razão social, o CNPJ, o endereço e o contato; **faixa de título** com o tipo do documento, o número e a data, sublinhada no acento cobalto; **rodapé** "Emitido por {usuário} em {dd/MM/aaaa HH:mm} · Página x de y" (horário de São Paulo). O documento só escreve `ComposeContent` e usa os auxiliares `Fields`, `Section`, `HeaderCell`, `BodyCell` e `Signature`.
- **Marcas padrão** (`PdfMark`): `Draft` "Rascunho", `Simulation` "Simulação", `Cancelled` "Cancelado", `Preview` "Prévia, sem valor fiscal". Cada uma é uma faixa logo abaixo do título (âmbar; vermelha no cancelado) **e** a marca d'água diagonal clara, em todas as páginas.
- **Tema** (`PdfTheme`): as únicas cores e medidas dos documentos novos (tinta, secundário, linha, fundo, acento, marca d'água, âmbar e vermelho da faixa) e os formatos pt-BR (`Money`, `Quantity`, `Percent`, `Date`, `DateTime`). Documento novo não escreve cor literal.
- **Empresa e emissor:** `PdfFrameFactory` monta o `PdfFrame` a cada geração. A empresa vem de `IPdfCompanySource` (BuildingBlocks), implementado pelo Identity sobre o mesmo perfil do `ICompanyIdentity`; o logo é lido do storage. O emissor é o nome do usuário atual (`IUserDirectory`) ou "Sistema" em job.
- **Dados incompletos:** o frame mostra o que existe (o nome do tenant como fallback). A exigência "Complete os dados da empresa para o PDF" continua só onde o documento sai para o cliente (orçamento, COM-03); documento interno (ordem de pagamento, listas) não é bloqueado.
- **Encaixe:** documento novo nasce no frame. Os 12 layouts antigos **não** migram nesta decisão; migram quando a story tocar neles (pedido de compra e orçamento na EXP-03, os demais na EXP-05), com teste de texto garantindo o mesmo conteúdo.

### 2. Exportação de tabela (`BuildingBlocks/Export`)
- `ITabularExporter` (um por formato): `XlsxTabularExporter` com o **ClosedXML 0.105** (MIT, sobre o DocumentFormat.OpenXml, MIT; fontes pelo SixLabors.Fonts 1.x, Apache-2.0) e `PdfTabularExporter`, que desenha a mesma tabela no frame (`TabularPdfDocument`: título da tela, linha "Filtros: …" com a contagem, cabeçalho repetido em cada página, números à direita, **paisagem acima de 6 colunas**).
- Planilha: aba com o título da tela, **cabeçalho congelado e com autofiltro**, aba **"Filtros"** (lista, emitido por, emitido em, linhas e cada filtro; vazio é "Todos"). Colunas **tipadas** (`ExportColumnType`): texto, dinheiro em reais (`[$R$-416] #,##0.00`), quantidade (até 4 casas), data (`dd/mm/aaaa`), data e hora de São Paulo e percentual (pontos viram fração com `0.00%`). Largura fixa por tipo; texto pelo maior conteúdo, de 12 a 60 caracteres, sem medir fonte. O ClosedXML mede com a Archivo embutida (`LoadOptions.DefaultGraphicEngine`), porque a imagem Alpine não tem fonte.
- **Anti-injeção de fórmula** (mesma regra do `SafeCsv`): texto é sempre célula de texto, nunca fórmula; o que começa com `= + - @`, tabulação ou retorno ganha o **prefixo de texto da planilha** (`QuotePrefix`), sem mudar o valor.
- Alternativas descartadas: EPPlus 5+ (Polyform, comercial), SheetJS CE (fora do npm, CDN própria, fere a regra de supply chain) e ExcelJS no front (o dado iria ao navegador sem permissão, máscara nem auditoria). MiniExcel e OpenXML SAX ficam como plano B se o teto subir.

### 3. Contrato das rotas de exportação
- **Lista:** `GET /api/v1/{módulo}/{recurso}/export?format=xlsx|pdf&<os mesmos filtros e a ordenação da lista>`, `WithName("ExportXxx")`, com a **mesma `RequirePermission` da lista**. O handler reaproveita a **mesma consulta da lista**, sem paginação; o módulo declara só um `ExportSpec<T>` (entidade da auditoria, `fileBase`, título e colunas) e a função que lê as linhas até o teto, com o total do filtro. O `ListExportService` faz o resto: formato, teto, máscara, geração, auditoria e o download.
- **Teto:** `Export:MaxXlsxRows` = **10.000** e `Export:MaxPdfRows` = **2.000** (configuráveis). Acima, **422 `export_too_large`** com o total e o teto ("A lista tem {n} linhas e o limite para {Excel|PDF} é {teto}. Refine os filtros (período, situação ou busca) e exporte de novo."), **antes** de montar o arquivo. Formato desconhecido: 422 `export_format_invalid`.
- **Nome do arquivo:** `{tela}-{AAAA-MM-DD}.{ext}` (data de São Paulo), nunca com nome ou documento de pessoa. Resposta `attachment`, `nosniff`, `private, no-store` (`AttachmentEndpoints.ApplyDownloadHeaders`).
- **Documento:** `GET /api/v1/{módulo}/{recurso}/{id}/pdf` → `application/pdf`, `{número}.pdf`, com a permissão de leitura do documento e o mesmo serviço de leitura da tela (IDOR coberto por ele). Gerado na hora, do jeito que o documento está.

### 4. Front
- `<DocumentActions url fileName label? print?>` (`components/ui/document-actions.tsx`): "Baixar PDF" e "Imprimir" (abre o PDF numa aba nova, aberta no clique para não cair no bloqueio de pop-up), no cabeçalho do documento, primeiro botão secundário. Estados `aria-busy` "Gerando PDF…"/"Abrindo…", erro abaixo (`role="alert"`) com a mensagem do ProblemDetails e "Tentar de novo".
- `<ExportMenu url={(format) => getExportXxxUrl({...filtrosDaTela, format})} fileBase>` (`components/ui/export-menu.tsx`): botão "Exportar" com o menu (Radix: `aria-haspopup`, setas, Esc) "Excel (.xlsx)" e "PDF", com **os filtros, a busca e a ordem da tela** (todas as páginas). O 422 aparece como mensagem abaixo do botão.
- Slot `toolbar` do `DataTable`, à direita da busca (`role="toolbar"`). Tudo por token; nenhuma cor literal.

### 5. Máscara e auditoria
- Coluna sensível declara `ExportSensitivity` com as ações que a liberam (qualquer uma basta, ex.: `hr.view_sensitive`). Sem nenhuma, a coluna continua no arquivo (o formato é o mesmo para todos) com a máscara `***`, como texto. **Sem 2FA** na exportação (decisão do PO); a regra é a mesma da tela. Regra de linha que a tela já tem (ex.: salários agrupados por folha sem `finance.approve`/`finance.pay`, RH-02c CA 7) vale igual na exportação, porque a consulta é a mesma.
- Toda exportação grava `Exported` na auditoria **na hora**: lista com entidade da tela, id `list`, formato, número de linhas e os filtros aplicados (rótulo e valor); documento com a entidade e o id do documento, formato e itens. Nunca o conteúdo.

## Consequências
- EXP-01 entrega a base e dois pilotos: Contas a pagar (`GET /finance/payables/export`) e o PDF da ordem de pagamento (`GET /finance/payment-orders/{id}/pdf`, "Rascunho" antes de aprovada, "Cancelado" se cancelada ou estornada).
- EXP-02 a EXP-05 usam a base sem código novo de infraestrutura: cada lista é um `ExportSpec` + a função de linhas + uma rota; cada documento é um `PdfDocumentFrame` + uma rota + `DocumentActions`.
- `EndpointAuthorizationTests` já cobre as rotas novas pela varredura; `OpenApiDocumentTests` + `pnpm gen:api` geram `getExportXxxUrl` e `getDownloadXxxPdfUrl`.
- ClosedXML mantém o arquivo em memória: o teto de 10.000 linhas limita o pico. Acima disso, a evolução é um job Hangfire com aviso no sino (padrão do lote de holerites), com ADR nova.
- QuestPDF Community continua sob a regra de receita da ADR 0022.

## Adendo (EXP-02, 03/11/2026)
- `ExportData<T>`: a consulta da tela pode recusar (`Result`: conta não encontrada, período inválido) e decide as colunas pelo resultado (os meses do DRE, o cenário do fluxo). O formato é conferido antes da consulta; teto, máscara, auditoria e download continuam no `ListExportService`.
- Planilha com várias abas (`ExportSheet`, entre a principal e a "Filtros"), notas da tela (topo do PDF de lista e aba "Filtros") e destaque por linha (`ExportRowStyle`: recuo na coluna marcada com agrupamento, negrito e negativo em vermelho pelo `PdfTheme`). Sem fórmula em nenhuma célula.
- PDF próprio da tela (`ExportData.Document`) no lugar do PDF de lista, com o mesmo teto e a mesma auditoria: extrato da conta, relatório de conciliação ("Prévia" com o período não fechado) e DRE gerencial. A rota `GET …/pdf` do período é a mesma exportação com o formato PDF.
