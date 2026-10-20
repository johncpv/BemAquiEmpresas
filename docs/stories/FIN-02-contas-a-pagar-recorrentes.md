# FIN-02 — Contas a pagar e despesas recorrentes

| Campo | Valor |
|---|---|
| ID | FIN-02 |
| Título | Títulos a pagar manuais e parcelados, com origem, forma de pagamento (Pix, boleto, TED), campos de retenção e trava enquanto estão em ordem de pagamento; contrato para outros módulos criarem títulos (instalador, compra, folha); despesas recorrentes geradas por job sem duplicar |
| Tamanho | M |
| Status | Ready |
| Requisitos | R-FIN-4 (CP: compras, folha, instaladores, recorrentes, manual) · R-FIN-6 (despesas recorrentes por regra) · R-FIN-5 (base da OP: o aprovador vê para onde vai o dinheiro) · R-FIN-1 · R-COM-4 (numeração CP) · R-PRP-1 · R-SEC-5 |
| Dependências | FIN-01 (molde de título, provedores de uso, parcelamento, parâmetros do Finance, relatório de descontos e cancelamentos), FND-09e, CAD-05a, CAD-05b, CAD-01a, CAD-06; Fase 0 (FND-07 `TenantJob`/recorrente global, FND-09c, FND-14) |
| Bloqueia | FIN-00 (implantação), FIN-05, FIN-03a (OP agrupa títulos a pagar), COM-06 (CP do instalador), CMP-03/CMP-04 (previsão e confirmação do CP), RH-02 (CP de salários), PAT-03 |
| Ordem de execução | 2 (Marco 2.1a "Ver o caixa") |

## User story
Como **financeiro**, quero **lançar as contas a pagar, inclusive as fixas que se repetem todo mês, já com fornecedor, conta, projeto e forma de pagamento**, para **ver tudo o que vai sair do caixa e preparar as ordens de pagamento sem esquecer nenhuma despesa**.

## Contexto e referências
- PRD R-FIN-4, R-FIN-6; `04-fluxos-estados` §1 "Título a pagar e Ordem de Pagamento" (`Planned → Open → AwaitingApproval → Approved → Paid`) e a regra dura "nenhum pagamento é baixado sem OP aprovada".
- `03-modelo-dados` → `finance.payables`, `finance.recurring_expenses`.
- Revisão pré-Fase 2: D8 (limite de cancelamento), D9 (forma de pagamento), §2.1 (A2 idempotência, A3 título órfão, M2 `paid_amount` sobre o líquido, M8 jobs, M10 vocabulário, M13 CP estimado), §2.2-C1 (título travado em OP), §2.2-M8 (retenção ≥ 0), §2.3.
- Revisão pré-Fase 0 §2.1: "o status do Payable passa a ser derivado da OP" (aplicado na FIN-03a).
- P-15 (decidida): retenções ISS/INSS/IR sobre serviço tomado são **opcionais**; o título tem campos de retenção, preenchidos quando aplicável, **sem cálculo automático** no MVP.
- ADR 0006 (job recorrente global que enfileira por tenant, `TenantJob<T>`, fuso America/Sao_Paulo); ADR 0019.
- Padrões comuns da Fase 2 (`docs/stories/README.md`).

## Critérios de aceite
**Título manual**
1. **Dado** `finance.create`, **quando** abro "Novo título a pagar", informo fornecedor, descrição, conta de despesa, valor e vencimento e clico "Salvar título a pagar", **então** o título é gravado `Open`, origem `Manual`, número `CP-2026-00001`, a mensagem de sucesso diz "Título a pagar salvo" e a faixa "Próximos passos" oferece "Incluir em ordem de pagamento" (FIN-03a, quando existir) e "Novo título para este fornecedor"; o mesmo envio repetido com o mesmo `Idempotency-Key` não cria outro título (ADR 0019 §3).
2. **Dado** o formulário, **quando** escolho o terceiro, **então** só aparecem terceiros ativos com papel Fornecedor ou Instalador (422 "Escolha um fornecedor ou instalador."); a conta só pode ser analítica ativa do tipo Despesa, Ativo (investimentos, "4.1") ou Passivo (`IChartOfAccounts`); centro de custo é opcional; os campos de projeto e de natureza do custo ficam ocultos até a COM-04a, que os cria (seção 1.1, item 26).
3. **Dado** "Parcelar pela condição", **quando** escolho a condição (padrão: a do fornecedor), **então** a prévia e a gravação seguem `IPaymentTerms.SplitAsync`, com o mesmo agrupamento e descrição "(n/N)" da FIN-01 (CA 3), e com idempotência.
4. **Dado** os campos de retenção (ISS, INSS, IR, PIS/COFINS/CSLL), **quando** informo valores, **então** são gravados em `numeric(18,2)`, cada um ≥ 0 (422 "A retenção não pode ser negativa."), a tela mostra "Valor líquido a pagar = valor − retenções" e a OP (FIN-03a) paga o líquido; não há cálculo automático (P-15) e a soma das retenções maior ou igual ao valor responde 422.
5. **Dado** valor ≤ 0 ou terceiro sem papel, **quando** salvo, **então** 422 com mensagem PT-BR no campo.

