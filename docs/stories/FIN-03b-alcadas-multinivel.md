# FIN-03b — Alçadas de aprovação configuráveis, multinível e aprovação pelo link do e-mail

| Campo | Valor |
|---|---|
| ID | FIN-03b |
| Título | Regras de alçada da ordem de pagamento por faixa de valor e por conta, com aprovador por grupo ou usuário e níveis; a regra mais específica vence; todas as aprovações do nível N antes do N+1; e-mail ao aprovador com link autenticado para a OP |
| Tamanho | M (quebra da FIN-03) |
| Status | Draft |
| Requisitos | R-FIN-5 (alçadas configuráveis por valor/categoria; aprovação no sistema e por link no e-mail, autenticado) · R-COM-1 · R-COM-2 · R-PRP-5 · R-SEC-5 |
| Dependências | FIN-03a (OP, `IApprovalPolicy`, decisões), FND-08 (e-mail), FND-06b (grupos) |
| Bloqueia | CMP-03 (alçada do pedido de compra reaproveita o motor) |
| Ordem de execução | 14 (Marco 2.4 — controles; o padrão "Proprietário aprova tudo" já funciona desde a FIN-03a) |

## User story
Como **proprietário**, quero **definir quem aprova cada faixa de valor ou tipo de despesa, em um ou mais níveis, e receber por e-mail o pedido de aprovação**, para **delegar o miúdo sem perder o controle do que é grande**.

## Contexto e referências
- PRD R-FIN-5 ("alçadas configuráveis por valor/categoria (padrão: proprietário aprova tudo)"; "aprovação/reprovação com comentário, no sistema e por link no e-mail (autenticado)").
- `04-fluxos-estados` §3.6 (regra mais específica vence: categoria/conta > faixa de valor > padrão; multinível: todas as aprovações do nível N antes de liberar o N+1); §3.7 (`finance.approve` não dispensa a alçada).
- `03-modelo-dados` → `approval_rules` (`document_type`, `min_amount`, `max_amount`, `account_id`, aprovador, `level`); revisão pré-Fase 0 §2.1 (`approver_role_id` vira `approver_group_id`/`user_id`).
- Riscos (`06`): "Aprovador único vira gargalo" → mitigação "delegação temporária / aprovador substituto" (sem requisito no PRD; ver Pontos em aberto 4).
- ADR 0008 (`*.approve` reservado ao Proprietário).

## Critérios de aceite
**Regras**
1. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed roda, **então** existe a regra padrão "Proprietário aprova tudo" (`PaymentOrder`, faixa de R$ 0,00 sem teto, grupo Proprietário, nível 1, `seed_key`), sem recriar se o tenant a excluiu; com ela, o comportamento é igual ao da FIN-03a.
2. **Dado** um membro do Proprietário, **quando** cadastro uma regra com tipo de documento (só Ordem de pagamento nesta fase), faixa de valor (mínimo e máximo opcional, `numeric(18,2)`), conta do plano opcional (analítica de Despesa), aprovador (um grupo **ou** um usuário ativo) e nível (1 a 3) e clico "Salvar regra de alçada", **então** a regra é gravada, auditada, e os membros do Proprietário são notificados ("Regra de alçada alterada por {usuário}").
3. **[Segurança]** **Dado** um usuário que não é do Proprietário, mesmo com `finance.approve`, **quando** tenta criar, editar ou excluir regra, **então** 403 `forbidden` (ver Pontos em aberto 1); o grupo ou usuário aprovador precisa ter `finance.approve` na permissão efetiva, senão 422 "O aprovador escolhido não tem a permissão Aprovar em Caixa e pagamentos."
4. **Dado** faixas sobrepostas no mesmo nível e na mesma conta (ou ambas sem conta), **quando** salvo, **então** 422 "A faixa se sobrepõe à regra {descrição}."

