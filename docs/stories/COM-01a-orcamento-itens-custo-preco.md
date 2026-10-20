# COM-01a — Orçamento: cabeçalho, itens, composição de custo, preço e margem

| Campo | Valor |
|---|---|
| ID | COM-01a |
| Título | Orçamento em rascunho com cliente, contato e endereço da obra; itens (equipamento, produto, serviço) com composição de custo estimada por natureza; preço pelo modo do tenant (markup ou margem); totais e margem ao vivo; marcar como enviado |
| Tamanho | G (quebra da COM-01: condições por marco, validade e cópia foram para a COM-01b) |
| Status | Draft |
| Requisitos | R-COML-1 (itens, composição de custo estimada, margem/markup → preço) · R-COM-4 (numeração ORC) · R-PRP-1 · R-PRP-3 · R-SEC-5 |
| Dependências | CAD-01a (`IParties`), CAD-02a (`IItems`), CAD-03 (`IWorkCenters`), CAD-04a (`IHourlyCost`, cargos), CAD-06 (`ITenantParameters.PricingModeAsync`, numeração), FND-09d (moldes, `RowsEditor`, `MoneyInput`, `PercentInput`, `QuantityInput`); Fase 0 |
| Bloqueia | COM-01b, COM-02, COM-03, COM-04a, COM-08, PLN-05 |
| Ordem de execução | 6 (Marco 2.2 — orçamento real) |

## User story
Como **comercial**, quero **montar o orçamento item a item, com o custo estimado de material, horas, instalação, frete e impostos, e ver o preço e a margem enquanto preencho**, para **mandar ao cliente um preço que eu sei que dá lucro**.

## Contexto e referências
- PRD §5.4 (orçamento = pré-proposta com validade, **sem efeito financeiro**), R-COML-1; `04-fluxos-estados` §1 "Orçamento" (`Draft → Sent`).
- `03-modelo-dados` → `commercial.quotes`, `quote_items`, `quote_item_costs` (`cost_type` Material/Labor/Installation/Freight/Tax/Other).
- P-14 (decidida): os dois modos, **markup é o padrão** (CAD-06); preço = custo × (1 + markup) ou custo ÷ (1 − margem); "o % padrão entra na COM-01".
- P-01 (**pendente**): regime tributário; bloqueia "impostos estimados" automáticos (ver Pontos em aberto 2).
- CAD-04a CA 8 / revisão pré-Fase 1 §2.5: custo-hora **só por cargo**, por `IHourlyCost`; nunca por funcionário.
- CAD-02a: `items.average_cost` existe, só leitura, alimentado pela EST-01 (Fase 3); `IItems` hoje não o expõe.
- `ModuleCatalog`: `quotes` (`view`, `create`, `edit`, `approve` = "gerar o contrato", `delete`). Comercial do seed: `quotes.view/create/edit`; Financeiro: `quotes.view`.
- Protótipo: tela "Orçamentos" (orçamento, cliente e equipamento, status, validade, custo, preço, margem).

## De onde vem cada componente do custo
| Natureza (`cost_type`) | Origem nesta story | Depois |
|---|---|---|
| Material | Item do estoque (`IItems.SearchAsync`), quantidade na unidade de estoque do item e **custo unitário digitado**, sugerido pelo último custo usado para o mesmo item num orçamento deste tenant | Sugestão pelo custo médio do item (`items.average_cost`, EST-01, Fase 3) por um contrato novo `IItemCosts` em `Inventory.Contracts` (proposta); último custo de compra na CMP-04 |
| Mão de obra | Horas × custo-hora do **cargo** (`IHourlyCost.ForPositionAsync`); o centro de trabalho é informado para a capacidade (PLN-05) e sugere o cargo cujo centro padrão é ele (CAD-04a) | Igual; a PLN-05 usa as horas por centro |
| Instalação | Valor estimado digitado (instalação terceirizada), instalador opcional (papel Instalador) | OS de instalação e CP do instalador na COM-06 |
| Frete | Valor estimado digitado | — |
| Impostos | **Valor estimado digitado** (P-01 pendente) | Percentual por regime depois da P-01 (ver Pontos em aberto 2) |
| Outros | Valor digitado com descrição | — |

