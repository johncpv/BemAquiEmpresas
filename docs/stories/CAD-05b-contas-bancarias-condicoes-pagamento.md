# CAD-05b — Contas bancárias, caixas e condições de pagamento

| Campo | Valor |
|---|---|
| ID | CAD-05b |
| Título | Contas bancárias e caixas com saldo inicial opcional, conta padrão e troca protegida dos dados bancários; condições de pagamento do Finance com divisão de parcelas; condição padrão no terceiro |
| Tamanho | M (quebra da CAD-05) |
| Status | Done |
| Requisitos | R-FIN-2 (contas bancárias e caixas, saldo inicial) · R-TER-2 (condições padrão de pagamento do terceiro) · R-PRP-1 · R-SEC-5 |
| Dependências | CAD-01a (terceiro, para a condição padrão), FND-09d (moldes, `RowsEditor`, `MoneyInput`); Fase 0 (FND-07, FND-09c, FND-14). Não depende da CAD-05a |
| Bloqueia | MIG-01b (condições e contas do Dolibarr), FIN-01, FIN-02, FIN-04, CMP-03, BNK-01 |
| Ordem de execução | 3 (Marco A — terceiros reais) |

## User story
Como **financeiro**, quero **cadastrar as contas bancárias e os caixas, com o saldo de partida quando eu tiver, e as condições de pagamento que usamos**, para **que o caixa comece do saldo real e cada título nasça com as parcelas certas, sem conta de cabeça**.

## Contexto e referências
- PRD R-FIN-2 (contas bancárias e caixas, saldo inicial), R-TER-2 (condições padrão de pagamento do terceiro).
- `docs/03-modelo-dados.md` → `finance.bank_accounts`, `finance.payment_terms` (dono Finance, ADR 0016) e `parties.parties.default_payment_term_id`.
- `docs/04-fluxos-estados.md` §3.5 (o fluxo de caixa parte do saldo atual de cada conta).
- ADR 0016 (dono das condições = Finance, `IPaymentTerms`, `IPaymentTermUsageProvider`; sem `credentials_secret` na Fase 1), ADR 0017 (as contas do Dolibarr entram sem saldo e sem `opening_date`).
- Revisão pré-Fase 1: tabela 1.1 (seed, saldo negativo, tipos, dia não útil), §2.2 (CA 12 da CAD-01a movido para cá), §2.4 (padrões), §2.5 (contas bancárias).
- Padrões comuns a todos os cadastros: `docs/stories/README.md`. Esta story usa o molde **painel lateral** (conta) e **cadastro em seções** (condição, por causa do editor de parcelas).
- `ModuleCatalog`: `finance` com `view`, `create`, `edit`, `approve`, `delete` e `pay`. O grupo Financeiro do seed tem `view`, `create` e `edit`.
- Esta é a primeira story do Finance: cria o schema `finance`.

## Critérios de aceite
**Contas bancárias e caixas**
1. **Dado** `finance.create`, **quando** cadastro uma conta do tipo Banco com nome, banco (código COMPE de 3 dígitos, escolhido no `Combobox` da lista estática), agência e conta com dígito, e clico "Salvar conta bancária", **então** a conta é gravada e o toast diz "Conta bancária salva".
2. **Dado** o tipo Caixa, **quando** cadastro, **então** banco, agência e conta não são pedidos; só existem os tipos Banco e Caixa.
3. **Dado** o saldo inicial e a data do saldo, **quando** informo os dois, **então** o saldo é gravado em `numeric(18,2)` (exibido com `Money`) e a data como `date`; os dois são opcionais, mas só valem juntos (400 "Informe a data do saldo inicial." quando falta um deles); saldo negativo é aceito com o aviso âmbar "Saldo inicial negativo. Confira se a conta começa no cheque especial." (não bloqueia).
4. **Dado** uma conta sem saldo inicial (criada assim ou importada do Dolibarr), **quando** aparece na lista e no detalhe, **então** mostra a `Tag` "Saldo inicial não informado" e o próximo passo "Informar saldo inicial", que abre o painel no campo do saldo.
5. **Dado** uma conta com transação financeira (a partir da FIN-01/FIN-04), **quando** tento mudar o saldo inicial ou a data, **então** a API responde 409 "A conta já tem movimentação. Ajuste o saldo por um lançamento."; nesta story o teste usa um provedor de uso de teste (`IBankAccountUsageProvider`).
6. **Dado** várias contas ativas, **quando** marco uma como "Conta padrão", **então** ela passa a ser a única padrão (a anterior é desmarcada na mesma transação; índice único parcial) e vem pré-selecionada nos lançamentos futuros; a primeira conta cadastrada já nasce padrão.
7. **Dado** a conta padrão, **quando** tento inativá-la ou excluí-la, **então** a API responde 409 "Escolha outra conta padrão antes."
8. **Dado** `finance.delete` e uma conta sem movimentação, **quando** clico "Excluir conta bancária" e confirmo "Excluir", **então** ela é excluída logicamente; com movimentação, a API responde 409 "Conta com movimentação não pode ser excluída. Inative-a." e o diálogo oferece "Inativar".