**Resolução da alçada**
5. **Dado** as regras: A (faixa 0–5.000, grupo Financeiro com `finance.approve`, nível 1), B (acima de 5.000, Proprietário, nível 1), C (conta "3.2 Ocupação", qualquer valor, Financeiro, nível 1) e D (acima de 20.000, Proprietário, nível 2), **quando** a OP é enviada, **então**: OP de R$ 3.000,00 → nível 1 por A; R$ 8.000,00 → nível 1 por B; R$ 8.000,00 só com títulos da conta 3.2 → nível 1 por C (conta vence faixa); R$ 25.000,00 → nível 1 por B e nível 2 por D; a resolução é gravada na OP (`approval_rule_id` por nível) no envio.
6. **Dado** uma OP com títulos de contas diferentes, **quando** é enviada, **então** a regra por conta só se aplica se todos os títulos forem da mesma conta; senão vale a faixa de valor (ver Pontos em aberto 2).
7. **Dado** uma regra alterada depois do envio, **quando** a OP pendente é decidida, **então** vale a resolução gravada no envio; "Reenviar para aprovação" (solicitante, com motivo) recalcula.

**Multinível**
8. **Dado** a OP de R$ 25.000,00 do CA 5, **quando** um membro do Proprietário aprova o nível 1, **então** a OP continua `Pending`, a decisão fica registrada com nível 1 e os aprovadores do nível 2 são notificados; só depois da aprovação do nível 2 ela passa a `Approved`.
9. **Dado** um aprovador que já decidiu um nível, **quando** tenta decidir o nível seguinte, **então** 403 "Você já aprovou o nível {n} desta ordem." (ver Pontos em aberto 3); a regra do solicitante (FIN-03a CA 6) vale em todos os níveis.
10. **Dado** uma reprovação em qualquer nível, **quando** gravada, **então** a OP passa a `Rejected` e os títulos voltam a `Open` (FIN-03a CA 5).
11. **[Segurança]** **Dado** um usuário com `finance.approve` que não está no grupo (ou não é o usuário) da regra do nível pendente, **quando** tenta aprovar, **então** 403 `outside_approval_rule` "Esta ordem não está na sua alçada."; a lista "Aguardando você" mostra só as OPs da alçada dele.

**E-mail**
12. **Dado** a OP enviada ou liberada para um novo nível, **quando** grava, **então** cada aprovador do nível recebe, pela fila, o e-mail `payment_order_pending` com número, solicitante, total, data prevista, a lista de títulos (fornecedor, valor) e o botão "Abrir ordem de pagamento" com o link `https://{host primário}/app/financeiro/ordens-de-pagamento/{id}` (FND-08).
13. **[Segurança]** **Dado** o link do e-mail, **quando** é aberto sem sessão, **então** leva ao login (com 2FA quando exigido) e volta para a OP; o link não carrega token, não aprova sozinho e não mostra dado da OP antes do login; quem não está na alçada vê a OP em modo leitura (com `finance.view`) ou 403.

**Permissões e auditoria**
14. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; `finance.view` vê as regras em modo leitura. `RequireModule` e `perm:`; IDOR em `/approval-rules/{id}`.
15. **Dado** qualquer alteração de regra ou decisão, **quando** abro o histórico, **então** vejo antes/depois, usuário e data; a OP mostra, por nível, a regra aplicada e quem decidiu.

## Regras de negócio e técnicas
- **`approval_rules`** (03, com a correção da revisão): `document_type` (`PaymentOrder`; `PurchaseOrder` reservado para a CMP-03), `min_amount`, `max_amount` (nulo = sem teto), `account_id` (nulo = qualquer), `approver_group_id` **ou** `approver_user_id` (exatamente um), `level` (1–3), `is_active`, `seed_key`.
- **Resolução** por nível: regras com conta (todos os títulos na conta) > regras por faixa > padrão; a faixa compara o total da OP; uma regra resolvida por nível.
- **`payment_order_levels`** (proposta): `payment_order_id`, `level`, `approval_rule_id`, `status` (Pending/Approved/Rejected), para gravar a resolução no envio.
- O `IApprovalPolicy` da FIN-03a passa a usar as regras, sem mudar os casos de uso de aprovar e reprovar.
- Membros de grupo resolvidos pelo Identity (`IUserDirectory.GroupMembersAsync`, proposta); dinheiro `numeric(18,2)`; auditoria `IAuditable`.

