# FIS-03a — NF-e de saída: rascunho a partir do romaneio, emissão, autorização, rejeição, DANFE e XML ao cliente, impostos e receita pela nota

| Campo | Valor |
|---|---|
| ID | FIS-03a |
| Título | NF-e de saída do equipamento criada a partir do romaneio expedido (PRD-06b), com os itens que não são serviço (venda mista, D4), séries, NCM, CFOP e tributos pela regra fiscal (FIS-02b); saneamento pelos limites do esquema antes de reservar o número; emissão pelo `IFiscalEmitter`; retorno assíncrono (processando, autorizada, rejeitada, denegada, contingência); correção da rejeitada; XML e DANFE em PDF guardados e enviados ao cliente; a nota só referencia o romaneio (nunca baixa estoque); imposto realizado na razão do projeto (`InvoiceTax`) e receita pela data da nota; fila "A emitir" |
| Tamanho | G (quebra da FIS-03) |
| Status | Ready (pré-condição externa) |
| Requisitos | R-FIS-3 (NF-e de saída a partir da expedição/venda; autorização, DANFE PDF e XML por e-mail) · R-PRD-8 (vínculo com a NF-e de saída) · R-PRJ-5 (impostos no realizado) · R-FIS-4 · R-COM-1 · R-COM-3 · R-COM-4 · R-PRP-2 · R-PRP-3 · R-PRP-7 · R-SEC-5 |
| Dependências | FIS-01 (`IFiscalEmitter`, webhook, `fiscal-pending-check`), FIS-02a (configuração, `ReadinessAsync`), FIS-02b (regras e cálculo), PRD-06b (romaneio `Shipped`, `IShipments`), PRD-04 (`commercial.project_costs`), COM-04a/COM-05 (`IProjects`, preço e tipo do item), CAD-01a/CAD-02a (terceiro e item), COM-03 (e-mail com anexo), DSH-04 (`IAgendaSource`), BNK-01a (`EnvironmentBanner`); protótipo de nota de saída (D18) |
| Bloqueia | FIS-03b (eventos), FIS-06 (venda mista), GOL-01a |
| Ordem de execução | 10 (Marco 5.4 "Nota de saída") |

## User story
Como **financeiro**, quero **emitir a nota do equipamento expedido sem digitar item, série nem imposto**, e como **proprietário**, quero **que o imposto da nota apareça no custo do projeto e a receita na data da nota**, para **faturar no dia da entrega e ver a margem real**.

## Contexto e referências
- PRD §4 ("Expedição: nº de série ─► NF-e"), R-FIS-3, R-PRD-8; `05-backlog` FIS-03 ("Da expedição/venda; autorização, rejeição tratada, cancelamento, CC-e, DANFE/XML por e-mail. CA: emitida e autorizada em homologação SEFAZ com 3 cenários definidos pela contadora").
- **Revisão pré-Fases 4 a 8** ([2026-10-21-revisao-pre-fases4-8.md](../revisoes/2026-10-21-revisao-pre-fases4-8.md)): D3 (lacunas do Emissor antes desta story), **D4 (venda mista e receita)**, D8 (imposto realizado só pela nota), D12 (NF-e só referencia o romaneio; série exclusiva), D18 (protótipo de nota de saída antes do Marco 5.4); tabela 3.2, pontos 45 a 51; §4.1 (sem os dois caminhos de baixa); §4.3 (saneamento e escape); §4.4 padrão 6 e o achado "fila A emitir em Notas de saída e na pauta".
- **Revisão pré-Fase 3, D4:** o romaneio confirmado baixa o estoque (chamada síncrona); na Fase 5 a NF-e referencia o romaneio e nunca baixa de novo (PRD-06b CA 12).
- **ADR 0025, adendo de 21/10/2026** ([0025-custo-real-do-projeto.md](../adr/0025-custo-real-do-projeto.md): fontes `InvoiceTax` e `InvoiceTaxReversal` na razão do projeto, com data de corte). **ADR 0027** ([0027-integracao-emissorfiscal.md](../adr/0027-integracao-emissorfiscal.md)). **ADR 0042** ([0042-configuracoes-fiscais-e-integracoes.md](../adr/0042-configuracoes-fiscais-e-integracoes.md): produção exige os campos críticos conferidos). **ADR 0034** ([0034-promocao-a-producao.md](../adr/0034-promocao-a-producao.md): SEFAZ em produção só na GOL-01b).
- `04-fluxos-estados` §1 "NF-e de saída" (`Draft → Processing → Authorized | Rejected | Denied`); §2: `InvoiceAuthorized` passa a não mexer no estoque (D4); §3.3 (realizado: "impostos (NF autorizadas)").
- `03-modelo-dados` → `fiscal.outgoing_invoices`, `outgoing_invoice_items`; `outgoing_invoice_receivables` (junção da revisão pré-Fase 0 §2.1, agora só referência).
- `commercial.quote_items.kind` (`Equipment`, `Product`, `Service`): base da divisão da venda mista (D4).
- ADR 0019 §7 (arquivo gerado e e-mail com anexo), ADR 0021 (assíncrono com estado de demora), ADR 0018 (guarda fiscal de 5 anos).

