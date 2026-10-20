# COM-01a — Orçamento: cabeçalho, itens, composição de custo, preço e margem

| Campo | Valor |
|---|---|
| ID | COM-01a |
| Título | Orçamento em rascunho com cliente, contato e endereço da obra; itens (equipamento, produto, serviço) com composição de custo estimada por natureza; preço pelo modo do tenant (markup ou margem); totais e margem ao vivo; custo e margem só para quem tem `quotes.view_cost`; marcar como enviado |
| Tamanho | G (quebra da COM-01: condições por marco, validade e cópia foram para a COM-01b) |
| Status | Ready |
| Requisitos | R-COML-1 (itens, composição de custo estimada, margem/markup → preço) · R-COM-4 (numeração ORC) · R-PRP-1 · R-PRP-3 · R-SEC-5 · R-SEC-10 (permissão por ação) |
| Dependências | FND-09e (diálogo grande, `RowsEditor` com modelo de colunas, concorrência), CAD-01a (`IParties`), CAD-02a (`IItems`), CAD-03 (`IWorkCenters`), CAD-04a (`IHourlyCost`, cargos), CAD-06 (`ITenantParameters.PricingModeAsync`, numeração), FND-09d (moldes, `MoneyInput`, `PercentInput`, `QuantityInput`); Fase 0 |
| Bloqueia | COM-01b, COM-02, COM-03, COM-04a, COM-08, PLN-05 |
| Ordem de execução | 8 (Marco 2.2 "Orçamento") |

## User story
Como **comercial**, quero **montar o orçamento item a item, com o custo estimado de material, horas, instalação, frete e impostos, e ver o preço e a margem enquanto preencho**, para **mandar ao cliente um preço que eu sei que dá lucro**.

## Contexto e referências
- PRD §5.4 (orçamento = pré-proposta com validade, **sem efeito financeiro**), R-COML-1; `04-fluxos-estados` §1 "Orçamento" (`Draft → Sent`).
- `03-modelo-dados` → `commercial.quotes`, `quote_items`, `quote_item_costs` (`cost_type` Material/Labor/Installation/Freight/Tax/Other).
- P-14 (decidida): os dois modos, **markup é o padrão** (CAD-06); preço = custo × (1 + markup) ou custo ÷ (1 − margem).
- P-01 (**pendente**): regime tributário. Decisão do PO de 20/10/2026 (seção 1.1, item 16): imposto estimado **digitado** enquanto a P-01 está aberta.
- **D7 (PO, 20/10/2026):** custo e margem são sigilo comercial: ações `quotes.view_cost` e `projects.view_cost` (ADR 0003), no seed do Proprietário, do Comercial e do Financeiro; DTO sem custo para os demais; o PDF nunca mostra custo (ADR 0019 §20).
- Revisão pré-Fase 2: §2.1-M5 (arredondamento), M6 (parâmetros do Commercial), M7 (número com revisão, ADR 0016 adendo), M10 (`CostType` no SharedKernel), §2.3 (editor de custo por natureza, rótulos de %, valor digitado à mão, margem negativa, totais fixos, 400 px).
- CAD-04a CA 8 / revisão pré-Fase 1 §2.5: custo-hora **só por cargo**, por `IHourlyCost`; nunca por funcionário.
- CAD-02a: `items.average_cost` existe, só leitura, alimentado pela EST-01 (Fase 3); `IItems` hoje não o expõe.
- `ModuleCatalog`: `quotes` (`view`, `create`, `edit`, `approve` = "gerar o contrato", `delete`). Comercial do seed: `quotes.view/create/edit`; Financeiro: `quotes.view`.
- Protótipo: tela "Orçamentos" (orçamento, cliente e equipamento, status, validade, custo, preço, margem).