**Troca dos dados bancários (segurança)**
9. **[Segurança]** **Dado** uma conta existente, **quando** um usuário altera banco, agência ou conta, **então** a API exige ser membro do Proprietário ou ter `finance.approve` (senão 403 `forbidden`), grava o histórico (auditoria antes/depois) e notifica todos os membros do Proprietário pelo sino ("Dados da conta {nome} alterados por {usuário}").
10. **[Segurança]** **Dado** um usuário só com `finance.view`, **quando** consulta a lista ou o detalhe de contas pela API, **então** agência e conta vêm mascaradas do servidor (ex.: `****-5`, `*****678-9`); com `finance.edit`, vêm completas.

**Condições de pagamento**
11. **Dado** `finance.create`, **quando** cadastro uma condição com nome e parcelas no `RowsEditor` (dias a partir da data base e percentual de cada uma) e clico "Salvar condição de pagamento", **então** ela é gravada quando a soma dos percentuais é exatamente 100,0000% e os dias são crescentes e maiores ou iguais a 0; o total ao vivo fica em `aria-live`.
12. **Dado** parcelas que somam diferente de 100%, dias fora de ordem ou nenhuma parcela, **quando** salvo, **então** a API responde 400 com a mensagem PT-BR ("A soma das parcelas precisa ser 100%. Hoje está em {soma}%.").
13. **Dado** a condição "30/60/90 dias" (dias 30, 60 e 90; 33,3333% + 33,3333% + 33,3334%, como gera o "Dividir igualmente"), **quando** a tela mostra a prévia para R$ 1.000,00 com data base 10/11/2026, **então** aparecem 3 parcelas de R$ 333,33, R$ 333,33 e R$ 333,34 com vencimentos 10/12/2026, 09/01/2027 e 08/02/2027; a última parcela absorve a diferença de arredondamento e o vencimento em dia não útil não é ajustado (regra revista na FIN-01).
14. **Dado** um módulo da Fase 2 (CR, CP, pedido de compra), **quando** precisa gerar parcelas, **então** chama `IPaymentTerms.Split(termId, amount, baseDate)` de `Finance.Contracts` e recebe a mesma divisão da prévia (CA 13).
15. **Dado** um tenant novo ou um tenant existente no próximo deploy, **quando** o seed roda, **então** existem as condições "À vista", "30 dias", "30/60 dias" e "30/60/90 dias", com `seed_key`; o seed é idempotente e não recria a condição que o tenant excluiu (busca com `IgnoreQueryFilters()`).

**Condição padrão do terceiro (vindo da CAD-01a)**
16. **Dado** `parties.edit` e as condições ativas, **quando** escolho a "Condição de pagamento padrão" na aba Dados do terceiro, **então** só aparecem condições ativas (lidas por `IPaymentTerms`), a escolha é gravada em `parties.parties.default_payment_term_id` (só o id) e `IParties` passa a devolvê-la para os documentos das próximas fases (R-PRP-1); um id inexistente ou inativo responde 400 "Condição de pagamento inválida."
17. **Dado** uma condição usada como padrão por um terceiro, **quando** tento excluí-la, **então** a API responde 409 "A condição está em uso. Inative-a." (uso consultado por `IPaymentTermUsageProvider`, implementado pelo Parties) e o diálogo oferece "Inativar"; a condição inativa some dos seletores e continua nos terceiros que a usam, com o chip neutro "Inativa".

