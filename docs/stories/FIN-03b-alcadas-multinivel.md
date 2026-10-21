# FIN-03b — Alçadas de aprovação configuráveis, multinível e aprovação pelo link do e-mail

| Campo | Valor |
|---|---|
| ID | FIN-03b |
| Título | Regras de alçada da ordem de pagamento por faixa de valor e por conta (com teto), com aprovador por grupo ou usuário e níveis; a regra mais específica vence; proteção contra fracionamento; alçada viável; todas as aprovações do nível N antes do N+1; e-mail ao aprovador com link autenticado para a OP |
| Tamanho | M (quebra da FIN-03) |
| Status | Done |
| Requisitos | R-FIN-5 (alçadas configuráveis por valor/categoria; aprovação no sistema e por link no e-mail, autenticado) · R-COM-1 · R-COM-2 · R-PRP-5 · R-SEC-5 |
| Dependências | FIN-03a (OP, `IApprovalPolicy`, decisões, retrato), FND-08 (e-mail), FND-06b (grupos), FND-09e |
| Bloqueia | CMP-03 (alçada do pedido de compra reaproveita o motor) |
| Ordem de execução | 19 (Marco 2.4 "Controles e D"; fica no fim da fase pela D5: o padrão "Proprietário aprova tudo" já funciona desde a FIN-03a) |

## User story
Como **proprietário**, quero **definir quem aprova cada faixa de valor ou tipo de despesa, em um ou mais níveis, e receber por e-mail o pedido de aprovação**, para **delegar o miúdo sem perder o controle do que é grande**.

## Contexto e referências
- PRD R-FIN-5 ("alçadas configuráveis por valor/categoria (padrão: proprietário aprova tudo)"; "aprovação/reprovação com comentário, no sistema e por link no e-mail (autenticado)").
- `04-fluxos-estados` §3.6 (regra mais específica vence: categoria/conta > faixa de valor > padrão; multinível: todas as aprovações do nível N antes de liberar o N+1); §3.7 (`finance.approve` não dispensa a alçada).
- `03-modelo-dados` → `approval_rules` (`document_type`, `min_amount`, `max_amount`, `account_id`, aprovador, `level`); revisão pré-Fase 0 §2.1 (`approver_role_id` vira `approver_group_id`/`user_id`).
- Revisão pré-Fase 2: D5 (`finance.approve` só no Proprietário na Fase 2; a delegação a outro grupo fica para quando houver mais de um aprovador: esta story entrega o motor, e a concessão de `finance.approve` a outro grupo continua sendo um ato do Proprietário, nunca do seed), §2.1-A1 (alçada viável), §2.2-A2 (fracionamento), §2.2-A3 (regra por conta com teto), §2.2 baixos (e-mail só com número, total e link), §2.1 baixos (`EXCLUDE USING gist`).
- Riscos (`06`): "Aprovador único vira gargalo" → mitigação "delegação temporária / aprovador substituto" (fora do MVP).
- ADR 0008 (`*.approve` reservado ao Proprietário, adendo D5), ADR 0019 §6.

## Critérios de aceite
**Regras**
1. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed roda, **então** existe a regra padrão "Proprietário aprova tudo" (`PaymentOrder`, faixa de R$ 0,00 sem teto, grupo Proprietário, nível 1, `seed_key`), sem recriar se o tenant a excluiu; com ela, o comportamento é igual ao da FIN-03a.
2. **Dado** um membro do Proprietário, **quando** cadastro uma regra com tipo de documento (só Ordem de pagamento nesta fase), faixa de valor (mínimo e máximo opcional, `numeric(18,2)`), conta do plano opcional (analítica de Despesa) — com **teto obrigatório** quando há conta —, aprovador (um grupo **ou** um usuário ativo) e nível (1 a 3) e clico "Salvar regra de alçada", **então** a regra é gravada, auditada, e os membros do Proprietário são notificados ("Regra de alçada alterada por {usuário}").
3. **[Segurança]** **Dado** um usuário que não é do Proprietário, mesmo com `finance.approve`, **quando** tenta criar, editar ou excluir regra, **então** 403 `forbidden`; o grupo ou usuário aprovador precisa ter `finance.approve` na permissão efetiva, senão 422 "O aprovador escolhido não tem a permissão Aprovar em Caixa e pagamentos."
4. **Dado** faixas sobrepostas no mesmo nível e na mesma conta (ou ambas sem conta), **quando** salvo, **então** 422 "A faixa se sobrepõe à regra {descrição}." (conferido no serviço e garantido no banco por `EXCLUDE USING gist`).
5. **[Segurança]** **Dado** o mesmo grupo ou usuário em níveis diferentes com faixas que se sobrepõem, **quando** salvo, **então** 422 "O mesmo aprovador não pode decidir dois níveis da mesma faixa." (ADR 0019 §6).

