# EXP-05 — Onda 3: PDFs restantes, Excel de RH, fiscal, patrimônio e cadastros, e migração dos layouts antigos para o template

| Campo | Valor |
|---|---|
| ID | EXP-05 |
| Título | PDFs da BOM, do roteiro, do resumo do projeto, da solicitação de compra, da ficha do título a receber e a pagar, da OS de manutenção, da inspeção de qualidade, do painel do proprietário e da prévia do 13º; exportação xlsx de RH (CPF e salário só com `hr.view_sensitive`, senão mascarados), notas fiscais, patrimônio e cadastros; migração dos layouts antigos para o `PdfDocumentFrame` com o mesmo conteúdo |
| Tamanho | M |
| Status | Done (03/11/2026) |
| Requisitos | Pedido do PO de 02/11/2026 (auditoria, seções A, B, D1, D4 "EXP-05" e E) · R-ENG-1 · R-PRJ-2 · R-CMP-1 · R-FIN-2 e R-FIN-4 (títulos) · R-PAT (manutenção, PAT-03a) · R-RH-5 · PRD §5.14 (painel) · R-COM-3 · PRD §6 LGPD · R-SEC-5 |
| Dependências | EXP-01 (frame, marcas, `ITabularExporter`, `Sensitivity`, `<DocumentActions>`, `<ExportMenu>`); EXP-03 (`PdfQr`, `PdfStageIndicator`, migração do pedido de compra como modelo); EXP-04 (`Sensitivity.Omit`); ENG-01, ENG-02, COM-05, CMP-01, FIN-01, FIN-02, PAT-03a, PRD-05, DSH-01a/01b, FLH-04, CAD-04b/04c2, RH-01c, RH-02a, FIS-03a, FIS-04a, PAT-01a (telas e consultas entregues) |
| Bloqueia | — |
| Ordem de execução | 5 (Pós-go-live: documentos e exportação) |

## User story
Como **qualquer usuário do sistema**, quero **que todo documento restante tenha PDF, que as listas de RH, fiscal, patrimônio e cadastros saiam em Excel com a mesma proteção da tela e que todos os PDFs tenham o mesmo cabeçalho e rodapé**, para **imprimir e arquivar qualquer coisa do mesmo jeito, sem expor dado pessoal**.

## Contexto e referências
- Auditoria de 02/11/2026 (`pdf-export-auditoria.md`): BOM, roteiro, resumo do projeto, solicitação de compra, títulos, OS de manutenção, inspeção de qualidade, painel do dono e prévia do 13º **sem PDF**; nenhuma lista de RH, fiscal, patrimônio ou cadastro exporta; os **12 layouts antigos** copiam cabeçalho, cores e rodapé cada um (seção C).
- Layouts antigos (`IDocument`): `QuotePdfDocument`, `PurchaseOrderPdfDocument` (já migrado na EXP-03), `ShipmentPdfDocument`, `PayslipPdfDocument`, `TimesheetPdf`, `VacationNoticePdfDocument`, `TerminationTermPdfDocument`, `PayrollParallelTermDocument`, `FiscalSettingsPdf`, `PersonalDataPdfDocument`, `AssetLabelPdfDocument` e `DanfePreviewDocument`. **Nesta story: os 11 restantes.**
- **EXP-01 (base):** máscara por coluna igual à tela, **sem 2FA** (decisão do PO de 02/11/2026); tetos 10.000/2.000; auditoria de exportação; `Cache-Control: no-store`.
- Regras herdadas: salário, documentos e dados pessoais do RH só com `hr.view_sensitive`; tipo e motivo de afastamento por doença só com `hr.view_health` (CAD-04c1); prévia do 13º só com `hr.view_sensitive` (`/thirteenth-preview/sensitive`); valor de aquisição do bem só com `assets.edit` (PAT-01a); custo e margem do projeto só com `projects.view_cost`; blocos do painel por permissão (DSH-01a/01b).