**Permissões (3 camadas)**
18. **Dado** o módulo `finance` não liberado, **quando** chamo os endpoints, **então** recebo 403 `module_not_released`; sem grupo que habilite `finance`, 403 `forbidden`; com `finance.view` sem `create`/`edit`/`delete`, as ações ficam ocultas por `<Can>` e a escrita responde 403 `forbidden`. Todo endpoint tem `RequireModule` e `perm:` (teste de arquitetura).

**Auditoria**
19. **Dado** qualquer alteração de conta ou condição, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário e data.

## Regras de negócio e técnicas
- **Dinheiro:** `opening_balance` em `numeric(18,2)`, anulável junto com `opening_date`. **Percentual de parcela:** `numeric(7,4)` dentro do JSON, somando exatamente 100.
- **Datas:** `opening_date` e data base da divisão são `date`; vencimentos em dias corridos.
- **Arredondamento da divisão:** cada parcela = `round(valor × pct / 100, 2, AwayFromZero)`; a última recebe `valor − soma das anteriores`.
- **Conta padrão:** `is_default`, única entre as ativas (índice único parcial).
- **Inativação:** `is_active` em contas e condições (D10: excluir sem uso, inativar com uso).
- **Sem `credentials_secret`** nesta fase (ADR 0016); a coluna e o provedor entram na BNK-01.
- **Lista de bancos:** estática do sistema (código COMPE e nome), sem consulta externa.
- **Dono das condições:** Finance (`finance.payment_terms`, `IPaymentTerms`). O Parties referencia `Finance.Contracts` para validar e implementa `IPaymentTermUsageProvider`.
- **Ids:** Guid v7. **Auditoria:** `IAuditable`. **Soft delete** com índice único parcial.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `finance.view`, `finance.create`, `finance.edit`, `finance.delete`, `finance.approve` (troca dos dados bancários, CA 9); `parties.edit` (condição padrão, CA 16).
- **Eventos:** nenhum. A notificação do CA 9 usa o sino (FND-07) na mesma transação.
- **Tabelas:** `finance.bank_accounts` (+ `is_active`, `is_default`; `opening_balance` e `opening_date` anuláveis; sem `provider` e `credentials_secret` até a BNK-01); `finance.payment_terms` (`name`, `installments` jsonb, `is_active`, `seed_key`); `parties.parties.default_payment_term_id`.

## Telas e estados
- **Financeiro → Contas e caixas** (`/app/financeiro/contas`): h1 com a linha "Contas bancárias e caixas da empresa, com o saldo de partida."; DataTable com nome (link), tipo, banco/agência/conta (mascarados para quem só vê), saldo inicial (`Money`, `tabular-nums`) ou `Tag` "Saldo inicial não informado", padrão (chip) e situação; painel lateral de edição.
- **Financeiro → Condições de pagamento** (`/app/financeiro/condicoes-de-pagamento`): lista e página da condição com o `RowsEditor` de parcelas, "Dividir igualmente" e a prévia para um valor e uma data base.
- **Terceiro, aba Dados:** campo "Condição de pagamento padrão" (`Combobox` só com ativas).
- **Verbos:** "Salvar conta bancária" / "Conta bancária salva"; "Salvar condição de pagamento" / "Condição de pagamento salva"; "Inativar" / "Inativada"; "Excluir" / "Excluída".
- **Estados:** carregando; vazio em contas ("Cadastre a primeira conta bancária ou o caixa da empresa." + "Nova conta bancária"); vazio filtrado; erro com motivo e "Tentar de novo"; modo leitura sem `finance.edit`.
- **Propositivo:** a primeira conta vira padrão; "Dividir igualmente" preenche os percentuais; a data do saldo vem com hoje quando o saldo é digitado; depois da primeira conta, a faixa "Próximos passos" oferece "Cadastrar condições de pagamento"; conta sem saldo oferece "Informar saldo inicial".
- **400 px e teclado:** um CA de E2E para o editor de parcelas (padrões comuns, item 12).
- **Ajuda:** `Ajuda/financeiro-contas.md` (aba `contas`) e `Ajuda/financeiro-condicoes-de-pagamento.md` (aba `condicoes-de-pagamento`); a ajuda da aba `dados` do terceiro ganha a seção "Condição de pagamento padrão"; `<Dica>` em "Saldo inicial" e em "Conta padrão".

