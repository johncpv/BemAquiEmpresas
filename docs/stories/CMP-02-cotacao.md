# CMP-02 — Cotação

| Campo | Valor |
|---|---|
| ID | CMP-02 |
| Título | Cotação a partir de linhas de solicitação, com um ou mais fornecedores; registro das respostas (preço unitário, frete, prazo, condição de pagamento); comparativo lado a lado com o menor custo total destacado; escolha do vencedor por linha, que alimenta o pedido de compra |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-CMP-2 (cotação com 1+ fornecedores, comparativo e escolha) · R-CMP-6 · R-COM-4 (numeração COT) · R-PRP-1 (último fornecedor e preço do item) · R-PRP-3 · R-SEC-5 |
| Dependências | CMP-01 (linhas de solicitação), CAD-01a (`IParties`, papel Fornecedor), CAD-02b (`IItemSupplierCodes`, conversão da unidade do fornecedor), CAD-05b (`IPaymentTerms`) |
| Bloqueia | CMP-03a (pedido a partir da cotação) |
| Ordem de execução | 9 (Marco 3.3 "Comprar o que falta") |

## User story
Como **comprador**, quero **pedir preço a mais de um fornecedor, ver as respostas lado a lado e escolher o melhor**, para **comprar pelo menor custo total e deixar registrado por que escolhi**.

## Contexto e referências
- PRD §5.10 R-CMP-2; R-PRP-1 ("último fornecedor e preço do item").
- `03-modelo-dados` → `purchasing.rfqs` (`number`, `request_id`, `status`), `rfq_suppliers / rfq_lines` (`rfq_id`, `party_id`, `item_id`, `quantity`, `unit_price`, `lead_days`, `freight`, `selected`). `04-fluxos` §4: `COT-AAAA-0001` (em `NumberFormats`).
- CAD-02b: `item_supplier_codes` (código e unidade do fornecedor com fator para a unidade de estoque).
- `ModuleCatalog`: `purchasing` (`view`, `create`, `edit`, `approve`, `delete`).

## Critérios de aceite
**Criar**
1. **Dado** `purchasing.create` e linhas de SC `Open` (uma ou várias SC, pela visão "Por item" da CMP-01), **quando** clico "Cotar", escolho os fornecedores (terceiros ativos com papel Fornecedor; padrão: os últimos fornecedores desses itens, até 3; R-PRP-1) e clico "Criar cotação", **então** nasce a cotação `Open` com número `COT-2026-0001`, uma linha por item (quantidades somadas das linhas de SC, com o vínculo a cada linha de origem), as linhas de SC passam a `Quoting`, com `Idempotency-Key`; uma cotação pode ser criada também sem SC (itens digitados), para compra pontual.
2. **Dado** a cotação, **quando** abro "Fornecedores", **então** posso incluir ou remover fornecedor enquanto ela está `Open`; o mesmo fornecedor não se repete (422).

**Registrar respostas**
3. **Dado** `purchasing.edit`, **quando** registro a resposta de um fornecedor por linha — preço unitário (`numeric(18,6)`, na unidade do fornecedor convertida pelo fator do `item_supplier_codes` quando houver, ou na unidade de estoque), quantidade disponível (padrão: a pedida), prazo em dias, e, por fornecedor, frete (`numeric(18,2)`), IPI % (proposta, P-16), condição de pagamento (`IPaymentTerms`) e validade da proposta (`date`) —, **então** grava a resposta; "Não cotou" marca o fornecedor sem resposta.
4. **Dado** a proposta do fornecedor em PDF ou planilha, **quando** anexo (dono `Rfq`, proposta: pdf, png, jpg, xlsx, docx até 10 MB, ClamAV, leitura `purchasing.view`, escrita `purchasing.edit`), **então** fica na aba Documentos da cotação.

