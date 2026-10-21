# FIS-04b — Recebimento pela NF-e de entrada: estoque pelo custo da nota e título a pagar pelas duplicatas

| Campo | Valor |
|---|---|
| ID | FIS-04b |
| Título | "Receber pela nota" a partir da NF-e de entrada conferida: o recebimento da CMP-04 nasce preenchido com quantidades, lote e custo de entrada pelos valores da nota (com rateio de frete e IPI e o tratamento de crédito de impostos conforme a P-01), e o Finance confirma o título a pagar com o valor e os vencimentos das duplicatas da nota; nota e recebimento ligados nos dois sentidos |
| Tamanho | M (quebra da FIS-04) |
| Status | Draft |
| Requisitos | R-CMP-5 (recebimento por NF-e, entrada no estoque com lote, efetivação do CP) · R-EST-7 (custo médio na entrada) · R-FIN-4 · R-FIN-10 · R-PRP-2 · R-PRP-3 · R-PRP-7 · R-SEC-5 |
| Dependências | FIS-04a (nota `Checked`), CMP-04 (recebimento, `GoodsReceived`, consumidor do Finance; Fase 3), EST-01a (`IStockMovements`), FIN-02 (`IPayables`) |
| Bloqueia | GOL-01a |
| Ordem de execução | 13 (Marco 5.5 "Nota de entrada") |

## User story
Como **almoxarife**, quero **dar entrada no material direto pela nota conferida**, e como **financeiro**, quero **o título a pagar com o valor e os vencimentos da nota**, para **o custo médio e o caixa ficarem com o valor real da compra sem digitar nada de novo**.

## Contexto e referências
- PRD R-CMP-5; `04-fluxos-estados` §2 (`GoodsReceived` → Inventory entrada pelo custo médio; Finance confirma o payable (Open) **com o valor da NF**); §3.1 ("Custo de entrada da compra = valor do item na NF + rateio de frete/IPI quando não recuperável (regra definida com a contadora)").
- `06-pendencias-riscos` **P-16 decidida** (frete e IPI entram no custo de entrada, rateio proporcional ao valor dos itens) e **P-01 aberta** (crédito de imposto no custo médio; EST-01a ponto 2 e CMP-04 ponto 1 da Fase 3: IPI tratado como não recuperável até a decisão).
- `03-modelo-dados` → `purchasing.goods_receipts.incoming_invoice_id`.
- **Fase 3 (rascunho em revisão):** CMP-04 (recebimento manual: CA 3 custo de entrada com rateio, CA 6 confirmação do CP por parcela "proposta, ponto em aberto 4", pontos em aberto 2 tolerância, 3 preço diferente, 4 confirmação no parcial, 5 quem estorna); CMP-03a (títulos `Planned` por parcela, pontos 39–41).

## Critérios de aceite
**Receber**
1. **Dado** `purchasing.create` (a permissão de registrar recebimento da CMP-04) e uma nota de entrada `Checked` com pedido, **quando** clico "Receber pela nota", **então** abre o formulário do recebimento da CMP-04 com: data de recebimento (padrão hoje), as linhas do pedido ligadas aos itens da nota, a quantidade **da nota** em unidade de estoque, o preço unitário **da nota**, o frete, o seguro, as outras despesas e o IPI **da nota** rateados por item (P-16), o lote (obrigatório para item com lote; sugerido pelo `nLote`/`rastro` do XML quando existir) e o local padrão; a origem do recebimento é `Invoice`, com a nota ligada.
2. **Dado** o custo de entrada, **quando** é calculado, **então** segue a regra da P-01 vigente por tenant (`fiscal.input_tax_credit`, parâmetro do Fiscal, proposta): enquanto a P-01 estiver aberta, **nenhum crédito** (ICMS, IPI, PIS e COFINS da nota ficam no custo, como na CMP-04); com crédito decidido, o tributo recuperável sai do custo e é guardado na nota como "crédito"; o diálogo mostra a regra usada ("Sem crédito de impostos (aguardando a contadora)").
3. **Dado** o formulário, **quando** confirmo no diálogo grande com o impacto (por item: quantidade, custo de entrada, custo médio antes → depois; títulos a pagar com valores e vencimentos das duplicatas), **então** na mesma transação o recebimento é registrado pelo serviço da CMP-04 (entrada `Receipt`, lote, quantidades recebidas do pedido, `GoodsReceived`), a nota fica `Received` com o recebimento ligado, e a mensagem diz "Recebimento registrado pela nota {número}"; com `Idempotency-Key`; a mesma nota não é recebida duas vezes (409 com link).
4. **Dado** a quantidade da nota acima do saldo do pedido ou preço acima do pedido, **quando** confirmo, **então** vale a regra da CMP-04 decidida para tolerância e preço (pontos em aberto 2 e 3 da CMP-04): proposta, quantidade acima é recusada e preço diferente exige `purchasing.approve` com motivo, registrado no recebimento; a diferença aparece no histórico.
5. **Dado** itens marcados "Sem item de estoque" (FIS-04a ponto 4), **quando** recebo, **então** não entram no estoque e o valor vai para o título a pagar na conta do plano escolhida (Despesa), com o projeto do pedido quando houver.

