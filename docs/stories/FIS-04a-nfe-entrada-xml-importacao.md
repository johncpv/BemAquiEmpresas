# FIS-04a — NF-e de entrada por XML: importação segura, de-para dos itens e conferência contra o pedido

| Campo | Valor |
|---|---|
| ID | FIS-04a |
| Título | Upload do XML da NF-e do fornecedor com `fiscal.import_incoming` e validação segura (sem DTD e sem entidade externa, esquema, protocolo de autorização obrigatório, exatamente um `infNFe` e uma assinatura, `Reference URI` igual ao `Id`, CNPJ do certificado igual ao do emitente, CNPJ do destinatário igual ao do tenant), aviso "Situação na SEFAZ não conferida", recusa de chave duplicada, fornecedor pelo CNPJ, de-para dos itens pelo código do fornecedor (lembrado para a próxima vez), vínculo ao pedido de compra (nota sem pedido com `purchasing.approve`) e conferência das divergências de quantidade e preço |
| Tamanho | M (quebra da FIS-04) |
| Status | Done |
| Requisitos | R-FIS-5 (importação de XML com de-para de itens do fornecedor) · R-CMP-5 (recebimento por NF-e, conferência contra o pedido) · R-PRP-3 ("XML preenche recebimento") · R-EST-3 (conversão de unidade) · R-COM-3 · R-SEC-5 |
| Dependências | FIS-02a (`fiscal.import_incoming` no catálogo e no seed do Almoxarifado), CAD-02b (`item_supplier_codes`, `IUnitConversion`), CAD-01a (`IParties`, criação rápida de fornecedor), CMP-03a (pedido de compra, `IPurchaseOrders`), FND-07 (anexos e varredura) |
| Bloqueia | FIS-04b (recebimento pela nota), FIS-05 (a leitura do PDF usa a mesma conferência) |
| Ordem de execução | 12 (Marco 5.5 "Nota de entrada") |

## User story
Como **almoxarife**, quero **subir o XML que o fornecedor mandou e ver na hora os itens já ligados aos nossos e o que difere do pedido**, para **não digitar a nota e não receber material errado ou mais caro sem perceber**.

## Contexto e referências
- PRD R-FIS-5, R-CMP-5; `05-backlog` FIS-04 ("Upload, validação, de-para de itens do fornecedor (lembrado para a próxima vez), vínculo ao pedido, recebimento. CA: XML duplicado (mesma chave) é recusado; divergência de quantidade/preço com pedido é destacada").
- **Revisão pré-Fases 4 a 8** ([2026-10-21-revisao-pre-fases4-8.md](../revisoes/2026-10-21-revisao-pre-fases4-8.md)): **D12** (`fiscal.import_incoming`; recusar XML sem protocolo; exatamente um `infNFe` e uma assinatura, `Reference URI` igual ao `Id`, CNPJ do certificado igual ao do emitente); tabela 3.2, pontos 56 a 60; §4.3 transversal (anexos autorizados por registro, storage com prefixo de tenant, rate limit).
- **ADR 0003, adendo de 21/10/2026** (`fiscal.import_incoming`). **ADR 0024** (recebimento com `purchasing.receive`; preço do pedido muda só com `purchasing.approve`). **ADR 0027** ([0027-integracao-emissorfiscal.md](../adr/0027-integracao-emissorfiscal.md): sem Distribuição DF-e nem consulta de NF-e de terceiros).
- Revisão pré-Fase 0 §3, "NF de entrada (S)": "XML com `DtdProcessing.Prohibit`, validação de XSD e de assinatura, conferência do CNPJ do destinatário".
- `03-modelo-dados` → `fiscal.incoming_invoices`, `incoming_invoice_items`; `inventory.item_supplier_codes` (CAD-02b: "de-para da NF de entrada").
- CMP-04 (Fase 3, entregue antes): recebimento manual com `purchasing.receive`, preço do pedido, teto e títulos do recebimento.

