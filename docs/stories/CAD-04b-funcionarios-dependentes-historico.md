# CAD-04b — Funcionários, dependentes, histórico e afastamentos (LGPD)

| Campo | Valor |
|---|---|
| ID | CAD-04b |
| Título | Cadastro completo do funcionário estruturado para a folha, com dados pessoais só para `hr.view_sensitive`, dependentes, dados bancários/Pix, histórico salarial e de cargo, afastamentos e vínculo com o usuário |
| Tamanho | G (quebra da CAD-04) |
| Status | Draft |
| Requisitos | R-RH-1 · R-RH-3 · PRD §6 LGPD (acesso restrito ao RH) · R-SEC-5 · R-SEC-10 (ação específica `hr.view_sensitive`, ADR 0003) · R-PRP-1 |
| Dependências | CAD-04a (cargos, jornadas, sindicatos), CAD-06 (validador de CPF), CAD-03 (centro do cargo); Fase 0 (FND-06c, FND-07, FND-09c, FND-14). **Pré-condição:** pontos em aberto 1 e 2 |
| Bloqueia | PRD-03 (operador = funcionário), PRD-04, RH-01, RH-02, PAT-01 (responsável pelo bem) |
| Ordem de execução | 10 |

## User story
Como **RH**, quero **cadastrar cada funcionário com tudo o que a folha precisa e manter o histórico de salário, cargo e afastamentos**, e como **proprietário**, quero **que só quem tem permissão veja dados pessoais e salário**, para **ter a base da folha pronta e cumprir a LGPD**.

## Contexto e referências
- PRD R-RH-1 (dados pessoais, documentos, dependentes para IR e salário-família, dados bancários/Pix, admissão, cargo, CBO, salário, jornada, sindicato, vale-transporte, adicionais de insalubridade e periculosidade), R-RH-3 (histórico salarial, de cargo e afastamentos), PRD §6 LGPD ("dados pessoais de funcionários com acesso restrito ao RH; exportação e anonimização sob pedido").
- `docs/03-modelo-dados.md` → `hr.employees` (`name`, `cpf`, `rg`, `pis`, `birth_date`, `address`, `phone`, `email`, `bank/pix`, `admission_date`, `termination_date`, `job_position_id`, `base_salary`, `work_schedule_id`, `union_id`, `transport_voucher`, `insalubrity_pct`, `hazard_pay`, `status`), `hr.employee_dependents` (`name`, `cpf`, `birth_date`, `kinship`, `ir_dependent`, `family_allowance`), `hr.employee_history` (`type` Salary/Position/Leave, `effective_date`, `old_value`, `new_value`, `reason`); `identity.users.employee_id` ("opcional, entra com o RH").
- ADR 0003 (`hr.view_sensitive`), ADR 0008 (só o Proprietário concede `hr.view_sensitive`), ADR 0009 (`[PersonalData]` mascarado no diff), ADR 0010 item 3 (o grupo RH tem `hr.view_sensitive`).
- Revisão pré-Fase 0 §3, "Auditoria e LGPD": registrar a leitura de dado sensível.
- Backlog (CA): dados pessoais visíveis só com `hr.view_sensitive`; histórico salarial registrado a cada alteração.
- FND-06c "Fora do escopo": vínculo do usuário com funcionário (`employee_id`, Fase 1).

## Critérios de aceite
**Cadastro**
1. **Dado** `hr.create` e `hr.view_sensitive`, **quando** cadastro um funcionário com nome, CPF válido, data de nascimento, data de admissão, cargo, salário base, jornada e situação "Ativo", e clico "Salvar funcionário", **então** ele é gravado e a confirmação diz "Funcionário salvo", com as ações "Cadastrar dependentes" e "Vincular usuário".
2. **Dado** um CPF já usado por outro funcionário não excluído, **quando** salvo, **então** a API responde 409 "Já existe um funcionário com este CPF."; CPF inválido responde 400 "CPF inválido."
3. **Dado** um salário base fora da faixa do cargo, **quando** salvo, **então** a tela avisa "Salário fora da faixa do cargo ({mín}–{máx})." e permite salvar.
4. **Dado** os documentos (RG, PIS e os do ponto em aberto 1), endereço, telefone, e-mail pessoal, dados bancários (banco, agência, conta, tipo) e Pix (tipo e chave validada pelo tipo: CPF, e-mail, telefone ou aleatória), **quando** salvo, **então** são gravados.
5. **Dado** adicionais, **quando** informo insalubridade (grau: 10%, 20% ou 40%), periculosidade (sim/não) e vale-transporte (formato no ponto em aberto 3), **então** são gravados.

