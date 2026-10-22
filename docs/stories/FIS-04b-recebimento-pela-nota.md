# FIS-04b — Recebimento pela NF-e de entrada: estoque pelo custo da nota e títulos a pagar pelas duplicatas, na mesma transação

| Campo | Valor |
|---|---|
| ID | FIS-04b |
| Título | "Receber pela nota" a partir da NF-e de entrada conferida, com `purchasing.receive`: o recebimento da CMP-04 nasce preenchido com quantidades, lote, frete, seguro, outras despesas e IPI da nota (rateio da P-16, crédito de impostos pelo parâmetro da P-01, pré-configurado "nenhum"); na mesma transação o Purchasing chama o `IPayables.CreateOpenInstallmentsAsync` com as duplicatas da nota (ADR 0024, chamada síncrona) e replaneja o previsto do pedido; nota e recebimento ligados nos dois sentidos |
| Tamanho | M (quebra da FIS-04) |
| Status | Ready (pré-condição externa) |
| Requisitos | R-CMP-5 (recebimento por NF-e, entrada no estoque com lote, efetivação do CP) · R-EST-7 (custo médio na entrada) · R-FIN-4 · R-FIN-10 · R-PRP-2 · R-PRP-3 · R-PRP-7 · R-SEC-5 |
| Dependências | FIS-04a (nota `Checked`), CMP-04 (recebimento com `purchasing.receive`, títulos do recebimento, teto e correção de preço), CMP-03a (`IPayables` por parcela: `CreateOpenInstallmentsAsync`, `ReplacePlannedAsync`, `CancelAllFromOriginAsync`), EST-01a (`IStockMovements`), FIS-02a (Configurações → Fiscal) |
| Bloqueia | FIS-05 (a nota lida do PDF segue por aqui), GOL-01a |
| Ordem de execução | 13 (Marco 5.5 "Nota de entrada") |

## User story
Como **almoxarife**, quero **dar entrada no material direto pela nota conferida**, e como **financeiro**, quero **os títulos a pagar com o valor e os vencimentos das duplicatas da nota no mesmo instante**, para **o custo médio e o caixa ficarem com o valor real da compra sem digitar nada de novo**.

## Contexto e referências
- PRD R-CMP-5; `04-fluxos-estados` §3.1 ("Custo de entrada da compra = valor do item na NF + rateio de frete/IPI quando não recuperável").
- **ADR 0024** ([0024-compras-e-financeiro.md](../adr/0024-compras-e-financeiro.md)) §1 (o Purchasing chama o `IPayables` **na própria transação** do recebimento; `GoodsReceived` só para aviso e projeção), §2 (`CreateOpenInstallmentsAsync` por parcela, idempotente), §3 (títulos do recebimento, replanejamento, teto, preço do pedido) e §5 (`purchasing.receive`, segregação). **ADR 0023** §1 (efeito de estoque síncrono). **ADR 0003, adendo de 21/10/2026** (`purchasing.receive`).
- **Revisão pré-Fase 3:** D8 (segregação em compras) e tabela 1.1, pontos 38 a 53 (CMP-03a e CMP-04 decididos: IPI não recuperável até a P-01, tolerância 0%, preço do pedido, títulos por recebimento, estorno com `purchasing.approve`, registro com `purchasing.receive`).
- **Revisão pré-Fases 4 a 8** ([2026-10-21-revisao-pre-fases4-8.md](../revisoes/2026-10-21-revisao-pre-fases4-8.md)): D12 (duplicatas pelo `CreateOpenInstallmentsAsync` na transação do recebimento, com `purchasing.receive`); tabela 3.2, pontos 61 a 63; §4.1 (FIS-04b pela ADR 0024).
- **Decisão do PO de 21/10/2026 (novo requisito):** parâmetros fiscais em Configurações, pré-configurados "a confirmar" (**ADR 0042**, [0042-configuracoes-fiscais-e-integracoes.md](../adr/0042-configuracoes-fiscais-e-integracoes.md)).
- `06-pendencias-riscos` **P-16 decidida** (frete e IPI no custo de entrada, rateio proporcional) e **P-01** (crédito de imposto; até a contadora, nenhum).
- `03-modelo-dados` → `purchasing.goods_receipts.incoming_invoice_id`.