## Critérios de aceite
**PDFs novos (comum)**
1. **Dado** a permissão `view` do documento e um dos documentos da tabela "PDFs", **quando** clico "Baixar PDF" no cabeçalho (`<DocumentActions>`), **então** baixa `{número}.pdf` no `PdfDocumentFrame`, com as seções e a marca da tabela; acima de 300 linhas, 422. ~~Dados da empresa incompletos, 422 com o link.~~ (descartado por decisão do PO de 03/11/2026: o PDF não bloqueia por dado faltante da empresa, ADR 0045 §1).
2. **[Segurança]** **Dado** um usuário sem a permissão do documento, **quando** chama o `/pdf`, **então** 403 (varredura); id de outro recurso dá 404 (IDOR, teste próprio na ficha do título); cada download entra na auditoria de exportação.

**Engenharia (ENG-01, ENG-02)**
3. **Dado** `engineering.view` e a BOM da OS, **quando** baixo o PDF, **então** sai: OS, produto, revisão, situação, quem liberou e quando, linhas (código, descrição, quantidade, unidade, observação, item fantasma/agrupador quando houver) e a lista de anexos técnicos do item; `Draft` sai com **"Rascunho"**, `Superseded` com a faixa "Revisão substituída pela {n}"; `Released` sem marca; sem custo.
4. **Dado** `engineering.view` e um roteiro, **quando** baixo o PDF, **então** sai o nome, os produtos ligados, as etapas (letra, nome, centro de trabalho, horas padrão, qualidade) e o checklist de cada etapa; roteiro inativo sai com a faixa "Roteiro inativo".

**Projeto, compras e financeiro**
5. **Dado** `projects.view` e um projeto, **quando** baixo o **resumo do projeto**, **então** sai: número, nome, cliente, responsável, endereço da obra, o indicador de etapa (P, E, F, I, T, C), data prometida e atraso, itens e OS (número, produto, etapa atual, situação), cronograma (marcos e datas), e, **só com `projects.view_cost`**, o bloco "Custos" (orçado, realizado, projetado e desvio por natureza, margens) e, **só com `finance.view`**, o bloco "Financeiro" (a receber e recebido, % recebido); `Cancelled` sai com **"Cancelado"**.
6. **Dado** `purchasing.view` e uma solicitação de compra, **quando** baixo o PDF, **então** sai: número, origem, destino (projeto ou Estoque geral), OS, quem pediu, necessidade, itens (código, descrição, quantidade, unidade, situação da linha: aberta, em cotação, pedida, cancelada) e os vínculos (cotação e pedido); `Cancelled` com **"Cancelado"**.
7. **Dado** `finance.view` e um título a receber ou a pagar, **quando** baixo a **ficha do título**, **então** sai: número, terceiro, origem (contrato, pedido, recebimento, folha, manual), conta do plano e centro de custo, emissão, vencimento, valor, retenções (a pagar), baixas (data, valor, juros, multa, desconto, conta, estornada), saldo, boleto (situação e linha digitável, quando houver) e ordem de pagamento (número e situação, a pagar); `Planned` sai com a faixa "Previsto: {aguarda marco ou data estimada}"; `Cancelled` com **"Cancelado"** e o motivo; título da folha sem `hr.view_sensitive` sai sem o nome do funcionário por linha (como a tela).

**Patrimônio e qualidade**
8. **Dado** `assets.view` e uma OS de manutenção, **quando** baixo o PDF, **então** sai: número (`MAN-…`), bem (número do patrimônio, nome, local), tipo (preventiva, corretiva), plano de origem, agendamento, executor (interno ou terceiro), checklist com caixas (marcadas quando feitas), horímetro, observações, o custo (peças, mão de obra, terceiro) **como a tela mostra para o usuário** e as assinaturas "Executado por" e "Conferido por"; `Open` e `InProgress` sem marca; `Cancelled` com **"Cancelado"** e o motivo.
9. **Dado** `production.view` e uma inspeção de qualidade (etapa de qualidade da OS), **quando** baixo o PDF, **então** sai: OS, projeto, produto, etapa, tentativa, inspetor, data, o checklist com o resultado de cada item (aprovado, reprovado, observação), a decisão (aprovada ou reprovada), a NC aberta quando reprovada (número) e a lista de fotos (nome e data, sem embutir); inspeção não decidida sai com **"Prévia"**.