**LGPD e permissão específica**
6. **[Segurança]** **Dado** um usuário com `hr.view` sem `hr.view_sensitive`, **quando** chama a lista ou o detalhe pela API, **então** a resposta traz só nome, cargo, centro de trabalho, data de admissão e situação; CPF, RG, PIS, nascimento, endereço, telefone, e-mail pessoal, dados bancários e Pix, salário, adicionais, dependentes e histórico salarial **não vêm na resposta** (não basta esconder na tela). A tela mostra "Dados protegidos" com `<Dica>` "Só quem tem a permissão Ver dados sensíveis vê estes dados."
7. **[Segurança]** **Dado** `hr.edit` sem `hr.view_sensitive`, **quando** tento gravar qualquer campo sensível, **então** a API responde 403 `forbidden`; os campos não sensíveis continuam editáveis.
8. **[Segurança]** **Dado** um usuário sem `hr.view_sensitive`, **quando** busca funcionário por CPF, **então** a busca não considera o CPF (não vira oráculo de "este CPF trabalha aqui").
9. **[Segurança]** **Dado** qualquer alteração de funcionário ou dependente, **quando** abro o histórico de auditoria, **então** os campos sensíveis aparecem como `***` (`[PersonalData]`), para qualquer usuário.
10. **[Segurança]** **Dado** um usuário com `hr.view_sensitive`, **quando** abre o detalhe com dados sensíveis, **então** a leitura é registrada na auditoria (`action = "SensitiveRead"`, funcionário, usuário, data e IP), conforme a revisão §3; a listagem não registra leitura por linha.
11. **[Segurança]** **Dado** os logs da API, **quando** um funcionário é criado, lido ou alterado, **então** nenhum dado sensível aparece em log.

**Histórico**
12. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** altero o salário base, **então** a tela pede data de vigência e motivo, e o `employee_history` ganha uma linha `Salary` (valor anterior, novo, vigência, motivo, usuário) na mesma transação; sem data ou motivo, 400.
13. **Dado** `hr.edit`, **quando** troco o cargo, **então** grava uma linha `Position` com o cargo anterior e o novo, vigência e motivo; o salário sugerido passa a ser o de referência do cargo novo (o usuário confirma).
14. **Dado** `hr.view_sensitive`, **quando** abro a aba Histórico, **então** vejo a linha do tempo de salário, cargo e afastamentos, do mais recente ao mais antigo.

**Afastamentos e desligamento**
15. **Dado** `hr.edit`, **quando** registro um afastamento com tipo (lista no ponto em aberto 4), início, fim previsto e motivo, **então** grava uma linha `Leave` e a situação mostra "Afastado" entre o início e o fim; afastamentos sobrepostos respondem 409.
16. **Dado** `hr.edit`, **quando** registro o desligamento com data (maior ou igual à admissão), **então** a situação passa a "Desligado", o funcionário some dos seletores (apontamento, responsável) e nada é apagado; o usuário vinculado não é desativado sozinho, mas a tela oferece "Desativar o usuário {nome}" (próximo passo), visível só para quem tem `identity.edit` e executado pelo endpoint da FND-06c, com a mesma regra do último Proprietário.
17. **Dado** `hr.delete`, **quando** excluo um funcionário sem histórico, sem apontamento e sem vínculo, **então** ele é excluído logicamente; com qualquer um deles, a API responde 409 "Funcionário com histórico não pode ser excluído. Registre o desligamento."

**Dependentes**
18. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** cadastro dependentes com nome, CPF (validado quando informado), nascimento, parentesco, "dependente de IR" e "salário-família", **então** são gravados; o mesmo CPF não se repete entre os dependentes do funcionário.

**Vínculo com o usuário**
19. **Dado** `hr.edit` e um usuário ativo sem funcionário, **quando** clico "Vincular usuário" e confirmo "Vincular", **então** o `identity.users.employee_id` é gravado por `IUserEmployeeLink` de `Identity.Contracts` (o HR não escreve na tabela do Identity); um usuário fica ligado a no máximo um funcionário (409); o vínculo não muda grupo nem permissão.