## Critérios de aceite
**Receber**
1. **Dado** `purchasing.receive` e uma nota de entrada `Checked` ligada a um pedido, **quando** clico "Receber pela nota", **então** abre o formulário do recebimento da CMP-04 com a faixa "Valores da NF-e {número} · {fornecedor}" e: data de recebimento (padrão hoje), as linhas do pedido ligadas aos itens da nota, a quantidade **da nota** em unidade de estoque, o frete, o seguro, as outras despesas e o IPI **da nota** rateados por item (P-16, a última linha absorve o centavo), o lote (obrigatório para item com lote; sugerido pelo `rastro` do XML, com a `Tag` "Sugerido") e o local padrão; a origem do recebimento é `Invoice`, com a nota ligada.
2. **[Segurança]** **Dado** o usuário que pediu o pedido ou decidiu algum nível da aprovação dele, **quando** tenta receber pela nota, **então** vale a segregação da CMP-04 (403 `segregation_of_duties`, exceto o Proprietário único com a marca); `purchasing.create` sem `purchasing.receive` recebe 403.
3. **Dado** o preço unitário da nota diferente do preço do pedido em alguma linha (mais de R$ 0,01 por unidade de estoque), **quando** abro o formulário, **então** a linha mostra pedido × nota e "Receber pela nota" fica desabilitado com o motivo "O preço da nota é diferente do pedido. Peça a correção do preço a quem aprova compras."; o botão "Pedir correção de preço" notifica quem tem `purchasing.approve` com o link para o "Corrigir preço" da CMP-04 (CA 14 de lá), já com o preço da nota sugerido; quem recebe nunca corrige o preço (ADR 0024 §3).
4. **Dado** a quantidade da nota acima do saldo do pedido mais a tolerância, ou frete e IPI da nota acima do proporcional do pedido, ou o total acima do teto, **quando** confirmo, **então** valem as recusas da CMP-04 (422 na linha ou no campo; acima do teto, a reaprovação), e a diferença fica no histórico da nota.
5. **Dado** o custo de entrada, **quando** é calculado, **então** segue o parâmetro **"Crédito de impostos na entrada"**, novo em Configurações → Fiscal (seção "Entrada"), pré-configurado **"Nenhum: ICMS, IPI, PIS e COFINS da nota ficam no custo"** com o chip "A confirmar com a contadora" (P-01; não é campo crítico para emitir); com crédito configurado, o tributo recuperável sai do custo e fica guardado na nota como "crédito"; o diálogo mostra a regra usada ("Sem crédito de impostos (a confirmar com a contadora)").
6. **Dado** o formulário válido, **quando** clico "Receber pela nota" e confirmo no diálogo grande com o impacto (por item: quantidade, custo de entrada, custo médio antes → depois; os títulos a pagar que nascem, um por duplicata, com valor e vencimento; o previsto do pedido antes → depois), **então** na **mesma transação**, pelo serviço da CMP-04: nasce o recebimento com as entradas `Receipt` (lote, local, custo unitário de entrada em 6 casas) pelo `IStockMovements`; `quantity_received` do pedido sobe; o Purchasing chama **`IPayables.CreateOpenInstallmentsAsync`** (origem `GoodsReceipt`, id do recebimento) com **as duplicatas da nota** (uma parcela por duplicata: número, vencimento e valor), fornecedor, projeto, conta e natureza do pedido, e a descrição "NF-e {número} · recebimento {número} — parcela {n}/{m}"; os previstos do pedido são replanejados pelo `ReplacePlannedAsync`; a nota fica `Received` com o recebimento ligado (`IIncomingInvoices.MarkReceivedAsync`, na mesma transação); `GoodsReceived` (com `incomingInvoiceId`) vai para o outbox **só para aviso e projeção**; falha em qualquer parte desfaz tudo; com `Idempotency-Key`; a mesma nota não é recebida duas vezes (409 com link); a mensagem diz "Recebimento registrado pela nota {número}".
7. **Dado** uma nota sem duplicatas, **quando** recebo, **então** os títulos seguem a condição de pagamento do pedido a partir da **data de emissão da nota** (ponto 61), com a `<Dica>` "A nota não trouxe duplicatas: os vencimentos vêm da condição do pedido."
8. **Dado** a soma das duplicatas diferente do valor do recebimento (itens + frete + IPI + seguro + outras despesas), **quando** recebo, **então** os títulos são criados pelo valor do recebimento com as duplicatas proporcionais (a última absorve o centavo) e o aviso "As duplicatas não somam o total da nota" fica nos títulos para o financeiro conferir (não bloqueia).
9. **Dado** itens marcados "Sem item de estoque" (FIS-04a CA 7), **quando** recebo, **então** não entram no estoque, e a parte deles vira títulos próprios na conta do plano escolhida (origem `IncomingInvoice`, id da nota, uma parcela por duplicata, proporcional), pelo mesmo `CreateOpenInstallmentsAsync` e na mesma transação, com o projeto do pedido quando houver.
10. **Dado** o recebimento registrado, **quando** o `NextSteps` aparece, **então** oferece "Conferir títulos a pagar" (com `finance.view`) e "Ver movimentos de estoque".