## Critérios de aceite
**Rascunho**
1. **Dado** `fiscal.create` e um romaneio `Shipped` sem nota ativa (rascunho, processando ou autorizada), **quando** clico "Emitir NF-e" no romaneio (o `NextSteps` da PRD-06b passa a ter a ação) ou na fila "A emitir" (CA 16), **então** nasce a nota `Draft` com: destinatário = cliente do projeto (`IParties`: documento, IE, indicador de contribuinte, endereço, e-mail de cobrança), operação **Venda de produção própria** (ou Revenda, pelo tipo do item; nunca remessa, D4), os itens do romaneio com descrição, NCM, CEST, origem, unidade, quantidade e séries (nas informações do item), valor unitário pelo contrato (preço do item do projeto de tipo `Equipment` ou `Product`, `IProjects`), transportadora, volumes e peso do romaneio com a modalidade do frete padrão (FIS-02a), o endereço da obra nas informações complementares, e os tributos calculados pelo `TaxCalculator` (FIS-02b); `Idempotency-Key` na criação.
2. **Dado** um projeto com itens a faturar sem romaneio, **quando** clico "Faturar" no projeto, **então** vejo os romaneios expedidos sem nota (com "Emitir NF-e") e, para o que ainda não saiu, "Novo romaneio" (PRD-06b): toda NF-e de mercadoria nasce de um romaneio, porque só o romaneio baixa o estoque (D4); nota avulsa sem projeto fica fora do MVP.
3. **Dado** o rascunho, **quando** edito (`fiscal.edit`), **então** posso mudar a regra aplicada (entre as ativas que se aplicam), quantidades até o expedido, valores, desconto, frete e informações complementares; o cálculo refaz na hora; **não** existe campo de imposto digitado (ponto 47); totais da nota ao vivo; a nota **não** traz duplicatas (FIS-02a: "Duplicatas na NF-e: não", D4) e mostra, só como referência, as parcelas do contrato (`IReceivables.ForContractAsync`), com a `<Dica>` "A parcela do contrato é só cobrança. A receita conta pela data desta nota."
4. **[Segurança]** **Dado** o rascunho, **quando** salvo, pré-visualizo ou emito, **então** o servidor saneia antes de reservar qualquer número: textos livres (descrição, informações do item e complementares, nome e endereço do destinatário) sem caracteres de controle, com espaços normalizados e dentro dos limites do esquema da NF-e 4.00 (por exemplo `xProd` 120, `infAdProd` 500, `infCpl` 5000, `xNome` 60, `xLgr` 60), recusando com 422 por campo ("A descrição do item 3 passa de 120 caracteres.") sem chamar o Emissor; os campos livres vão escapados ao XML (serializador do Emissor) e ao DANFE em PDF; nada é montado como HTML.
5. **Dado** o rascunho, **quando** clico "Pré-visualizar", **então** o `IFiscalEmitter.PreviewAsync` devolve a DANFE em PDF sem consumir número, aberta numa nova aba com a faixa "Prévia, sem valor fiscal".