## Critérios de aceite
**Importar com segurança**
1. **Dado** `fiscal.import_incoming`, **quando** envio em "Fiscal → Notas de entrada → Importar XML" um ou mais arquivos `.xml` (até 2 MB cada, até 20 por vez), **então** cada arquivo passa pela varredura de anexos e pelo leitor XML com `DtdProcessing.Prohibit`, `XmlResolver = null`, limite de profundidade e de caracteres; DOCTYPE, entidade externa ou XML malformado são recusados com "Este arquivo não é um XML de NF-e válido." (teste com XXE e "billion laughs"); política de rate limit nomeada `fiscal-incoming-import` (60 arquivos por hora por usuário).
2. **[Segurança]** **Dado** o XML lido, **quando** é validado, **então** confere, recusando com o motivo em qualquer falha: o esquema da NF-e 4.00 (XSD oficial embutido); **o protocolo de autorização** (`protNFe` com `cStat` 100 e a mesma chave; sem ele: "Este XML não tem o protocolo de autorização da SEFAZ. Peça ao fornecedor o XML autorizado."); **exatamente um** `infNFe` e **exatamente uma** `Signature`, cuja `Reference URI` é `#` + o `Id` do `infNFe` ("A assinatura não corresponde a esta nota."); só as transformações permitidas (enveloped e C14N); digest e assinatura batendo; certificado da cadeia ICP-Brasil conferida sem acesso à rede (raízes embutidas); **CNPJ do certificado da assinatura igual ao CNPJ do emitente** (`emit/CNPJ`); e **CNPJ do destinatário igual ao do tenant** ("A nota não é destinada a esta empresa.").
3. **Dado** o XML válido, **quando** importado, **então** a nota mostra o aviso informativo **"Situação na SEFAZ não conferida"** com a `<Dica>` "O sistema confere a assinatura e o protocolo, mas não consulta se a nota foi cancelada depois. Em caso de dúvida, consulte a chave no portal da SEFAZ." (o Emissor não consulta NF-e de terceiros).
4. **Dado** uma chave de acesso já importada (não recusada), **quando** envio de novo, **então** 409 `incoming_invoice_duplicate` "Esta nota já foi importada em dd/mm por {usuário}." com o link (índice único parcial em `access_key` entre as não recusadas).
5. **Dado** o XML válido, **quando** importado, **então** nasce a nota `Imported` com emitente, número, série, chave, data de emissão, totais (produtos, frete, seguro, desconto, outras despesas, IPI, ICMS, ICMS-ST, total), duplicatas (número, vencimento e valor, se houver) e os itens com código do fornecedor, descrição, NCM, CFOP, unidade, quantidade, valor unitário, impostos do item e lote (`rastro`, quando houver); o XML fica guardado como anexo de sistema (dono `IncomingInvoice`, prefixo do tenant no storage, 5 anos, sem expurgo).

**Fornecedor e de-para**
6. **Dado** o CNPJ do emitente, **quando** a nota é importada, **então** o fornecedor é encontrado no Parties; se não existe, a tela oferece "Cadastrar fornecedor com os dados da nota" (criação rápida da CAD-01a com razão social, IE e endereço do XML, R-PRP-3; exige `parties.create`); se existe sem o papel Fornecedor, "Adicionar papel".
7. **Dado** cada item da nota, **quando** existe `item_supplier_codes` para (fornecedor, código), **então** o item do estoque e o fator de conversão vêm preenchidos com a `Tag` "Lembrado"; sem de-para, sugere pelo NCM e pela descrição (busca `pg_trgm`, `Tag` "Sugerido") e o usuário escolhe, cria o item (CAD-02a, com `inventory.create`) ou marca **"Sem item de estoque"** (despesa ou serviço na mesma nota, com a conta do plano escolhida; ponto 59); ao conferir, o de-para novo é gravado pelo `IItemSupplierCodes.RememberAsync` com a unidade do fornecedor e o fator, para a próxima nota.
8. **Dado** a unidade do fornecedor diferente da unidade de estoque, **quando** o fator existe (CAD-02b), **então** a quantidade em unidade de estoque é calculada (4 casas); sem fator, o campo é obrigatório antes de conferir.

