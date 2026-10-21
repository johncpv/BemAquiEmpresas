# FIS-04a — NF-e de entrada por XML: importação segura, de-para dos itens e conferência contra o pedido

| Campo | Valor |
|---|---|
| ID | FIS-04a |
| Título | Upload do XML da NF-e do fornecedor com validação segura (sem DTD e sem entidade externa, esquema, assinatura digital, CNPJ do destinatário = o do tenant), recusa de chave duplicada, fornecedor identificado pelo CNPJ, de-para dos itens pelo código do fornecedor (lembrado para a próxima vez), vínculo ao pedido de compra e conferência das divergências de quantidade e preço |
| Tamanho | M (quebra da FIS-04) |
| Status | Draft |
| Requisitos | R-FIS-5 (importação de XML com de-para de itens do fornecedor) · R-CMP-5 (recebimento por NF-e, conferência contra o pedido) · R-PRP-3 ("XML preenche recebimento") · R-EST-3 (conversão de unidade) · R-COM-3 · R-SEC-5 |
| Dependências | CAD-02b (`item_supplier_codes`, `IUnitConversion`), CAD-01a (`IParties`, criação rápida de fornecedor), CMP-03a (pedido de compra; Fase 3), FND-07 (anexos e varredura), FIS-02a (seed de permissões do `fiscal`) |
| Bloqueia | FIS-04b (recebimento pela nota), FIS-05 (leitura do PDF usa a mesma conferência) |
| Ordem de execução | 12 (Marco 5.5 "Nota de entrada") |

## User story
Como **almoxarife**, quero **subir o XML que o fornecedor mandou e ver na hora os itens já ligados aos nossos e o que difere do pedido**, para **não digitar a nota e não receber material errado ou mais caro sem perceber**.

## Contexto e referências
- PRD R-FIS-5, R-CMP-5; `05-backlog` FIS-04 ("Upload, validação, de-para de itens do fornecedor (lembrado para a próxima vez), vínculo ao pedido, recebimento. CA: XML duplicado (mesma chave) é recusado; divergência de quantidade/preço com pedido é destacada").
- Revisão pré-Fase 0 §3, "NF de entrada (S)": "XML com `DtdProcessing.Prohibit`, validação de XSD e de assinatura, conferência do CNPJ do destinatário".
- `03-modelo-dados` → `fiscal.incoming_invoices` (`access_key`, `party_id`, `number`, `series`, `issue_date`, `total`, `source` (XML/PDF/DFe), `status` (Imported/Checked/Received/Rejected), `xml_path`, `pdf_path`, `purchase_order_id`), `incoming_invoice_items` (`supplier_code`, `description`, `ncm`, `cfop`, `quantity`, `unit`, `unit_price`, `item_id`, `conversion_factor`); `inventory.item_supplier_codes` (CAD-02b: "de-para da NF de entrada").
- FIS-01: o EmissorFiscal **não** consulta a situação de NF-e de terceiros nem tem Distribuição DF-e (lacuna L-f); a autenticidade aqui é pela assinatura e, se o PO quiser, pela consulta pública da chave (Pontos em aberto 3).
- **Fase 3 (rascunho em revisão):** CMP-04 (recebimento manual; "o recebimento por NF-e (Fase 5) traz o preço da nota"; pontos em aberto 1 IPI recuperável, 2 tolerância de quantidade, 3 preço diferente do pedido, 6 quem registra o recebimento).