**Emitir**
6. **Dado** o rascunho válido e as pré-condições (integração testada, `IFiscalSettings.ReadinessAsync` aprovado, com os campos críticos e as regras usadas **conferidos** quando o ambiente é Produção (ADR 0042), certificado válido, regra ativa para cada item, sem DIFAL, cliente com documento e endereço com IBGE), **quando** clico "Emitir NF-e" e confirmo no `ConfirmDialog` grande com o impacto (destinatário, itens, totais, impostos, o que entra no custo do projeto, ambiente pelo `EnvironmentBanner`), **então** a nota vai a `Processing` com o `payload_hash` (FIS-01 CA 5), a chamada sai pelo `IFiscalEmitter.IssueNfeAsync` com o `Idempotency-Key` `nfe-{invoiceId}-{tentativa}`, e a tela mostra "Nota enviada à SEFAZ. Aguardando a autorização…"; pré-condição faltando: o diálogo lista cada uma com o link para resolver e o botão fica desabilitado com o motivo.
7. **Dado** a resposta ou o webhook (FIS-01 CA 11 e 14), **quando** a nota é **autorizada**, **então** numa transação: `Authorized` com número, série, chave de acesso, protocolo e data; XML autorizado guardado (FIS-01 CA 13) e DANFE em PDF do Emissor guardada (lacuna L-c; reserva da ADR 0027: `IPdfRenderer` a partir do XML), ambos anexos de sistema da nota e listados no projeto (aba Documentos); e o evento `InvoiceAuthorized` (`{ invoiceId, type: NFe, projectId, shipmentId, partyId, issuedOn, totalAmount, taxes: [{ tax, amount, isCost }], costTaxAmount }`, sem dado pessoal) vai para o outbox.
8. **Dado** a resposta **rejeitada**, **quando** chega, **então** a nota fica `Rejected` com o código, o motivo e a ação sugerida traduzidos, a notificação vai a quem emitiu com link, e "Corrigir e emitir de novo" volta a nota a `Draft` com os dados; a nova emissão usa a próxima tentativa do `Idempotency-Key`; o número consumido pela rejeição entra em "Números a inutilizar" (FIS-03b).
9. **Dado** a resposta **denegada**, **quando** chega, **então** a nota fica `Denied` (não pode ser corrigida nem reemitida para o mesmo destinatário sem resolver a situação dele), com a orientação "A SEFAZ denegou a nota por irregularidade do destinatário. Fale com a contadora."; o número fica registrado como denegado.
10. **Dado** a situação `ContingencyPending` ou `Processing` por mais de 30 s, **quando** a tela está aberta, **então** mostra "Demorando" ("A SEFAZ está demorando. Avisaremos quando a nota for autorizada.") e **nunca** oferece reemitir; a conferência segue pelo `fiscal-pending-check` com a consulta forçada (FIS-01 CA 14); acima de 2 h, o alerta da FIS-01 CA 12.

**Efeitos da nota autorizada**
11. **Dado** o `InvoiceAuthorized`, **quando** o consumidor do **Commercial** processa (inbox), **então** grava na razão `commercial.project_costs` o imposto realizado (`CostType.Tax`, fonte **`InvoiceTax`**, `source_id` = nota, `seq` 1, valor = tributos marcados como custo na regra, `occurred_on` = data da nota), idempotente por `(InvoiceTax, invoiceId, seq)` (ADR 0025 adendo); o Projeto 360 e a DSH-02 passam a mostrar o imposto realizado do equipamento pela nota no lugar do orçado (D8).
12. **Dado** a nota autorizada, **quando** o estoque é olhado, **então** **nada** muda nele: a nota só referencia o romaneio (`shipment_id`) e as séries, nenhum consumidor do Inventory reage ao `InvoiceAuthorized` e nenhum movimento nasce (teste: emitir e autorizar não cria movimento de estoque; D4 da Fase 3); o estorno da expedição de um romaneio com nota ativa é recusado pela Produção, que consulta `IOutgoingInvoices.ActiveForShipmentAsync`, com 409 "Cancele a NF-e {número} antes de estornar a expedição." (PRD-06b CA 11).
13. **Dado** a venda mista (D4), **quando** o Financeiro e o painel leem o faturamento, **então** `IOutgoingInvoices` (Fiscal.Contracts, novo) devolve as notas autorizadas e não canceladas por período (`RevenueAsync(from, to)`, pela **data da nota**) e por projeto (`InvoicedByProjectAsync`, separando NF-e de equipamento e NFS-e de serviço); a FIN-08 (receita do DRE pela data da nota, sinal como adiantamento até a nota) e a DSH-01a/01b (faturamento e "a faturar" = contratado − notas) passam a usar esse contrato; a NF-e leva só os itens do orçamento que não são `Service`; os `Service` ficam para a NFS-e no aceite técnico (FIS-06); o rótulo "provisório" dessas telas continua enquanto o tenant não tiver nota real (no `konebras`, até a GOL-01b).
14. **Dado** a nota autorizada, **quando** a opção "Enviar ao cliente" está marcada (padrão sim), **então** o e-mail `invoice_authorized` sai pela fila para o e-mail de cobrança do cliente com o XML e a DANFE em PDF anexados (depois do `Clean`), corpo só com empresa, número, série, chave, valor total e data, cópia opcional; reenvio por "Reenviar XML e DANFE".
15. **Dado** a nota autorizada, **quando** abro o romaneio, o projeto ou um título do contrato, **então** vejo o número da nota com link.
16. **Dado** `fiscal.create`, **quando** abro "Fiscal → Notas de saída", **então** a aba **"A emitir"** lista os romaneios `Shipped` sem nota ativa (projeto, cliente, data da expedição, dias desde a expedição, valor estimado) com "Emitir NF-e"; a mesma fila entra na pauta (`IAgendaSource`, categoria "Notas a emitir", para quem tem `fiscal.create`), com o atraso acima de 1 dia útil em destaque.