## De onde vem cada componente do custo
| Natureza (`CostType`) | Origem nesta story | Depois |
|---|---|---|
| Material | Item do estoque (`IItems.SearchAsync`), quantidade na unidade de estoque do item e **custo unitário digitado**, sugerido pelo último custo usado para o mesmo item num orçamento deste tenant | Sugestão pelo custo médio do item (`items.average_cost`, EST-01, Fase 3) por um contrato novo `IItemCosts` em `Inventory.Contracts` (proposta); último custo de compra na CMP-04 |
| Mão de obra | Horas × custo-hora do **cargo** (`IHourlyCost.ForPositionAsync`); o centro de trabalho é informado para a capacidade (PLN-05) e sugere o cargo cujo centro padrão é ele (CAD-04a) | Igual; a PLN-05 usa as horas por centro |
| Instalação | Valor estimado digitado (instalação terceirizada), instalador opcional (papel Instalador) | OS de instalação e CP do instalador na COM-06 |
| Frete | Valor estimado digitado | — |
| Impostos | **Valor estimado digitado** (P-01 pendente) | Percentual por regime depois da P-01, em story própria |
| Outros | Valor digitado com descrição | — |

O custo unitário de cada linha é **gravado no orçamento** (snapshot): mudar depois o custo-hora do cargo ou o custo do item não altera orçamentos já feitos; "Atualizar custos" (CA 13) recalcula um rascunho com confirmação.

## Critérios de aceite
**Permissão de custo (D7)**
1. **Dado** um tenant novo ou existente no próximo deploy, **quando** o deploy roda, **então** o `ModuleCatalog` tem as ações específicas `quotes.view_cost` ("Ver custo e margem do orçamento") e `projects.view_cost` ("Ver orçado, custo lançado e margem do projeto"), e o seed patch `view-cost-default-permissions` as concede aos grupos Comercial e Financeiro do seed (o Proprietário tem tudo), uma vez, com o `perm_version` dos membros incrementado.
2. **[Segurança]** **Dado** um usuário com `quotes.view` sem `quotes.view_cost`, **quando** abre a lista, o detalhe ou qualquer endpoint de orçamento, **então** recebe o DTO sem custo (sem custo total, custo unitário, composição, markup, margem, cargo nem custo-hora — não são campos nulos, são DTOs sem as propriedades), as colunas e o resumo de custo não aparecem, e a composição responde 403; um teste de arquitetura impede propriedade de custo nos DTOs "sem custo".
3. **Dado** `quotes.edit` sem `quotes.view_cost`, **quando** edita um rascunho, **então** muda cabeçalho, descrição, quantidade e preço dos itens, mas não a composição de custo nem "Atualizar custos" (ocultos e 403).

**Cabeçalho**
4. **Dado** `quotes.create`, **quando** clico "Novo orçamento", escolho o cliente e clico "Criar orçamento", **então** o orçamento nasce `Draft`, revisão 0, `is_current = true`, número `ORC-2026-0001` (`INumberSequence`, tipo `ORC`), e o detalhe abre na aba Itens; o contato vem com o contato de cobrança do cliente (`IParties.BillingContactAsync`) e o endereço da obra com o endereço padrão do tipo Obra (`DefaultAddressAsync(Site)`), editáveis (R-PRP-1).
5. **Dado** o cliente, **quando** escolho, **então** só aparecem terceiros ativos com o papel Cliente; um terceiro sem o papel mostra "Adicionar papel Cliente" (abre o terceiro, CAD-01a) e a API responde 422 "Escolha um terceiro com o papel Cliente."; contato e endereço precisam pertencer ao cliente (422 "O contato não pertence a este cliente.").
6. **Dado** o cabeçalho, **quando** edito prazo de entrega prometido (dias corridos após a aprovação, inteiro ≥ 0) e observações (até 2.000 caracteres), **então** grava com "Salvar orçamento" e a mensagem de sucesso diz "Orçamento salvo".

