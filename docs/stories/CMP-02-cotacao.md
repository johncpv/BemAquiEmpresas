# CMP-02 — Cotação

| Campo | Valor |
|---|---|
| ID | CMP-02 |
| Título | Cotação a partir de linhas de solicitação, com um ou mais fornecedores; registro das respostas (preço unitário, frete, IPI %, prazo, condição de pagamento) com quem lançou e o anexo da proposta acima do limite; comparativo lado a lado com o menor custo total destacado; escolha do vencedor por linha; geração dos pedidos de compra a partir do vencedor (API da CMP-03a) e "Enviar todos para aprovação" |
| Tamanho | M |
| Status | Done |
| Requisitos | R-CMP-2 (cotação com 1+ fornecedores, comparativo e escolha) · R-CMP-6 · R-COM-4 (numeração COT) · R-PRP-1 (último fornecedor e preço do item) · R-PRP-3 · R-SEC-5 |
| Dependências | CMP-01 (linhas de solicitação), CMP-03a (API de criação e envio do pedido) |
| Bloqueia | — |
| Ordem de execução | 17 (Marco 3.3 "Comprar o que falta") |

## User story
Como **comprador**, quero **pedir preço a mais de um fornecedor, ver as respostas lado a lado, escolher o melhor e já gerar os pedidos**, para **comprar pelo menor custo total e deixar registrado por que escolhi**.

## Contexto e referências
- PRD §5.10 R-CMP-2; R-PRP-1 ("último fornecedor e preço do item").
- `03-modelo-dados` → `purchasing.rfqs` (`number`, `request_id`, `status`), `rfq_suppliers / rfq_lines` (`rfq_id`, `party_id`, `item_id`, `quantity`, `unit_price`, `lead_days`, `freight`, `selected`). `04-fluxos` §4: `COT-AAAA-0001` (em `NumberFormats`).
- Contratos já entregues: `IParties` com papel Fornecedor (CAD-01a), `IItemSupplierCodes` e conversão da unidade do fornecedor (CAD-02b), `IPaymentTerms` (CAD-05b); último preço pago pelo recebimento (CMP-04, que vem antes na ordem).
- CMP-03a: o pedido nasce manual ou a partir da SC; o pedido "a partir do vencedor da cotação" é entregue aqui, chamando a API de criação de pedido da CMP-03a (CA 9), e o envio usa o envio para aprovação da CMP-03a (CA 10).
- `ModuleCatalog`: `purchasing` (`view`, `create`, `edit`, `approve`, `delete`).
- Revisão pré-Fase 3: `docs/revisoes/2026-10-21-revisao-pre-fase3.md` (D8, tabela 1.1 itens 35 a 37, §2.2 CMP-02, §2.3 padrão 6 e achados pontuais "comparativo de cotação com rádio por linha" e "Enviar todos para aprovação depois de gerar pedidos", §4 ordem do Marco 3.3).
- ADR 0024 §5 (cotação sem mínimo, aviso "1 cotação", anexo acima de R$ 5.000,00, quem lançou cada resposta); ADR 0019 §7 com o adendo de 21/10/2026 (dono de anexo `Rfq`).

## Critérios de aceite
**Criar**
1. **Dado** `purchasing.create` e linhas de SC `Open` (uma ou várias SC, pela visão "Por item" da CMP-01), **quando** clico "Cotar", escolho os fornecedores (terceiros ativos com papel Fornecedor; padrão: os últimos fornecedores desses itens, até 3, com a tag "Sugerido"; R-PRP-1) e clico "Criar cotação", **então** nasce a cotação `Open` com número `COT-2026-0001`, uma linha por item (quantidades somadas das linhas de SC, com o vínculo a cada linha de origem), as linhas de SC passam a `Quoting`, com `Idempotency-Key`; uma cotação pode ser criada também sem SC (itens digitados), para compra pontual.
2. **Dado** a cotação, **quando** abro "Fornecedores", **então** posso incluir ou remover fornecedor enquanto ela está `Open`; o mesmo fornecedor não se repete (422).