**Consulta, permissões e segurança**
17. **Dado** `fiscal.view`, **quando** abro a aba "Emitidas", **então** vejo número, série, data, cliente, projeto, valor, ambiente e situação (Rascunho neutro, Processando info, Contingência alerta, Autorizada ok, Rejeitada crítico, Denegada crítico, Cancelada neutro); filtros por período, situação, cliente e projeto na URL; totais do filtro (valor e impostos); 50 mil notas em menos de 1 s.
18. **Dado** o módulo `fiscal` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; criar e emitir sem `fiscal.create`, editar o rascunho sem `fiscal.edit`: 403 e o botão some; `RequireModule` e `perm:`; IDOR em `/outgoing-invoices/{id}/items/{itemId}`; XML e DANFE só por endpoint autorizado que confere `fiscal.view` e a nota do tenant (anexo autorizado por registro, storage com prefixo do tenant); romaneio, projeto, terceiro, item e títulos validados pelos Contracts dos donos.
19. **[Segurança]** **Dado** duas emissões simultâneas do mesmo rascunho, ou dois rascunhos do mesmo romaneio, **quando** chegam, **então** só uma nota vai à SEFAZ (`xmin` na transição `Draft → Processing` e índice único parcial de nota ativa por romaneio, `status NOT IN ('Cancelled','Rejected','Denied')` e não descartada); o mesmo `Idempotency-Key` devolve a nota original.
20. **[Segurança]** **Dado** qualquer nota, **quando** existe, **então** nunca é apagada: rascunho só é descartado (exclusão lógica, sem número); nota com número fica para sempre, só muda por cancelamento ou inutilização (FIS-03b); XML e DANFE guardados 5 anos sem expurgo; rota DELETE inexistente (teste de arquitetura).
21. **Dado** qualquer mudança, **quando** abro o histórico, **então** vejo antes e depois, quem e quando, a regra fiscal e a versão usadas; dados do destinatário mascarados no diff.

**Homologação SEFAZ**
22. **Dado** os 3 cenários (ponto 50): **venda a contribuinte em SP** (5101), **venda a não contribuinte em SP** (5101, consumidor final) e **peça ou outro estado** (5102 ou 6101), **quando** emitidos no ambiente de homologação da SEFAZ no tenant `demo` (ADR 0020; FIS-01 CA 9), **então** os três são **autorizados** e o XML é conferido pela contadora (CA do backlog); o registro fica no runbook; nada é emitido no `konebras` antes da GOL-01b.

