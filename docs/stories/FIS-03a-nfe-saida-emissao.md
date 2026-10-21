# FIS-03a — NF-e de saída: rascunho a partir da expedição, emissão, autorização, rejeição, DANFE e XML ao cliente, impostos no custo do projeto

| Campo | Valor |
|---|---|
| ID | FIS-03a |
| Título | NF-e de saída criada a partir do romaneio (PRD-06b) ou de uma venda do projeto, com itens, séries, NCM, CFOP e tributos pela regra fiscal (FIS-02b); conferência e emissão pelo `IFiscalEmitter`; retorno assíncrono (processando, autorizada, rejeitada, denegada, contingência); correção da rejeitada; XML e DANFE guardados e enviados ao cliente; evento `InvoiceAuthorized` com os impostos para o custo realizado do projeto e o vínculo com o romaneio e os títulos |
| Tamanho | G (quebra da FIS-03) |
| Status | Draft |
| Requisitos | R-FIS-3 (NF-e de saída a partir da expedição/venda; autorização, DANFE PDF e XML por e-mail) · R-PRD-8 (vínculo com a NF-e de saída) · R-PRJ-5 (impostos no realizado) · R-FIS-4 · R-COM-1 · R-COM-3 · R-COM-4 · R-PRP-2 · R-PRP-3 · R-PRP-7 · R-SEC-5 |
| Dependências | FIS-01 (`IFiscalEmitter`, webhook), FIS-02a (configuração), FIS-02b (regras e cálculo), PRD-06b (romaneio, `ShipmentDispatched`; Fase 3), COM-04a/COM-05 (`IProjects`, custo do projeto), CAD-01a/CAD-02a (terceiro e item), COM-03 (e-mail com anexo), FIN-01 (`IReceivables`) |
| Bloqueia | FIS-03b (eventos), FIS-06 (venda mista), DSH-02 e FIN-08 (Fase 4: impostos e receita pela emissão), GOL-01a |
| Ordem de execução | 10 (Marco 5.4 "Nota de saída") |

## User story
Como **financeiro**, quero **emitir a nota do que foi expedido sem digitar item, série nem imposto**, e como **proprietário**, quero **que o imposto da nota apareça no custo do projeto**, para **faturar no dia da entrega e ver a margem real**.

## Contexto e referências
- PRD §4 ("Expedição: nº de série ─► NF-e"), R-FIS-3, R-PRD-8; `05-backlog` FIS-03 ("Da expedição/venda; autorização, rejeição tratada, cancelamento, CC-e, DANFE/XML por e-mail; baixa do produto acabado. CA: emitida e autorizada em homologação SEFAZ com 3 cenários definidos pela contadora").
- `04-fluxos-estados` §1 "NF-e de saída" (`Draft → Processing → Authorized | Rejected`) e revisão pré-Fase 0 §2.1 (estado `Denied` e inutilização); §2: `InvoiceAuthorized` (Fiscal) → "Inventory baixa produto acabado (Shipment); anexa XML/PDF ao projeto; e-mail ao cliente"; §3.3 (realizado: "impostos (NF autorizadas)").
- `03-modelo-dados` → `fiscal.outgoing_invoices` (`number`, `series`, `access_key`, `type`, `party_id`, `project_id`, `shipment_id`, `status`, `xml_path`, `pdf_path`, `protocol`, `totals`, `receivable_ids` → tabela de junção pela revisão pré-Fase 0 §2.1), `outgoing_invoice_items` (`item_id`, `serial_ids`, `ncm`, `cfop`, `quantity`, `unit_price`, `taxes`).
- **Fase 3 (rascunho em revisão, `D:\GIT\BemAquiEmpresas-fase3`):** PRD-06b (romaneio `Shipped` com séries; `ShipmentDispatched`; ponto em aberto 1 "**quem baixa o estoque**: romaneio ou NF-e autorizada"; CA 6 "Emitir NF-e" como "Disponível na Fase 5"; CA 8 estorno da expedição passa a exigir o cancelamento da nota).
- ADR 0019 §7 (arquivo gerado e e-mail com anexo), ADR 0021 (assíncrono com estado de demora), ADR 0022 (DANFE próprio pelo `IPdfRenderer`, se a ADR 0023 decidir), ADR 0018 (guarda fiscal de 5 anos).

