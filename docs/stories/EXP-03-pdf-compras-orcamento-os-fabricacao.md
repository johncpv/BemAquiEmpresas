# EXP-03 — Onda 1: PDF da cotação e do mapa comparativo, pedido no template novo, "Baixar PDF" no orçamento, romaneio e ficha da OS de fabricação

| Campo | Valor |
|---|---|
| ID | EXP-03 |
| Título | PDF do pedido de cotação por fornecedor e do mapa comparativo; pedido de compra migrado para o `PdfDocumentFrame` com prévia "Rascunho" antes de aprovar; "Baixar PDF" no cabeçalho do orçamento (GET direto da revisão corrente); romaneio com `<DocumentActions>`; ficha da OS de fabricação em PDF (etapas, checklist, materiais, desenhos listados, QR por etapa, sem custo) |
| Tamanho | M |
| Status | Ready |
| Requisitos | Pedido do PO de 02/11/2026 (auditoria, seções A, D4 "EXP-03" e E) · R-CMP-2 (cotação e comparativo) · R-CMP-3 (pedido em PDF) · R-COML-5 (PDF do orçamento) · R-PRD-1 (OS) · R-ENG-6 (desenhos por OS) · R-PRD-8 (romaneio) · R-COM-3 · R-SEC-5 |
| Dependências | EXP-01 (`PdfDocumentFrame`, `PdfMark`, `PdfTable`, `PdfSignature`, `<DocumentActions>`, `useDownload`); CMP-02 (cotação), CMP-03b (`PurchaseOrderPdfDocument`), COM-03 (`QuotePdfDocument`), PRD-06b (`ShipmentPdfDocument`), PRD-01, ENG-02, EST-02 (OS, BOM e reservas) |
| Bloqueia | EXP-05 (usa o `PdfQr` criado aqui na migração da etiqueta) |
| Ordem de execução | 3 (Pós-go-live: documentos e exportação) |

## User story
Como **comprador e PCP**, quero **imprimir o pedido de cotação, o mapa comparativo, o pedido de compra e a ficha da OS, e baixar o PDF do orçamento sem procurar**, para **mandar ao fornecedor e levar o papel certo para o chão de fábrica**.

## Contexto e referências
- Auditoria de 02/11/2026 (`pdf-export-auditoria.md`): cotação, comparativo e OS de fabricação **sem PDF**; pedido de compra com PDF bom, mas **sem prévia em rascunho** (409 "Aprove o pedido antes de enviar."); orçamento com PDF **difícil de achar** (aba Documentos, "Gerar PDF" grava anexo e o download é outro clique); romaneio com PDF bom, botão próprio.
- **EXP-01 (base):** `GET /api/v1/{módulo}/{recurso}/{id}/pdf` com a permissão `view` do documento, `Content-Disposition` `{número}.pdf`, `PdfMark` (`Preview`, `Simulation`, `Draft`, `Cancelled`) com marca d'água e faixa em todas as páginas, 422 "Complete os dados da empresa para o PDF." com link, rodapé "Emitido por … · Página x de y"; `<DocumentActions pdf={{ url, fileName, label? }} print? />` à direita do título, primeiro botão secundário.
- CMP-03b: `PurchaseOrderPdfDocument` (300 linhas, 2.000 caracteres de observação, código e unidade do fornecedor, sem custo médio nem projeto); "Baixar PDF" gera na hora e não guarda arquivo; o PDF guardado é o do envio.
- COM-03: `POST /commercial/quotes/{id}/pdf` (`quotes.view`) gera e **guarda** o anexo `{número}-rev{n}.pdf`; o `POST` continua para o histórico e o envio por e-mail.
- Código: `QRCoder` já é usado em `AssetLabelPdfDocument` (Assets); o app do operador abre a etapa em `/fab/etapa/$stepId` (login do chão, PRD-03a/03b).