O custo unitário de cada linha é **gravado no orçamento** (snapshot): mudar depois o custo-hora do cargo ou o custo do item não altera orçamentos já feitos; "Atualizar custos" (CA 12) recalcula um rascunho com confirmação.

## Critérios de aceite
**Cabeçalho**
1. **Dado** `quotes.create`, **quando** clico "Novo orçamento", escolho o cliente e clico "Criar orçamento", **então** o orçamento nasce `Draft`, revisão 0, número `ORC-2026-0001` (`INumberSequence`, tipo `ORC`), e o detalhe abre na aba Itens; o contato vem com o contato de cobrança do cliente (`IParties.BillingContactAsync`) e o endereço da obra com o endereço padrão do tipo Obra (`DefaultAddressAsync(Site)`), editáveis (R-PRP-1).
2. **Dado** o cliente, **quando** escolho, **então** só aparecem terceiros ativos com o papel Cliente; um terceiro sem o papel mostra "Adicionar papel Cliente" (abre o terceiro, CAD-01a) e a API responde 422 "Escolha um terceiro com o papel Cliente."; contato e endereço precisam pertencer ao cliente (422 "O contato não pertence a este cliente.").
3. **Dado** o cabeçalho, **quando** edito prazo de entrega prometido (dias corridos após a aprovação, inteiro ≥ 0) e observações (até 2.000 caracteres), **então** grava com "Salvar orçamento" e o toast diz "Orçamento salvo".

**Itens e composição de custo**
4. **Dado** um orçamento `Draft`, **quando** clico "Adicionar item", escolho o tipo (Equipamento, Produto ou Serviço), opcionalmente um item do estoque (`IItems`; a descrição vem do nome do item), a descrição e a quantidade (`numeric(18,4)`, > 0), **então** o item aparece com custo zero e a composição vazia.
5. **Dado** a aba "Composição de custo" do item, **quando** adiciono linhas no `RowsEditor`: Material (item, quantidade, custo unitário), Mão de obra (centro de trabalho, cargo, horas), Instalação, Frete, Impostos e Outros (valor), **então** cada linha mostra o total (`quantidade × custo unitário`, ou `horas × custo-hora`) e o item mostra o custo unitário = soma das linhas para **uma unidade** do item (ver Pontos em aberto 4); tudo recalcula ao vivo, com o total em `aria-live`.
6. **Dado** uma linha de mão de obra, **quando** escolho o centro de trabalho, **então** o cargo vem com o cargo ativo cujo centro padrão é esse (se houver um só; senão fico na escolha) e o custo-hora vem de `IHourlyCost.ForPositionAsync(cargo)`, gravado na linha (`unit_cost`, `numeric(18,6)`); cargo inativo ou inexistente responde 422.
7. **[Segurança]** **Dado** qualquer linha de mão de obra, **quando** a API recebe um `employeeId` ou o front pede custo por funcionário, **então** não existe esse campo nem rota: o custo é sempre do cargo (CAD-04a CA 8); um teste de arquitetura impede o Commercial de referenciar `IEmployees` para custo.
8. **Dado** uma linha de material, **quando** escolho o item, **então** a quantidade é na unidade de estoque do item (mostrada ao lado) e o custo unitário vem com o último custo usado para esse item em outro orçamento do tenant, ou vazio; nunca vem de preço do Dolibarr (não migrou, ADR 0017).