## Regras de negócio e técnicas
- **`outgoing_invoices`** (03 + colunas): `type` (`NFe`/`NFSe`), `operation`, `environment`, `status` (`Draft`/`Processing`/`ContingencyPending`/`Authorized`/`Rejected`/`Denied`/`Cancelled`), `emitter_document_id`, `attempt`, `payload_hash`, `rejection_code`, `rejection_reason`, `suggested_action`, `authorized_at`, `issued_on`, `xml_attachment_id`, `danfe_attachment_id` (no lugar de `xml_path`/`pdf_path`), `totals` (jsonb), `cost_tax_amount`, `shipment_id`, `discarded_at`, `xmin`; índice único parcial de nota ativa por romaneio; `outgoing_invoice_items` com `taxes` (jsonb), `tax_rule_version_id`, `serial_ids`, `quote_item_kind`; `outgoing_invoice_receivables` (junção, só referência).
- **Número e chave** vêm do Emissor; o BemAqui não numera NF-e.
- **Venda mista (D4):** equipamento e peças (`Equipment`, `Product`) em NF-e na expedição; `Service` em NFS-e no aceite técnico (FIS-06); a parcela do contrato é só cobrança (boleto), nunca faturamento; a nota não cria nem muda título.
- **Receita:** pela data da nota (FIN-08 lê `IOutgoingInvoices`); o sinal fica como adiantamento até a nota.
- **Estoque:** a nota não baixa (D4 da Fase 3); a baixa foi no romaneio.
- **Imposto no projeto:** fonte `InvoiceTax` na razão (ADR 0025 adendo); até a nota, o projetado usa o orçado (D8).
- **Dono do anexo** `OutgoingInvoice` (xml e pdf; leitura `fiscal.view`, escrita só do sistema).
- Payload com dados do destinatário só no Emissor (FIS-01 CA 16).

## Permissões, eventos e tabelas
- **Permissões:** `fiscal.view`, `fiscal.create` (criar e emitir), `fiscal.edit` (rascunho). Cancelar e inutilizar: FIS-03b (`fiscal.approve`).
- **Eventos (`Fiscal.Contracts`):** `InvoiceAuthorized`, `InvoiceRejected` (sino), `InvoiceDenied`; consumidor só no Commercial (imposto na razão); nenhum no Inventory.
- **Contratos:** `IShipments` (Production), `IProjects`, `IParties`, `IItems`, `IReceivables`, `IFiscalSettings`; **`IOutgoingInvoices`** (Fiscal, novo: `ActiveForShipmentAsync`, `RevenueAsync`, `InvoicedByProjectAsync`, notas por projeto e por romaneio); fonte da pauta `IAgendaSource`.
- **Tabelas:** `fiscal.outgoing_invoices`, `fiscal.outgoing_invoice_items`, `fiscal.outgoing_invoice_receivables`; `commercial.project_costs` com a fonte `InvoiceTax`. Template de e-mail `invoice_authorized`.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 6 (emissão: "Enviado / Aguardando / Demorando (30 s) / Confirmado / Recusado com motivo e ação", sem reenvio enquanto processa; `EnvironmentBanner`); 7 (emissão com impacto em lista no `ConfirmDialog` criado na BNK-01b, reutilizado); achado pontual "fila A emitir em Notas de saída e na pauta"; botões de `EmptyState`/`ErrorState` com 44 px a 400 px. Segue o **protótipo de nota de saída** (rascunho, emissão, rejeitada) feito antes do Marco 5.4 (D18).
- **Fiscal → Notas de saída** (`/app/fiscal/saida`, abas "A emitir" (padrão para `fiscal.create`) e "Emitidas"), **Nota** (`/app/fiscal/saida/$id`, abas Dados, Itens e tributos, Documentos, Eventos (FIS-03b), Histórico de alterações); "Emitir NF-e" no romaneio; "Faturar" no projeto.
- **Verbos:** "Salvar rascunho" / "Rascunho salvo"; "Pré-visualizar"; "Emitir NF-e" / "Nota enviada à SEFAZ"; "Corrigir e emitir de novo" / "Rascunho reaberto"; "Reenviar XML e DANFE" / "XML e DANFE reenviados"; "Descartar rascunho" / "Rascunho descartado".
- **Estados:** carregando; "A emitir" vazia ("Nenhum romaneio esperando nota." + "Ver romaneios"); "Emitidas" vazia ("Nenhuma nota emitida. Emita a nota a partir de um romaneio expedido." + "Ver a fila A emitir"); processando com o estado de demora; rejeitada com o motivo e a ação sugerida em destaque; sem configuração ou campos críticos não conferidos em produção ("Configure o fiscal antes de emitir." com o que falta e o link para o primeiro campo); `EnvironmentBanner` de homologação.
- **Propositivo:** tudo preenchido do romaneio, do projeto e da regra; a fila "A emitir" e a pauta trazem o próximo romaneio; o `NextSteps` da nota autorizada oferece "Ver projeto" e, com instalação, "Emitir NFS-e da instalação" quando o aceite técnico existir (FIS-06); rejeição leva direto ao campo da causa quando o código permite.
- **400 px:** colunas sempre visíveis: número, cliente, valor e situação; itens e tributos em cartões; diálogo de emissão em tela cheia; E2E de 400 px e só pelo teclado no diálogo de emissão.
- **Ajuda:** `Ajuda/fiscal-saida.md` (abas `a-emitir` padrão, `emitidas`), `Ajuda/fiscal-nota-saida.md` (abas `dados` padrão, `itens`, `documentos`, `eventos`, `historico`) (previstas); `<Dica>` em "Chave de acesso", "Contingência", "Rejeitada", "Denegada", "Regra aplicada", "Ambiente", "Parcelas do contrato" e "Venda mista".