**Comparar e escolher**
5. **Dado** duas ou mais respostas, **quando** abro "Comparativo", **então** vejo por linha e fornecedor: preço unitário, total da linha (qtd × preço, 2 casas `AwayFromZero`), e por fornecedor: subtotal, frete, IPI, **custo total** (subtotal + frete + IPI, P-16), prazo e condição; o menor custo total por linha e o menor total geral são destacados (chip ok "Menor custo"), e o último preço pago do item aparece como referência ("Última compra: {preço} em {data} de {fornecedor}", a partir da CMP-04).
6. **Dado** o comparativo, **quando** escolho o vencedor de cada linha (padrão sugerido: o menor custo total da linha, com o frete rateado pelo valor; ou "Escolher tudo de {fornecedor}") e clico "Concluir cotação", **então** a cotação fica `Closed`, cada linha guarda o vencedor (`selected`), e quando o vencedor não é o de menor custo, o motivo é obrigatório (lista: Prazo, Qualidade, Condição de pagamento, Fornecedor homologado, Outro + texto; até 200) e fica no histórico.
7. **Dado** a cotação concluída, **quando** o `NextSteps` aparece, **então** oferece "Gerar pedidos de compra" (um pedido por fornecedor vencedor, CMP-03a), com a prévia de quantos pedidos e o total de cada.
8. **Dado** `purchasing.edit` e uma cotação `Open`, **quando** cancelo com motivo, **então** fica `Cancelled` e as linhas de SC voltam a `Open`.

**Permissões e segurança**
9. **Dado** o módulo `purchasing` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem a ação, 403 e o botão some. `RequireModule` e `perm:`; IDOR em `/rfqs/{id}/suppliers/{supplierId}` e `/rfqs/{id}/lines/{lineId}`; fornecedor validado por `IParties.HasRoleAsync(Supplier)`.
10. **[Segurança]** **Dado** uma cotação `Closed`, **quando** alguém tenta mudar preço ou vencedor, **então** 409 "A cotação está concluída." (`xmin` e transição pela entidade); reabrir não existe: nova cotação.
11. **Dado** qualquer alteração, **quando** abro "Histórico de alterações", **então** vejo antes/depois (preços, vencedor e motivo), usuário e data.

## Regras de negócio e técnicas
- **Tabelas** (03 + proposta): `rfqs` (`number`, `status` Open/Closed/Cancelled, `closed_at`, `closed_by`, `notes`, `xmin`); `rfq_suppliers` (`rfq_id`, `party_id`, `freight`, `ipi_pct`, `payment_term_id`, `valid_until`, `responded`); `rfq_lines` (`rfq_id`, `item_id`, `quantity`, `request_line_ids` ou tabela de vínculo `rfq_line_requests`); `rfq_quotes` (`rfq_line_id`, `rfq_supplier_id`, `unit_price`, `available_qty`, `lead_days`); vencedor por linha em `rfq_lines.selected_supplier_id` e `selection_reason`. O 03 juntava fornecedor e linha numa só; a separação é proposta para guardar frete e condição por fornecedor.
- **Custo total** com frete e IPI (P-16 decidida: entram no custo; rateio proporcional ao valor dos itens), só para comparação; o custo de entrada real é do recebimento (CMP-04).
- **Sem efeito financeiro nem de estoque.**
- Guid v7; numeração `COT`; `xmin`; auditoria `IAuditable`; preços `numeric(18,6)`, totais `numeric(18,2)`.

## Permissões, eventos e tabelas
- **Permissões:** `purchasing.view`, `purchasing.create`, `purchasing.edit`.
- **Eventos:** nenhum.
- **Contratos:** usa `IParties`, `IItems`, `IItemSupplierCodes`, `IUnitConversion`, `IPaymentTerms`; dono de anexo `Rfq` (novo).
- **Tabelas:** `purchasing.rfqs`, `purchasing.rfq_suppliers`, `purchasing.rfq_lines`, `purchasing.rfq_quotes` (propostas sobre o 03).