**Preço e margem**
9. **Dado** o modo de precificação do tenant (`ITenantParameters.PricingModeAsync`) = Markup e o % padrão (parâmetro `pricing.default_pct`, proposta, ver Pontos em aberto 1), **quando** o item tem custo unitário R$ 1.000,00 e markup 40,0000%, **então** o preço unitário sugerido é R$ 1.400,00 (`round(custo × (1 + markup/100), 2, AwayFromZero)`); no modo Margem, com margem 40,0000%, é R$ 1.666,67 (`round(custo ÷ (1 − margem/100), 2)`); margem ≥ 100% responde 422.
10. **Dado** o preço unitário sugerido, **quando** digito outro preço, **então** o preço digitado vale (`price_overridden = true`) e o % do item é recalculado e exibido (markup ou margem, conforme o modo); "Voltar ao preço sugerido" desfaz.
11. **Dado** o orçamento, **quando** qualquer linha muda, **então** os totais do orçamento (custo total = Σ custo unitário × quantidade; preço total = Σ preço unitário × quantidade; margem % = (preço − custo) ÷ preço) são recalculados ao vivo no front e gravados pelo servidor com as mesmas fórmulas (`total_cost`, `total_price`, `margin_pct`), e a margem negativa aparece em crítico com o aviso âmbar "Preço abaixo do custo." (não bloqueia).
12. **Dado** um orçamento `Draft` cujos custos de mão de obra ficaram diferentes do custo-hora atual dos cargos, **quando** clico "Atualizar custos", **então** um diálogo mostra cada linha com o valor antigo e o novo e o impacto no custo total, e só grava ao confirmar "Atualizar custos"; preços digitados à mão não mudam.

**Ciclo nesta story**
13. **Dado** um orçamento `Draft` com ao menos um item com preço > 0, **quando** clico "Marcar como enviado" e confirmo, **então** ele passa a `Sent` com a data do envio; sem item, 422 "Inclua ao menos um item com preço."; o envio do PDF por e-mail é a COM-03.
14. **Dado** um orçamento `Sent`, **quando** tento editar itens, custos ou preços, **então** a API responde 409 "Orçamento enviado não é alterado. Crie uma nova revisão." (a nova revisão é a COM-02); o cabeçalho também fica só leitura.
15. **Dado** `quotes.delete` e um orçamento `Draft` de revisão 0 sem revisões, **quando** clico "Excluir orçamento" e confirmo "Excluir", **então** ele é excluído logicamente (o número não volta para a sequência); orçamento enviado responde 409 "Orçamento enviado não é excluído. Registre a perda." (rejeição na COM-01b).

**Permissões (3 camadas) e segurança**
16. **Dado** o módulo `quotes` não liberado, **quando** chamo os endpoints, **então** 403 `module_not_released` e o front mostra `ModuleNotReleased`; sem grupo que habilite, 403 `forbidden`; com `quotes.view` sem `create`/`edit`/`delete`, ações ocultas por `<Can>` e escrita com 403. `RequireModule` e `perm:` em todos os endpoints.
17. **[Segurança]** **Dado** as rotas `/quotes/{id}/items/{itemId}` e `/quotes/{id}/items/{itemId}/costs/{costId}`, **quando** o filho não pertence ao pai, **então** 404 (IDOR); cliente, contato, endereço, item, centro e cargo são validados pelos Contracts dos donos.
18. **Dado** duas pessoas editando o mesmo orçamento, **quando** a segunda salva uma versão antiga, **então** 409 "O orçamento mudou desde que você abriu. Recarregue." (`xmin`).
19. **Dado** um orçamento usado, **quando** o terceiro, o item ou o centro são excluídos no cadastro, **então** a exclusão é recusada pelos provedores de uso (`IPartyUsageProvider` "orçamentos", `IItemUsageProvider`, `IWorkCenterUsageProvider`), e o orçamento antigo continua mostrando o nome (resolução por id inclui inativos).
20. **Dado** qualquer alteração, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário e data.