**Forma de pagamento (D9)**
6. **Dado** a seção "Forma de pagamento" do título, **quando** escolho o tipo, **então** os campos são: **Pix** (tipo de chave, chave validada pelo tipo, nome e CPF/CNPJ do titular), **Boleto** (linha digitável de 47 dígitos, com os dígitos verificadores conferidos, ou de 48 para concessionária; o valor e o vencimento lidos da linha são comparados com o título e a diferença vira aviso âmbar), **TED** (banco pelo código COMPE, agência, conta, tipo, nome e CPF/CNPJ do titular) ou **Outro** (descrição obrigatória, ex.: "Débito em conta"); a forma é opcional no título e obrigatória para entrar numa OP (FIN-03a).
7. **[Segurança]** **Dado** uma forma de pagamento cujo CPF/CNPJ do titular é diferente do documento do fornecedor, **quando** salvo, **então** o título grava e mostra o aviso âmbar "O titular ({documento mascarado}) não é o fornecedor."; o aviso volta na OP, ao aprovar e ao pagar (FIN-03a). Toda mudança da forma de pagamento grava `payment_method_changed_at`, é auditada (com os dados mascarados no diff) e alimenta o aviso "Dados de pagamento alterados há {n} dias" da OP.
8. **[Segurança]** **Dado** os dados da forma de pagamento (chave Pix, conta, documento do titular), **quando** saem pela API, **então** os campos são `[PersonalData]`, a resposta leva `Cache-Control: no-store` e a lista mostra só o tipo ("Pix", "Boleto", "TED").

**Situação, trava e regra dura**
9. **Dado** um título `Open` com vencimento anterior a hoje, **quando** consulto, **então** aparece "Vencido" (calculado, como na FIN-01 CA 6), sem gravar.
10. **[Segurança]** **Dado** qualquer título a pagar, **quando** tento dar baixa por esta tela ou pela API de títulos, **então** não existe rota de baixa: o pagamento só acontece pela OP aprovada (FIN-03a); um teste de arquitetura garante que só o caso de uso de pagamento da OP cria transação `Payment`.
11. **[Segurança]** **Dado** um título numa OP não finalizada (`AwaitingApproval` ou `Approved`), **quando** tento mudar fornecedor, valor, retenções, forma de pagamento, conta, centro de custo, projeto ou natureza do custo, **então** a API responde 409 `payable_in_payment_order` "O título está na {OP}. Retire-o da ordem antes." (§2.2-C1); descrição e vencimento continuam editáveis.
12. **Dado** `finance.edit` e um título `Open` fora de OP, **quando** clico "Cancelar título", informo o motivo e confirmo "Cancelar título", **então** ele fica `Cancelled`, os membros do Proprietário são notificados ("Título a pagar {número} de {valor} cancelado por {usuário}: {motivo}") e o cancelamento entra no relatório "Descontos e cancelamentos" (FIN-01 CA 22); título em OP responde 409 "O título está na {OP}. Retire-o da ordem antes."; acima de `finance.cancel_approval_amount` só com `finance.approve` (como na FIN-01 CA 18); não há exclusão.
13. **Dado** um título de origem diferente de `Manual`, `Recurring` e `Opening` (ex.: `Installer`, COM-06), **quando** tento mudar valor, fornecedor ou cancelar por esta tela, **então** 409 "Título gerado por {origem} {número}: ajuste na origem."; descrição, conta, centro de custo, vencimento e forma de pagamento continuam editáveis (fora de OP); quando a origem já está `Cancelled` (consultada pelo Contracts do dono), o cancelamento é liberado com `finance.edit` e motivo, com a marca "Origem cancelada" (ADR 0019 §4).