**Itens e composição de custo**
7. **Dado** um orçamento `Draft`, **quando** clico "Adicionar item", escolho o tipo (Equipamento, Produto ou Serviço), opcionalmente um item do estoque (`IItems`; a descrição vem do nome do item), a descrição e a quantidade (`numeric(18,4)`, > 0), **então** o item aparece com custo zero e a composição vazia.
8. **Dado** `quotes.view_cost` e o painel "Composição de custo" do item, **quando** adiciono linhas, **então** o editor tem uma **seção por natureza** (Material, Mão de obra, Instalação, Frete, Impostos, Outros), cada uma com o seu `RowsEditor` (chave estável por linha, foco na linha seguinte depois de remover) e o subtotal; cada linha mostra o total (`round(quantidade × custo unitário, 2)` ou `round(horas × custo-hora, 2)`) e o item mostra o custo unitário = soma das linhas para **uma unidade** do item; tudo recalcula ao vivo.
9. **Dado** uma linha de mão de obra, **quando** escolho o centro de trabalho, **então** o cargo vem com o cargo ativo cujo centro padrão é esse (se houver um só; senão fico na escolha) e o custo-hora vem de `IHourlyCost.ForPositionAsync(cargo)`, gravado na linha (`unit_cost`, `numeric(18,6)`); cargo inativo ou inexistente responde 422 no campo da linha.
10. **[Segurança]** **Dado** qualquer linha de mão de obra, **quando** a API recebe um `employeeId` ou o front pede custo por funcionário, **então** não existe esse campo nem rota: o custo é sempre do cargo (CAD-04a CA 8); um teste de arquitetura impede o Commercial de referenciar `IEmployees` para custo.
11. **Dado** uma linha de material, **quando** escolho o item, **então** a quantidade é na unidade de estoque do item (mostrada ao lado) e o custo unitário vem com o último custo usado para esse item em outro orçamento do tenant, ou vazio; nunca vem de preço do Dolibarr (não migrou, ADR 0017).

**Preço e margem**
12. **Dado** o modo de precificação do tenant (`ITenantParameters.PricingModeAsync`) = Markup e o % padrão (`pricing.default_pct`, parâmetro do Commercial), **quando** o item tem custo unitário R$ 1.000,00 e markup 40,0000%, **então** o preço unitário sugerido é R$ 1.400,00 (`round(custo × (1 + markup/100), 2, AwayFromZero)`) e o rótulo mostra "Markup 40,00% · Margem 28,57%"; no modo Margem, com margem 40,0000%, é R$ 1.666,67 (`round(custo ÷ (1 − margem/100), 2)`), com "Margem 40,00% · Markup 66,67%"; margem ≥ 100% responde 422; com o % padrão vazio (seed), o primeiro item pede o % com a Dica "Defina o % que a empresa costuma aplicar".
13. **Dado** um orçamento `Draft` cujos custos de mão de obra ficaram diferentes do custo-hora atual dos cargos, **quando** clico "Atualizar custos", **então** um diálogo (`size="lg"`) mostra cada linha com o valor antigo e o novo e o impacto no custo total, e só grava ao confirmar "Atualizar custos"; preços digitados à mão não mudam.
14. **Dado** o preço unitário sugerido, **quando** digito outro preço, **então** o preço digitado vale (`price_overridden = true`), aparece a `Tag` "Digitado à mão" com a ação "Voltar ao sugerido", e o % do item é recalculado e exibido com o rótulo do que é.
15. **Dado** o orçamento, **quando** qualquer linha muda, **então** os totais (custo total = Σ custo unitário × quantidade, cada produto arredondado a 2 casas; preço total = Σ preço unitário × quantidade; `margin_pct` = (preço − custo) ÷ preço em `numeric(9,4)`, nulo com preço 0) são recalculados ao vivo num **rodapé fixo**, anunciados por `aria-live` com espera de cerca de 500 ms, e gravados pelo servidor com as mesmas fórmulas (`total_cost`, `total_price`, `margin_pct`); margem negativa aparece no chip crítico "Preço abaixo do custo" (não bloqueia).

**Ciclo nesta story**
16. **Dado** um orçamento `Draft` com ao menos um item com preço > 0, **quando** clico "Marcar como enviado" e confirmo, **então** ele passa a `Sent` com a data do envio; sem item, 422 "Inclua ao menos um item com preço."; o envio do PDF por e-mail é a COM-03.
17. **Dado** um orçamento `Sent`, **quando** tento editar itens, custos ou preços, **então** a API responde 409 "Orçamento enviado não é alterado. Crie uma nova revisão." (a nova revisão é a COM-02); o cabeçalho também fica só leitura, com a faixa de modo leitura que oferece "Nova revisão" (COM-02, quando existir).
18. **Dado** `quotes.delete` e um orçamento `Draft` de revisão 0 sem revisões, **quando** clico "Excluir orçamento" e confirmo "Excluir orçamento", **então** ele é excluído logicamente (o número não volta para a sequência); orçamento enviado responde 409 "Orçamento enviado não é excluído. Registre a perda." (COM-01b).