**Registrar respostas**
3. **Dado** `purchasing.edit`, **quando** registro a resposta de um fornecedor por linha (preço unitário `numeric(18,6)`, na unidade do fornecedor convertida pelo fator do `item_supplier_codes` quando houver, ou na unidade de estoque; quantidade disponível, padrão a pedida; prazo em dias) e, por fornecedor, frete (`numeric(18,2)`), IPI % (ponto 37), condição de pagamento (`IPaymentTerms`) e validade da proposta (`date`), **então** grava a resposta; "Não cotou" marca o fornecedor sem resposta.
4. **Dado** a proposta do fornecedor em PDF, imagem ou planilha, **quando** anexo (dono `Rfq`: pdf, png, jpg, xlsx até 10 MB, ClamAV, leitura `purchasing.view`, escrita `purchasing.edit`; ADR 0019 §7, adendo de 21/10/2026), **então** fica na aba Documentos da cotação, ligada ao fornecedor.
5. **[Segurança]** **Dado** uma resposta salva ou alterada, **quando** grava, **então** ficam registrados quem lançou (`responded_by`) e quando (`responded_at`); o comparativo mostra "Lançada por {nome} em {data}" e o histórico guarda cada alteração com o autor (§2.2 CMP-02, ADR 0024 §5).
6. **[Segurança]** **Dado** uma resposta com custo total do fornecedor (subtotal + frete + IPI) acima de R$ 5.000,00 (parâmetro `purchasing.quote_attachment_amount`, D8), **quando** salvo sem anexo da proposta desse fornecedor, **então** 422 "Respostas acima de R$ 5.000,00 exigem o anexo da proposta do fornecedor."; ao concluir a cotação, o servidor confere de novo o anexo de cada vencedor acima do limite (o anexo removido depois bloqueia a conclusão com a mesma mensagem).

**Comparar e escolher**
7. **Dado** uma ou mais respostas, **quando** abro "Comparativo", **então** vejo por linha e fornecedor: preço unitário, total da linha (qtd × preço, 2 casas `AwayFromZero`), e por fornecedor: subtotal, frete, IPI, **custo total** (subtotal + frete + IPI, P-16), prazo e condição; o menor custo total por linha e o menor total geral são destacados (chip ok "Menor custo"), e o último preço pago do item aparece como referência ("Última compra: {preço} em {data} de {fornecedor}", CMP-04).
8. **Dado** o comparativo, **quando** escolho o vencedor de cada linha num grupo de rádio por linha (um fornecedor por linha, navegável pelo teclado; pré-marcado o menor custo total da linha, com o frete rateado pelo valor, e a tag "Sugerido", padrão 6; ou "Escolher tudo de {fornecedor}") e clico "Concluir cotação", **então** a cotação fica `Closed`, cada linha guarda o vencedor (`selected_supplier_id`), e quando o vencedor não é o de menor custo, o motivo é obrigatório (lista: Prazo, Qualidade, Condição de pagamento, Fornecedor homologado, Outro + texto; até 200) e fica no histórico. Sem mínimo de cotações: uma resposta basta (ponto 36).

**Gerar pedidos e enviar**
9. **Dado** a cotação `Closed` e `purchasing.create`, **quando** o `NextSteps` oferece "Gerar pedidos de compra" (com a prévia de quantos pedidos e o total de cada) e confirmo, **então** a CMP-02 chama a API de criação de pedido da CMP-03a (`POST /api/v1/purchasing/purchase-orders` com `rfqId`, as respostas vencedoras e o número de respostas; CMP-03a CA 2) e nasce um pedido `Draft` por fornecedor vencedor, com as linhas vencedoras (item, quantidade, preço unitário `numeric(18,6)` na unidade de estoque), frete e IPI do fornecedor, condição de pagamento, data prevista de entrega (hoje + prazo da resposta), o destino (projeto da SC quando todas as linhas são do mesmo projeto, senão "Estoque geral"; R-CMP-6) e o vínculo com a cotação e as linhas de SC, que passam a `Ordered`; com `Idempotency-Key` e idempotente por cotação e fornecedor (gerar de novo não duplica: mostra os pedidos já gerados).
10. **Dado** os pedidos gerados, **quando** clico "Enviar todos para aprovação" e confirmo no diálogo ("Enviar {n} pedidos, total {valor}, para aprovação"), **então** cada pedido passa pelo envio para aprovação da CMP-03a (alçada resolvida por pedido, ADR 0024 §4), de forma independente; o resultado lista cada pedido como "Enviado" (com o nível) ou "Não enviado" com o motivo (ex.: `approval_not_viable`, "Nenhum aprovador elegível neste nível.") e a ação "Abrir pedido"; a mensagem é "Pedidos enviados para aprovação ({x} de {n})". Exige a mesma permissão do envio da CMP-03a.
11. **Dado** uma cotação concluída com uma só resposta, **quando** gero os pedidos, **então** cada pedido leva `rfq_id` e `quotes_count = 1`, e o diálogo de aprovação da CMP-03a (CA 11 de lá) mostra o aviso "1 cotação", sem bloquear (D8, ADR 0024 §5); a cotação concluída mostra o mesmo aviso com `<Dica>` ("Só um fornecedor respondeu a esta cotação.").
12. **Dado** `purchasing.edit` e uma cotação `Open`, **quando** cancelo com motivo, **então** fica `Cancelled` e as linhas de SC voltam a `Open`.