**Contrato para os outros módulos**
14. **Dado** um módulo que gera conta a pagar (Production na COM-06; Purchasing e HR depois), **quando** chama `IPayables.CreateFromOriginAsync(origin, originId, partyId, projectId, costType, amount, dueDate, estimatedDueDate, description, planned)` de `Finance.Contracts`, **então** o título nasce na mesma transação do chamador, com `Planned` (sem vencimento ou com data estimada e a marca `is_estimate`, ADR 0019 §17) ou `Open`, e a mesma chamada repetida com o mesmo `(origin, originId)` devolve o título existente (idempotente, índice único).
15. **Dado** um título `Planned` de origem, **quando** a origem chama `IPayables.ConfirmAsync(origin, originId, amount, dueDate)`, **então** ele passa a `Open` com o valor e o vencimento confirmados e sem a marca de estimativa; `IPayables.UpdatePlannedAsync` muda o valor de um título `Planned`; `IPayables.CancelFromOriginAsync` cancela um título `Planned` ou `Open` fora de OP; título já em OP responde erro `payable_in_payment_order` nas três.

**Despesas recorrentes**
16. **Dado** `finance.create`, **quando** cadastro uma recorrente com descrição, fornecedor, conta, centro de custo, valor, forma de pagamento (opcional, copiada para os títulos), dia do vencimento (1 a 31), início e fim opcional e clico "Salvar despesa recorrente", **então** ela é gravada e a tela mostra as próximas 3 ocorrências previstas.
17. **Dado** a recorrente "Aluguel" com dia 10 e antecedência de geração de 10 dias (padrão `lead_days`), **quando** o job diário roda em 30/11/2026, **então** é criado um título `Open`, origem `Recurring`, competência 12/2026, vencimento 10/12/2026, descrição "Aluguel — 12/2026"; rodando de novo no mesmo dia ou em paralelo, **não** cria outro (índice único em `(recurring_expense_id, competence)`), e um tenant não gera título no outro (teste de dois tenants).
18. **Dado** o dia 31 numa competência de 30 dias ou fevereiro, **quando** o job gera, **então** o vencimento é o último dia do mês.
19. **Dado** o job parado por 3 dias, **quando** volta, **então** gera as competências que ficaram para trás, uma vez cada, e registra o atraso no log; um tenant suspenso não roda (`TenantJob`).
20. **Dado** uma recorrente com títulos gerados, **quando** tento excluí-la, **então** 409 "A despesa já gerou títulos. Encerre-a informando o fim." e o diálogo oferece "Encerrar"; mudar o valor vale só para as competências ainda não geradas.

**Permissões (3 camadas)**
21. **Dado** o módulo `finance` não liberado, **quando** chamo os endpoints, **então** 403 `module_not_released`; sem grupo que habilite, 403 `forbidden`; com `finance.view` sem `create`/`edit`/`delete`, ações ocultas e escrita com 403. Todo endpoint tem `RequireModule` e `perm:`; IDOR pai/filho conferido; ids de outro módulo validados pelo Contracts do dono.
22. **Dado** qualquer alteração de título ou recorrente, **quando** abro "Histórico de alterações", **então** vejo antes/depois (dados de pagamento mascarados), usuário e data.