## Critérios de aceite
**Importar com segurança**
1. **Dado** `fiscal.create` (Pontos em aberto 1), **quando** envio em "Fiscal → Notas de entrada → Importar XML" um ou mais arquivos `.xml` (até 2 MB cada, até 20 por vez), **então** cada arquivo passa pela varredura de anexos e pelo leitor XML com `DtdProcessing.Prohibit`, `XmlResolver = null`, limite de profundidade e de caracteres; DOCTYPE, entidade externa ou XML malformado são recusados com "Este arquivo não é um XML de NF-e válido." (teste com XXE e "billion laughs").
2. **[Segurança]** **Dado** o XML lido, **quando** é validado, **então** confere: o esquema da NF-e (versão 4.00, XSD oficial embutido), a **assinatura digital** do `infNFe` (certificado da cadeia ICP-Brasil, referência ao Id da nota, digest e assinatura batendo), a presença do protocolo de autorização (`protNFe` com `cStat` 100) e que o **CNPJ do destinatário é o do tenant**; qualquer falha recusa com o motivo ("A nota não é destinada a esta empresa.", "A assinatura da nota não confere: o arquivo pode ter sido alterado.").
3. **Dado** uma chave de acesso já importada (não rejeitada), **quando** envio de novo, **então** 409 `incoming_invoice_duplicate` "Esta nota já foi importada em dd/mm por {usuário}." com o link (índice único em `access_key` entre as não rejeitadas).
4. **Dado** o XML válido, **quando** importado, **então** nasce a nota `Imported` com emitente, número, série, chave, data de emissão, totais (produtos, frete, seguro, desconto, IPI, ICMS, ICMS-ST, total), duplicatas (número, vencimento e valor, se houver) e os itens com código do fornecedor, descrição, NCM, CFOP, unidade, quantidade, valor unitário, impostos do item; o XML fica guardado como anexo de sistema (dono `IncomingInvoice`, 5 anos, sem expurgo).

**Fornecedor e de-para**
5. **Dado** o CNPJ do emitente, **quando** a nota é importada, **então** o fornecedor é encontrado no Parties; se não existe, a tela oferece "Cadastrar fornecedor com os dados da nota" (criação rápida da CAD-01a com razão social, IE e endereço do XML, R-PRP-3); se existe sem o papel Fornecedor, "Adicionar papel".
6. **Dado** cada item da nota, **quando** existe `item_supplier_codes` para (fornecedor, código), **então** o item do estoque e o fator de conversão vêm preenchidos e marcados "Lembrado"; sem de-para, sugere pelo NCM e pela descrição (busca `pg_trgm`) e o usuário escolhe, cria o item (CAD-02a) ou marca "Sem item de estoque" (despesa ou serviço, Pontos em aberto 4); ao conferir, o de-para novo é gravado em `item_supplier_codes` com a unidade do fornecedor e o fator, para a próxima nota.
7. **Dado** a unidade do fornecedor diferente da unidade de estoque, **quando** o fator existe (CAD-02b), **então** a quantidade em unidade de estoque é calculada (4 casas); sem fator, o campo é obrigatório antes de conferir.

**Pedido e divergências**
8. **Dado** pedidos de compra `Approved`, `Sent` ou `PartiallyReceived` do mesmo fornecedor (`IPurchaseOrders`), **quando** a nota é importada, **então** o sistema sugere o pedido pelo número informado no XML (`xPed` do item ou informação complementar) ou pelos itens em comum, e o usuário confirma ou escolhe outro (ou "Sem pedido", que exige `purchasing.approve` para receber, Pontos em aberto 5); cada item da nota é ligado a uma linha do pedido.
9. **Dado** o vínculo, **quando** confiro, **então** cada item mostra lado a lado pedido × nota: quantidade (saldo a receber × faturado, em unidade de estoque), preço unitário (pedido × nota, em unidade de estoque), e a diferença em quantidade, valor e %; quantidade acima do saldo e preço acima do pedido ficam em vermelho, abaixo em âmbar; o total de frete e IPI da nota × o do pedido também (CA do backlog).
10. **Dado** todas as linhas ligadas a item (ou marcadas sem item), **quando** clico "Conferir nota", **então** a nota fica `Checked` com quem conferiu e as divergências registradas; a partir daí o recebimento (FIS-04b) pode ser feito; "Recusar nota" (motivo) deixa `Rejected` e libera a chave para nova importação.