## Critérios de aceite
**Rascunho**
1. **Dado** `fiscal.create` e um romaneio `Shipped` sem nota autorizada (ou `Draft`, Pontos em aberto 1), **quando** clico "Emitir NF-e" no romaneio (o `NextSteps` da PRD-06b passa a ter a ação), **então** nasce a nota `Draft` com: destinatário = cliente do projeto (`IParties`: documento, IE, indicador de contribuinte, endereço de cobrança ou o de entrega conforme a regra, e-mail de cobrança), operação sugerida (Venda de produção própria; Remessa para obra quando o projeto tem instalação e a regra existe, Pontos em aberto 2), itens do romaneio com descrição, NCM, CEST, origem, unidade, quantidade e séries (nas informações do item), valor unitário pelo contrato (preço do item do projeto, `IProjects`), transportadora, volumes e peso do romaneio (se a lacuna L-d estiver resolvida), e os tributos calculados pelo `TaxCalculator` (FIS-02b); `Idempotency-Key` na criação.
2. **Dado** uma venda sem romaneio (componente ou peça do projeto), **quando** clico "Nova NF-e" no projeto, **então** escolho itens do projeto (ou do estoque com `inventory.view`) e quantidades, com o mesmo preenchimento; nota avulsa sem projeto fica fora do MVP (Fase 10).
3. **Dado** o rascunho, **quando** edito (`fiscal.edit`), **então** posso mudar operação, regra aplicada (entre as ativas que se aplicam), quantidades até o expedido, valores, desconto, frete e informações complementares; o cálculo refaz na hora; **não** posso digitar imposto à mão (Pontos em aberto 3); totais da nota ao vivo; o vínculo com os títulos a receber do contrato (parcelas a faturar) é escolhido numa lista (`IReceivables.ForContractAsync`), só como referência (Pontos em aberto 4).
4. **Dado** o rascunho, **quando** clico "Pré-visualizar", **então** o `IFiscalEmitter.PreviewAsync` devolve a DANFE sem consumir número e ela abre numa nova aba, com a faixa "Prévia, sem valor fiscal".

**Emitir**
5. **Dado** o rascunho válido e as pré-condições (integração testada, configuração fiscal completa, certificado válido, regra ativa para cada item, cliente com documento e endereço com IBGE), **quando** clico "Emitir NF-e" e confirmo no diálogo grande com o impacto (destinatário, itens, totais, impostos, ambiente "Homologação: sem valor fiscal" ou "Produção"), **então** a nota vai a `Processing`, a chamada sai pelo `IFiscalEmitter.IssueNfeAsync` com o `Idempotency-Key` estável (FIS-01), e a tela mostra "Nota enviada à SEFAZ. Aguardando a autorização…"; pré-condição faltando: o diálogo lista com link e o botão fica desabilitado com o motivo.
6. **Dado** a resposta ou o webhook (FIS-01 CA 8 e 10), **quando** a nota é **autorizada**, **então** numa transação: `Authorized` com número, série, chave de acesso, protocolo e data; XML autorizado guardado (FIS-01 CA 9) e DANFE em PDF guardada (conforme a ADR 0023, Pontos em aberto 5 da FIS-01), ambos anexos de sistema da nota e também listados no projeto (aba Documentos); e o evento `InvoiceAuthorized` (`{ invoiceId, projectId, shipmentId, partyId, issuedOn, totalAmount, costTaxes: [{ costType: Tax, amount }], receivableIds }`, sem dado pessoal) vai para o outbox.
7. **Dado** a resposta **rejeitada**, **quando** chega, **então** a nota fica `Rejected` com o código, o motivo e a ação sugerida traduzidos (catálogo do EmissorFiscal), a notificação vai a quem emitiu com link, e "Corrigir e emitir de novo" volta a nota a `Draft` com os dados; a nova emissão usa a próxima tentativa do `Idempotency-Key` (FIS-01 ponto 7); o número consumido pela rejeição aparece na lista "Números a inutilizar" (FIS-03b).
8. **Dado** a resposta **denegada**, **quando** chega, **então** a nota fica `Denied` (não pode ser corrigida nem reemitida para o mesmo destinatário sem resolver a situação dele), com a orientação "A SEFAZ denegou a nota por irregularidade do destinatário. Fale com a contadora."; o número fica registrado como denegado.
9. **Dado** a situação `ContingencyPending` ou `Processing` por mais de 30 s, **quando** a tela está aberta, **então** mostra o estado de demora ("A SEFAZ está demorando. Avisaremos quando a nota for autorizada.") e **nunca** oferece reemitir; acima de 2 h, o alerta da FIS-01 CA 11.