## Plano de testes
- **Unidade:** montagem do rascunho a partir do romaneio, só itens que não são `Service` (CA 1, 13); totais e reaplicação do cálculo, sem campo de imposto (CA 3); saneamento pelos limites do esquema e caracteres de controle (CA 4); pré-condições, inclusive campos não conferidos em produção (CA 6).
- **Integração (Testcontainers + `FakeFiscalEmitter`):** rascunho idempotente e "Faturar" sem romaneio (CA 1, 2); 422 do saneamento sem chamar o Emissor (CA 4); emissão com 201, 202 e webhook, `payload_hash` (CA 6, 7, 10); rejeição, correção e nova tentativa com o número a inutilizar (CA 8); denegada (CA 9); `InvoiceAuthorized` → razão com `InvoiceTax` idempotente (CA 11); nenhum movimento de estoque e estorno da expedição recusado com nota ativa (CA 12); `IOutgoingInvoices` por período e por projeto (CA 13); e-mail com XML e DANFE depois do `Clean` (CA 14); vínculos (CA 15); fila "A emitir" e pauta (CA 16); desempenho da lista (CA 17); três camadas, IDOR e anexo por registro (CA 18); concorrência e idempotência (CA 19); imutabilidade e arquitetura (CA 20); auditoria mascarada (CA 21).
- **Homologação SEFAZ (manual, CA 22):** 3 cenários autorizados no tenant `demo`, XML conferido pela contadora.
- **E2E (Playwright):** romaneio expedido → fila "A emitir" → emitir NF-e (fake autoriza) → ver o imposto realizado no projeto; rejeição e correção; tela a 400 px.

## Fora do escopo
- Cancelamento, carta de correção e inutilização: FIS-03b. NFS-e: FIS-06. Nota avulsa sem projeto, nota complementar e de ajuste, remessa para obra: fora do MVP (D4). DIFAL: bloqueado (FIS-02b).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.2, pontos 45–51):
1. O romaneio baixa; a nota só referencia o romaneio e os dois caminhos de baixa saem (D4 da Fase 3, §4.1; CA 12).
2. Remessa para obra e venda: só a NF-e de venda do equipamento na expedição, sem remessa, com o endereço da obra nas informações complementares (D4; pré-configurado "a confirmar" na FIS-02a; CA 1).
3. Imposto nunca digitado à mão (CA 3).
4. Nota × parcelas pela regra da venda mista: a parcela é só cobrança; a nota é do equipamento, sem duplicatas; a receita é pela data da nota; o sinal é adiantamento até a nota (D4; CA 3, 13).
5. DANFE em PDF pelo Emissor, reserva pelo `IPdfRenderer` a partir do XML (ponto 10; CA 5, 7).
6. Os 3 cenários: venda a contribuinte em SP, a não contribuinte em SP, peça ou outro estado (CA 22).
7. e-CNPJ A1 comprado pela Konebras (pré-condição externa).

Decisões da revisão aplicadas: D3 (lacunas L-a, L-b, L-c e L-d antes desta story; consulta forçada, CA 10); D4 (venda mista pelo `kind` do item do orçamento; receita pela nota; `IOutgoingInvoices`, CA 13); D8 e ADR 0025 adendo (imposto realizado só pela nota, fonte `InvoiceTax`, CA 11); D12 (só referencia o romaneio, CA 12); novo requisito (produção exige os campos críticos e as regras conferidos, CA 6); §4.3 (saneamento pelos limites do esquema antes de reservar o número e campos livres escapados, CA 4; anexo autorizado por registro, CA 18); §4.4 (padrão 6; fila "A emitir" e pauta, CA 16); D18 (protótipo de nota de saída antes do Marco 5.4).