## Regras de negócio e técnicas
- **Dinheiro** `numeric(18,2)`; **datas** `date` (vencimento, competência como o primeiro dia do mês); job em America/Sao_Paulo.
- **Situação gravada:** `Planned`, `Open`, `AwaitingApproval`, `Approved`, `PartiallyPaid`, `Paid`, `Cancelled`. Nesta story só existem `Planned`, `Open` e `Cancelled`; as demais são gravadas pela OP (FIN-03a), nunca pela tela do título.
- **Valor líquido** = `amount − Σ retenções`; a OP paga o líquido; `paid_amount` é medido contra o líquido (ADR 0019 §8); saldo = líquido − `paid_amount`.
- **Origem** (`DocumentOrigin` no SharedKernel): `Manual`, `Opening` (FIN-00), `Recurring`, `Installer`, `Purchase`, `Payroll`, `Maintenance`; `(origin, origin_id)` único quando `origin_id` não é nulo.
- **Forma de pagamento:** colunas `payment_method` (`Pix` | `Boleto` | `Ted` | `Other`), `payment_details` (jsonb com os campos do tipo, `[PersonalData]`), `payment_method_changed_at`. A linha digitável é validada pelo módulo 10/11 da FEBRABAN.
- **Recorrente:** `recurring_expenses` + `lead_days` (padrão 10), `is_active`, forma de pagamento; o job é um recorrente global diário às 06:00 de São Paulo, registrado no `RecurringJobsRegistrar`, que enfileira `GenerateRecurringPayablesJob` por tenant ativo, com argumentos só de ids (ADR 0019 §12).
- **Concorrência** pela entidade rastreada com `xmin` (ADR 0019 §2); **lista** com nomes por página (`IParties.FindManyAsync`); índice parcial `(status, due_date)`.
- **Ids** Guid v7; **auditoria** `IAuditable`; **numeração** `CP`; soft delete só em recorrente sem título.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `finance.view`, `finance.create`, `finance.edit` (editar, cancelar, encerrar recorrente), `finance.delete` (excluir recorrente sem título), `finance.approve` (cancelamento acima do limite).
- **Eventos:** nenhum novo nesta story. O contrato `IPayables` é chamada síncrona via `Finance.Contracts` (ADR 0006: compartilha a transação do chamador).
- **Tabelas:** `finance.payables` (como no 03, mais `retention_iss`, `retention_inss`, `retention_ir`, `retention_pcc`, `issue_date`, `recurring_expense_id`, `competence`, `installment_group_id`, `payment_method`, `payment_details`, `payment_method_changed_at`, `is_estimate`, `cancel_reason`, `cancelled_at`, `cancelled_by`, `client_request_id`; `due_date` anulável para `Planned`; `account_id` = conta do plano de contas; `project_id` e `cost_type` entram na COM-04a); `finance.recurring_expenses` (como no 03, mais `lead_days`, `is_active`, `payment_method`, `payment_details`, `end` anulável).

## Telas e estados
- **Financeiro → A pagar** (`/app/financeiro/pagar`; grupo "Caixa e pagamentos"): h1 "O que a empresa precisa pagar, por vencimento."; DataTable com número (link), fornecedor, descrição, origem (`Tag`), forma de pagamento (tipo), projeto, vencimento, valor líquido, situação (Previsto neutro, Estimado com `Tag`, Em aberto info, Vence hoje alerta, Vencido crítico com faixa vermelha, Em OP info, Aprovado ok, Pago ok, Cancelado neutro); seleção múltipla (`DataTable` com `selectable`) com "Criar ordem de pagamento" (visível quando a FIN-03a existir); rodapé de totais do filtro; filtros na URL.
- **Novo título a pagar** (`/app/financeiro/pagar/novo`) e **Título a pagar** (`/app/financeiro/pagar/$id`, cabeçalho com número, chip e fornecedor; abas Dados, Forma de pagamento, Retenções, Histórico de alterações; faixa de modo leitura quando o título está em OP: "Este título está na {OP}. Para mudar valor ou dados de pagamento, retire-o da ordem.").
- **Financeiro → Recorrentes** (`/app/financeiro/recorrentes`; grupo "Configuração financeira"): lista com próxima geração e painel lateral de edição.
- **Estados:** carregando; vazio ("Lance a primeira conta a pagar ou cadastre as despesas fixas do mês." + "Novo título a pagar" e "Nova despesa recorrente"); vazio filtrado; erro; modo leitura.
- **Propositivo:** condição padrão do fornecedor, conta, centro de custo e forma de pagamento do último título do mesmo fornecedor (R-PRP-1); "Duplicar" título; depois de cadastrar a recorrente, mostra as próximas ocorrências; título vencido oferece "Incluir em ordem de pagamento".
- **400 px:** colunas sempre visíveis: número, fornecedor, vencimento, valor líquido e situação; descrição, origem, forma e projeto na linha expansível.
- **Ajuda:** `Ajuda/financeiro-pagar.md` (`padrao: true`), `Ajuda/financeiro-pagar-novo.md`, `Ajuda/financeiro-pagar-titulo.md` (abas `dados`, `forma-de-pagamento`, `retencoes`, `historico`), `Ajuda/financeiro-recorrentes.md`; a ajuda `financeiro-descontos-cancelamentos.md` ganha os títulos a pagar; `<Dica>` em "Retenções" ("Preencha quando a nota do prestador destacar retenção. O sistema não calcula."), "Forma de pagamento", "Titular" e "Antecedência de geração".