**Painel e 13º**
10. **Dado** `dashboard.view`, **quando** clico "Baixar PDF" no painel do proprietário, **então** sai a **fotografia do painel** no mês escolhido: cada bloco que **o usuário vê na tela** (caixa, a receber e a pagar, faturamento do mês e do ano, margem, produção), com os mesmos números e a data e hora da geração ("Fotografia de {data hora}: os números mudam com novos lançamentos."); um bloco sem permissão não sai; sem gráficos (tabelas com os mesmos valores).
11. **[Segurança]** **Dado** `hr.view_sensitive` e a prévia do 13º (ano e tipo da tela), **quando** baixo o PDF (`GET /hr/payroll-runs/thirteenth-preview/sensitive/pdf?year&type`), **então** sai com a marca **"Simulação"**: por funcionário, matrícula, nome, avos, base, médias, adiantamento abatido e valor da parcela, com os totais; sem a ação, 403; o nome do arquivo é `previa-13-{ano}-{tipo}.pdf` (sem nome nem CPF) e a resposta leva `no-store`.

**Excel de RH**
12. **[Segurança]** **Dado** `hr.view` e a lista de funcionários, **quando** exporto em xlsx, **então** saem matrícula, nome, cargo, admissão, situação, jornada e sindicato; **CPF** e **salário** saem **completos só com `hr.view_sensitive`** e **mascarados** sem ela (CPF `123.***.***-45`, como o holerite; salário `***`); o tipo e o motivo do afastamento saem só com `hr.view_health` (sem ela, só "Afastado" e as datas); a auditoria registra se o arquivo levou dado sensível sem máscara.
13. **[Segurança]** **Dado** a folha de uma competência (aba do detalhe), **quando** exporto o resumo por funcionário (`GET /hr/payroll-runs/{id}/payslips/sensitive/export?format=xlsx`), **então** exige `hr.view_sensitive` (403 sem ela) e sai matrícula, nome, cargo, proventos, descontos, líquido, FGTS e situação do holerite, com os totais iguais aos da tela; a lista de folhas (competência, tipo, situação, totais gerais) exporta com `hr.view` sem valores por pessoa.
14. **Dado** `hr.view` e a visão geral do espelho de ponto do período, **quando** exporto, **então** sai por funcionário: horas trabalhadas, extras, faltas, atrasos, banco de horas e situação do espelho; afastamento por doença aparece como "Afastado" sem `hr.view_health`.
15. **Dado** `hr.view` e Cargos, **quando** exporto, **então** saem nome, CBO e jornada; o salário base e o custo-hora saem só com `hr.view_sensitive` (mascarados sem ela).

**Excel de fiscal, patrimônio e cadastros**
16. **Dado** `fiscal.view`, **quando** exporto as notas de saída (emitidas) ou de entrada, **então** sai o "livro simples": número, série, chave, emissão, destinatário ou emitente, CNPJ/CPF **como a tela mostra**, CFOP principal, valor dos produtos, impostos destacados (ICMS, IPI) e valor total, e a situação (autorizada, cancelada, denegada, rejeitada); a última linha soma as autorizadas; XML em lote fica fora.
17. **Dado** `assets.view`, **quando** exporto os bens, as manutenções ou o custo de manutenção, **então** saem as colunas da tela; o **valor de aquisição** sai só com `assets.edit` (omitido sem ela, como a tela).
18. **Dado** a permissão de cada cadastro, **quando** exporto Terceiros (`parties.view`), Itens (`inventory.view`), Plano de contas e Centros de custo (`finance.view`) ou Descontos e cancelamentos (`finance.view`), **então** saem as colunas da tela; em Terceiros, CPF de pessoa física sai como a tela mostra e os contatos não vão (ficam na ficha); o plano de contas sai com a hierarquia (código e recuo por nível).