## Regras de negócio e técnicas
- **Dinheiro** `numeric(18,2)` (totais, preço unitário, valores de instalação/frete/impostos/outros); **custo unitário** `numeric(18,6)` (linhas e `quote_items.unit_cost`); **quantidade e horas** `numeric(18,4)`; **percentuais** `numeric(7,4)`.
- **Arredondamento:** total da linha = `round(qtd × custo, 2)`; custo unitário do item = Σ totais das linhas (2 casas); preço = fórmula do modo, 2 casas, `AwayFromZero`.
- **Snapshot de custo** na linha (CA 6, 8); o cargo, o centro e o item são só ids (referência entre módulos sem FK, ADR 0006).
- **Sem efeito financeiro:** orçamento nunca cria título nem movimenta estoque (PRD §5.4).
- **Situação** nesta story: `Draft`, `Sent`; as demais na COM-01b, COM-02 e COM-04a.
- **Ids** Guid v7; **auditoria** `IAuditable` em orçamento, item e linha de custo; **numeração** `ORC`, a mesma em todas as revisões (COM-02); soft delete só no rascunho rev. 0.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `quotes.view`, `quotes.create`, `quotes.edit` (itens, custos, preço, marcar enviado), `quotes.delete`.
- **Parâmetro (proposta):** `pricing.default_pct` (`numeric(7,4)`) em `identity.tenant_settings`, lido por `ITenantParameters.DefaultPricingPctAsync` (proposta), editado em Configurações → Parâmetros ao lado do modo (CAD-06).
- **Eventos:** nenhum nesta story.
- **Tabelas:** `commercial.quotes` (como no 03; nesta story sem `payment_term` e sem `valid_until` obrigatório; mais `sent_at`, `pricing_mode` gravado no orçamento) ; `commercial.quote_items` (+ `sequence`, `markup_pct` ou `margin_pct` do item, `price_overridden`) ; `commercial.quote_item_costs` (+ `position_id`, `description`, `party_id` opcional para instalador) — colunas novas são **proposta**.

## Telas e estados
- **Comercial → Orçamentos** (`/app/orcamentos`): h1 "Pré-propostas com validade. Ao aprovar, o sistema gera o contrato, o projeto e as parcelas a receber." (protótipo); DataTable com orçamento (número e revisão), cliente e equipamento principal, situação (Rascunho neutro, Enviado info), custo, preço, margem (`tabular-nums`); filtros na URL.
- **Orçamento** (`/app/orcamentos/$id`), abas: Dados, Itens (com a composição de cada item em painel expansível), Histórico de alterações; as abas Condições (COM-01b), Revisões (COM-02) e Documentos (COM-03) entram depois.
- **Resumo fixo** no topo: custo total, preço total, margem e o modo ("Markup sobre o custo").
- **Verbos:** "Criar orçamento" / "Orçamento criado"; "Salvar orçamento" / "Orçamento salvo"; "Adicionar item"; "Atualizar custos" / "Custos atualizados"; "Marcar como enviado" / "Marcado como enviado"; "Excluir orçamento" / "Orçamento excluído".
- **Estados:** carregando; vazio da lista ("Crie o primeiro orçamento para um cliente." + "Novo orçamento"); vazio de itens ("Adicione o equipamento ou serviço que o cliente pediu." + "Adicionar item"); erro; modo leitura (sem `quotes.edit` ou orçamento enviado).
- **Propositivo:** contato e obra do cliente preenchidos; % padrão do tenant; cargo sugerido pelo centro; custo do material sugerido pelo último orçamento; "Duplicar item"; depois de criar, "Próximos passos": "Adicionar item" e "Definir condições de pagamento" (COM-01b).
- **400 px e teclado:** E2E do editor de composição de custo.
- **Ajuda:** `Ajuda/comercial-orcamentos.md` (`padrao: true`), `Ajuda/comercial-orcamento.md` com uma por aba (`dados`, `itens`, `historico`); `<Dica>` em "Markup", "Margem", "Custo-hora do cargo", "Custo unitário do item" e "Impostos estimados"; `Ajuda/configuracoes-parametros.md` ganha "% padrão de markup ou margem".

