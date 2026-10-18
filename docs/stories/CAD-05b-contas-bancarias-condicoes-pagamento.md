# CAD-05b — Contas bancárias, caixas e condições de pagamento

| Campo | Valor |
|---|---|
| ID | CAD-05b |
| Título | Contas bancárias e caixas com saldo inicial, conta padrão e condições de pagamento com divisão de parcelas |
| Tamanho | M (quebra da CAD-05) |
| Status | Draft |
| Requisitos | R-FIN-2 (contas bancárias e caixas, saldo inicial) · R-TER-2 (condições padrão de pagamento) · R-PRP-1 · R-SEC-5 · PRD §6 Segurança (segredo bancário write-only) |
| Dependências | CAD-05a (mesmo módulo e padrões de tela); Fase 0 (FND-07, FND-09c, FND-11, FND-14). **Pré-condição:** ponto em aberto 1 (dono das condições de pagamento) |
| Bloqueia | CAD-01a (condição de pagamento padrão do terceiro), MIG-01b (condições e contas do Dolibarr), FIN-01, FIN-02, FIN-04, CMP-03, BNK-01 |
| Ordem de execução | 3 |

## User story
Como **financeiro**, quero **cadastrar as contas bancárias e os caixas com o saldo de partida e as condições de pagamento que usamos**, para **que o caixa comece do saldo real e cada título nasça com as parcelas certas, sem conta de cabeça**.

## Contexto e referências
- PRD R-FIN-2 (contas bancárias e caixas, saldo inicial, transferências), R-TER-2 (condições padrão de pagamento do terceiro), R-FIN-7/R-FIN-8 (Inter e OFX, Fase 5).
- `docs/03-modelo-dados.md` → `finance.bank_accounts` (`name`, `type` Bank/Cash, `bank_code`, `agency`, `account`, `provider` Inter/OFX/None, `credentials_secret`, `opening_balance`, `opening_date`) e `parties.payment_terms` (`name`, `installments` JSON dias/percentual).
- `docs/04-fluxos-estados.md` §3.5 (o fluxo de caixa parte do saldo atual de cada conta).
- ADR 0007 (segredos write-only; o tipo de segredo das credenciais Inter entra na Fase 5), ADR 0006 (referência entre módulos só por Id).
- Backlog CAD-05: "contas bancárias/caixa, condições de pagamento".
- Código existente: `Secret`, `SecretExposureRules` (teste que impede `Secret` em DTO de resposta), `ISoftDeletable`, `IAuditable`, `DataTable`, formulário RHF + Zod, `Money`.

## Critérios de aceite
**Contas bancárias e caixas**
1. **Dado** `finance.create`, **quando** cadastro uma conta do tipo Banco com nome, banco (código COMPE de 3 dígitos, escolhido de uma lista), agência, conta com dígito, saldo inicial e data do saldo inicial, e clico "Salvar conta", **então** a conta é gravada e a confirmação diz "Conta salva".
2. **Dado** o tipo Caixa, **quando** cadastro, **então** banco, agência e conta não são pedidos; nome, saldo inicial e data continuam obrigatórios.
3. **Dado** um saldo inicial, **quando** salvo, **então** ele é gravado como `numeric(18,2)` em reais, exibido com `Money`, e a data do saldo inicial como `date` (sem hora); saldo negativo é aceito com o aviso "Saldo inicial negativo. Confira se a conta começa no cheque especial." (ponto em aberto 3).
4. **Dado** uma conta com transação financeira (a partir da FIN-01/FIN-04), **quando** tento mudar o saldo inicial ou a data, **então** a API responde 409 "A conta já tem movimentação. Ajuste o saldo por um lançamento."; nesta story o teste usa um provedor de uso de teste (mesmo mecanismo da CAD-05a).
5. **Dado** várias contas ativas, **quando** marco uma como "Conta padrão", **então** ela passa a ser a única padrão (a anterior é desmarcada na mesma transação) e vem pré-selecionada nos lançamentos futuros (R-PRP-1).
6. **[Segurança]** **Dado** o campo `credentials_secret` (credenciais do Inter, Fase 5), **quando** consulto qualquer conta pela API, **então** ele nunca aparece na resposta; o teste `SecretExposureRules` cobre os DTOs desta story. A tela não pede credencial nesta story; o provedor (Inter, OFX, nenhum) é só informativo até a BNK-01.
7. **Dado** `finance.delete` e uma conta sem movimentação, **quando** excluo e confirmo "Excluir", **então** ela é excluída logicamente; com movimentação, a API responde 409 "Conta com movimentação não pode ser excluída. Inative-a." e a tela oferece "Inativar".
8. **Dado** a conta padrão, **quando** tento inativá-la ou excluí-la, **então** a API responde 409 "Escolha outra conta padrão antes."