**Caixa**
6. **Dado** o `GoodsReceived` com `incomingInvoiceId`, **quando** o consumidor do Finance processa, **então** confirma os títulos `Planned` do pedido **pelas duplicatas da nota** (valor e vencimento de cada duplicata, `IPayables.ConfirmAsync`), tira a marca "Estimado", ajusta os `Planned` restantes do pedido pelo valor recebido, e, sem duplicatas na nota, usa a condição do pedido a partir da data de emissão da nota (proposta, Pontos em aberto 1); idempotente pela inbox e por `(goodsReceiptId, duplicata)`; o título leva o número da nota, a chave e o fornecedor, e a nota de entrada aparece como documento do título.
7. **Dado** a soma das duplicatas diferente do total da nota, **quando** processa, **então** o título é criado pelo total da nota com as duplicatas proporcionais e o aviso "As duplicatas não somam o total da nota" fica no título para o financeiro conferir (não bloqueia).
8. **Dado** o recebimento registrado, **quando** o `NextSteps` aparece, **então** oferece "Conferir título a pagar" (com `finance.view`) e "Ver movimentos de estoque".

**Estorno, permissões e auditoria**
9. **Dado** o estorno do recebimento (CMP-04, permissão decidida lá), **quando** é feito, **então** a nota volta a `Checked` (pode ser recebida de novo) e os títulos voltam como na CMP-04; título já em OP: 409 `payable_in_payment_order` antes de estornar.
10. **Dado** os módulos `fiscal`, `purchasing` ou `inventory` não liberados, **quando** chamo, **então** 403 `module_not_released`; receber sem `purchasing.create`: 403; `RequireModule` e `perm:`; IDOR em `/incoming-invoices/{id}/receipt`.
11. **[Segurança]** **Dado** dois "Receber pela nota" simultâneos, **quando** chegam, **então** só um passa (transição `Checked → Received` da nota com `xmin` e a idempotência da CMP-04), e o estoque não entra duas vezes.
12. **Dado** o recebimento pela nota, **quando** abro o histórico da nota, do recebimento e do título, **então** cada um aponta para os outros (link) e mostra a regra de crédito usada.

## Regras de negócio e técnicas
- **Um caminho só:** o recebimento pela nota é o recebimento da CMP-04 com os valores da nota; nada de lançamento paralelo de estoque ou de título.
- **Rateio** (P-16): frete, seguro e outras despesas e IPI proporcionais ao valor dos itens, a última linha absorve o centavo; custo unitário em 6 casas.
- **Crédito de impostos:** parâmetro do Fiscal `fiscal.input_tax_credit` (proposta: `none` | lista de tributos recuperáveis), sem seed além de `none`; decisão da contadora (P-01).
- **`goods_receipts`**: `origin` (`Manual`/`Invoice`), `incoming_invoice_id`.