## Permissões, eventos e tabelas
- **Permissões:** `finance.view` (ver regras), membro do Proprietário (editar regras, proposta), `finance.approve` + alçada (decidir).
- **Eventos:** `PaymentOrderLevelReleased` (`{ paymentOrderId, level }`) → e-mail e sino aos aprovadores do nível.
- **Tabelas:** `finance.approval_rules` (com `approver_group_id`/`approver_user_id`, `is_active`, `seed_key`); `finance.payment_order_levels` (**proposta**); template de e-mail `payment_order_pending`.

## Telas e estados
- **Financeiro → Alçadas** (`/app/financeiro/alcadas`): h1 "Quem aprova cada ordem de pagamento."; lista por nível com faixa, conta, aprovador; painel lateral de edição; simulador "Quem aprovaria uma OP de R$ {valor} na conta {conta}?" (propositivo).
- **OP (FIN-03a):** a aba Decisões mostra os níveis, a regra e a situação de cada um.
- **Estados:** carregando; só a regra padrão ("Hoje o Proprietário aprova tudo. Crie faixas para delegar."); erro; modo leitura.
- **Ajuda:** `Ajuda/financeiro-alcadas.md`; a ajuda da OP ganha "Níveis de aprovação"; `<Dica>` em "Nível" e "Regra mais específica".

## Plano de testes
- **Unidade:** resolução das regras (CA 5, 6) com tabela de casos; sobreposição (CA 4).
- **Integração (Testcontainers, obrigatória):** seed idempotente (CA 1); edição só pelo Proprietário e aprovador com permissão (CA 2, 3); resolução gravada no envio e estável (CA 5–7); multinível e notificações (CA 8); mesmo aprovador em dois níveis (CA 9); reprovação (CA 10); fora da alçada (CA 11); e-mail com link sem token (CA 12, 13); três camadas (CA 14); auditoria (CA 15).
- **E2E (Playwright):** criar faixa para o Financeiro, enviar OP pequena e grande, aprovar pelo link do e-mail depois do login.

## Fora do escopo
- Alçada do pedido de compra: CMP-03 (reaproveita o motor com `PurchaseOrder`).
- Aprovador substituto e delegação temporária (risco do `06`, sem requisito): depois do MVP.
- Aprovação em lote (R-PRP-6, D).

## Pontos em aberto
1. **Quem edita as alçadas.** Os docs não dizem. **Recomendação:** só membros do Proprietário, como as ações reservadas da ADR 0008, porque a regra decide quem libera dinheiro.
2. **OP com contas diferentes.** **Recomendação:** a regra por conta só vale se todos os títulos forem da mesma conta (CA 6); alternativa: separar a OP por conta no envio.
3. **Mesmo aprovador em níveis diferentes.** **Recomendação:** proibir (CA 9), senão o multinível perde o sentido; com um único Proprietário, não criar dois níveis para o mesmo grupo.
4. **Aprovador substituto.** **Recomendação:** fora do MVP; registrar como melhoria quando houver mais de um aprovador.
5. **P-18:** ver FIN-01, ponto 5.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva); seed da regra padrão idempotente.
- [ ] Permissões só do `ModuleCatalog`; template de e-mail registrado.
- [ ] Tela com estados.
- [ ] Ajuda escrita; teste de cobertura passando.
- [ ] Alertas acionáveis (e-mail e sino com link direto).
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `ApprovalRule`, `ApprovalRuleResolver` (Domain, puro) e `PaymentOrderLevel`.
- [ ] `IApprovalPolicy` baseado em regras; `IUserDirectory.GroupMembersAsync`.
- [ ] Endpoints `/api/v1/finance/approval-rules` (+ `/simulate`); template de e-mail; tela e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (alçada e link do e-mail), @qa.
- Gate: CA 5, 8, 11 e 13 obrigatórios.

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

- **Correções feitas na validação (19/10/2026):** aprovador por grupo ou usuário (revisão pré-Fase 0 §2.1); resolução gravada no envio; link do e-mail sem token e sem aprovação automática (CA 13); fora da alçada mesmo com `finance.approve` (CA 11, `04-fluxos` §3.7); ordem no Marco 2.4 justificada pelo padrão da FIN-03a.
- **Pré-condições para `Ready`:** Pontos em aberto 1 a 3 (com padrão recomendado) e P-18.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada (quebra da FIN-03) | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