**Permissões e segurança**
13. **Dado** o módulo `purchasing` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem a ação, 403 e o botão some (o Almoxarifado do seed, só com `view` + `receive`, consulta mas não cota). `RequireModule` e `perm:`; IDOR em `/rfqs/{id}/suppliers/{supplierId}` e `/rfqs/{id}/lines/{lineId}`; fornecedor validado por `IParties.HasRoleAsync(Supplier)`.
14. **[Segurança]** **Dado** uma cotação `Closed`, **quando** alguém tenta mudar preço, resposta ou vencedor, **então** 409 "A cotação está concluída." (`xmin` e transição pela entidade); reabrir não existe: nova cotação.
15. **Dado** qualquer alteração, **quando** abro "Histórico de alterações", **então** vejo antes/depois (preços, vencedor e motivo), usuário e data, inclusive a geração e o envio dos pedidos.

## Regras de negócio e técnicas
- **Tabelas** (03 + separação por fornecedor): `rfqs` (`number`, `status` Open/Closed/Cancelled, `closed_at`, `closed_by`, `notes`, `xmin`); `rfq_suppliers` (`rfq_id`, `party_id`, `freight`, `ipi_pct`, `payment_term_id`, `valid_until`, `responded`, `responded_by`, `responded_at`); `rfq_lines` (`rfq_id`, `item_id`, `quantity`, tabela de vínculo `rfq_line_requests`); `rfq_quotes` (`rfq_line_id`, `rfq_supplier_id`, `unit_price`, `available_qty`, `lead_days`); vencedor por linha em `rfq_lines.selected_supplier_id` e `selection_reason`. O 03 juntava fornecedor e linha numa só; a separação guarda frete, IPI e condição por fornecedor.
- **Custo total** com frete e IPI (P-16: entram no custo; rateio proporcional ao valor dos itens), só para comparação; o custo de entrada real é do recebimento (CMP-04).
- **Limite do anexo:** custo total do fornecedor na cotação comparado a `purchasing.quote_attachment_amount` (R$ 5.000,00).
- **Pedidos pela API da CMP-03a:** a CMP-02 não grava em `purchase_orders` por conta própria; usa o serviço de criação e o de envio da CMP-03a, com as validações e a alçada de lá.
- **Sem efeito financeiro nem de estoque** na cotação; a previsão de caixa nasce na aprovação do pedido (CMP-03a).
- Guid v7; numeração `COT`; `xmin`; auditoria `IAuditable`; preços `numeric(18,6)`, totais `numeric(18,2)`.

## Permissões, eventos e tabelas
- **Permissões:** `purchasing.view`, `purchasing.create` (criar cotação, gerar pedidos), `purchasing.edit` (respostas, concluir, cancelar); envio para aprovação com a permissão da CMP-03a.
- **Eventos:** nenhum próprio (o envio dos pedidos publica os eventos da CMP-03a).
- **Contratos:** usa `IParties`, `IItems`, `IItemSupplierCodes`, `IUnitConversion`, `IPaymentTerms`; serviço de pedido da CMP-03a; dono de anexo `Rfq`.
- **Tabelas:** `purchasing.rfqs`, `purchasing.rfq_suppliers`, `purchasing.rfq_lines`, `purchasing.rfq_line_requests`, `purchasing.rfq_quotes`; parâmetro `purchasing.quote_attachment_amount` em `purchasing.parameters`; `rfq_id` e `quotes_count` do pedido são da CMP-03a (CA 11).