**Migração dos layouts antigos**
19. **Dado** os 11 layouts antigos, **quando** migram para o `PdfDocumentFrame`, **então** o **texto extraído** de cada um, com o dataset fixo, é igual ao de antes (teste de regressão por texto, ignorando só o cabeçalho e o rodapé, que passam a ser os do frame), os limites e os nomes de arquivo não mudam e nenhuma cor literal fica nos layouts (só `PdfTheme`).
20. **Dado** as marcas próprias de hoje, **quando** migram, **então** passam a ser o `PdfMark` padrão: holerite em rascunho → **"Rascunho"**; termo de rescisão para conferência → **"Simulação"**; prévia do DANFE → **"Prévia"** (o texto "sem valor fiscal" continua na faixa); romaneio `Draft` e `AwaitingStockApproval` → **"Rascunho"**, `Cancelled` → **"Cancelado"** (o rodapé "não é documento fiscal" continua); orçamento com as faixas da EXP-03.
21. **Dado** a etiqueta do patrimônio e a prévia do DANFE, **quando** migram, **então** **mantêm o próprio formato** (etiqueta no tamanho de Configurações → Parâmetros → Patrimônio; DANFE no layout do DANFE) e passam a usar só o `PdfTheme`, a fonte do frame, o `PdfQr` (etiqueta) e o `PdfMark` (DANFE), sem o cabeçalho e o rodapé do frame.

**Ajuda**
22. **Dado** cada tela desta story, **quando** abro a Ajuda, **então** a seção "Imprimir e exportar" diz o que sai, as marcas e o que sai mascarado; as ajudas dos documentos migrados não mudam de conteúdo (só onde o lugar do botão mudou para o `<DocumentActions>`).

## PDFs (por tela)
Botão no **cabeçalho do documento** (`<DocumentActions>`), com a **permissão da tela**.

| Documento (rota) | Endpoint | Permissão | Seções | Marca por situação |
|---|---|---|---|---|
| BOM da OS (`/app/producao/os/$id`, aba `bom`; `/app/engenharia/boms`) | `GET /engineering/boms/{id}/pdf` | `engineering.view` | CA 3 | `Draft`: **Rascunho**; `Superseded`: faixa |
| Roteiro (`/app/engenharia/roteiros/$id`) | `GET /engineering/routing-templates/{id}/pdf` | `engineering.view` | CA 4 | Inativo: faixa |
| Resumo do projeto (`/app/projetos/$id`) | `GET /commercial/projects/{id}/pdf` | `projects.view` (+ `projects.view_cost`, `finance.view` por bloco) | CA 5 | `Cancelled`: **Cancelado** |
| Solicitação de compra (`/app/compras/solicitacoes/$id`) | `GET /purchasing/purchase-requests/{id}/pdf` | `purchasing.view` | CA 6 | `Cancelled`: **Cancelado** |
| Título a receber (`/app/financeiro/receber/$id`) | `GET /finance/receivables/{id}/pdf` | `finance.view` | CA 7 | `Planned`: faixa; `Cancelled`: **Cancelado** |
| Título a pagar (`/app/financeiro/pagar/$id`) | `GET /finance/payables/{id}/pdf` | `finance.view` | CA 7 | `Planned`: faixa; `Cancelled`: **Cancelado** |
| OS de manutenção (`/app/patrimonio/manutencao/$id`) | `GET /assets/maintenance-orders/{id}/pdf` | `assets.view` | CA 8 | `Cancelled`: **Cancelado** |
| Inspeção de qualidade (`/app/producao/qualidade/$stepId`) | `GET /production/quality/{stepId}/pdf` | `production.view` | CA 9 | Não decidida: **Prévia** |
| Painel do proprietário (`/app/painel`) | `GET /dashboard/pdf?month` | `dashboard.view` (+ a permissão de cada bloco) | CA 10 | Nenhuma; nota "Fotografia de …" |
| Prévia do 13º (`/app/rh/folha`, prévia do 13º) | `GET /hr/payroll-runs/thirteenth-preview/sensitive/pdf?year&type` | `hr.view_sensitive` | CA 11 | Sempre **Simulação** |