**Estorno, permissões e auditoria**
11. **Dado** o estorno do recebimento (CMP-04, `purchasing.approve`), **quando** é feito, **então** na mesma transação os títulos das duas origens são cancelados pelo `CancelAllFromOriginAsync` antes do estoque (409 `payable_in_payment_order` ou pago, com "Ver OP"), a nota volta a `Checked` (pode ser recebida de novo) e o saldo volta ao previsto do pedido.
12. **Dado** os módulos `fiscal`, `purchasing` ou `inventory` não liberados, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; receber sem `purchasing.receive`, estornar sem `purchasing.approve`, alterar o parâmetro de crédito sem `settings.edit`: 403; `RequireModule` e `perm:`; IDOR em `/incoming-invoices/{id}/receipt`.
13. **[Segurança]** **Dado** dois "Receber pela nota" simultâneos, **quando** chegam, **então** só um passa (transição `Checked → Received` da nota com `xmin`, a do pedido e a idempotência da CMP-04), o estoque não entra duas vezes e os títulos não duplicam (idempotência por `(origem, id, parcela)`).
14. **Dado** o recebimento pela nota, **quando** abro o histórico da nota, do recebimento e de cada título, **então** cada um aponta para os outros (link) e mostra a regra de crédito usada; a mudança do parâmetro de crédito aparece no histórico de Configurações → Fiscal.

## Regras de negócio e técnicas
- **Um caminho só:** o recebimento pela nota é o recebimento da CMP-04 com os valores da nota; nada de lançamento paralelo de estoque ou de título; **nenhum consumidor** do Finance cria título por evento (ADR 0024 §1).
- **Preço:** o do pedido; diferença só pelo "Corrigir preço" da CMP-04 (`purchasing.approve`), antes de receber.
- **Rateio** (P-16): frete, seguro, outras despesas e IPI proporcionais ao valor dos itens, a última linha absorve o centavo; custo unitário em 6 casas.
- **Títulos:** uma parcela por duplicata; sem duplicatas, a condição do pedido a partir da emissão da nota; títulos `Open` sem a marca "Estimado".
- **Crédito de impostos:** parâmetro `input_tax_credit` em `fiscal.fiscal_settings` (`None` | lista de tributos recuperáveis), pré-configurado `None` e "a confirmar" (P-01).
- **`goods_receipts`**: `origin` (`Manual`/`Invoice`), `incoming_invoice_id`. `DocumentOrigin.IncomingInvoice` (SharedKernel, novo) para a parte sem item de estoque.

## Permissões, eventos e tabelas
- **Permissões:** `purchasing.receive` (receber pela nota), `purchasing.approve` (corrigir preço, estornar; CMP-04), `fiscal.view`, `settings.edit` (parâmetro de crédito).
- **Eventos:** `GoodsReceived` (CMP-04) com `incomingInvoiceId`, só aviso e projeção; `GoodsReceiptReversed`.
- **Contratos:** `IIncomingInvoices` (Fiscal, novo: dados conferidos da nota, `MarkReceivedAsync`, `ReturnToCheckedAsync`), `IPayables.CreateOpenInstallmentsAsync`/`ReplacePlannedAsync`/`CancelAllFromOriginAsync` (CMP-03a), `IStockMovements`.
- **Tabelas:** `purchasing.goods_receipts.origin` (nova coluna); `fiscal.incoming_invoices.goods_receipt_id`, `input_tax_credit_applied`; `fiscal.fiscal_settings.input_tax_credit` (nova coluna, seed `None`); `DocumentOrigin.IncomingInvoice`.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** botões de `EmptyState`/`ErrorState` com 44 px a 400 px; diferença de preço com texto e cor semântica.
- Botão "Receber pela nota" na nota de entrada; o formulário é o da CMP-04 com a faixa "Valores da NF-e {número} · {fornecedor}"; seção **"Entrada"** em Configurações → Fiscal com o parâmetro de crédito e o chip de conferência.
- **Verbos:** "Receber pela nota" / "Recebimento registrado"; "Pedir correção de preço" / "Pedido de correção enviado".
- **Estados:** carregando; sem pedido ("Ligue a nota a um pedido para receber." ou a nota conferida sem pedido pela FIS-04a CA 11, que recebe com a mesma regra); preço diferente com o bloqueio e a ação (CA 3); erro dentro do formulário; recebida (modo leitura com o link do recebimento e dos títulos).
- **Propositivo:** tudo preenchido pela nota; lote sugerido do XML; correção de preço já sugerida ao aprovador; próximos passos do CA 10.
- **400 px:** como na CMP-04; o impacto do diálogo em tela cheia.
- **Ajuda:** a ajuda da nota de entrada (`Ajuda/fiscal-nota-entrada.md`) ganha "Receber pela nota" e "Crédito de impostos"; `Ajuda/configuracoes-fiscal.md` ganha a seção "Entrada" (previstas); `<Dica>` em "Duplicatas" e "Crédito de impostos".