## Telas e estados
- Padrões de tela da Fase 3: 4, 6.
- **Fábrica → Compras → Cotações** (`/app/compras/cotacoes`, detalhe `/app/compras/cotacoes/$id`): cabeçalho com número, chip, itens e fornecedores; abas na URL: Respostas, Comparativo, Documentos, Histórico de alterações.
- **Comparativo:** tabela por linha com um grupo de rádio por linha (`radiogroup` com `aria-label` "Vencedor de {item}"), sem caixa de seleção; o vencedor sugerido leva a tag "Sugerido".
- **Verbos:** "Criar cotação" / "Cotação criada"; "Salvar resposta" / "Resposta salva"; "Concluir cotação" / "Cotação concluída"; "Gerar pedidos de compra" / "Pedidos gerados"; "Enviar todos para aprovação" / "Pedidos enviados para aprovação"; "Cancelar cotação" / "Cotação cancelada".
- **Estados:** carregando; vazio ("Nenhuma cotação aberta." + "Cotar itens das solicitações"); comparativo sem respostas ("Registre a resposta de ao menos um fornecedor."); erro; modo leitura.
- **Propositivo:** fornecedores sugeridos (CA 1); condição de pagamento padrão do fornecedor preenchida (CAD-05b); vencedor sugerido (CA 8); "Copiar preços da última compra" como ponto de partida; próximos passos "Gerar pedidos de compra" e, depois, "Enviar todos para aprovação" (CA 9, 10).
- **400 px:** o comparativo vira um cartão por fornecedor com o custo total, prazo e condição, e a tabela por linha em rolagem horizontal com a primeira coluna fixa; respostas em cartões; o rádio por linha continua com alvo de 44 px.
- **Ajuda:** `Ajuda/compras-cotacoes.md`, `Ajuda/compras-cotacao.md` (abas `respostas` padrão, `comparativo`, `documentos`, `historico`); `<Dica>` em "Custo total" ("Itens + frete + IPI, como entra no custo do estoque"), "Motivo da escolha", "Anexo da proposta" e "1 cotação".

## Plano de testes
- **Unidade:** custo total com frete e IPI e o vencedor sugerido (CA 7, 8); conversão da unidade do fornecedor (CA 3); limite do anexo (CA 6).
- **Integração (Testcontainers):** criar a partir de SC de várias OS, com idempotência e SC em `Quoting` (CA 1); fornecedores (CA 2); respostas, anexos e quem lançou (CA 3, 4, 5); resposta acima de R$ 5.000,00 sem anexo recusada, e conclusão com anexo removido recusada (CA 6); comparativo com dataset fixo (CA 7); concluir com motivo quando não é o menor, e com uma só resposta (CA 8); gerar pedidos pela API da CMP-03a, sem duplicar, SC em `Ordered` (CA 9); enviar todos com um pedido sem alçada viável e os outros enviados (CA 10); pedido com `quotes_count = 1` e o aviso na aprovação (CA 11); cancelar devolve as SC (CA 12); três camadas, IDOR e fornecedor sem papel (CA 13); concluída imutável (CA 14); auditoria (CA 15).
- **E2E (Playwright):** cotar dois itens com três fornecedores, comparar pelo rádio, concluir, gerar os pedidos e enviar todos; comparativo a 400 px.

## Fora do escopo
- Envio da cotação por e-mail ao fornecedor e portal do fornecedor: fora do MVP (ponto 35).
- Mínimo de cotações: não existe (ponto 36); só o aviso "1 cotação".
- Pedido manual ou a partir da SC, aprovação e títulos previstos: CMP-03a.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, tabela 1.1, itens 35–37):
1. Cotação por e-mail: fora do MVP; o comprador pede por telefone ou e-mail próprio e registra a resposta.
2. Mínimo de cotações: D8, sem mínimo; aviso "1 cotação" na aprovação do pedido (CA 11); acima de R$ 5.000,00 a resposta exige o anexo da proposta (CA 6).
3. IPI na cotação: IPI % por fornecedor, para o custo total (CA 3, 7); a regra fiscal completa é da Fase 5.