## Critérios de aceite
**Cotação (CMP-02)**
1. **Dado** `purchasing.view` e uma cotação, **quando** clico "Baixar PDF" na linha de um fornecedor (aba Respostas) ou escolho o fornecedor em "Baixar pedido de cotação" no cabeçalho, **então** baixa o **pedido de cotação** daquele fornecedor (`GET /api/v1/purchasing/rfqs/{id}/suppliers/{supplierId}/pdf`): número da cotação, data, fornecedor (razão social, CNPJ, contato), itens (código do fornecedor quando houver, descrição, quantidade e unidade do fornecedor convertidas), prazo de resposta (validade pedida), local de entrega (endereço da empresa, como no pedido) e o campo em branco para preço, prazo e condição por item, e "Resposta por: {nome e e-mail do comprador}"; **nunca** traz preço, nome ou resposta de outro fornecedor, nem a última compra (teste de extração de texto).
2. **Dado** a cotação `Open` com ao menos uma resposta, ou `Closed`, **quando** clico "Baixar mapa comparativo" no cabeçalho (ou na aba Comparativo), **então** baixa o **mapa comparativo** (`GET /api/v1/purchasing/rfqs/{id}/comparison/pdf`), paisagem: cartões por fornecedor (custo total = itens + frete + IPI, prazo, condição, situação da resposta, "1 cotação" quando houver só uma), a tabela item × fornecedor (preço, total da linha, custo com frete e IPI), o **menor custo** de cada linha destacado (negrito e o rótulo "Menor custo", sem depender de cor), o **vencedor** escolhido com o motivo quando não é o menor, a última compra como referência, e as assinaturas "Comprador" e "Aprovação" (`PdfSignature`).
3. **Dado** a situação da cotação, **quando** o mapa é gerado, **então** `Open` sai com a marca **"Prévia"** (vencedores ainda não gravados; as colunas mostram o "Sugerido"), `Closed` sem marca e `Cancelled` com **"Cancelado"**; o pedido de cotação por fornecedor não tem marca em `Open` e recusa `Closed`/`Cancelled` com 409 "A cotação não está aberta."

**Pedido de compra (CMP-03b)**
4. **Dado** o `PurchaseOrderPdfDocument`, **quando** migra para o `PdfDocumentFrame`, **então** o **texto extraído** do PDF de um pedido aprovado do dataset fixo é igual ao de antes (teste de regressão por texto, ignorando só o cabeçalho e o rodapé, que passam a ser os do frame), com os mesmos limites (300 linhas, 2.000 caracteres) e sem custo médio, margem nem projeto.
5. **Dado** `purchasing.view` e um pedido `Draft` ou `PendingApproval`, **quando** clico "Baixar PDF" (agora também nessas situações), **então** o PDF sai com a marca **"Rascunho"** e a faixa "Pedido não aprovado: não envie ao fornecedor."; `Cancelled` sai com **"Cancelado"**; de `Approved` em diante, sem marca. Enviar ao fornecedor continua só do `Approved` (o 409 do envio não muda); o PDF guardado no envio nunca tem marca.
6. **Dado** a tela do pedido, **quando** abre, **então** "Baixar PDF" fica no `<DocumentActions>` do cabeçalho em todas as situações (saindo do `OrderActions`), com "Imprimir"; o atalho "Baixar PDF" do erro de envio e do limite diário continua.