**Pedido e divergências**
9. **Dado** pedidos de compra `Approved`, `Sent` ou `PartiallyReceived` do mesmo fornecedor (`IPurchaseOrders`), **quando** a nota é importada, **então** o sistema sugere o pedido pelo número informado no XML (`xPed` do item ou informação complementar) ou pelos itens em comum, e o usuário confirma ou escolhe outro; cada item da nota é ligado a uma linha do pedido (linha de outro pedido: 422).
10. **Dado** o vínculo, **quando** confiro, **então** cada item mostra lado a lado pedido × nota: quantidade (saldo a receber × faturado, em unidade de estoque), preço unitário (pedido × nota, em unidade de estoque) e a diferença em quantidade, valor e %; quantidade acima do saldo e preço acima do pedido ficam em vermelho (crítico), abaixo em âmbar (alerta), com o texto da diferença ao lado (nunca só a cor); o total de frete e IPI da nota × o do pedido também (CA do backlog); preço diferente mostra que o recebimento usará o preço do pedido até alguém com `purchasing.approve` corrigir (ADR 0024, FIS-04b).
11. **[Segurança]** **Dado** uma nota sem pedido, **quando** escolho "Sem pedido", **então** "Conferir nota" exige `purchasing.approve` e motivo (ponto 60; quem aprova compras autoriza a compra que não teve pedido), registrado na nota; sem a ação, o botão fica desabilitado com o motivo "Nota sem pedido: peça a conferência a quem aprova compras." e a API responde 403.
12. **Dado** todas as linhas ligadas a item (ou marcadas sem item), **quando** clico "Conferir nota", **então** a nota fica `Checked` com quem conferiu e as divergências registradas; a partir daí o recebimento (FIS-04b) pode ser feito; "Recusar nota" (motivo) deixa `Rejected` e libera a chave para nova importação.

**Permissões, privacidade e auditoria**
13. **Dado** o módulo `fiscal` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; importar, ligar itens, conferir e recusar sem `fiscal.import_incoming`: 403 (inclusive com `fiscal.create`, que emite nota de saída mas não importa, D12); ver com `fiscal.view`; o pedido só é lido com `purchasing.view` (sem ela, o vínculo é sugerido mas os valores do pedido não aparecem); `RequireModule` e `perm:`; IDOR em `/incoming-invoices/{id}/items/{itemId}`; XML e PDF só por endpoint autorizado que confere `fiscal.view` e a nota do tenant (anexo autorizado por registro).
14. **[Segurança]** **Dado** o XML, **quando** é lido, **então** o log traz só a chave mascarada (últimos 6), o fornecedor (id) e o resultado; os dados de pessoas físicas que aparecem na nota (transportador PF, por exemplo) não vão para log nem auditoria.
15. **Dado** qualquer importação, de-para, vínculo, conferência ou recusa, **quando** abro o histórico, **então** vejo quem, quando e o quê.

## Regras de negócio e técnicas
- **`incoming_invoices`** (03 + colunas): `status` (`Imported`/`Checked`/`Received`/`Rejected`), `source` (`Xml`/`Pdf`), `xml_attachment_id`, `pdf_attachment_id`, `totals` (jsonb), `duplicates` (jsonb: número, vencimento, valor), `signature_valid`, `signer_document`, `protocol`, `sefaz_status_checked` (sempre falso no MVP), `without_order`, `without_order_reason`, `without_order_approved_by`, `checked_by`, `checked_at`, `rejected_reason`, `goods_receipt_id` (FIS-04b), `xmin`; único parcial `(access_key) WHERE status <> 'Rejected'`.
- **`incoming_invoice_items`** (03 + colunas): `po_line_id`, `stock_quantity`, `stock_unit_price` (6 casas), `taxes` (jsonb), `lot_number`, `no_stock_item`, `expense_account_id`, `mapping_source` (`Remembered`/`Suggested`/`Manual`).
- **Validação de assinatura** com `SignedXml` sobre o documento carregado com segurança, com a referência resolvida só pelo `Id` do único `infNFe` (contra assinatura deslocada); cadeia ICP-Brasil conferida sem rede (raízes embutidas, atualizadas pela story que atualizar o XSD).
- O Fiscal não grava no Inventory nem no Purchasing: o de-para é gravado pelo contrato do dono (`IItemSupplierCodes.RememberAsync`), o pedido é lido por `IPurchaseOrders`.