**Permissões (3 camadas) e segurança**
19. **Dado** o módulo `quotes` não liberado, **quando** chamo os endpoints, **então** 403 `module_not_released` e o front mostra `ModuleNotReleased`; sem grupo que habilite, 403 `forbidden`; com `quotes.view` sem `create`/`edit`/`delete`, ações ocultas por `<Can>` e escrita com 403. `RequireModule` e `perm:` em todos os endpoints.
20. **[Segurança]** **Dado** as rotas `/quotes/{id}/items/{itemId}` e `/quotes/{id}/items/{itemId}/costs/{costId}`, **quando** o filho não pertence ao pai, **então** 404 (IDOR); cliente, contato, endereço, item, centro e cargo são validados pelos Contracts dos donos.
21. **[Segurança]** **Dado** duas pessoas editando o mesmo orçamento, **quando** a segunda salva uma versão antiga, **então** 409 `concurrency_conflict` "O orçamento mudou desde que você abriu. Recarregue." (`xmin`, ADR 0019 §2), com "Recarregar".
22. **Dado** um orçamento usado, **quando** o terceiro, o item ou o centro são excluídos no cadastro, **então** a exclusão é recusada pelos provedores de uso (`IPartyUsageProvider` "orçamentos", `IItemUsageProvider`, `IWorkCenterUsageProvider`), e o orçamento antigo continua mostrando o nome (resolução por id inclui inativos).
23. **Dado** qualquer alteração, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário e data; sem `quotes.view_cost`, os campos de custo aparecem como "valor protegido".

## Regras de negócio e técnicas
- **Dinheiro** `numeric(18,2)` (totais, preço unitário, valores de instalação/frete/impostos/outros); **custo unitário** `numeric(18,6)` (linhas e `quote_items.unit_cost`); **quantidade e horas** `numeric(18,4)`; **percentuais** `numeric(7,4)`; `margin_pct` `numeric(9,4)` anulável.
- **Arredondamento** (ADR 0019 §9): só no total da linha (custo unitário com 6 casas × quantidade → 2 casas, `AwayFromZero`); custo unitário do item = Σ totais das linhas; preço = fórmula do modo, 2 casas.
- **Snapshot de custo** na linha; o cargo, o centro e o item são só ids (referência entre módulos sem FK, ADR 0006).
- **Sem efeito financeiro:** orçamento nunca cria título nem movimenta estoque (PRD §5.4).
- **Número com revisão** (ADR 0016 adendo M7): a primeira migration já cria `is_current`, o índice único `(number, revision)` e o único parcial `(number) WHERE is_current`.
- **Situação** nesta story: `Draft`, `Sent`; as demais na COM-01b, COM-02 e COM-04a.
- **Vocabulário:** `CostType` no SharedKernel (ADR 0019 §14), usado também pelo projeto (COM-04a) e pelo título a pagar.
- **DTOs** por permissão: `QuoteSummaryDto`/`QuoteDetailDto` (sem custo) e `QuoteCostDetailDto` (com `quotes.view_cost`).
- **Ids** Guid v7; **auditoria** `IAuditable` em orçamento, item e linha de custo; **numeração** `ORC`; soft delete só no rascunho rev. 0; lista com `IParties.FindManyAsync`.