## Plano de testes
- **Unidade:** fórmulas de preço nos dois modos, arredondamento e margem (CA 9, 10, 11); custo unitário por unidade (CA 5); atualizar custos (CA 12).
- **Integração (Testcontainers):** criar com padrões do cliente (CA 1, 2); itens e linhas com validação pelos Contracts (CA 4–6, 8, 17); custo-hora pelo cargo e snapshot (CA 6); totais gravados iguais aos do front (CA 11); marcar enviado e bloqueio de edição (CA 13, 14); exclusão (CA 15); três camadas (CA 16); IDOR (CA 17); concorrência (CA 18); provedores de uso (CA 19); auditoria (CA 20).
- **Arquitetura:** Commercial só referencia `Parties`, `Inventory`, `Production`, `HR` e `Identity` pelos Contracts; sem uso de `IEmployees` para custo (CA 7).
- **E2E (Playwright):** comercial monta orçamento com dois itens e vê preço e margem ao vivo; editor a 400 px e só pelo teclado.

## Fora do escopo
- Condições por marco, validade, rejeição e cópia: COM-01b. Revisões e expiração: COM-02. PDF e e-mail: COM-03. Aprovação: COM-04a.
- BOM estimada vinda da Engenharia (ENG-02, Fase 3) e custo médio real (EST-01, Fase 3).
- Simulação de prazo pela capacidade: PLN-05 (Fase 4, D).

## Pontos em aberto
1. **% padrão de markup/margem** (P-14 diz "entra na COM-01"). **Recomendação:** parâmetro `pricing.default_pct` em Configurações → Parâmetros, com seed **vazio** (o primeiro orçamento pede o %) e Dica "Defina o % que a empresa costuma aplicar"; o PO pode indicar um valor de seed.
2. **Impostos estimados** (P-01 pendente com a contadora). **Recomendação:** no MVP, imposto como valor digitado por item (entra no custo, como os outros); depois da P-01, opção de % sobre o preço por regime (cálculo "por dentro"), em story própria.
3. **Mão de obra por cargo ou por centro.** O 03 tem só `work_center_id` na linha; o custo-hora é do cargo (CAD-04a). **Recomendação:** a linha guarda os dois (centro para a capacidade, cargo para o custo), com o cargo sugerido pelo centro padrão.
4. **Composição por unidade ou pelo total do item.** **Recomendação:** por unidade do item (o custo do item × quantidade dá o total), que é como o 03 separa `unit_cost` e `quantity`.
5. **Custo e margem como dado sensível.** Os docs não restringem. **Recomendação:** visíveis com `quotes.view` (quem orça precisa ver); o PDF nunca mostra custo nem margem (COM-03); ação específica `quotes.view_cost` só se o PO pedir (ADR 0003 exige necessidade real).
6. **P-18:** ver FIN-01, ponto 5.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (schema `commercial`, não destrutiva); parâmetro com seed idempotente.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio, erro e modo leitura.
- [ ] Ajuda da lista e de cada aba; teste de cobertura passando.
- [ ] Padrões preenchidos e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Entidades `Quote`, `QuoteItem`, `QuoteItemCost` e `PriceCalculator` (Domain, puro).
- [ ] `ICommercialDb`; provedores de uso para Parties, Inventory e Production.
- [ ] `ITenantParameters.DefaultPricingPctAsync` (Identity) e campo em Configurações → Parâmetros.
- [ ] Endpoints `/api/v1/quotes` (+ itens, custos, `/send-mark`, `/refresh-costs`); telas e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, design (editor de composição).
- Gate: CA 6, 7, 9 e 11 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 19/10/2026.

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

- **Correções feitas na validação (19/10/2026):** quebra da COM-01 (G+) em 01a e 01b; tabela "De onde vem cada componente do custo"; custo do material digitado nesta fase (o custo médio só existe na EST-01) com contrato futuro marcado como proposta; imposto digitado enquanto a P-01 está pendente; mão de obra só por cargo com teste de arquitetura (CA 7); "Marcar como enviado" para a COM-04a não depender da COM-03; o parâmetro novo entra na tela e na ajuda de Configurações → Parâmetros (mudou a tela, muda a ajuda).
- **Pré-condições para `Ready`:** Pontos em aberto 1 a 5 (com padrão recomendado), P-01 (ao menos aceitar o imposto digitado) e P-18.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada (quebra da COM-01) | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