**Permissões, privacidade e auditoria**
11. **Dado** o módulo `fiscal` não liberado, **quando** chamo, **então** 403 `module_not_released`; importar e conferir sem `fiscal.create`/`fiscal.edit`: 403; ver com `fiscal.view`; o pedido só é lido com `purchasing.view` (sem ela, o vínculo é sugerido mas os valores do pedido não aparecem); `RequireModule` e `perm:`; IDOR em `/incoming-invoices/{id}/items/{itemId}`; linha de pedido de outro pedido: 422.
12. **[Segurança]** **Dado** o XML, **quando** é lido, **então** o log traz só a chave mascarada (últimos 6), o fornecedor (id) e o resultado; os dados de pessoas físicas que aparecem na nota (transportador PF, por exemplo) não vão para log nem auditoria.
13. **Dado** qualquer importação, de-para, vínculo, conferência ou recusa, **quando** abro o histórico, **então** vejo quem, quando e o quê.

## Regras de negócio e técnicas
- **`incoming_invoices`** (03 + proposta): `status` (`Imported`/`Checked`/`Received`/`Rejected`), `xml_attachment_id`, `pdf_attachment_id`, `totals` (jsonb), `duplicates` (jsonb: número, vencimento, valor), `signature_valid`, `protocol`, `checked_by`, `checked_at`, `rejected_reason`, `goods_receipt_id` (FIS-04b), `xmin`; único parcial `(access_key) WHERE status <> 'Rejected'`.
- **`incoming_invoice_items`** (03 + proposta): `po_line_id`, `stock_quantity`, `stock_unit_price` (6 casas), `taxes` (jsonb), `no_stock_item`, `mapping_source` (`Remembered`/`Suggested`/`Manual`).
- **Validação de assinatura** com `SignedXml` sobre o documento carregado com segurança; cadeia ICP-Brasil conferida sem acesso à rede (raízes embutidas, proposta).
- O Fiscal não grava no Inventory nem no Purchasing: o de-para é gravado pelo contrato do dono (`IItemSupplierCodes.RememberAsync`), o pedido é lido por `IPurchaseOrders`.

## Permissões, eventos e tabelas
- **Permissões:** `fiscal.view`, `fiscal.create` (importar), `fiscal.edit` (de-para, vínculo, conferir, recusar); `purchasing.view` (ver o pedido). Nenhuma ação nova (ponto 1).
- **Eventos:** nenhum entre módulos nesta story (o recebimento é a FIS-04b).
- **Contratos:** `IItemSupplierCodes.RememberAsync` (Inventory, novo), `IPurchaseOrders` (Purchasing, Fase 3), `IParties`.
- **Tabelas:** `fiscal.incoming_invoices`, `fiscal.incoming_invoice_items` (03 com as colunas acima); dono de anexo `IncomingInvoice` (xml e pdf, até 2 MB; leitura `fiscal.view`, escrita `fiscal.create`).

## Telas e estados
- **Fiscal → Notas de entrada** (`/app/fiscal/entrada`, entrada no `modules.ts`), **Importar XML** (arrastar e soltar, vários arquivos com o resultado de cada), **Nota de entrada** (`/app/fiscal/entrada/$id`, abas Itens e de-para, Pedido e divergências, Documentos, Histórico de alterações).
- **Verbos:** "Importar XML" / "{n} notas importadas"; "Conferir nota" / "Nota conferida"; "Recusar nota" / "Nota recusada".
- **Estados:** carregando; vazio ("Nenhuma nota de entrada. Importe o XML que o fornecedor enviou." + "Importar XML"); erro por arquivo, sem bloquear os outros; divergências em destaque; modo leitura da nota `Received`.
- **Propositivo:** de-para lembrado, pedido sugerido, fornecedor criado a partir da nota; depois de conferir, o `NextSteps` oferece "Receber pela nota" (FIS-04b).
- **400 px:** itens em cartões (código do fornecedor, nosso item, quantidade, preço, divergência); comparativo em duas linhas por item.
- **Ajuda:** `Ajuda/fiscal-entrada.md`, `Ajuda/fiscal-entrada-importar.md`, `Ajuda/fiscal-nota-entrada.md` (abas `itens` padrão, `pedido`, `documentos`, `historico`); `<Dica>` em "De-para", "Lembrado", "Fator de conversão" e "Assinatura da nota".