## Listas (xlsx)
"Exportar" na **barra da lista** (`<ExportMenu formats={['xlsx']}>`), com a **permissão da lista**; `GET .../export?format=xlsx` com os filtros do `GET` da lista. Máscara: **M** = mascarado sem a ação; **O** = omitido sem a ação.

| Lista (rota) | Permissão | Colunas (tipo; máscara) |
|---|---|---|
| Funcionários (`/app/rh/funcionarios`) | `hr.view` | Matrícula (texto) · Nome (texto) · CPF (texto; **M** `hr.view_sensitive`) · Cargo (texto) · Admissão (data) · Situação (texto; tipo do afastamento **M** `hr.view_health`) · Jornada (texto) · Sindicato (texto) · Salário (moeda; **M** `hr.view_sensitive`) |
| Folhas (`/app/rh/folha`) | `hr.view` | Competência (texto `MM/AAAA`) · Tipo (texto) · Situação (texto) · Funcionários (número) · Proventos (moeda) · Descontos (moeda) · Líquido (moeda) |
| Holerites da folha (`/app/rh/folha/$id`, aba holerites) | `hr.view_sensitive` | Matrícula · Nome · Cargo · Proventos (moeda) · Descontos (moeda) · Líquido (moeda) · FGTS (moeda) · Situação; totais |
| Espelho do período (`/app/rh/ponto/espelho`) | `hr.view` | Matrícula · Nome · Horas trabalhadas (número) · Extras (número) · Faltas (número) · Atrasos (número) · Banco de horas (número) · Situação do espelho (texto; afastamento **M** `hr.view_health`) |
| Cargos (`/app/rh/cargos`) | `hr.view` | Nome · CBO · Jornada · Salário base (moeda; **M** `hr.view_sensitive`) · Custo-hora (moeda; **M** `hr.view_sensitive`) |
| Notas de saída emitidas (`/app/fiscal/saida`, aba emitidas) | `fiscal.view` | Número · Série · Chave (texto) · Emissão (data) · Destinatário · CNPJ/CPF (como a tela) · CFOP · Produtos (moeda) · ICMS (moeda) · IPI (moeda) · Total (moeda) · Situação; total das autorizadas |
| Notas de entrada (`/app/fiscal/entrada`) | `fiscal.view` | Número · Série · Chave · Emissão (data) · Emitente · CNPJ · Total (moeda) · Pedido (texto) · Situação |
| Bens (`/app/patrimonio/bens`) | `assets.view` | Patrimônio (texto) · Nome · Categoria · Local · Responsável · Situação · Aquisição (data) · Valor de aquisição (moeda; **O** `assets.edit`) |
| Manutenções (`/app/patrimonio/manutencao`) | `assets.view` | Número · Bem · Tipo · Agendada (data) · Executor · Situação · Custo (moeda, como a tela) |
| Custo de manutenção (`/app/patrimonio/custo`) | `assets.view` | Bem · Período · Peças (moeda) · Mão de obra (moeda) · Terceiros (moeda) · Total (moeda) |
| Terceiros (`/app/cadastros/terceiros`) | `parties.view` | Nome/razão social · Nome fantasia · Tipo (cliente, fornecedor, instalador) · CNPJ/CPF (como a tela) · Cidade/UF · Situação |
| Itens (`/app/estoque/itens`) | `inventory.view` | Código · Descrição · Tipo · Categoria · Unidade · Controle (lote, série) · Mínimo (número) · Situação |
| Plano de contas (`/app/financeiro/plano-de-contas`) | `finance.view` | Código (texto) · Conta (texto, recuo por nível) · Natureza · Grupo do DRE · Situação |
| Centros de custo (`/app/financeiro/centros-de-custo`) | `finance.view` | Código · Centro (recuo por nível) · Situação |
| Descontos e cancelamentos (`/app/financeiro/receber/descontos-e-cancelamentos`) | `finance.view` | Data (data) · Título · Cliente · Tipo (desconto, cancelamento) · Valor (moeda) · Motivo · Por (texto); total |