**Efeitos da nota autorizada**
10. **Dado** o `InvoiceAuthorized`, **quando** o consumidor do **Commercial** processa, **então** grava em `commercial.project_costs` o custo realizado de impostos (`CostType.Tax`, origem `Invoice`, valor dos tributos marcados como custo na regra, FIS-02b CA 9), idempotente por `(Invoice, invoiceId)`; o Projeto 360 (COM-05) passa a mostrar o realizado de impostos e a margem refeita.
11. **Dado** o `InvoiceAuthorized`, **quando** o consumidor do **Inventory** processa, **então** age conforme a decisão do ponto em aberto 1 da PRD-06b: (a) se o romaneio já baixou o estoque, só vincula a nota ao romaneio e às séries (sem nova saída); (b) se a baixa espera a nota, lança a saída `Shipment` das séries do romaneio; em ambos, idempotente e sem duplicar saída (teste dos dois caminhos com uma flag até a decisão).
12. **Dado** a nota autorizada, **quando** a opção "Enviar ao cliente" está marcada (padrão sim), **então** o e-mail `invoice_authorized` sai pela fila para o e-mail de cobrança do cliente com o XML e a DANFE anexados (depois do `Clean`), corpo só com empresa, número, série, chave, valor total e data, cópia opcional; reenvio pelo botão "Reenviar XML e DANFE".
13. **Dado** a nota autorizada, **quando** abro o romaneio, o projeto ou o título vinculado, **então** vejo o número da nota com link; o romaneio com nota autorizada passa a exigir o cancelamento dela para o estorno (PRD-06b CA 8).

**Consulta, permissões e segurança**
14. **Dado** `fiscal.view`, **quando** abro "Fiscal → Notas de saída", **então** vejo número, série, data, cliente, projeto, valor, ambiente e situação (Rascunho neutro, Processando info, Contingência alerta, Autorizada ok, Rejeitada crítico, Denegada crítico, Cancelada neutro); filtros por período, situação, cliente e projeto na URL; totais do filtro (valor e impostos); 50 mil notas em menos de 1 s.
15. **Dado** o módulo `fiscal` não liberado, **quando** chamo, **então** 403 `module_not_released`; criar e emitir sem `fiscal.create`, editar o rascunho sem `fiscal.edit`: 403; `RequireModule` e `perm:`; IDOR em `/outgoing-invoices/{id}/items/{itemId}`; romaneio, projeto, terceiro, item e títulos validados pelos Contracts dos donos.
16. **[Segurança]** **Dado** duas emissões simultâneas do mesmo rascunho, ou dois rascunhos do mesmo romaneio, **quando** chegam, **então** só uma nota vai à SEFAZ (`xmin` na transição `Draft → Processing` e índice único parcial de nota não cancelada nem rejeitada por romaneio); o mesmo `Idempotency-Key` devolve a nota original.
17. **[Segurança]** **Dado** qualquer nota, **quando** existe, **então** nunca é apagada: rascunho só é descartado (exclusão lógica, sem número); nota com número fica para sempre, só muda por cancelamento ou inutilização (FIS-03b); XML e DANFE guardados 5 anos sem expurgo; rota DELETE inexistente (teste de arquitetura).
18. **Dado** qualquer mudança, **quando** abro o histórico, **então** vejo antes e depois, quem e quando, a regra fiscal e a versão usadas; dados do destinatário mascarados no diff.

**Homologação SEFAZ**
19. **Dado** os 3 cenários definidos pela contadora (Pontos em aberto 6; sugeridos: venda de elevador em SP, remessa para obra e devolução ou peça de reposição), **quando** emitidos no ambiente de homologação da SEFAZ, no tenant de teste (ADR 0020; FIS-01 ponto 3), **então** os três são **autorizados** e o XML é conferido pela contadora (CA do backlog); o registro fica no runbook.