**Resolução da alçada**
6. **Dado** as regras de teste: A (faixa 0–5.000, grupo de teste "Financeiro aprovador" com `finance.approve` concedido pelo Proprietário, nível 1), B (acima de 5.000, Proprietário, nível 1), C (conta "3.2 Ocupação", até 10.000, mesmo grupo de A, nível 1) e D (acima de 20.000, Proprietário, nível 2), **quando** a OP é enviada, **então**: OP de R$ 3.000,00 → nível 1 por A; R$ 8.000,00 → nível 1 por B; R$ 8.000,00 só com títulos da conta 3.2 → nível 1 por C (conta vence faixa); R$ 12.000,00 só com títulos da conta 3.2 → nível 1 por B (acima do teto de C, vale a faixa); R$ 25.000,00 → nível 1 por B e nível 2 por D; a resolução é gravada na OP (`payment_order_levels`) no envio. (No seed, `finance.approve` continua só no Proprietário, D5.)
7. **Dado** uma OP com títulos de contas diferentes, **quando** é enviada, **então** a regra por conta só se aplica se todos os títulos forem da mesma conta; senão vale a faixa de valor.
8. **[Segurança]** **Dado** a OP enviada, **quando** a faixa é resolvida, **então** o **valor de referência** é o maior entre: o total da OP, o maior valor original entre os títulos dela e a soma das OPs não reprovadas nem canceladas do mesmo fornecedor nos últimos 30 dias, incluindo esta (§2.2-A2); quando a soma muda a faixa, a OP mostra ao aprovador o aviso "Possível fracionamento: {n} ordens para {fornecedor} em 30 dias, {total}." (teste: 4 OPs de R$ 5.000,00 do mesmo fornecedor; a partir da segunda, a faixa é a de B).
9. **[Segurança]** **Dado** uma OP a enviar, **quando** algum nível resolvido não tem aprovador elegível (com `finance.approve` efetivo, diferente do solicitante e de quem está nos outros níveis), **então** 422 com o motivo ("O nível 2 não tem aprovador diferente do nível 1.") e a OP não é enviada (ADR 0019 §6; teste "um Proprietário só" com duas regras de nível, que só passa pela exceção da D6 quando o único aprovador é o próprio solicitante no nível 1).
10. **Dado** uma regra alterada depois do envio, **quando** a OP pendente é decidida, **então** vale a resolução gravada no envio; "Reenviar para aprovação" (solicitante, com motivo) recalcula.
11. **[Segurança]** **Dado** um título com saldo acima do teto da regra por conta, **quando** alguém troca a conta dele (FIN-02), **então** os membros do Proprietário são notificados ("Conta do título {número} trocada de {antes} para {depois} por {usuário}.") (§2.2-A3).

**Multinível**
12. **Dado** a OP de R$ 25.000,00 do CA 6, **quando** um aprovador do nível 1 aprova, **então** a OP continua `Pending`, a decisão fica registrada com nível 1 e os aprovadores do nível 2 são notificados; só depois da aprovação do nível 2 ela passa a `Approved`.
13. **Dado** um aprovador que já decidiu um nível, **quando** tenta decidir o nível seguinte, **então** 403 "Você já aprovou o nível {n} desta ordem."; a regra do solicitante (FIN-03a CA 9) vale em todos os níveis.
14. **Dado** uma reprovação em qualquer nível, **quando** gravada, **então** a OP passa a `Rejected` e os títulos voltam a `Open` (FIN-03a CA 8).
15. **[Segurança]** **Dado** um usuário com `finance.approve` que não está no grupo (ou não é o usuário) da regra do nível pendente, **quando** tenta aprovar, **então** 403 `outside_approval_rule` "Esta ordem não está na sua alçada."; a lista "Aguardando você" mostra só as OPs da alçada dele.