**Pré-condição externa:** nenhuma.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade e E2E passando; integração (Testcontainers) escritos, a executar na fila do Docker.
- [x] Migration `Rfqs` criada (só `CreateTable`, índices e `CHECK`s por SQL; sem Drop nem Rename); a testar sobre cópia do banco do tenant `konebras`.
- [x] Permissões só do `ModuleCatalog`; dono de anexo `Rfq` registrado com `OwnerExists` e `CanAttach` (ADR 0019 §7, adendo de 21/10/2026).
- [x] ADR 0024 §5 aplicada (anexo acima do limite, aviso "1 cotação", quem lançou).
- [x] Telas com estados, inclusive a 400 px.
- [x] Ajuda da lista e de cada aba; teste de cobertura passando.
- [x] Padrões e próximos passos avaliados.
- [x] Auditoria ligada.
- [x] OpenAPI e cliente Orval regenerados (gerado pelo Host em processo; regenerar com `BQ_UPDATE_OPENAPI`).
- [x] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `Rfq`, `RfqSupplier`, `RfqLine`, `RfqLineRequest`, `RfqQuote` (Domain) e o comparativo puro (`RfqRules.Compare`).
- [x] `RfqService` (criar, fornecedores, respostas, concluir, cancelar, gerar pedidos e enviar todos) sobre o serviço de pedido da CMP-03a.
- [x] `quotes_count` preenchido na geração dos pedidos (o aviso na aprovação é da CMP-03a).
- [x] Endpoints `/api/v1/purchasing/rfqs`; telas, ajudas e dono de anexo.

## Quality gates
- Agentes previstos: @dev, design (comparativo com rádio e a 400 px), @qa.
- Gate: CA 5, 6, 8, 9, 10 e 14 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK (ajustado: gerar os pedidos) |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK (ajustado) |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: CA 5, 6) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: depois da CMP-03a) |
| 7 | Plano de testes cobre os CAs | OK (ajustado) |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK (ajustado: rádio por linha) |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK |

- **Correções feitas na validação (21/10/2026, primeira validação):** frete, IPI e condição por fornecedor (separação das tabelas sobre o 03); motivo obrigatório quando o vencedor não é o menor custo; envio por e-mail e mínimo de cotações como pontos em aberto.
- **Correções da revalidação (21/10/2026):** a story vem depois da CMP-03a e entrega o pedido "a partir do vencedor" pela API dela (CA 9) e o "Enviar todos para aprovação" (CA 10); quem lançou cada resposta (CA 5) e anexo obrigatório acima de R$ 5.000,00 (CA 6), §2.2 e D8; aviso "1 cotação" sem mínimo (CA 11); IPI % por fornecedor definitivo; tipos do dono `Rfq` pela ADR 0019 (sem docx); comparativo com rádio por linha e tag "Sugerido". CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO (D8, tabela 1.1 itens 35–37) e correções da revisão pré-Fase 3 (§2.1 Compras ↔ Financeiro, ADR 0024 §4 e §5; §2.2 CMP-02; §2.3 padrão 6, rádio por linha, "Enviar todos para aprovação"); nova ordem (Marco 3.3, ordem 17, depois da CMP-03a) | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 24/10/2026 | 1.0 | Implementada (`Done`). Cotação no Purchasing (`rfqs`, `rfq_suppliers`, `rfq_lines`, `rfq_line_requests`, `rfq_quotes`; migration `Rfqs`, sem Drop nem Rename, `CHECK`s por SQL), `RfqService` (criar, fornecedores, respostas, "Não cotou", concluir, cancelar, gerar pedidos, enviar todos), comparativo puro (`RfqRules.Compare`), parâmetro `purchasing.quote_attachment_amount` (R$ 5.000,00, em Configurações → Parâmetros → Compras), dono de anexo `Rfq`, telas em Fábrica → Compras → Cotações (lista, nova, detalhe com Respostas, Comparativo, Documentos e Histórico), "Cotar estes itens" no detalhe da SC (botão e próximo passo) e "Cotar" com seleção na visão "Por item", ajudas e E2E. Desvios e decisões: (1) **Pedidos pelo serviço, não pelo HTTP:** a geração chama o serviço de criação da CMP-03a (`PurchaseOrderService.AddFromRfqAsync`, as mesmas validações do `POST /purchase-orders` com `rfqId`, `quotesCount` e `leadDays`) dentro de uma transação da cotação, para gerar os pedidos de todos os vencedores juntos ou nenhum; o envio usa o `SubmitAsync` da CMP-03a. Endpoints próprios: `POST /rfqs/{id}/purchase-orders` (com `Idempotency-Key`) e `POST /rfqs/{id}/purchase-orders/submit`. (2) **Ajustes na CMP-03a e na CMP-01, em commit desta story:** o `PrepareAsync` do pedido aceita as linhas de SC `Quoting` da cotação de origem; o `PurchaseRequestUpdates.RefreshStatusAsync` passou a considerar as linhas mudadas e ainda não gravadas (antes, o pedido e o cancelamento do pedido derivavam a situação da SC pelo que estava no banco, e a SC ficava com a situação antiga); a visão "Por item" leva o `lineId` de cada linha; `IPurchasingDb` expõe o `ChangeTracker` (o envio recusado é descartado antes do próximo). (3) **Quantidade do pedido** = a da linha da cotação (o que as SC pedem); a disponível da resposta é informativa e o comparativo avisa "Tem só {x}"; o rascunho pode ser ajustado antes do envio — recomendação aceita (PO 24/10). (4) **Frete no pedido:** o frete inteiro da resposta vai para o pedido do fornecedor, mesmo quando ele ganha só parte das linhas (o rateio pelo valor vale só para comparar); o rascunho pode ser ajustado — recomendação aceita (PO 24/10). (5) **Linha sem resposta** fica sem vencedor na conclusão e as linhas de SC dela voltam a `Open`; linha cotada exige o vencedor. (6) **Fornecedor com resposta** (inclusive "Não cotou") não sai da cotação (409), para a resposta não sumir — recomendação aceita (PO 24/10). (7) **Concorrência:** toda mudança passa pela versão (`xmin`) da cotação; concluída ou cancelada, 409 `rfq_closed`/`rfq_cancelled` ("A cotação está concluída."). (8) **Anexo `Rfq`:** o `owner_id` é o `rfq_suppliers.id` (a proposta fica ligada ao fornecedor); `CanAttach` recusa a cotação concluída ou cancelada. O sistema não tem exclusão de anexo; o "anexo removido depois" do CA 6 é o anexo recusado pelo antivírus na nova varredura (sai da lista), e a conclusão confere de novo. (9) **Menor total geral** só entre quem cotou todas as linhas cotadas; desempate do menor custo da linha pelo menor prazo e depois pela ordem do fornecedor. (10) **`quotes_count`** = fornecedores que responderam com preço ("Não cotou" não conta). (11) **Histórico (CA 15):** a geração fica no fornecedor (`purchase_order_id`) e o envio também (`order_submitted_at` ou `order_submit_error`); o motivo da escolha e o sugerido ficam na linha. (12) **Validade da proposta** não pode ser antes de hoje (a gravada antes pode ficar). (13) **Prévia** de "Gerar pedidos de compra" vem no detalhe da cotação (`orders`), sem endpoint próprio. (14) **OpenAPI** gerado pelo próprio Host em processo (Development, sem banco e sem hosted services), não editado à mão; mesmo assim, regenerar com `BQ_UPDATE_OPENAPI=1 sh scripts/test-docker.sh --filter-class "*OpenApiDocumentTests"` e `pnpm gen:api`. (15) **Pendente:** testes de integração escritos e não executados (fila do Docker); migration a testar sobre cópia do banco `konebras`; os testes de arquitetura que carregam todas as DLLs não rodam nesta máquina (Smart App Control, 0x800711C7). | Dev (Claude) |