## Regras de negócio e técnicas
- **Layouts novos** no módulo dono, sobre o frame: `BomPdfDocument`, `RoutingTemplatePdfDocument` (Engineering), `ProjectSummaryPdfDocument` (Commercial), `PurchaseRequestPdfDocument` (Purchasing), `ReceivablePdfDocument`, `PayablePdfDocument` (Finance), `MaintenanceOrderPdfDocument` (Assets), `QualityInspectionPdfDocument` (Production), `OwnerDashboardPdfDocument` (Dashboard, lendo os mesmos contratos do painel), `ThirteenthPreviewPdfDocument` (HR).
- **Resumo do projeto e painel por bloco:** cada bloco é montado só se o usuário tem a permissão daquele bloco (o mesmo critério da tela); o modelo do bloco ausente nem é lido.
- **Máscara do RH:** colunas `Sensitivity.Masked` com o formatador de CPF da RH-02b (3 primeiros e 2 últimos dígitos) e `***` para valores; com `hr.view_sensitive` sai completo. A auditoria grava `sensitive = true` quando o arquivo leva CPF ou salário sem máscara (adendo aos campos da EXP-01, sem tabela nova). Nome do arquivo sem nome nem CPF (`funcionarios-{data}.xlsx`, `folha-{competência}-{tipo}.xlsx`).
- **Migração:** cada layout antigo troca o próprio cabeçalho, rodapé, cores e record de empresa (`ShipmentPdfCompany`, `PayslipPdfCompany`, …) pelo `PdfCompany` e pelo frame; o conteúdo do corpo não muda. Teste de regressão: o texto extraído antes da migração é gravado como fixture no primeiro commit da story e comparado depois.
- **Etiqueta e DANFE:** ficam fora do frame (formato próprio, CA 21); a etiqueta passa a usar o `PdfQr` da EXP-03 em vez do `QRCoder` direto.
- **Sem dado novo exposto:** nenhum PDF ou xlsx traz coluna que a tela não mostra para aquele usuário.

## Permissões, eventos e tabelas
- **Permissões:** as das telas (`engineering.view`, `projects.view`, `purchasing.view`, `finance.view`, `assets.view`, `production.view`, `dashboard.view`, `hr.view`, `fiscal.view`, `parties.view`, `inventory.view`) e as de máscara (`hr.view_sensitive`, `hr.view_health`, `assets.edit`, `projects.view_cost`). Nenhuma ação nova.
- **Eventos:** nenhum. **Tabelas:** nenhuma; **migration:** nenhuma.

## Telas e estados
- **Documentos:** `<DocumentActions>` no cabeçalho, primeiro botão secundário; no painel, "Baixar PDF" ao lado do seletor de mês; na prévia do 13º, ao lado do seletor de ano e tipo.
- **Listas:** `<ExportMenu>` no slot `toolbar`; vazia, desabilitado com "Nada para exportar com estes filtros.".
- **Estados:** "Gerando PDF…" / "Gerando…" (`aria-busy`); erro abaixo do botão (`role="alert"`) com a mensagem do ProblemDetails e "Tentar de novo"; 422 com a mensagem.
- **Propositivo:** na BOM liberada, o próximo passo "Imprimir a BOM para a fábrica"; na OS de manutenção agendada, "Imprimir a OS para o executor"; na lista de funcionários sem `hr.view_sensitive`, a dica no menu "CPF e salário saem mascarados.".
- **400 px:** botões no menu "Mais ações".