**E-mail**
16. **[Segurança]** **Dado** a OP enviada ou liberada para um novo nível, **quando** grava, **então** cada aprovador do nível recebe, pela fila, o e-mail `payment_order_pending` **só com** o número da OP, o total e o botão "Abrir ordem de pagamento" com o link `https://{host primário}/app/financeiro/ordens-de-pagamento/{id}` (FND-08); fornecedores, títulos e dados de pagamento ficam só na tela.
17. **[Segurança]** **Dado** o link do e-mail, **quando** é aberto sem sessão, **então** leva ao login (com 2FA quando exigido) e volta para a OP; o link não carrega token, não aprova sozinho e não mostra dado da OP antes do login; quem não está na alçada vê a OP em modo leitura (com `finance.view`) ou 403.

**Permissões e auditoria**
18. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; `finance.view` vê as regras em modo leitura. `RequireModule` e `perm:`; IDOR em `/approval-rules/{id}`.
19. **Dado** qualquer alteração de regra ou decisão, **quando** abro o histórico, **então** vejo antes/depois, usuário e data; a OP mostra, por nível, a regra aplicada, o valor de referência e quem decidiu.

## Regras de negócio e técnicas
- **`approval_rules`** (03, com a correção da revisão): `document_type` (`PaymentOrder`; `PurchaseOrder` reservado para a CMP-03), `min_amount`, `max_amount` (nulo = sem teto; obrigatório quando há `account_id`), `account_id` (nulo = qualquer), `approver_group_id` **ou** `approver_user_id` (exatamente um), `level` (1–3), `is_active`, `seed_key`; `EXCLUDE USING gist (document_type WITH =, level WITH =, coalesce(account_id) WITH =, numrange(min_amount, max_amount) WITH &&) WHERE is_active`.
- **Resolução** por nível: regras com conta (todos os títulos na conta e valor de referência ≤ teto) > regras por faixa > padrão; a faixa compara o **valor de referência** (CA 8); uma regra resolvida por nível.
- **`payment_order_levels`**: `payment_order_id`, `level`, `approval_rule_id`, `reference_amount`, `split_warning`, `status` (Pending/Approved/Rejected), para gravar a resolução no envio.
- O `IApprovalPolicy` da FIN-03a passa a usar as regras, sem mudar os casos de uso de aprovar e reprovar.
- Membros de grupo resolvidos pelo Identity (`IUserDirectory.GroupMembersAsync`); dinheiro `numeric(18,2)`; auditoria `IAuditable`.

## Permissões, eventos e tabelas
- **Permissões:** `finance.view` (ver regras), membro do Proprietário (editar regras), `finance.approve` + alçada (decidir).
- **Eventos:** `PaymentOrderLevelReleased` (`{ paymentOrderId, level }`) → e-mail e sino aos aprovadores do nível.
- **Tabelas:** `finance.approval_rules` (com `approver_group_id`/`approver_user_id`, `is_active`, `seed_key` e a restrição de exclusão); `finance.payment_order_levels` (nova); template de e-mail `payment_order_pending`.

## Telas e estados
- **Financeiro → Alçadas** (`/app/financeiro/alcadas`; grupo "Configuração financeira"): h1 "Quem aprova cada ordem de pagamento."; lista por nível com faixa, conta e teto, aprovador; painel lateral de edição com as pré-condições (aprovador sem permissão, sobreposição) antes de salvar; simulador "Quem aprovaria uma OP de R$ {valor} na conta {conta} para {fornecedor}?" (propositivo, mostra o valor de referência).
- **OP (FIN-03a):** a aba Decisões mostra os níveis, a regra, o valor de referência, o aviso de fracionamento e a situação de cada um.
- **Estados:** carregando; só a regra padrão ("Hoje o Proprietário aprova tudo. Crie faixas para delegar quando houver outro aprovador."); erro; modo leitura.
- **400 px:** lista com nível, faixa e aprovador sempre visíveis; conta na linha expansível.
- **Ajuda:** `Ajuda/financeiro-alcadas.md`; a ajuda da OP ganha "Níveis de aprovação" e "Possível fracionamento"; `<Dica>` em "Nível", "Teto da regra por conta" e "Regra mais específica".