**Contrato**
20. **Dado** Production (operador, apontamento) ou Assets (responsável), **quando** precisam do funcionário, **então** usam `IEmployees` de `HR.Contracts`, que devolve só id, nome, cargo, centro, situação e o custo-hora do cargo (`IHourlyCost`), nunca dado sensível.

**Permissões (3 camadas)**
21. **Dado** o módulo `hr` não liberado, **quando** chamo os endpoints, **então** recebo 403 `module_not_released` e o front mostra `ModuleNotReleased`; sem grupo que habilite `hr`, 403 `forbidden`; com `hr.view` sem `create`/`edit`/`delete`, as ações ficam ocultas por `<Can>` e a escrita responde 403 `forbidden`.

## Regras de negócio e técnicas
- **Campos sensíveis** (`[PersonalData]` e só com `hr.view_sensitive`): `cpf`, `rg`, `pis`, documentos extras, `birth_date`, endereço, `phone`, `email`, dados bancários e Pix, `base_salary`, adicionais, dependentes inteiros e as linhas `Salary` do histórico. **Não sensíveis:** nome, cargo, centro, admissão, desligamento, situação.
- **Corte na API:** dois DTOs (resumo e completo) ou serialização condicional no servidor; o teste do CA 6 compara o JSON bruto.
- **Dinheiro:** `base_salary` em `numeric(18,2)`. **Datas de negócio** (`birth_date`, `admission_date`, `termination_date`, `effective_date`, início e fim do afastamento) em `date`.
- **Endereço:** campos estruturados como os do terceiro (CEP, logradouro, número, complemento, bairro, cidade, UF), **proposta** em lugar do `address` único do 03.
- **Banco/Pix:** `bank_code`, `bank_agency`, `bank_account`, `bank_account_type`, `pix_key_type`, `pix_key` (**proposta** de desdobramento do `bank/pix` do 03).
- **Situação:** `Active`, `OnLeave` (derivada do afastamento vigente), `Terminated` (**proposta** de valores).
- **Histórico:** `old_value` e `new_value` como texto estruturado (JSON) para salário e cargo; o histórico não substitui a auditoria (os dois existem).
- **Leitura sensível:** grava em `audit.audit_log` com `action = "SensitiveRead"` (cabe nos 64 caracteres; tabela imutável). Mecanismo novo, **proposta** (ponto em aberto 5).
- **Ids:** Guid v7. **Soft delete** só conforme CA 17. **Auditoria:** `IAuditable` em funcionário, dependente e histórico.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `hr.view`, `hr.create`, `hr.edit`, `hr.delete`, `hr.view_sensitive`. Nenhuma ação nova (ADR 0003).
- **Eventos (propostas, sem consumidor nesta fase):** `EmployeeTerminated` (`{ employeeId, terminationDate }`), `EmployeeSalaryChanged` (sem o valor no payload, só o id e a vigência).
- **Tabelas:** `hr.employees` (03, com os desdobramentos **propostos**), `hr.employee_dependents` (03), `hr.employee_history` (03); `identity.users.employee_id` (03, coluna nova no Identity).

## Telas e estados
- **RH → Funcionários** (`/app/rh/funcionarios`): DataTable com nome, cargo, centro, admissão e situação (chips Ativo, Afastado, Desligado); filtros por situação, cargo e centro.
- **Detalhe** (`/app/rh/funcionarios/$id`), abas: Dados gerais, Dados pessoais e documentos, Contrato e salário, Dependentes, Histórico, Auditoria. As abas sensíveis mostram o aviso "Dados protegidos" sem `hr.view_sensitive`.
- **Estados:** carregando; vazio ("Cadastre os funcionários para apontar horas e preparar a folha." + "Novo funcionário"); erro com motivo e "Tentar de novo".
- **Propositivo:** ao escolher o cargo, o salário vem com o de referência e a jornada com a mais usada; depois de salvar, "Cadastrar dependentes" e "Vincular usuário"; no desligamento, "Desativar o usuário".
- **Ajuda:** `Ajuda/rh-funcionarios.md` (lista, `padrao: true`) e `Ajuda/rh-funcionario.md`, uma por aba (`geral`, `pessoais`, `contrato`, `dependentes`, `historico`, `auditoria`); `<Dica>` em "Ver dados sensíveis", "PIS", "Dependente de IR" e "Salário-família".