**Condições de pagamento**
9. **Dado** `finance.create`, **quando** cadastro uma condição com nome e parcelas (dias a partir da data base e percentual de cada uma) e clico "Salvar condição", **então** ela é gravada quando a soma dos percentuais é exatamente 100,00% e os dias são crescentes e maiores ou iguais a 0.
10. **Dado** parcelas que somam diferente de 100%, ou dias fora de ordem, ou nenhuma parcela, **quando** salvo, **então** a API responde 400 com a mensagem PT-BR do campo ("A soma das parcelas precisa ser 100%. Hoje está em {soma}%.").
11. **Dado** a condição "30/60/90" (dias 30, 60 e 90; 33,3333% + 33,3333% + 33,3334%, como gera o "Dividir igualmente"), **quando** a tela mostra a prévia para R$ 1.000,00 com data base 10/11/2026, **então** aparecem 3 parcelas de R$ 333,33, R$ 333,33 e R$ 333,34 com vencimentos 10/12/2026, 09/01/2027 e 08/02/2027; a última parcela absorve a diferença de arredondamento.
12. **Dado** um módulo da Fase 2 (CR, CP, pedido de compra), **quando** precisa gerar parcelas, **então** chama `IPaymentTerms.Split(termId, amount, baseDate)` do Contracts do módulo dono e recebe a mesma divisão da prévia (CA 11), sem repetir a regra.
13. **Dado** uma condição usada como padrão por um terceiro (CAD-01a), **quando** tento excluir, **então** a API responde 409 e oferece "Inativar"; a condição inativa some dos seletores e continua nos terceiros que a usam, com o chip "Inativa".
14. **Dado** um tenant novo, **quando** é provisionado, **então** existem as condições padrão do seed (sugestão: "À vista", "30 dias", "30/60/90"), idempotentes como o seed da CAD-05a.

**Permissões (3 camadas)**
15. **Dado** o módulo `finance` não liberado, **quando** chamo os endpoints, **então** recebo 403 `module_not_released`; sem grupo que habilite `finance`, 403 `forbidden`; com `finance.view` sem `create`/`edit`/`delete`, as ações ficam ocultas por `<Can>` e a escrita responde 403 `forbidden`.

**Auditoria**
16. **Dado** qualquer alteração de conta ou condição, **quando** abro "Histórico", **então** vejo antes/depois, usuário e data; `credentials_secret`, quando existir, aparece só como "alterado" (`[ProtectedSecret]`).

## Regras de negócio e técnicas
- **Dinheiro:** `opening_balance` em `numeric(18,2)`. **Percentual de parcela:** `numeric(7,4)` (**proposta**) dentro do JSON, somando exatamente 100.
- **Datas:** `opening_date` e data base da divisão são `date`; os vencimentos são calculados em dias corridos a partir da data base (vencimento em fim de semana ou feriado não é ajustado nesta story; ver ponto em aberto 5).
- **Arredondamento da divisão:** cada parcela = `round(valor × pct / 100, 2)`; a última recebe `valor − soma das anteriores`.
- **Conta padrão:** `is_default` (**proposta**), única entre as contas ativas (índice único parcial).
- **Inativação:** `is_active` (**proposta**) em contas e condições.
- **`credentials_secret`:** a coluna pode nascer na migration (nula) ou só na BNK-01; se nascer agora, leva `[ProtectedSecret]` e nunca é preenchida nesta story.
- **Lista de bancos:** lista estática do sistema (código COMPE e nome), sem consulta externa.
- **Ids:** Guid v7. **Auditoria:** `IAuditable`. **Soft delete** com índice único parcial.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `finance.view`, `finance.create`, `finance.edit`, `finance.delete`.
- **Eventos:** nenhum nesta story.
- **Tabelas:**
  - `finance.bank_accounts` (03) + `is_active`, `is_default` (**propostas**).
  - Condições de pagamento: o 03 coloca `payment_terms` no schema `parties`, e o backlog coloca o cadastro na CAD-05 (Financeiro). **Proposta desta story:** tabela `finance.payment_terms`, dona o módulo Finance, exposta por `IPaymentTerms` em `Finance.Contracts`; `parties.default_payment_term_id` guarda só o Id (ADR 0006). Se o PO mantiver o 03, a tabela fica em `parties.payment_terms`, o Contracts passa a ser `Parties.Contracts` e a tela continua em Financeiro. Ver ponto em aberto 1.