## Permissões, eventos e tabelas
- **Permissões:** `quotes.view`, `quotes.create`, `quotes.edit` (itens, preço, marcar enviado), `quotes.delete`; **novas** `quotes.view_cost` e `projects.view_cost` no `ModuleCatalog`, com o seed patch `view-cost-default-permissions` (Comercial e Financeiro).
- **Parâmetro:** `pricing.default_pct` (`numeric(7,4)`, seed vazio) em `commercial.parameters` (ADR 0016 adendo M6), lido por `ICommercialParameters`, editado em Configurações → Parâmetros (seção Comercial, `settings.edit`) ao lado do modo (CAD-06).
- **Eventos:** nenhum nesta story.
- **Tabelas:** `commercial.quotes` (como no 03; sem `payment_term` e sem `valid_until` obrigatório nesta story; mais `sent_at`, `pricing_mode`, `is_current`, `client_request_id`); `commercial.quote_items` (+ `sequence`, `markup_pct` ou `margin_pct` do item, `price_overridden`); `commercial.quote_item_costs` (+ `position_id`, `description`, `party_id` opcional para instalador); `commercial.parameters` (nova).

## Telas e estados
- **Comercial → Orçamentos** (`/app/orcamentos`; entrada no `modules.ts` na área Comercial): h1 "Pré-propostas com validade. Ao aprovar, o sistema gera o contrato, o projeto e as parcelas a receber." (protótipo); DataTable com orçamento (número e revisão), cliente e equipamento principal, situação (Rascunho neutro, Enviado info), preço e, com `quotes.view_cost`, custo e margem (`tabular-nums`); filtros na URL.
- **Orçamento** (`/app/orcamentos/$id`): cabeçalho com número, revisão, chip e cliente; abas na URL: Dados, Itens (com a composição de cada item em linha expansível), Histórico de alterações; as abas Condições (COM-01b), Revisões (COM-02) e Documentos (COM-03) entram depois.
- **Rodapé fixo de totais:** custo total, preço total, margem e o modo ("Markup sobre o custo"); sem `quotes.view_cost`, só o preço total.
- **Verbos:** "Criar orçamento" / "Orçamento criado"; "Salvar orçamento" / "Orçamento salvo"; "Adicionar item"; "Atualizar custos" / "Custos atualizados"; "Marcar como enviado" / "Marcado como enviado"; "Excluir orçamento" / "Orçamento excluído".
- **Estados:** carregando (skeleton por aba); vazio da lista ("Crie o primeiro orçamento para um cliente." + "Novo orçamento"); vazio de itens ("Adicione o equipamento ou serviço que o cliente pediu." + "Adicionar item"); erro; modo leitura (sem `quotes.edit` ou orçamento enviado).
- **Propositivo:** contato e obra do cliente preenchidos; % padrão do tenant; cargo sugerido pelo centro; custo do material sugerido pelo último orçamento; "Duplicar item"; depois de criar, "Próximos passos": "Adicionar item" e "Definir condições de pagamento" (COM-01b).
- **400 px:** a lista mostra orçamento, cliente, situação e preço; o editor de composição vira cartões por linha, com o resumo compacto (custo, preço, margem) no rodapé fixo. E2E de 400 px e só pelo teclado do editor de composição.
- **Ajuda:** `Ajuda/comercial-orcamentos.md` (`padrao: true`), `Ajuda/comercial-orcamento.md` com uma por aba (`dados`, `itens`, `historico`); `<Dica>` em "Markup", "Margem", "Custo-hora do cargo", "Custo unitário do item", "Digitado à mão" e "Impostos estimados"; `Ajuda/configuracoes-parametros.md` ganha "% padrão de markup ou margem"; a ajuda de grupos e permissões ganha "Ver custo e margem".

## Plano de testes
- **Unidade:** fórmulas de preço nos dois modos, arredondamento, rótulos de % e margem nula com preço 0 (CA 12, 14, 15); custo unitário por unidade (CA 8); atualizar custos (CA 13).
- **Integração (Testcontainers):** seed patch e ações novas (CA 1); DTO sem custo e 403 na composição sem `quotes.view_cost` (CA 2, 3); criar com padrões do cliente e índices de número com revisão (CA 4, 5); itens e linhas com validação pelos Contracts (CA 7–9, 11, 20); custo-hora pelo cargo e snapshot (CA 9); totais gravados iguais aos do front (CA 15); marcar enviado e bloqueio de edição (CA 16, 17); exclusão (CA 18); três camadas (CA 19); concorrência (CA 21); provedores de uso (CA 22); auditoria com campos protegidos (CA 23).
- **Arquitetura:** Commercial só referencia `Parties`, `Inventory`, `Production`, `HR` e `Identity` pelos Contracts; sem uso de `IEmployees` para custo (CA 10); DTOs "sem custo" sem propriedades de custo (CA 2); `CostType` no SharedKernel.
- **E2E (Playwright):** comercial monta orçamento com dois itens e vê preço e margem ao vivo; usuário sem `quotes.view_cost` não vê custo; editor a 400 px e só pelo teclado.