## Plano de testes
- **Unidade:** validação de Pix por tipo (CA 4); situação derivada do afastamento (CA 15); regras de exclusão (CA 17).
- **Integração (Testcontainers):** cadastro e CPF duplicado (CA 1, 2); JSON bruto sem campos sensíveis com `hr.view` (CA 6); 403 ao gravar sensível (CA 7); busca por CPF ignorada sem permissão (CA 8); `***` na auditoria (CA 9); registro de leitura (CA 10); histórico na mesma transação (CA 12, 13); afastamento sobreposto (CA 15); desligamento (CA 16); dependentes (CA 18); vínculo pelo Contracts e unicidade (CA 19); `IEmployees` sem sensível (CA 20); três camadas (CA 21).
- **Arquitetura:** HR não referencia `Identity.Application`/`Infrastructure`; `IEmployees` não expõe propriedade sensível (regra no estilo do `SecretExposureRules`, **proposta**).
- **E2E (Playwright):** RH cadastra e altera salário; usuário com só `hr.view` não vê dados pessoais nem pela resposta da rede.

## Fora do escopo
- Ponto, banco de horas e espelho: RH-01.
- Folha lançada, holerite e CP de salários: RH-02.
- Exportação e anonimização a pedido do titular (PRD §6 LGPD): ver ponto em aberto 6.
- Foto do funcionário e anexos de documentos.

## Pontos em aberto
1. **Lista de documentos** (pré-condição): o 03 tem CPF, RG e PIS; o R-RH-1 fala em "documentos". Sugestão: CTPS (número e série), título de eleitor, CNH (opcional), certificado de reservista (opcional).
2. **Campos sensíveis** (pré-condição): confirmar a lista da seção de regras, em especial se cargo e data de admissão podem ser vistos com `hr.view`.
3. **Vale-transporte:** sim/não (proposta), valor diário ou linhas e tarifas.
4. **Tipos de afastamento** (sugestão: doença, acidente de trabalho, licença-maternidade, licença-paternidade, serviço militar, outros). Férias ficam para a folha.
5. **Registro de leitura sensível:** mecanismo (proposta: `audit_log` com `SensitiveRead`), retenção e se a tela de auditoria mostra essas leituras ao Proprietário.
6. **Exportação e anonimização LGPD** (PRD §6): em que story entram e quem aprova o pedido.
7. **Matrícula do funcionário:** campo e numeração (ver CAD-06 ponto 9).
8. **Mascaramento parcial** (ex.: CPF `***.456.789-**`) para quem só tem `hr.view`, em vez de esconder.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migrations criadas (`hr` e a coluna `employee_id` em `identity.users`, não destrutivas).
- [ ] Permissões usadas só do `ModuleCatalog` (`hr.view_sensitive`, ADR 0003).
- [ ] Telas com estados de carregando, vazio e erro; aviso "Dados protegidos".
- [ ] Ajuda da lista e de cada aba escrita; teste de cobertura passando.
- [ ] Padrões e próximo passo propositivos avaliados.
- [ ] Auditoria ligada, com `[PersonalData]` e registro de leitura sensível.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado (desdobramentos de endereço, banco/Pix e situação); story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Entidades `Employee`, `EmployeeDependent`, `EmployeeHistoryEntry` com as regras de histórico, afastamento e desligamento.
- [ ] DTOs resumo e completo; filtro de campos por `hr.view_sensitive` no servidor.
- [ ] Registro de `SensitiveRead` na auditoria.
- [ ] `IUserEmployeeLink` em `Identity.Contracts` (implementação no Identity) e `IEmployees` em `HR.Contracts`.
- [ ] Endpoints `/api/v1/hr/employees` (+ dependentes, histórico, afastamentos, desligamento, vínculo); telas e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (LGPD, corte de campos e leitura sensível), @qa.
- Gate: CA 6, 7, 8 e 10 obrigatórios e nunca ignorados.

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

- **Correções feitas na validação:** CA 16: o próximo passo "Desativar o usuário" só aparece com `identity.edit` e usa o endpoint da FND-06c.
- **Pré-condições para `Ready`:** Pontos em aberto 1 (lista de documentos), 2 (campos sensíveis) e 5 (mecanismo de registro da leitura sensível).
- **Prontidão:** 8/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da CAD-04) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
