# ADR 0036: Folha no financeiro e conta de salário

- Status: Aceito (21/10/2026). O PO (John) aprovou a decisão D14 da revisão pré-Fases 4 a 8 (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`) pela **opção do arquiteto** (favorecido no título por `payee_type`/`payee_id`), e não pela recomendação consolidada do produto (título único "Folha {mês}"). Aprovou também os pontos 17, 19 e 23 a 27 da tabela 3.3 e as proteções da conta de salário da D14 (ponto 23 da Fase 6 decidido assim).
- Contexto: o fechamento da folha (RH-02c) precisa virar contas a pagar: o salário líquido de cada funcionário e as guias (INSS, FGTS, IRRF e outras). O título a pagar de hoje (`finance.payables`) exige um terceiro (`party_id` não nulo, ADR 0019 §8), e o funcionário não é terceiro: cadastrá-lo como `Party` copiaria CPF e conta bancária para o Finance, fora da proteção do HR (`hr.view_sensitive`, ADR 0016). Os revisores divergiram: o produto propôs um título único "Folha {mês}" com o detalhe no HR; o arquiteto, um favorecido tipado no título com os dados bancários lidos do HR só no pagamento. A conta de salário é também o alvo clássico de fraude (troca da conta de um funcionário pela de outra pessoa às vésperas do pagamento).
- Complementa as ADRs 0008 (2FA de novo, adendo de 21/10/2026), 0016 (`SensitiveRead`, rota `/sensitive`), 0019 (§8 título a pagar), 0024 (§1 chamada síncrona, §2 `IPayables` por parcela) e 0039 (folha calculada).

## Decisão

### 1. Favorecido tipado no título
- `finance.payables` ganha **`payee_type`** (`Party` | `Employee`) e **`payee_id`**, com `CHECK`:
  - `payee_type = 'Party'` ⇒ `payee_id = party_id` e `party_id` não nulo;
  - `payee_type = 'Employee'` ⇒ `party_id` nulo e `payee_id` = id do funcionário no HR.
- **`party_id` passa a aceitar nulo.** É migração de **expansão** (ADR 0006): os títulos existentes ganham `payee_type = 'Party'` e `payee_id = party_id` no backfill; o código N-1 continua lendo `party_id` dos títulos de terceiro e tolera nulo (nenhum título de funcionário existe antes da RH-02c). O `CHECK` entra depois do backfill, na mesma story.
- Telas e contratos que listam favorecido (CP, OP, aging, fluxo de caixa) passam a resolver o nome pelo `payee_type`: `IParties` para `Party`, **`IEmployeeNames`** (nome curto, sem dado sensível) para `Employee`.

### 2. Títulos do fechamento
- **Um título por funcionário** (salário líquido, `payee_type = Employee`, origem `Payroll`, `origin_id` = folha, `installment` = sequência do funcionário na folha) **e um título por guia** (`payee_type = Party`, o órgão como terceiro).
- **Encargos com terceiros:** os órgãos (INSS/Receita Federal, FGTS/Caixa, IRRF/Receita e os que a contadora indicar) são **`Party`** com papel Fornecedor, criados no seed do tenant (seed key própria), editáveis.
- Conta do plano e centro de custo **pelo centro do cargo** (parâmetro da chamada, ADR 0024 §2); vencimento dos salários no **5º dia útil** do mês seguinte; das guias, pelo vencimento padrão de cada guia (parâmetro).
- O valor do título de funcionário é o líquido do holerite; o Finance **não guarda** rubricas, salário bruto nem dado bancário.

### 3. Dados bancários só no pagamento
- Os dados bancários do funcionário ficam **só no HR** e saem por um contrato sensível: **`IEmployeeBankAccounts`** em `HR.Contracts` (`GetForPaymentAsync(employeeId, paymentOrderId)`).
- O Finance chama o contrato **só no pagamento da OP** (`finance.pay`), no momento de registrar a baixa ou montar a remessa; a leitura grava **`SensitiveRead`** (quem, quando, OP, funcionário, sem o dado lido, ADR 0018).
- O retrato do favorecido da OP guarda só banco, agência e os 4 últimos dígitos da conta (ou o tipo da chave Pix), para conferência; nunca a conta inteira.
- Conta em **quarentena** (§5) não é devolvida pelo contrato: a OP mostra "Conta em revisão" e o título não é pago sem a revisão expressa.

### 4. Fechamento síncrono e previsão
- **Fechar a folha** (`hr.approve`) chama o `IPayables` **na própria transação** (padrão da ADR 0024 §1): `CreateOpenInstallmentsAsync` para os salários e as guias. Falha no Finance desfaz o fechamento. `PayrollClosed` continua publicado, só para aviso e projeção.
- **Reabrir a folha** só com os títulos ainda em aberto e fora de OP (`CancelAllFromOriginAsync`, 409 se algum estiver em OP ou pago), com motivo, auditado.
- **Previsão:** origem nova **`PayrollForecast`** no `DocumentOrigin` (SharedKernel) para os títulos `Planned` da folha prevista: 3 meses à frente, com 13º e férias previstos, recalculados pelo HR (`ReplacePlannedAsync`) quando a folha muda; o fechamento do mês cancela o previsto daquele mês. Enquanto a folha não existir no BemAqui, continua a CP recorrente (FIN-02).

### 5. Conta de salário
- **Trocar a conta** exige `hr.view_sensitive` e **2FA de novo** (ADR 0008, adendo, D6).
- **Segunda confirmação** quando o titular da conta ou a chave Pix **não batem com o CPF** do funcionário ("O titular desta conta não é {nome}. Confirmar mesmo assim?"), registrada.
- Aviso ao funcionário no **e-mail pessoal** e aos membros do Proprietário a cada troca, sem os dados da conta.
- **Quarentena de 30 dias:** a conta nova fica fora do pagamento automático por 30 dias (parâmetro `hr.bank_account_quarantine_days`, padrão 30) e só é usada com **revisão expressa** de outra pessoa com `finance.approve`, registrada; o fechamento mostra o aviso "novos na folha / conta alterada".
- **Segregação:** quem fecha a folha (`hr.approve`) **não paga** (`finance.pay`) a mesma folha; exceção do Proprietário único (`SoleOwnerRule`), com a marca e a notificação.
- Fechar a folha exige **2FA de novo** (D6).

## Consequências
- **CAD-04c2** implementa a conta de salário e as proteções da seção 5 e o `IEmployeeBankAccounts`; **RH-02a** as rubricas e os títulos por guia (parâmetros de vencimento); **RH-02c** as seções 1, 2 e 4 (migração de expansão do `payables`, `PayrollForecast`, fechamento síncrono); **FIN-03a** (OP já entregue) passa a resolver o favorecido `Employee` e a chamar o contrato sensível no pagamento, com teste de regressão sobre cópia do `konebras`.
- `docs/03-modelo-dados.md`: `payables` (+ `payee_type`, `payee_id`, `party_id` anulável, `CHECK`), `DocumentOrigin.PayrollForecast`, `hr.employee_bank_accounts` (+ `quarantine_until`, `reviewed_by`); `docs/04-fluxos-estados.md`: fechamento síncrono e reabertura.
- Os órgãos de guia entram no seed do tenant e no seed patch dos tenants existentes.

## Alternativas consideradas
- **Título único "Folha {mês}" mais um por guia** (recomendação consolidada do produto, com o retrato das contas gravado no fechamento): **rejeitada**. Um título só não paga N funcionários (cada pagamento é uma saída de caixa para uma conta diferente, e a baixa parcial por funcionário viraria controle paralelo fora do Finance); o retrato das contas gravado no fechamento copiaria os dados bancários de todos para fora do HR e congelaria a conta antes da quarentena; e o fluxo de caixa, o aging e a OP perderiam o vencimento e a situação por pessoa.
- **Funcionário como `Party`:** rejeitada; copia CPF e conta para o cadastro de terceiros, fora da proteção do HR.