**Orçamento (COM-03)**
7. **Dado** `quotes.view` e um orçamento, **quando** abro a tela, **então** o cabeçalho tem "Baixar PDF" (`<DocumentActions>`) que baixa direto o PDF da **revisão corrente** (`GET /api/v1/commercial/quotes/{id}/pdf`, gerado na hora, **sem** gravar anexo), nome `{número}-rev{n}.pdf`; a aba Documentos continua com "Gerar PDF" (`POST`, grava no histórico) e o envio por e-mail; o PDF não tem custo nem margem (o mesmo layout da COM-03).
8. **Dado** a revisão corrente `Expired`, **quando** baixo, **então** o PDF sai com a faixa "Validade vencida em {data}: reabra com uma nova revisão." (sem marca d'água); `Draft`, `Sent` e `Approved` saem sem faixa (o rascunho é a proposta que se envia ao cliente); `Rejected` sai com a marca **"Cancelado"** e a faixa "Orçamento recusado".

**Romaneio (PRD-06b)**
9. **Dado** o romaneio, **quando** abro a tela, **então** o "Baixar PDF" do cabeçalho passa a ser o `<DocumentActions>` (mesma URL `GET /production/shipments/{id}/pdf`, mesmo nome, "Imprimir" junto); o layout e o "Baixar PDF" dos próximos passos não mudam (a migração do layout é da EXP-05).

**OS de fabricação (PRD-01, ENG-02, EST-02)**
10. **Dado** `production.view` e uma OS de fabricação, **quando** clico "Baixar PDF" no cabeçalho, **então** baixa a **ficha da OS** (`GET /api/v1/production/production-orders/{id}/pdf`, `{número}.pdf`) com: número, situação, projeto (número e nome), cliente, produto (código e descrição), quantidade, data prometida, fim previsto do plano, roteiro de origem, o **indicador de etapa** (letra da etapa atual e os segmentos, versão impressa do componente), e o QR da OS que abre `/app/producao/os/{id}`.
11. **Dado** a ficha, **quando** gerada, **então** tem a seção **Etapas**: uma linha por etapa com letra, nome, centro de trabalho, horas padrão, previsto início → fim (ou "Sem previsão: {motivo}"), situação, "Qualidade" quando for etapa de qualidade, o **checklist** com uma caixa em branco por item (ou marcada, quando a etapa já foi concluída) e o **QR da etapa** (`/fab/etapa/{stepId}`, para o operador abrir o apontamento); e o campo "Visto do supervisor".
12. **Dado** a BOM liberada, **quando** a ficha é gerada, **então** a seção **Materiais** lista, como a aba Material, item (código e descrição), necessidade, unidade, reservado, baixado, falta e local; sem BOM liberada, "BOM ainda não liberada." (a seção não some); com a BOM em `Draft`, só a nota "BOM em rascunho: materiais a confirmar.".
13. **Dado** anexos na aba Documentos, **quando** a ficha é gerada, **então** a seção **Desenhos e documentos** lista nome do arquivo, tipo, tamanho e data de cada anexo `Clean` (os em verificação aparecem como "Em verificação"); os desenhos **não** são embutidos no PDF.
14. **[Segurança]** **Dado** qualquer usuário, inclusive com `projects.view_cost`, **quando** baixa a ficha, **então** ela **não** traz custo apurado, custo-hora, custo do material, preço nem margem (teste de extração de texto com o dataset de custo); o QR carrega só a URL com o id (o acesso continua exigindo login e permissão).
15. **Dado** a situação da OS, **quando** gerada, **então** `Cancelled` sai com **"Cancelado"** e o motivo; `Open` sem etapa iniciada sai com a faixa "Etapas ainda podem mudar." (sem marca d'água); demais situações sem marca. Acima de 300 linhas somadas (etapas, itens de checklist e materiais), 422 antes de gerar (ADR 0022).

**Comum**
16. **[Segurança]** **Dado** um usuário sem a permissão `view` do documento (`purchasing.view`, `quotes.view`, `production.view`), **quando** chama o `/pdf`, **então** 403 (varredura `EndpointAuthorizationTests`); id de outro recurso, fornecedor fora da cotação ou etapa de outra OS dá 404 (IDOR, teste próprio no fornecedor da cotação); cada download fica na auditoria de exportação (`RecordExportAsync`, formato `pdf`, 1 documento).
17. **Dado** dados da empresa incompletos, **quando** gero qualquer PDF desta story, **então** 422 "Complete os dados da empresa para o PDF." com o link para Configurações → Empresa (regra do frame).

## Telas e conteúdo
| Tela (rota, aba) | Onde fica o botão | Rótulo | Permissão | Endpoint | Conteúdo do PDF | Marca por situação |
|---|---|---|---|---|---|---|
| Cotação (`/app/compras/cotacoes/$id`, aba `respostas`) | Cabeçalho (`<DocumentActions>` com menu de fornecedor) e linha de cada fornecedor | "Baixar pedido de cotação" / "Baixar PDF" | `purchasing.view` | `GET /purchasing/rfqs/{id}/suppliers/{supplierId}/pdf` | CA 1 | `Open`: nenhuma; `Closed`/`Cancelled`: 409 |
| Cotação (aba `comparativo`) | Cabeçalho e topo da aba | "Baixar mapa comparativo" | `purchasing.view` | `GET /purchasing/rfqs/{id}/comparison/pdf` | CA 2 | `Open`: **Prévia**; `Closed`: nenhuma; `Cancelled`: **Cancelado** |
| Pedido de compra (`/app/compras/pedidos/$id`) | Cabeçalho | "Baixar PDF" · "Imprimir" | `purchasing.view` | `GET /purchasing/purchase-orders/{id}/pdf` (existente, agora aceita todas as situações) | O da CMP-03b, no frame (CA 4) | `Draft`/`PendingApproval`: **Rascunho** + faixa; `Cancelled`: **Cancelado**; demais: nenhuma |
| Orçamento (`/app/orcamentos/$id`) | Cabeçalho | "Baixar PDF" · "Imprimir" | `quotes.view` | `GET /commercial/quotes/{id}/pdf` (novo; o `POST` fica) | O da COM-03 (sem custo) | `Expired`: faixa; `Rejected`: **Cancelado** + faixa; demais: nenhuma |
| Romaneio (`/app/producao/expedicao/$id`) | Cabeçalho | "Baixar PDF" · "Imprimir" | `production.view` | `GET /production/shipments/{id}/pdf` (existente) | Sem mudança | Sem mudança (EXP-05) |
| OS de fabricação (`/app/producao/os/$id`) | Cabeçalho, ao lado do indicador de etapa | "Baixar PDF" · "Imprimir" | `production.view` | `GET /production/production-orders/{id}/pdf` | CA 10–13 | `Cancelled`: **Cancelado**; `Open` sem etapa iniciada: faixa |

## Regras de negócio e técnicas
- **Layouts novos** no módulo dono, sobre o `PdfDocumentFrame`: `RfqRequestPdfDocument` e `RfqComparisonPdfDocument` (Purchasing), `ProductionOrderPdfDocument` (Production). Nenhum outro arquivo do módulo referencia QuestPDF (ADR 0022).
- **`PdfQr`** (BuildingBlocks/Pdf): helper do frame que desenha o QR (QRCoder, MIT, já no projeto pelo Assets) a partir de uma URL **absoluta do host do tenant** (o `TenantContext` dá o domínio; nunca o host da requisição sem validar); o `AssetLabelPdfDocument` passa a usá-lo na EXP-05.
- **Indicador de etapa impresso:** `PdfStageIndicator` (letra da etapa em destaque sobre fundo escuro + segmentos), com as cores do `PdfTheme`; impresso em preto e branco continua legível (letra e segmentos preenchidos/vazados).
- **Pedido de compra:** a regra "`Draft`/`PendingApproval` não gera PDF" da CMP-03b CA 1 muda para "gera com a marca Rascunho"; o envio continua exigindo `Approved`. Registrar a mudança no Change Log da CMP-03b.
- **Orçamento:** `GET .../pdf` chama o mesmo montador do `GeneratePdfAsync` sem o `StoreGeneratedAsync`; o `POST` não muda.
- **Ficha da OS:** lê a OS, as etapas e o plano (Production), a BOM e as reservas por `Engineering.Contracts` e `Inventory.Contracts` (os mesmos contratos da tela), os anexos pelo `IAttachments` do dono `ProductionOrder`; nenhum campo de custo é lido para o modelo do PDF (o modelo não tem propriedade de custo, verificado por teste de arquitetura simples no record).
- **Mapa comparativo:** o "menor custo" e o "Sugerido" vêm do mesmo cálculo da aba (frete rateado pelo valor dos itens); nada é recalculado no layout.
- **Nomes de arquivo:** `{COT}-{fornecedor curto}.pdf` (sem CNPJ), `{COT}-comparativo.pdf`, `{PC}.pdf`, `{ORC}-rev{n}.pdf`, `{OS}.pdf`.

## Permissões, eventos e tabelas
- **Permissões:** `purchasing.view`, `quotes.view`, `production.view` (as das telas). Nenhuma ação nova.
- **Eventos:** nenhum. **Tabelas:** nenhuma; **migration:** nenhuma.

## Telas e estados
- **`<DocumentActions>`** no cabeçalho das 5 telas, sempre o primeiro botão secundário à direita do título; na cotação, o botão abre a escolha do fornecedor (um item por fornecedor da cotação, com a situação da resposta).
- **Estados:** "Gerando PDF…" (`aria-busy`); erro abaixo do botão (`role="alert"`) com a mensagem do ProblemDetails e "Tentar de novo"; 422 dos dados da empresa com o link; 409 da cotação fechada com a mensagem.
- **Propositivo:** na cotação aberta sem resposta, o próximo passo "Baixar pedido de cotação" para cada fornecedor "Aguardando resposta"; na OS com a BOM liberada e a reserva feita, o próximo passo "Imprimir a ficha da OS"; no pedido em rascunho, a dica "A prévia sai com a marca Rascunho".
- **400 px:** "Baixar PDF" no menu "Mais ações" do cabeçalho.

## Ajuda (mesmo commit)
- `Ajuda/compras-cotacao.md` (pedido de cotação por fornecedor) e `Ajuda/compras-cotacao-comparativo.md` (mapa comparativo, "Prévia" enquanto aberta).
- `Ajuda/compras-pedido.md` e `Ajuda/compras-pedido-documentos.md` (prévia com "Rascunho", botão no cabeçalho).
- `Ajuda/comercial-orcamento.md` e `Ajuda/comercial-orcamento-documentos.md` ("Baixar PDF" no cabeçalho baixa sem guardar; "Gerar PDF" guarda no histórico).
- `Ajuda/producao-romaneio.md` (só o lugar do botão).
- `Ajuda/producao-os.md` e `Ajuda/producao-os-documentos.md` (ficha da OS: o que tem, o QR de cada etapa, sem custo, desenhos só listados).
- Seção "Imprimir e exportar" em cada uma; `<Dica>` em "Prévia" do mapa e em "QR da etapa".

## Plano de testes
- **Unidade:** modelo do pedido de cotação (unidade do fornecedor, sem preço); modelo do mapa (menor custo, vencedor com motivo); marcas por situação (cotação, pedido, orçamento, OS); modelo da ficha da OS sem campo de custo; `PdfQr` com URL do host do tenant.
- **Integração (Testcontainers, PDF na imagem Alpine, texto extraído):** pedido de cotação sem dado de outro fornecedor e IDOR do fornecedor (CA 1, 16); mapa aberto com "Prévia" e fechado sem marca (CA 2, 3); regressão de texto do pedido migrado (CA 4); pedido em rascunho com "Rascunho" e envio ainda recusado (CA 5); `GET` do orçamento sem gravar anexo (contagem de anexos igual) e faixa de vencido (CA 7, 8); ficha da OS com etapas, checklist, materiais, desenhos listados e **sem custo** com usuário `projects.view_cost` (CA 10–14); 300 linhas (CA 15); dados da empresa incompletos (CA 17); auditoria (CA 16).
- **Vitest:** botão no cabeçalho de cada tela com a URL e o nome certos; menu de fornecedor da cotação; o "Baixar PDF" do pedido em rascunho visível.
- **E2E (Playwright, 1 fluxo):** OS de fabricação → "Baixar PDF": download `{OS}.pdf` com tipo `application/pdf`.

## Fora do escopo
- Envio da cotação por e-mail ao fornecedor (fora do MVP, CMP-02).
- Migração dos layouts do orçamento e do romaneio para o frame, e as marcas do romaneio: EXP-05.
- Embutir os desenhos no PDF da OS; ficha da OS de instalação (EXP-04).
- Exportação xlsx das listas de compras e de produção: EXP-04.

## Pontos em aberto
Nenhum que segure a story. Propostas desta story, a confirmar pelo PO na revisão: marca "Cancelado" no orçamento recusado (CA 8) e QR da etapa para `/fab/etapa/{stepId}` (CA 11).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, PDF na imagem Alpine, texto extraído), Vitest e o E2E passando.
- [ ] Nenhuma migration; nenhuma permissão nova.
- [ ] Telas com os estados do `<DocumentActions>`, inclusive a 400 px.
- [ ] Ajudas atualizadas; teste de cobertura passando.
- [ ] Auditoria de exportação ligada.
- [ ] Change Log da CMP-03b atualizado (PDF em rascunho com marca).
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `PdfQr` e `PdfStageIndicator` nos BuildingBlocks.
- [ ] `RfqRequestPdfDocument`, `RfqComparisonPdfDocument`, `ProductionOrderPdfDocument`; migração do `PurchaseOrderPdfDocument`.
- [ ] `GET /commercial/quotes/{id}/pdf`; `<DocumentActions>` nas 5 telas; ajudas.

## Quality gates
- Agentes previstos: @dev, @qa; design (indicador de etapa impresso).
- Gate: CA 1, 4, 5 e 14 obrigatórios.

## Checklist de prontidão
Checklist do SM, 02/11/2026, sobre o pedido do PO do mesmo dia.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão | OK (CA 1, 14, 16) |
| 6 | Dependências coerentes | OK (EXP-01) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas | OK (auditoria, CMP-02, CMP-03b, COM-03, PRD-01, PRD-06b, código lido) |
| 9 | Telas com estados, ajuda e propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados | OK (2 propostas marcadas) |

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 02/11/2026 | 0.1 | Story detalhada a partir da auditoria de PDF e exportação (onda 1, compras, orçamento, romaneio e OS); `Ready` pelo pedido do PO de 02/11/2026 | Claude (SM) |