## Telas e estados
- **Financeiro → Contas e caixas** (`/app/financeiro/contas`): DataTable com nome, tipo, banco/agência/conta, saldo inicial (`Money`, `tabular-nums`), data, padrão (chip) e situação; painel de edição.
- **Financeiro → Condições de pagamento** (`/app/financeiro/condicoes-de-pagamento`): lista e editor de parcelas (linhas de dias + percentual, botão "Dividir igualmente"), com prévia para um valor e uma data base.
- **Estados:** carregando (skeleton); vazio em contas ("Cadastre a primeira conta bancária ou o caixa da empresa, com o saldo de hoje." + "Nova conta"); erro com motivo e "Tentar de novo".
- **Propositivo:** a primeira conta cadastrada vira padrão; "Dividir igualmente" preenche os percentuais; a data do saldo inicial vem com hoje; depois de salvar a primeira conta, oferece "Cadastrar condições de pagamento".
- **Ajuda:** `Ajuda/financeiro-contas.md` (aba `contas`) e `Ajuda/financeiro-condicoes-de-pagamento.md` (aba `condicoes-de-pagamento`).

## Plano de testes
- **Unidade:** divisão de parcelas com arredondamento e vencimentos (CA 11, 12); soma 100% e dias crescentes (CA 9, 10); unicidade da conta padrão (CA 5).
- **Integração (Testcontainers):** criar banco e caixa (CA 1–3); saldo inicial travado com uso de teste (CA 4); conta padrão única em gravação simultânea (CA 5); credencial ausente da resposta (CA 6); excluir e inativar (CA 7, 8, 13); seed idempotente (CA 14); três camadas (CA 15); auditoria (CA 16).
- **Arquitetura:** `SecretExposureRules` cobre os DTOs (CA 6); `IPaymentTerms` só via Contracts.
- **E2E (Playwright):** cadastrar conta e condição "30/60/90" com prévia.

## Fora do escopo
- Transferências entre contas e extrato interno: FIN-04.
- Credenciais e certificado do Inter, OFX: BNK-01 e BNK-03.
- Marcos de pagamento do orçamento e contrato (JSON por marco, COML-6): COM-01.
- Juros, multa e desconto padrão da condição: FIN-01.

## Pontos em aberto
1. **Dono das condições de pagamento** (pré-condição): `parties.payment_terms` (03) × `finance.payment_terms` (proposta). A escolha muda o Contracts que a CAD-01a e a Fase 2 usam.
2. **Seed de condições** (sugestão: "À vista", "30 dias", "30/60/90").
3. **Saldo inicial negativo:** aceitar com aviso (proposta) ou recusar.
4. **Contas por tipo:** se "Aplicação" ou "Cartão" entram como tipo além de Banco e Caixa (o 03 tem só Bank/Cash).
5. **Vencimento em dia não útil:** manter (proposta) ou adiar para o próximo dia útil usando o calendário da CAD-03.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva) e seed idempotente.
- [ ] Permissões usadas só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio e erro.
- [ ] Ajuda das duas telas escrita; teste de cobertura passando.
- [ ] Padrões e próximo passo propositivos avaliados.
- [ ] Auditoria ligada (segredo só como "alterado").
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado com o dono das condições e os campos propostos; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Entidades `BankAccount` e `PaymentTerm` com as regras de saldo, padrão e parcelas.
- [ ] `PaymentTermSplitter` (Domain) + `IPaymentTerms` no Contracts do módulo dono.
- [ ] Migration, seed de condições e lista estática de bancos.
- [ ] Endpoints `/api/v1/finance/bank-accounts` e `/api/v1/finance/payment-terms`.
- [ ] Telas, prévia de parcelas e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 6 (segredo fora da resposta) e CA 11 (divisão com arredondamento) obrigatórios.

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

- **Correções feitas na validação:** CA 11: percentuais da prévia corrigidos para 33,3333% + 33,3333% + 33,3334% (com 33,33% a divisão não daria R$ 333,33).
- **Pré-condições para `Ready`:** Ponto em aberto 1 (dono das condições de pagamento: `parties` × `finance`).
- **Prontidão:** 9/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da CAD-05) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