## Permissões, eventos e tabelas
- **Permissões:** `fiscal.view` (ver), `fiscal.import_incoming` (importar, de-para, vínculo, conferir, recusar; Almoxarifado no seed da FIS-02a), `purchasing.view` (ver o pedido), `purchasing.approve` (conferir nota sem pedido).
- **Eventos:** nenhum entre módulos nesta story (o recebimento é a FIS-04b).
- **Contratos:** `IItemSupplierCodes.RememberAsync` (Inventory, novo), `IPurchaseOrders` (Purchasing), `IParties`.
- **Tabelas:** `fiscal.incoming_invoices`, `fiscal.incoming_invoice_items` (03 com as colunas acima); dono de anexo `IncomingInvoice` (xml e pdf, até 2 MB; leitura `fiscal.view`, escrita `fiscal.import_incoming`).

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** botões de `EmptyState`/`ErrorState` com 44 px a 400 px; divergência com texto e cor semântica (nunca só cor).
- **Fiscal → Notas de entrada** (`/app/fiscal/entrada`, entrada no `modules.ts`), **Importar XML** (arrastar e soltar, vários arquivos com o resultado de cada), **Nota de entrada** (`/app/fiscal/entrada/$id`, abas Itens e de-para, Pedido e divergências, Documentos, Histórico de alterações).
- **Verbos:** "Importar XML" / "{n} notas importadas"; "Conferir nota" / "Nota conferida"; "Recusar nota" / "Nota recusada".
- **Estados:** carregando; vazio ("Nenhuma nota de entrada. Importe o XML que o fornecedor enviou." + "Importar XML"); erro por arquivo com o motivo, sem bloquear os outros; aviso "Situação na SEFAZ não conferida"; divergências em destaque; nota sem pedido com o motivo do bloqueio; modo leitura da nota `Received`.
- **Propositivo:** de-para lembrado, pedido sugerido, fornecedor criado a partir da nota; depois de conferir, o `NextSteps` oferece "Receber pela nota" (FIS-04b, com `purchasing.receive`).
- **400 px:** itens em cartões (código do fornecedor, nosso item, quantidade, preço, divergência); comparativo em duas linhas por item; botões de 44 px.
- **Ajuda:** `Ajuda/fiscal-entrada.md`, `Ajuda/fiscal-entrada-importar.md`, `Ajuda/fiscal-nota-entrada.md` (abas `itens` padrão, `pedido`, `documentos`, `historico`) (previstas); `<Dica>` em "De-para", "Lembrado", "Fator de conversão", "Assinatura da nota", "Situação na SEFAZ não conferida" e "Sem item de estoque".

## Plano de testes
- **Unidade:** leitor seguro (XXE, billion laughs, DOCTYPE) (CA 1); validação de assinatura com XML de teste assinado no próprio teste: alterado em 1 byte, sem protocolo, com dois `infNFe`, com duas assinaturas, com `Reference URI` apontando para outro elemento, com certificado de outro CNPJ (CA 2); cálculo em unidade de estoque e divergências (CA 8, 10).
- **Integração (Testcontainers):** importação com recusa por destinatário, assinatura, protocolo e rate limit (CA 1, 2); aviso da SEFAZ (CA 3); chave duplicada e reimportação depois de recusada (CA 4, 12); fornecedor novo e papel (CA 6); de-para lembrado e gravado, item sem estoque (CA 7); pedido sugerido (CA 9); divergências (CA 10); sem pedido com e sem `purchasing.approve` (CA 11); três camadas, `fiscal.create` sem `fiscal.import_incoming` recebendo 403, `purchasing.view`, IDOR e anexo por registro (CA 13); log sem dado pessoal (CA 14); auditoria (CA 15).
- **E2E (Playwright):** importar dois XMLs de exemplo (fictícios, assinados no teste), ligar um item novo e conferir contra o pedido com um usuário do Almoxarifado; tela a 400 px.