## Regras de negócio e técnicas
- **`outgoing_invoices`** (03 + proposta): `type` (`NFe`), `operation`, `tax_rule_versions` (por item), `environment`, `status` (`Draft`/`Processing`/`ContingencyPending`/`Authorized`/`Rejected`/`Denied`/`Cancelled`), `emitter_document_id` (id no EmissorFiscal), `attempt`, `rejection_code`, `rejection_reason`, `suggested_action`, `authorized_at`, `xml_attachment_id`, `danfe_attachment_id` (no lugar de `xml_path`/`pdf_path`), `totals` (jsonb: produtos, desconto, frete, impostos por tributo, total), `cost_tax_amount`, `client_request_id`, `xmin`; `outgoing_invoice_receivables` (junção, revisão pré-Fase 0 §2.1); itens com `taxes` (jsonb) e `serial_ids`.
- **Número e chave** vêm do EmissorFiscal; o BemAqui não numera NF-e.
- **Dono do anexo** `OutgoingInvoice` (xml e pdf; leitura `fiscal.view`, escrita só do sistema).
- **Visibilidade financeira:** a nota não cria título (os títulos nascem do contrato, COM-04b); o vínculo é referência; a receita por competência (FIN-08, P-17 "pela data da venda/emissão") usa a data de emissão da nota (Pontos em aberto 4).
- Payload com dados do destinatário só no EmissorFiscal (FIS-01 CA 13).

## Permissões, eventos e tabelas
- **Permissões:** `fiscal.view`, `fiscal.create` (criar e emitir), `fiscal.edit` (rascunho). (Cancelar e inutilizar: FIS-03b, `fiscal.approve`.)
- **Eventos (`Fiscal.Contracts`):** `InvoiceAuthorized`, `InvoiceRejected` (sino), `InvoiceDenied`; consumidores no Commercial (custo de impostos) e no Inventory (vínculo ou saída).
- **Contratos:** `IShipments` (Production, proposto na PRD-06b), `IProjects`, `IParties`, `IItems`, `IReceivables`, `IOutgoingInvoices` (Fiscal, novo: notas por projeto e por romaneio).
- **Tabelas:** `fiscal.outgoing_invoices`, `fiscal.outgoing_invoice_items`, `fiscal.outgoing_invoice_receivables`. Template de e-mail `invoice_authorized`. Tipo de numeração: nenhum (o número é da SEFAZ).

## Telas e estados
- **Fiscal → Notas de saída** (`/app/fiscal/saida`, entrada no `modules.ts`), **Nota** (`/app/fiscal/saida/$id`, abas Dados, Itens e tributos, Documentos, Eventos (FIS-03b), Histórico de alterações); botão "Emitir NF-e" no romaneio e "Nova NF-e" no projeto.
- **Verbos:** "Salvar rascunho" / "Rascunho salvo"; "Pré-visualizar"; "Emitir NF-e" / "Nota enviada à SEFAZ"; "Corrigir e emitir de novo" / "Rascunho reaberto"; "Reenviar XML e DANFE" / "XML e DANFE reenviados"; "Descartar rascunho" / "Rascunho descartado".
- **Estados:** carregando; vazio ("Nenhuma nota emitida. Emita a nota a partir de um romaneio expedido." + "Ver romaneios expedidos"); processando com o estado de demora; rejeitada com o motivo e a ação sugerida em destaque; sem configuração ("Configure o fiscal antes de emitir." com o que falta); faixa âmbar de homologação.
- **Propositivo:** tudo preenchido do romaneio, do projeto e da regra; o `NextSteps` da nota autorizada oferece "Ver projeto" e, com instalação, "Emitir NFS-e da instalação" (FIS-06, quando existir); rejeição leva direto ao campo da causa quando o código permite.
- **400 px:** colunas sempre visíveis: número, cliente, valor e situação; itens e tributos em cartões; diálogo de emissão em tela cheia; E2E de 400 px e só pelo teclado no diálogo de emissão.
- **Ajuda:** `Ajuda/fiscal-saida.md`, `Ajuda/fiscal-nota-saida.md` (abas `dados` padrão, `itens`, `documentos`, `eventos`, `historico`); `<Dica>` em "Chave de acesso", "Contingência", "Rejeitada", "Denegada", "Regra aplicada" e "Ambiente".