## Permissões, eventos e tabelas
- **Permissões:** `purchasing.create` (receber), `purchasing.approve` (preço diferente, se a CMP-04 decidir), `fiscal.view`.
- **Eventos:** `GoodsReceived` (CMP-04) com `incomingInvoiceId` e as duplicatas (`[{ number, dueDate, amount }]`); `GoodsReceiptReversed`.
- **Contratos:** `IIncomingInvoices` (Fiscal, novo: dados conferidos da nota para o Purchasing).
- **Tabelas:** `purchasing.goods_receipts.origin` (nova coluna); `fiscal.incoming_invoices.goods_receipt_id`; parâmetro `fiscal.input_tax_credit` (proposta).

## Telas e estados
- Botão "Receber pela nota" na nota de entrada; o formulário é o da CMP-04 com a faixa "Valores da NF-e {número} · {fornecedor}".
- **Verbos:** "Receber pela nota" / "Recebimento registrado".
- **Estados:** carregando; sem pedido ("Ligue a nota a um pedido para receber." ou, com `purchasing.approve`, "Receber sem pedido"); erro dentro do formulário; recebida (modo leitura com o link do recebimento).
- **Propositivo:** tudo preenchido pela nota; lote sugerido do XML; próximos passos do CA 8.
- **400 px:** como na CMP-04.
- **Ajuda:** a ajuda da nota de entrada ganha "Receber pela nota" e "Crédito de impostos"; `<Dica>` em "Duplicatas" e "Crédito de impostos".

## Plano de testes
- **Unidade:** rateio de frete, seguro, despesas e IPI com arredondamento (CA 1); custo com e sem crédito (CA 2); duplicatas proporcionais (CA 7).
- **Integração (Testcontainers, obrigatória: movimenta estoque e financeiro):** receber pela nota com custo médio de referência e nota `Received` (CA 1–3); quantidade e preço divergentes (CA 4); item sem estoque (CA 5); títulos pelas duplicatas, idempotente (CA 6, 7); estorno (CA 9); três camadas e IDOR (CA 10); concorrência (CA 11); auditoria e links (CA 12); dataset fixo nota → estoque → fluxo de caixa.
- **E2E (Playwright):** importar XML, conferir, receber pela nota e ver o título com os vencimentos da nota.

## Fora do escopo
- Escrituração fiscal da entrada (livros, SPED): fora do escopo (PRD §3.3). Devolução ao fornecedor: fora do MVP.

## Pontos em aberto
1. **Nota sem duplicatas** (PO/financeiro): proposta: condição do pedido a partir da emissão da nota.
2. **Crédito de impostos na entrada** (contadora, **P-01**): quais tributos são recuperáveis para a Konebras; até a decisão, nenhum.
3. **Dependências da Fase 3 ainda abertas:** CMP-04 pontos 2 (tolerância), 3 (preço diferente), 4 (confirmação no parcial), 5 (quem estorna) e 6 (quem registra); CMP-03a pontos 39–41 (conta e natureza do título, um título por parcela, compra para o projeto contada duas vezes).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Tela com estados.
- [ ] Ajuda atualizada; teste de cobertura passando.
- [ ] Efeito em estoque e caixa com confirmação; próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (`GoodsReceived` com a nota) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `IIncomingInvoices` (Fiscal) e o preenchimento do recebimento a partir da nota no Purchasing.
- [ ] Parâmetro `fiscal.input_tax_credit` e o cálculo do custo com e sem crédito.
- [ ] Consumidor do Finance pelas duplicatas; faixa e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 2, 3, 6 e 11 obrigatórios.

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

- **Correções feitas na validação:** um único caminho de recebimento (o da CMP-04); crédito de impostos atrás de parâmetro sem decidir a P-01 (CA 2); duplicatas que não fecham não bloqueiam (CA 7).
- **Pré-condições para `Ready`:** CMP-04 e CMP-03a com os pontos do item 3 decididos.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da FIS-04) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