## Fora do escopo
- Entrada no estoque e títulos a pagar: FIS-04b. Leitura do PDF: FIS-05. Distribuição DF-e, manifestação do destinatário e consulta da situação na SEFAZ: fora do MVP (o Emissor não tem).
- NF-e de serviço (NFS-e tomada) e CT-e: fora do MVP.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.2, pontos 56–60):
1. `fiscal.import_incoming` para importar e conferir, sem poder emitir (D12; CA 1, 13).
2. XML sem protocolo é recusado (CA 2).
3. Aviso "Situação na SEFAZ não conferida", sem consulta (CA 3).
4. Item sem estoque como proposto: despesa ou serviço com a conta do plano, sem entrada no estoque (CA 7; FIS-04b).
5. Nota sem pedido exige `purchasing.approve` (CA 11).

Decisões da revisão aplicadas: D12 (exatamente um `infNFe` e uma assinatura, `Reference URI` igual ao `Id`, CNPJ do certificado igual ao do emitente; CA 2); §4.3 transversal (anexos autorizados por registro e storage com prefixo de tenant, CA 5, 13; rate limit nomeado, CA 1); ADR 0024 (o preço do recebimento é o do pedido, CA 10).

**Pré-condição externa:** nenhuma.

## Definition of Done
- [x] Critérios de aceite atendidos (CA 6: o cadastro do fornecedor com os dados da nota usa o `POST /parties` da CAD-01a, com `parties.create`; CA 7: "Cadastrar item" abre o cadastro da CAD-02a em outra aba).
- [x] Testes de unidade e integração (Testcontainers) passando. E2E (Playwright): pendente, ver Change Log.
- [x] Migration criada (não destrutiva: só cria `fiscal.incoming_invoices` e `fiscal.incoming_invoice_items`). A conferência sobre cópia do banco do `konebras` fica para o deploy.
- [x] Permissões só do `ModuleCatalog` (`fiscal.import_incoming` da FIS-02a); dono de anexo `IncomingInvoice` registrado.
- [x] Telas com estados, inclusive a 400 px.
- [x] Ajudas escritas; teste de cobertura passando.
- [x] Padrões preenchidos e próximo passo aplicados.
- [x] Auditoria ligada.
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `SafeXmlReader`, `NfeXmlParser`, `NfeSignatureValidator` (XSD e raízes embutidos; um `infNFe`, uma assinatura, referência pelo `Id`, CNPJ do assinante).
- [x] `IncomingInvoice` e itens; sugestão de de-para e de pedido; `IItemSupplierCodes.RememberAsync`.
- [x] Endpoints `/api/v1/fiscal/incoming-invoices` (+ `/import`, `/{id}/items/{itemId}/mapping`, `/check`, `/reject`); telas e ajudas.