## Ajuda (mesmo commit)
- Documentos novos: `Ajuda/producao-os-bom.md`, `Ajuda/engenharia-boms.md`, `Ajuda/engenharia-roteiro.md`, `Ajuda/projetos-projeto.md`, `Ajuda/compras-solicitacao.md`, `Ajuda/financeiro-receber-titulo.md`, `Ajuda/financeiro-pagar-titulo.md`, `Ajuda/patrimonio-manutencao-os.md`, `Ajuda/producao-qualidade-etapa.md`, `Ajuda/painel.md`, `Ajuda/rh-folha.md` (prévia do 13º).
- Listas: `Ajuda/rh-funcionarios.md`, `Ajuda/rh-folha.md`, `Ajuda/rh-folha-detalhe.md`, `Ajuda/rh-ponto-espelho.md`, `Ajuda/rh-cargos.md`, `Ajuda/fiscal-saida-emitidas.md`, `Ajuda/fiscal-entrada.md`, `Ajuda/patrimonio-bens.md`, `Ajuda/patrimonio-manutencao.md`, `Ajuda/patrimonio-custo.md`, `Ajuda/cadastros-terceiros.md`, `Ajuda/estoque-itens.md`, `Ajuda/financeiro-plano-de-contas.md`, `Ajuda/financeiro-centros-de-custo.md`, `Ajuda/financeiro-descontos-cancelamentos.md`.
- Migrados: só onde o botão mudou de lugar (`Ajuda/producao-romaneio.md` já mudou na EXP-03; `Ajuda/rh-ferias.md` e `Ajuda/rh-funcionario-ferias.md` com "Baixar aviso de férias" no lugar de "Gerar aviso de férias"; `Ajuda/rh-rescisao.md`).
- Seção "Imprimir e exportar" em cada uma; `<Dica>` em "Fotografia do painel" e em "CPF mascarado".

## Plano de testes
- **Unidade:** modelo de cada PDF novo (seções, marca por situação, blocos por permissão no projeto e no painel); formatador de máscara do RH; colunas omitidas sem `assets.edit`.
- **Integração (Testcontainers, PDF na imagem Alpine com texto extraído; xlsx lido com ClosedXML):** cada PDF novo com o dataset fixo (CA 3–10); IDOR da ficha do título (CA 2); prévia do 13º com "Simulação", 403 sem `hr.view_sensitive` e `no-store` (CA 11); funcionários com e sem `hr.view_sensitive` e sem `hr.view_health` (CA 12, 14) e o registro `sensitive` na auditoria; holerites da folha com 403 sem a ação (CA 13); cargos mascarados (CA 15); livro simples com total das autorizadas (CA 16); bens sem o valor sem `assets.edit` (CA 17); plano de contas com recuo (CA 18); **regressão por texto dos 11 layouts migrados** (CA 19) e marcas padrão (CA 20); etiqueta no tamanho configurado e DANFE com "Prévia" (CA 21). Acesso por rota fica com a varredura.
- **Vitest:** `<DocumentActions>` nas telas dos documentos novos; dica de máscara no menu de funcionários; bloco do painel sem permissão fora da URL do PDF.
- **E2E (Playwright, 1 fluxo):** Funcionários → "Exportar" → "Planilha (Excel)" com um usuário sem `hr.view_sensitive` (download `funcionarios-….xlsx`); a máscara é conferida no teste de integração.

## Fora do escopo
- Pacote da contadora em xlsx (continua CSV, RH-03).
- XML das notas em lote.
- 2FA para exportar dado sensível (decidido sem 2FA pelo PO em 02/11/2026; reavaliar com os itens de segurança adiados).
- Exportação acima do teto por job.