## Plano de testes
- **Unidade:** montagem do rascunho a partir do romaneio (CA 1); totais e reaplicação do cálculo (CA 3); pré-condições (CA 5).
- **Integração (Testcontainers + fake do EmissorFiscal):** rascunho idempotente (CA 1); emissão com 201, 202 e webhook (CA 5, 6, 9); rejeição, correção e nova tentativa com o número a inutilizar (CA 7); denegada (CA 8); `InvoiceAuthorized` → custo de impostos idempotente (CA 10); Inventory nos dois caminhos do ponto 1 da PRD-06b (CA 11); e-mail com XML e DANFE depois do `Clean` (CA 12); vínculos (CA 13); desempenho da lista (CA 14); três camadas e IDOR (CA 15); concorrência e idempotência (CA 16); imutabilidade e arquitetura (CA 17); auditoria mascarada (CA 18).
- **Homologação SEFAZ (manual, CA 19):** 3 cenários autorizados no tenant de teste, XML conferido pela contadora.
- **E2E (Playwright):** romaneio expedido → emitir NF-e (fake autoriza) → ver o custo de impostos no projeto; rejeição e correção; tela a 400 px.

## Fora do escopo
- Cancelamento, carta de correção e inutilização: FIS-03b. NFS-e: FIS-06. Nota avulsa sem projeto: Fase 10. Nota complementar e de ajuste: fora do MVP.

## Pontos em aberto
1. **Quem baixa o estoque** (PO, herdado da PRD-06b ponto 1, Fase 3 em aberto): romaneio (a baixa da Fase 3 fica e a nota só vincula) ou nota autorizada (o `04-fluxos` §2). A story testa os dois caminhos até a decisão.
2. **Remessa para obra e venda** (contadora): a Konebras emite remessa para obra e depois a venda, ou só a venda? Define a operação sugerida e os títulos vinculados.
3. **Imposto digitado à mão** (PO/contadora): proposta: proibido no MVP; exceção só por uma regra ativa nova.
4. **Nota × parcelas do contrato** (PO/contadora): a nota é emitida pelo total do equipamento na expedição, e as parcelas (sinal, fabricação, entrega) já existem; vínculo só como referência (proposta) ou a nota deve trazer as duplicatas (lacuna L-d)? E a receita do DRE (P-17) passa a ser a data da nota?
5. **DANFE em PDF** (herdado da FIS-01 ponto 5).
6. **Os 3 cenários da contadora** (contadora): pré-condição do CA 19.
7. **Certificado A1** (Konebras, lacuna L1 do EmissorFiscal): sem ele, nenhuma autorização real; pré-condição externa.

## Definition of Done
- [ ] Critérios de aceite atendidos; CA 19 executado e registrado.
- [ ] Testes de unidade, integração (Testcontainers + fake), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`; dono de anexo `OutgoingInvoice`; template de e-mail registrado.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajudas de cada tela e aba; teste de cobertura passando.
- [ ] Efeito fiscal com confirmação e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (estados da NF-e, `InvoiceAuthorized` com impostos) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `OutgoingInvoice` e itens (Domain) com as transições; montagem a partir do romaneio e do projeto.
- [ ] Emissão pelo `IFiscalEmitter`, handler das transições (webhook e job), guarda do XML e da DANFE.
- [ ] Consumidores no Commercial (custo de impostos) e no Inventory (vínculo ou saída).
- [ ] Endpoints `/api/v1/fiscal/outgoing-invoices` (+ `/preview`, `/issue`, `/reopen`, `/resend`); telas, diálogos e ajudas; template `invoice_authorized`.

## Quality gates
- Agentes previstos: @dev, @qa, cybersecurity (idempotência, dados do destinatário, imutabilidade), contadora (CA 19).
- Gate: CA 5, 6, 7, 10, 11, 16, 17 e 19 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** a baixa do estoque ficou dependente da decisão da PRD-06b, testada nos dois caminhos (CA 11); estado `Denied` e "nunca reemitir em contingência" (CA 8, 9); impostos no custo do projeto pelo evento (CA 10); nota não cria título (regras).
- **Pré-condições para `Ready`:** PRD-06b ponto 1 decidido; FIS-01 (ADR 0023), FIS-02a e FIS-02b prontas; pontos em aberto 2, 4 e 6; A1 (ponto 7) para o CA 19.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da FIS-03) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