## Fora do escopo
- Condições por marco, validade, rejeição e cópia: COM-01b. Revisões, expiração e prorrogação: COM-02. PDF e e-mail: COM-03. Aprovação: COM-04a.
- BOM estimada vinda da Engenharia (ENG-02, Fase 3) e custo médio real (EST-01, Fase 3).
- Simulação de prazo pela capacidade: PLN-05 (Fase 4, D). Imposto por regime: depois da P-01.

## Pontos em aberto
Decididos pelo PO em 20/10/2026 (seção 1.1, itens 15 a 19, e D7):
1. % padrão de markup/margem em Configurações (parâmetro do Commercial), seed vazio.
2. Impostos estimados digitados enquanto a P-01 está aberta.
3. Mão de obra pelo custo-hora do cargo, com o centro guardado para a capacidade.
4. Composição de custo por unidade do item.
5. Custo e margem com `quotes.view_cost` (D7), no seed do Proprietário, do Comercial e do Financeiro; o PDF nunca os mostra.
6. P-18 decidida (ADR 0021).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (schema `commercial`, não destrutiva, com os índices de número com revisão); parâmetro e seed patch idempotentes.
- [ ] Permissões só do `ModuleCatalog` (ações novas registradas no catálogo).
- [ ] Telas com estados de carregando, vazio, erro e modo leitura.
- [ ] Ajuda da lista, de cada aba, dos parâmetros e da permissão nova; teste de cobertura passando.
- [ ] Padrões preenchidos e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Entidades `Quote`, `QuoteItem`, `QuoteItemCost` e `PriceCalculator` (Domain, puro); `CostType` no SharedKernel.
- [ ] Ações `quotes.view_cost` e `projects.view_cost` no `ModuleCatalog`; seed patch `view-cost-default-permissions`.
- [ ] `ICommercialDb`, `commercial.parameters` e `ICommercialParameters`; provedores de uso para Parties, Inventory e Production.
- [ ] Endpoints `/api/v1/quotes` (+ itens, custos, `/send-mark`, `/refresh-costs`); DTOs por permissão; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, design (editor de composição por natureza), cybersecurity (DTO sem custo).
- Gate: CA 2, 9, 10, 12 e 15 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 20/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 2).

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

- **Correções feitas na validação (19/10/2026):** quebra da COM-01; tabela "De onde vem cada componente do custo"; custo do material digitado nesta fase; imposto digitado; mão de obra só por cargo; "Marcar como enviado"; parâmetro novo na tela e na ajuda de Configurações.
- **Correções da revalidação (20/10/2026):** `quotes.view_cost` e `projects.view_cost` com seed patch e DTO sem custo (D7, CA 1–3); editor de composição por natureza com subtotal e foco depois de remover (§2.3, CA 8); rótulos de % e "Voltar ao sugerido" (CA 12, 14); arredondamento só na linha e `margin_pct numeric(9,4)` nulo com preço 0 (M5, CA 15); rodapé fixo de totais; índices de número com revisão na primeira migration (M7); `pricing.default_pct` como parâmetro do Commercial (M6); `CostType` no SharedKernel (M10); 409 com "Recarregar"; resumo compacto a 400 px. CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma pendente (P-01 continua aberta, sem bloquear: imposto digitado, decisão do PO).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada (quebra da COM-01) | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 20/10/2026 | 0.3 | Decisões do PO (D7, seção 1.1) e correções da revisão pré-Fase 2 (§2.1-M5, M6, M7, M10, §2.3); CAs renumerados | River (SM) |
| 20/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