## Plano de testes
- **Unidade:** valor líquido e retenções (CA 4); linha digitável e chave Pix (CA 6); titular diferente (CA 7); dia 31 e fevereiro (CA 18); competências atrasadas (CA 19).
- **Integração (Testcontainers):** criar e parcelar com idempotência (CA 1–3); forma de pagamento com `no-store` e diff mascarado (CA 6–8, 22); trava em OP com OP simulada (CA 11); cancelar dentro e fora de OP, com notificação e limite (CA 12); título de origem protegido e origem cancelada (CA 13); `IPayables` idempotente e na transação do chamador, com rollback do chamador desfazendo o título, estimativa, confirmação e cancelamento (CA 14, 15); job sem duplicar, com duas execuções paralelas e dois tenants (CA 17); tenant suspenso (CA 19); recorrente com títulos não excluída (CA 20); três camadas (CA 21); auditoria (CA 22).
- **Arquitetura:** só a OP cria transação `Payment` (CA 10); job herda de `TenantJob` e está no `RecurringJobsRegistrar`; Finance não referencia módulos fora dos Contracts.
- **E2E (Playwright):** cadastrar recorrente e título manual com retenção e Pix; lista a 400 px.

## Fora do escopo
- Ordem de pagamento, aprovação e pagamento: FIN-03a. Alçadas: FIN-03b. Títulos de implantação: FIN-00.
- Campo de projeto e natureza do custo no título: COM-04a. CP do instalador a partir da OS de instalação: COM-06. CP de compras: CMP-03/CMP-04. CP de folha: RH-02.
- Cálculo automático de retenções (P-15). Pagamento pela API do banco: fora do MVP.

## Pontos em aberto
Decididos pelo PO em 20/10/2026 (seção 1.1 e D8, D9):
1. Recorrente gerada com antecedência (`lead_days`, padrão 10 dias); as ocorrências ainda não geradas entram no fluxo como previsão (FIN-05).
2. Campo de retenção PCC (PIS/COFINS/CSLL), opcional, sem cálculo; a sugestão "Gerar título do recolhimento" ao pagar fica na FIN-03a, sem decidir pelo usuário.
3. Forma de pagamento no título (D9), congelada na OP.
4. P-18 decidida (ADR 0021).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`; job registrado no `RecurringJobsRegistrar`.
- [ ] Telas com estados de carregando, vazio, vazio filtrado e erro; modo leitura.
- [ ] Ajuda das telas e abas; teste de cobertura passando.
- [ ] Padrões comuns da Fase 2 e próximos passos aplicados.
- [ ] Auditoria ligada (com dados de pagamento mascarados).
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Entidades `Payable` e `RecurringExpense`; `PaymentMethod` e o validador de linha digitável e chave Pix.
- [ ] `IPayables` (`CreateFromOriginAsync`, `ConfirmAsync`, `UpdatePlannedAsync`, `CancelFromOriginAsync`, `ByProjectAsync`) em `Finance.Contracts`.
- [ ] `GenerateRecurringPayablesJob` (`TenantJob`) e o recorrente global.
- [ ] Endpoints `/api/v1/finance/payables` e `/api/v1/finance/recurring-expenses`; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, cybersecurity (forma de pagamento e trava em OP).
- Gate: CA 7, 10, 11, 14 e 17 obrigatórios.

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

- **Correções feitas na validação (19/10/2026):** campo de projeto oculto até a COM-04a; `IPayables` idempotente por origem; regra dura "sem baixa fora da OP" com teste de arquitetura; P-15 aplicada; conta de Ativo aceita.
- **Correções da revalidação (20/10/2026):** forma de pagamento com titular e avisos (D9, CA 6–8); título travado em OP (§2.2-C1, CA 11); cancelamento com notificação, limite e relatório (D8, §2.2-A5, CA 12); origem cancelada (CA 13); retenção ≥ 0 (CA 4); `IPayables` com natureza do custo, data estimada (M13), atualização e cancelamento (CA 14, 15); `paid_amount` sobre o líquido; jobs com teste de dois tenants e `RecurringJobsRegistrar`; projeto e natureza criados na COM-04a (item 26); idempotência; navegação (D12); colunas a 400 px. CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 20/10/2026 | 0.3 | Decisões do PO (D8, D9, seção 1.1) e correções da revisão pré-Fase 2 (§2.1, §2.2-C1, A4, A5, M8, §2.3); CAs renumerados | River (SM) |
| 20/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