## Quality gates
- Agentes previstos: @dev, cybersecurity (XXE, assinatura deslocada, upload), @qa.
- Gate: CA 1, 2, 4, 7, 10, 11 e 13 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: depois da FIS-02a, que cria `fiscal.import_incoming`) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** os cuidados da revisão pré-Fase 0 (DTD proibido, XSD, assinatura, destinatário) viraram CA de segurança; a chave recusada pode ser reimportada; o de-para é gravado pelo contrato do Inventory.
- **Correções da revalidação (21/10/2026):** pontos 56 a 60 decididos; importar e conferir com `fiscal.import_incoming`, e `fiscal.create` sozinha recebe 403 (D12; CA 1, 13); a assinatura passou a exigir protocolo, um `infNFe`, uma `Signature`, `Reference URI` igual ao `Id` e CNPJ do certificado igual ao do emitente (D12; CA 2); aviso "Situação na SEFAZ não conferida" (CA 3); item sem estoque com conta do plano (CA 7); nota sem pedido com `purchasing.approve` (CA 11); preço do pedido no recebimento (ADR 0024; CA 10); anexos por registro e rate limit (§4.3); CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da FIS-04) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D12, tabela 3.2 pontos 56–60, §4.3); antes do go-live, na ordem da fase (12, Marco 5.5); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 31/10/2026 | 1.0 | **Implementação.** Leitor seguro `NfeXmlReader` (DOCTYPE proibido, `XmlResolver = null`, limite de caracteres e de 32 níveis, duas passadas: forma e documento com os espaços preservados); esquema oficial da NF-e 4.00 do pacote **PL_010_V1** (com os grupos da reforma tributária) embutido e resolvido só entre os arquivos embutidos (`NfeSchema`); `NfeSignatureValidator` com `SignedXml` resolvendo a referência só para o único `infNFe` (contra assinatura deslocada), uma `Signature` filha do `NFe`, uma referência com `URI` = `#` + `Id`, só enveloped e C14N, cadeia até as raízes ICP-Brasil embutidas (`icp-brasil.pem`: raízes v5, v6, v7, v10, v11 e v12 e as AC intermediárias do pacote ACcompactado.zip do ITI, baixado em 31/10/2026) sem download nem revogação, na data de emissão, e CNPJ do certificado igual ao `emit/CNPJ`; `NfeXmlValidator` (sem `nfeProc` = sem protocolo; `cStat` 100 e a mesma chave; dígito da chave; modelo 55; produção). `IncomingInvoice` e itens (migration `IncomingInvoicesFis04a`), `IncomingInvoiceService` (importação com a triagem do anexo antes da leitura, destinatário pelo CNPJ do tenant, 409 `incoming_invoice_duplicate` com o link, fornecedor pelo CNPJ, de-para lembrado, fator pela unidade igual, pela conversão do item ou informado, sugestão de item pelo NCM e pelo `pg_trgm`, pedido sugerido pelo `xPed`/informação complementar ou pelos itens em comum, linhas ligadas pelo item e pelo `nItemPed`, divergências, conferência com o `IItemSupplierCodes.RememberAsync` na mesma transação, recusa que libera a chave); contratos novos `IParties.FindByDocumentAsync`, `IItemSupplierCodes.RememberAsync`, `IItemSuggestions` e `IPurchaseOrderLines`; `AttachmentService.StoreScreenedAsync` com prefixo e origem; política `fiscal-incoming-import` (60 arquivos por hora por usuário, um arquivo por requisição); endpoints `/api/v1/fiscal/incoming-invoices` (+ `/import`, `/{id}`, `/{id}/xml`, `/{id}/history`, `/{id}/supplier`, `/{id}/items/{itemId}/mapping`, `/{id}/order`, `/{id}/check`, `/{id}/reject`, `/item-options`, `/account-options`); telas Notas de entrada, Importar XML e Nota de entrada (abas Itens e de-para, Pedido e divergências, Documentos, Histórico); ajudas `fiscal-entrada`, `fiscal-entrada-importar`, `fiscal-nota-entrada` (+ `-pedido`, `-documentos`, `-historico`, uma por aba, no padrão das outras telas com abas). **Recomendações aceitas (PO 24/10), pela opção mais conservadora:** só `cStat` 100 vale como autorizada (o 150, "autorizado fora de prazo", fica recusado até decisão do PO); nota de homologação (`tpAmb` 2) é recusada; nota de emitente CPF é recusada (o CA 2 pede o CNPJ do certificado igual ao `emit/CNPJ`); o CNPJ do certificado tem de ser **igual** ao do emitente, não só a raiz (a SEFAZ aceita a matriz assinando pela filial: decisão do PO se for preciso); a `Signature` opcional da SEFAZ dentro do `protNFe` não conta para "exatamente uma" nem é conferida (o protocolo é conferido pela chave e pelo `cStat`); o esquema embutido é o PL_010_V1 (espelho nfephp `sped-nfe`), a conferir contra o portal da NF-e na próxima atualização do XSD; o arquivo vai numa requisição por vez (a tela manda até 20, o servidor conta os 60 por hora); o XML entra como anexo de **sistema** (fora do limite de 30 envios por hora dos anexos do usuário); o dono `IncomingInvoice` aceita só XML até a FIS-05 trazer o PDF; com pedido, todo item de estoque precisa estar ligado a uma linha dele para conferir (a FIS-04b recebe pela linha); o fornecedor sem o papel Fornecedor bloqueia a conferência. **Pendências:** "Receber pela nota" do próximo passo leva ao recebimento do pedido da CMP-04 até a FIS-04b; o histórico do item fica no histórico da nota (filhos pelo `invoiceId`); E2E (Playwright) fica para a rodada de E2E da fase. | Dex (dev) |