## Plano de testes
- **Unidade:** divisão de parcelas com arredondamento e vencimentos (CA 13, 14); soma 100% e dias crescentes (CA 11, 12); saldo e data juntos (CA 3).
- **Integração (Testcontainers):** criar banco e caixa (CA 1, 2); saldo negativo com aviso e sem saldo (CA 3, 4); saldo travado com uso de teste (CA 5); conta padrão única em gravação simultânea (CA 6, 7); excluir e inativar (CA 8, 17); troca dos dados bancários com e sem `finance.approve`, histórico e notificação (CA 9); máscara para `finance.view` (CA 10); seed idempotente, inclusive em tenant existente e sem recriar excluída (CA 15); condição padrão no terceiro via Contracts (CA 16); três camadas (CA 18); auditoria (CA 19).
- **Arquitetura:** `IPaymentTerms` só via `Finance.Contracts`; Parties não referencia `Finance.Application`/`Infrastructure`.
- **E2E (Playwright):** cadastrar conta e a condição "30/60/90 dias" com prévia; editor de parcelas a 400 px e só pelo teclado.

## Fora do escopo
- Importação das condições e contas do Dolibarr (`IPaymentTermImport`, `IBankAccountImport`): MIG-01b.
- Plano de contas e centros de custo: CAD-05a.
- Transferências e extrato interno: FIN-04. Credenciais do Inter e OFX: BNK-01 e BNK-03.
- Marcos de pagamento do orçamento: COM-01. Juros, multa e desconto: FIN-01.

## Pontos em aberto
- Decidido em 18/10/2026 (tabela 1.1 e ADR 0016): dono das condições = Finance; seed com À vista, 30, 30/60 e 30/60/90 dias; saldo negativo aceito com aviso; só Banco e Caixa; vencimento em dia não útil não ajustado (rever na FIN-01); sem `credentials_secret` até a BNK-01.
- Nenhum ponto em aberto.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (schema `finance` e coluna no `parties`, não destrutiva) e seed idempotente.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio, vazio filtrado e erro; modo leitura.
- [ ] Ajuda das duas telas e da aba Dados do terceiro; teste de cobertura passando.
- [ ] Padrões comuns dos cadastros e próximos passos aplicados.
- [ ] Auditoria ligada; notificação de troca dos dados bancários.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` conferido; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Entidades `BankAccount` e `PaymentTerm` com as regras de saldo, padrão e parcelas; `PaymentTermSplitter` (Domain).
- [ ] `IFinanceDb`, migration do schema `finance`, seed de condições, lista estática de bancos.
- [ ] `IPaymentTerms` e `IPaymentTermUsageProvider` em `Finance.Contracts`; provedor no Parties; campo de condição padrão.
- [ ] Endpoints `/api/v1/finance/bank-accounts` e `/api/v1/finance/payment-terms`, com o comando separado para troca dos dados bancários.
- [ ] Telas, prévia de parcelas e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (troca de conta bancária), @qa.
- Gate: CA 9 (troca protegida), CA 13 (divisão com arredondamento) e CA 16 (condição pelo Contracts) obrigatórios.

## Validação do PO
Checklist do @po (Pax), 18/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas do CLAUDE.md) que se aplicam | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação (18/10/2026):** sem dependência da CAD-05a (ordem 3 do Marco A); saldo inicial opcional, com a `Tag` "Saldo inicial não informado" para as contas importadas; troca dos dados bancários protegida e notificada; conta mascarada para `finance.view`; `credentials_secret` retirado (BNK-01); seed com 30/60 dias; condição padrão do terceiro trazida da CAD-01a; verbos específicos.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da CAD-05) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 18/10/2026 | 0.3 | Decisões do PO (D1, D10, tabela 1.1, contas do Dolibarr sem saldo) e correções da revisão pré-Fase 1; ordem 3 do Marco A | River (SM) |
| 18/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 19/10/2026 | 1.0 | Implementada. Erros de validação respondem 422 (convenção da API desde a FND-09c), e não 400 como no texto dos CAs; mensagens iguais. Notificação do CA 9 pelo novo `IUserDirectory` (membros do Proprietário, sem acesso às tabelas do Identity). Condições ativas para a aba Dados do terceiro em `GET /api/v1/parties/payment-terms` (`parties.view`), lidas por `IPaymentTerms` | Dev |