## Plano de testes
- **Unidade:** leitor seguro (XXE, billion laughs, DOCTYPE) (CA 1); validação de assinatura com XML de teste assinado no próprio teste e alterado em 1 byte (CA 2); cálculo em unidade de estoque e divergências (CA 7, 9).
- **Integração (Testcontainers):** importação com recusa por destinatário, assinatura e protocolo (CA 2); chave duplicada e reimportação depois de recusada (CA 3, 10); fornecedor novo e papel (CA 5); de-para lembrado e gravado (CA 6); pedido sugerido e sem pedido (CA 8); divergências (CA 9); três camadas, `purchasing.view` e IDOR (CA 11); log sem dado pessoal (CA 12); auditoria (CA 13).
- **E2E (Playwright):** importar dois XMLs de exemplo (fictícios), ligar um item novo e conferir contra o pedido; tela a 400 px.

## Fora do escopo
- Entrada no estoque e confirmação do título a pagar: FIS-04b. Leitura do PDF: FIS-05. Distribuição DF-e e manifestação do destinatário: lacuna L-f do EmissorFiscal, fora do MVP.
- NF-e de serviço (NFS-e tomada) e CT-e: fora do MVP.

## Pontos em aberto
1. **Permissão para importar** (PO/segurança): `fiscal.create` permite também emitir NF-e de saída; o Almoxarifado precisa importar. Proposta: ação específica `fiscal.import_incoming` (ADR 0003, segregação real) para o Almoxarifado; alternativa: Almoxarifado com `fiscal.create`.
2. **XML sem protocolo** (contadora): recusar (proposta) ou aceitar com aviso.
3. **Consulta da chave na SEFAZ** (PO/arquiteto): conferir a situação da nota (cancelada depois?) exige serviço que o EmissorFiscal não tem (lacuna L-f). Proposta: no MVP, só assinatura e protocolo, com o aviso "Situação atual na SEFAZ não conferida".
4. **Item sem estoque na nota** (PO): despesa ou serviço na mesma nota vai para o título a pagar com a conta do plano, sem entrada no estoque (proposta).
5. **Nota sem pedido** (PO, CMP-04): receber sem pedido exige `purchasing.approve` (proposta).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`; dono de anexo registrado.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajudas escritas; teste de cobertura passando.
- [ ] Padrões preenchidos e próximo passo aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `SafeXmlReader`, `NfeXmlParser`, `NfeSignatureValidator` (XSD e raízes embutidos).
- [ ] `IncomingInvoice` e itens; sugestão de de-para e de pedido; `IItemSupplierCodes.RememberAsync`.
- [ ] Endpoints `/api/v1/fiscal/incoming-invoices` (+ `/import`, `/{id}/items/{itemId}/mapping`, `/check`, `/reject`); telas e ajudas.

## Quality gates
- Agentes previstos: @dev, cybersecurity (XXE, assinatura, upload), @qa.
- Gate: CA 1, 2, 3, 6 e 9 obrigatórios.

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

- **Correções feitas na validação:** os cuidados da revisão pré-Fase 0 (DTD proibido, XSD, assinatura, destinatário) viraram CA de segurança (CA 1, 2); a chave rejeitada pode ser reimportada (CA 3, 10); o de-para é gravado pelo contrato do Inventory (regras).
- **Pré-condições para `Ready`:** CMP-03a e CMP-04 (Fase 3) prontas; pontos em aberto 1 e 5.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da FIS-04) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