**Pré-condição externa:** e-CNPJ A1 da Konebras; P-02 (CNAE de fabricação, em providência pela Konebras) para a NF-e de produção própria em **produção**; lacunas L-a, L-b, L-c e L-d do Emissor (dono PO); os 3 cenários conferidos pela contadora; homologação SEFAZ só no tenant `demo`. Não bloqueiam o `Ready`: o código e o CI usam o fake e os valores pré-configurados.

## Definition of Done
- [ ] Critérios de aceite atendidos; CA 22 executado no `demo` e registrado.
- [ ] Testes de unidade, integração (Testcontainers + fake), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`; dono de anexo `OutgoingInvoice`; template de e-mail registrado.
- [ ] Telas com estados, inclusive a 400 px, conforme o protótipo de nota de saída.
- [ ] Ajudas de cada tela e aba; teste de cobertura passando.
- [ ] Efeito fiscal com confirmação; fila "A emitir" e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (estados da NF-e; `InvoiceAuthorized` sem efeito no estoque; fonte `InvoiceTax`) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `OutgoingInvoice` e itens (Domain) com as transições e o saneamento; montagem a partir do romaneio e do projeto.
- [ ] Emissão pelo `IFiscalEmitter`, handler das transições (webhook e job), guarda do XML e da DANFE.
- [ ] Consumidor no Commercial (razão com `InvoiceTax`); `IOutgoingInvoices` (Contracts) e a recusa do estorno da expedição na Produção.
- [ ] Endpoints `/api/v1/fiscal/outgoing-invoices` (+ `/preview`, `/issue`, `/reopen`, `/resend`, `/to-issue`); telas, diálogos, fonte da pauta e ajudas; template `invoice_authorized`.

## Quality gates
- Agentes previstos: @dev, @architect (D4, `IOutgoingInvoices`, razão do projeto), @qa, cybersecurity (idempotência, saneamento, dados do destinatário, imutabilidade), contadora (CA 22).
- Gate: CA 4, 6, 7, 8, 11, 12, 13, 19, 20 e 22 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: FIN-08 e DSH-01a/01b vêm antes, na Fase 4, e passam a ler `IOutgoingInvoices` aqui) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** a baixa do estoque ficava dependente da decisão da PRD-06b, testada nos dois caminhos; estado `Denied` e "nunca reemitir em contingência"; impostos no custo do projeto pelo evento; nota não cria título.
- **Correções da revalidação (21/10/2026):** pontos 45 a 51 decididos; os dois caminhos de baixa saíram: a nota só referencia o romaneio, sem consumidor no Inventory, e o estorno da expedição com nota ativa é recusado (D4 da Fase 3, §4.1; CA 12); venda mista pela D4 (itens `Service` para a NFS-e, parcela só cobrança, sem duplicatas, receita pela data da nota, sinal como adiantamento; CA 3, 13); `IOutgoingInvoices` para a FIN-08 e a DSH-01a/01b (CA 13); imposto realizado na razão com a fonte `InvoiceTax` (ADR 0025 adendo; CA 11); saneamento pelos limites do esquema antes de reservar o número e campos livres escapados (§4.3; CA 4); produção exige campos e regras conferidos (CA 6); toda NF-e de mercadoria nasce de um romaneio (CA 2); fila "A emitir" e pauta (CA 16); consulta forçada no lugar da espera passiva (CA 10); 3 cenários definidos (CA 22); padrão 6 e protótipo da D18; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externas: A1, P-02 para a produção, lacunas do Emissor e conferência dos 3 cenários.
- **Conferência do fechamento (21/10/2026, amostragem do PO):** ConfirmDialog reutilizado da BNK-01b; nota sem baixa de estoque; checklist refeita, sem mudança de resultado.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da FIS-03) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D3, D4, D8, D12, D18, tabela 3.2 pontos 45–51, §4.1, §4.3, §4.4); nota só referencia o romaneio; receita e imposto pela nota; ordem 10 (Marco 5.4); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