## Pontos em aberto
Nenhum que segure a story. Proposta desta story, a confirmar pelo PO na revisão: etiqueta e DANFE fora do cabeçalho e rodapé do frame (CA 21), porque o formato deles é fixo.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, PDF na imagem Alpine, xlsx com ClosedXML, regressão por texto dos 11 layouts), Vitest e o E2E passando.
- [ ] Nenhuma migration; nenhuma permissão nova.
- [ ] Nenhum record de empresa próprio nem cor literal sobrando nos layouts (busca no código).
- [ ] Telas com os estados, inclusive a 400 px.
- [ ] Ajudas atualizadas; teste de cobertura passando.
- [ ] Auditoria de exportação ligada (com `sensitive`).
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Fixtures de texto dos 11 layouts antes de migrar; migração um a um.
- [ ] 10 layouts novos; `/export` das 15 listas; formatador de máscara do RH no `ExportTable`.
- [ ] `<DocumentActions>` e `<ExportMenu>` nas telas; ajudas.

## Quality gates
- Agentes previstos: @dev, cybersecurity (RH e LGPD), @qa.
- Gate: CA 11, 12, 13 e 19 obrigatórios.

## Checklist de prontidão
Checklist do SM, 02/11/2026, sobre o pedido do PO do mesmo dia.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão | OK (CA 2, 11–15) |
| 6 | Dependências coerentes | OK (EXP-01, EXP-03, EXP-04) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas | OK (auditoria, stories ENG/COM/CMP/FIN/PAT/PRD/DSH/FLH/CAD/RH/FIS, ajudas e código lidos) |
| 9 | Telas com estados, ajuda e propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados | OK (1 proposta marcada) |

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 02/11/2026 | 0.1 | Story detalhada a partir da auditoria de PDF e exportação (onda 3); `Ready` pelo pedido do PO de 02/11/2026 | Claude (SM) |
| 03/11/2026 | 1.0 | Implementada (`Done`). Decisões do PO aplicadas: exportação sensível sem 2FA (a permissão basta; sem `hr.view_sensitive`, CPF `123.***.***-45` e valores `***`); auditoria com `sensitive = true` quando alguma coluna sensível sai sem máscara; etiqueta e DANFE fora do cabeçalho e rodapé do frame (CA 21); PDF não bloqueia por dado faltante da empresa (CA 1, ADR 0045 §1). Desvios: (1) resumo do projeto em `GET /projects/{id}/pdf` (os projetos já moram em `/api/v1/projects`); (2) BOM sem a lista de anexos do item e sem a marca de fantasma/agrupador (campos não existem; desenhos seguem na ficha da OS); (3) inspeção usa a marca padrão `Preview` ("Prévia, sem valor fiscal" + "Inspeção ainda não decidida."); (4) painel sem `?month`: a tela não tem seletor de mês, o PDF é a fotografia de agora e não há bloco "a pagar" (o painel não tem o card); (5) custo de manutenção exporta com `assets.edit`, a mesma permissão da tela, com as abas da tela (por bem, por mês, por centro), sem o desdobramento peças/mão de obra/terceiros que a tela não tem; manutenções sem coluna de custo e bens sem data de aquisição (a lista não mostra); (6) custo-hora de Cargos mascarado sem `hr.view_sensitive` na planilha (a tela mostra com `hr.view`); (7) prévia do 13º em PDF com os valores do motor do 13º simulado sem gravar (`ThirteenthSimulationAsync`), nome `previa-13-{ano}-{1\|2}a-parcela.pdf`; (8) romaneio estornado sai "Cancelado" com a faixa "Romaneio estornado"; DANFE de reserva de nota cancelada sai "Cancelado"; (9) valor de aquisição omitido sem `assets.edit` pelo `ExportSensitivity.Omitted` da EXP-04; (10) E2E fica para o nightly. | Dev (Claude) |