## Plano de testes
- **Unidade:** resolução das regras (CA 6, 7) com tabela de casos; valor de referência e fracionamento (CA 8); sobreposição e mesmo aprovador (CA 4, 5); viabilidade (CA 9).
- **Integração (Testcontainers, obrigatória):** seed idempotente (CA 1); edição só pelo Proprietário e aprovador com permissão (CA 2, 3); restrição de exclusão no banco (CA 4); resolução gravada no envio e estável (CA 6–10); 4 × R$ 5.000,00 (CA 8); um Proprietário só (CA 9); troca de conta notificada (CA 11); multinível e notificações (CA 12); mesmo aprovador em dois níveis (CA 13); reprovação (CA 14); fora da alçada (CA 15); e-mail só com número, total e link (CA 16, 17); três camadas (CA 18); auditoria (CA 19).
- **E2E (Playwright):** criar faixa para um grupo de teste, enviar OP pequena e grande, aprovar pelo link do e-mail depois do login.

## Fora do escopo
- Alçada do pedido de compra: CMP-03 (reaproveita o motor com `PurchaseOrder`).
- Concessão de `finance.approve` a outro grupo no seed: não acontece (D5). Aprovador substituto e delegação temporária: depois do MVP.
- Aprovação em lote (R-PRP-6, D).

## Pontos em aberto
Decididos pelo PO em 20/10/2026 (seção 1.1, item 13, e D5):
1. Alçadas editadas só pelo Proprietário.
2. Regra por conta só quando todos os títulos são da mesma conta, com teto obrigatório (§2.2-A3).
3. O mesmo aprovador não decide dois níveis (CA 5, 13).
4. Aprovador substituto fora do MVP; a delegação a outro grupo fica para quando houver mais de um aprovador (D5).
5. P-18 decidida (ADR 0021).

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers) e E2E passando.
- [x] Migration criada (não destrutiva, com a extensão `btree_gist` se ainda não existir); seed da regra padrão idempotente.
- [x] Permissões só do `ModuleCatalog`; template de e-mail registrado.
- [x] Tela com estados.
- [x] Ajuda escrita; teste de cobertura passando.
- [x] Alertas acionáveis (e-mail e sino com link direto).
- [x] Auditoria ligada.
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `ApprovalRule`, `ApprovalRuleResolver` (Domain, puro, com o valor de referência) e `PaymentOrderLevel`.
- [ ] `IApprovalPolicy` baseado em regras; `IUserDirectory.GroupMembersAsync`; validação de viabilidade no envio.
- [ ] Endpoints `/api/v1/finance/approval-rules` (+ `/simulate`); template de e-mail; tela e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (alçada, fracionamento e link do e-mail), @qa.
- Gate: CA 6, 8, 9, 12, 15 e 17 obrigatórios.

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

- **Correções feitas na validação (19/10/2026):** aprovador por grupo ou usuário; resolução gravada no envio; link do e-mail sem token e sem aprovação automática; fora da alçada mesmo com `finance.approve`.
- **Correções da revalidação (20/10/2026):** movida para o fim da fase (D5) e o exemplo deixou de dar `finance.approve` ao Financeiro do seed (CA 6); regra por conta com teto (§2.2-A3, CA 2, 6, 11); valor de referência contra fracionamento (§2.2-A2, CA 8); alçada viável e mesmo aprovador (§2.1-A1, CA 5, 9); `EXCLUDE USING gist` (CA 4); e-mail só com número, total e link (CA 16). CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada (quebra da FIN-03) | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 20/10/2026 | 0.3 | Decisões do PO (D5, seção 1.1) e correções da revisão pré-Fase 2 (§2.1-A1, §2.2-A2, A3, baixos); ordem 19; CAs renumerados | River (SM) |
| 20/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 21/10/2026 | 1.0 | Implementada. Desvios: a sobreposição de faixas é conferida no serviço, sem `EXCLUDE USING gist` (a extensão `btree_gist` exige permissão de superusuário no banco do tenant; fica para quando o provisionamento a criar); a exclusão da regra é lógica (soft delete), e o seed não recria a padrão excluída; a tela mostra os botões de edição para quem tem `finance.approve` (reservado ao Proprietário) e o servidor confere o grupo Proprietário; OP pendente da FIN-03a, sem níveis gravados, segue a regra antiga até ser decidida ou reenviada; `IUserDirectory` ganhou membros de grupo, grupo Proprietário, nome do grupo e e-mail; o e-mail `payment-order-pending` sai junto com o sino, na mesma transação | @dev |