## Plano de testes
- **Unidade:** rateio de frete, seguro, despesas e IPI com arredondamento (CA 1); custo com e sem crédito (CA 5); parcelas pelas duplicatas, sem duplicatas e proporcionais (CA 6 a 8); parte sem item de estoque (CA 9).
- **Integração (Testcontainers, obrigatória: movimenta estoque e financeiro):** receber pela nota com estoque, títulos `Open` pelas duplicatas, replanejamento e nota `Received` **na mesma transação**, e rollback quando o `IPayables` ou o estoque recusam (CA 6); segregação e `purchasing.create` sem `purchasing.receive` (CA 2); preço diferente bloqueado e correção pelo aprovador (CA 3); quantidade, frete e teto (CA 4); parâmetro de crédito com o chip (CA 5); sem duplicatas e duplicatas que não fecham (CA 7, 8); item sem estoque em origem própria (CA 9); estorno com título em OP (CA 11); três camadas e IDOR (CA 12); concorrência (CA 13); auditoria e links (CA 14); teste de que nenhum consumidor cria título pelo `GoodsReceived`; dataset fixo nota → estoque → fluxo de caixa.
- **E2E (Playwright):** importar XML, conferir, receber pela nota com um usuário do Almoxarifado e ver os títulos com os vencimentos da nota.

## Fora do escopo
- Escrituração fiscal da entrada (livros, SPED): fora (PRD §3.3). Devolução ao fornecedor: fora do MVP. Frete de outro fornecedor (CT-e): título manual (CMP-04).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.2, pontos 61–63):
1. Sem duplicatas na nota, vale a condição do pedido, a partir da emissão da nota (CA 7).
2. Crédito na entrada pela P-01: parâmetro pré-configurado "Nenhum", a confirmar com a contadora (CA 5).
3. Fase 3 (pontos 38 a 53) e D12: chamada síncrona do `CreateOpenInstallmentsAsync` na transação do recebimento, `purchasing.receive`, preço do pedido, teto e estorno com `purchasing.approve` (CA 2 a 4, 6, 11).

Decisões da revisão aplicadas: ADR 0024 (§1 chamada síncrona, §2 `CreateOpenInstallmentsAsync`, §3 replanejamento e preço, §5 `purchasing.receive`; o consumidor do Finance pelo `GoodsReceived` saiu, §4.1); novo requisito do PO (parâmetro de crédito em Configurações → Fiscal, "a confirmar", ADR 0042; CA 5).

**Pré-condição externa:** conteúdo da contadora para o crédito de impostos (P-01), pré-configurado "Nenhum"; não bloqueia o `Ready`.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Tela com estados; seção "Entrada" em Configurações → Fiscal.
- [ ] Ajudas atualizadas; teste de cobertura passando.
- [ ] Efeito em estoque e caixa com confirmação; próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (recebimento pela nota síncrono; `GoodsReceived` só aviso) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `IIncomingInvoices` (Fiscal) e o preenchimento do recebimento a partir da nota no Purchasing.
- [ ] Parcelas pelas duplicatas no serviço da CMP-04 (`CreateOpenInstallmentsAsync`), parte sem item de estoque com `DocumentOrigin.IncomingInvoice`.
- [ ] Parâmetro `input_tax_credit` e o cálculo do custo com e sem crédito; seção "Entrada" e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (transação única Purchasing × Inventory × Finance × Fiscal), @qa.
- Gate: CA 2, 3, 5, 6, 9, 11 e 13 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: CMP-03a com o `IPayables` por parcela) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK (ajustado: deixou de contradizer a ADR 0024) |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** um único caminho de recebimento (o da CMP-04); crédito de impostos atrás de parâmetro sem decidir a P-01; duplicatas que não fecham não bloqueiam.
- **Correções da revalidação (21/10/2026):** pontos 61 a 63 decididos; a story contradizia a ADR 0024 e foi corrigida: os títulos nascem pelo `CreateOpenInstallmentsAsync` com as duplicatas, na mesma transação do recebimento, e o consumidor do Finance pelo `GoodsReceived` saiu (CA 6); `purchasing.receive` com a segregação da CMP-04 no lugar de `purchasing.create` (CA 2); preço da nota diferente do pedido só pela correção de quem aprova compras (CA 3); parâmetro de crédito em Configurações → Fiscal, "a confirmar" (CA 5); parte sem item de estoque em títulos próprios (CA 9); estorno pelas duas origens (CA 11); bloqueia a FIS-05; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: crédito de impostos da contadora (pré-configurado).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da FIS-04) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D12, novo requisito de Configurações, tabela 3.2 pontos 61–63, §4.1; ADR 0024); chamada síncrona pelas duplicatas; antes do go-live, na ordem da fase (13, Marco 5.5); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