## Telas e estados
- **Fábrica → Compras → Cotações** (`/app/compras/cotacoes`, detalhe `/app/compras/cotacoes/$id`): cabeçalho com número, chip, itens e fornecedores; abas na URL: Respostas, Comparativo, Documentos, Histórico de alterações.
- **Verbos:** "Criar cotação" / "Cotação criada"; "Salvar resposta" / "Resposta salva"; "Concluir cotação" / "Cotação concluída"; "Cancelar cotação" / "Cotação cancelada".
- **Estados:** carregando; vazio ("Nenhuma cotação aberta." + "Cotar itens das solicitações"); comparativo sem respostas ("Registre a resposta de ao menos um fornecedor."); erro; modo leitura.
- **Propositivo:** fornecedores sugeridos (CA 1); condição de pagamento padrão do fornecedor preenchida (CAD-05b); vencedor sugerido (CA 6); "Copiar preços da última compra" como ponto de partida; próximo passo para os pedidos (CA 7).
- **400 px:** o comparativo vira um cartão por fornecedor com o custo total, prazo e condição, e a tabela por linha em rolagem horizontal com a primeira coluna fixa; respostas em cartões.
- **Ajuda:** `Ajuda/compras-cotacoes.md`, `Ajuda/compras-cotacao.md` (abas `respostas` padrão, `comparativo`, `documentos`, `historico`); `<Dica>` em "Custo total" ("Itens + frete + IPI, como entra no custo do estoque") e "Motivo da escolha".

## Plano de testes
- **Unidade:** custo total com frete e IPI e o vencedor sugerido (CA 5, 6); conversão da unidade do fornecedor (CA 3).
- **Integração (Testcontainers):** criar a partir de SC de várias OS, com idempotência e SC em `Quoting` (CA 1); fornecedores (CA 2); respostas e anexos (CA 3, 4); comparativo com dataset fixo (CA 5); concluir com motivo quando não é o menor (CA 6); cancelar devolve as SC (CA 8); três camadas, IDOR e fornecedor sem papel (CA 9); concluída imutável (CA 10); auditoria (CA 11).
- **E2E (Playwright):** cotar dois itens com três fornecedores, comparar e concluir; comparativo a 400 px.

## Fora do escopo
- Envio da cotação por e-mail ao fornecedor e portal do fornecedor: Pontos em aberto 1.
- Pedido de compra: CMP-03a.

## Pontos em aberto
1. **Pedir a cotação por e-mail** (PO): R-CMP-2 não pede envio. Proposta: fora do MVP; o comprador pede por telefone/e-mail próprio e registra a resposta; se o PO quiser, reaproveita o envio da CMP-03b.
2. **Número mínimo de cotações** (PO/controle): proposta: não exigir; se o PO quiser controle, "pedido acima de R$ X exige ao menos 3 cotações ou justificativa" na CMP-03a.
3. **IPI na cotação** (PO/contadora): proposta: campo IPI % por fornecedor para o custo total (P-16); a regra fiscal completa é da Fase 5.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`; dono de anexo `Rfq` registrado com `OwnerExists`.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda da lista e de cada aba; teste de cobertura passando.
- [ ] Padrões e próximos passos avaliados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `Rfq`, `RfqSupplier`, `RfqLine`, `RfqQuote` (Domain) e `RfqComparison` puro.
- [ ] `RfqService` (criar, respostas, concluir, cancelar).
- [ ] Endpoints `/api/v1/purchasing/rfqs`; telas, ajudas e dono de anexo.

## Quality gates
- Agentes previstos: @dev, design (comparativo a 400 px), @qa.
- Gate: CA 5, 6 e 10 obrigatórios.

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

- **Correções feitas na validação:** frete, IPI e condição por fornecedor (separação das tabelas marcada como proposta sobre o 03); motivo obrigatório quando o vencedor não é o menor custo (CA 6); envio por e-mail e mínimo de cotações como pontos em aberto.
- **Pré-condições para `Ready`:** nenhuma bloqueante (os pontos 1 a 3 têm proposta que não muda o núcleo da story).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Draft` até a aprovação do PO.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
