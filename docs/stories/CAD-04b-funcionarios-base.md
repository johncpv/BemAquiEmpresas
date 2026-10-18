# CAD-04b — Funcionários: cadastro base, salário, cargo, desligamento e LGPD

| Campo | Valor |
|---|---|
| ID | CAD-04b |
| Título | Cadastro base do funcionário (identificação, documentos CTPS e título de eleitor, contrato, cargo, salário), linha do tempo de salário e cargo, desligamento, vínculo com o usuário e dados pessoais só com `hr.view_sensitive` por rota própria |
| Tamanho | M (quebra da CAD-04b original pela decisão D6: dependentes, banco/Pix, adicionais, vale-transporte, afastamentos e documentos extras foram para a CAD-04c, na Fase 3) |
| Status | Ready |
| Requisitos | R-RH-1 (parte: dados pessoais, documentos, admissão, cargo, CBO, salário, jornada, sindicato) · R-RH-3 (histórico salarial e de cargo) · PRD §6 LGPD · R-SEC-5 · R-SEC-10 (`hr.view_sensitive`, ADR 0003) · R-PRP-1 |
| Dependências | CAD-04a (cargos, jornadas, sindicatos, padrão `/sensitive` e `SensitiveRead`), CAD-01a (`BrazilianDocuments`), CAD-01b (`ICepLookup`), FND-09d; Fase 0 (FND-06c, FND-07, FND-09c, FND-14) |
| Bloqueia | CAD-04c, PRD-03 (operador = funcionário), PRD-04, RH-01, RH-02, PAT-01 |
| Ordem de execução | 13 (Marco C — base de custo; fecha a Fase 1) |

## User story
Como **RH**, quero **cadastrar cada funcionário com os dados de identificação e de contrato e manter o histórico de salário e de cargo**, e como **proprietário**, quero **que só quem tem permissão veja dados pessoais e salário**, para **ter a base do apontamento e da folha pronta e cumprir a LGPD**.

## Contexto e referências
- PRD R-RH-1, R-RH-3, PRD §6 LGPD ("acesso restrito ao RH; exportação e anonimização sob pedido").
- `docs/03-modelo-dados.md` → `hr.employees` (campos da base desta story), `hr.employee_history` (Salary/Position nesta story; Leave na CAD-04c); `identity.users.employee_id`.
- Decidido em 18/10/2026: **D6** (quebra em base e CAD-04c); tabela 1.1 (documentos CTPS e título de eleitor; campos sensíveis conforme esta story; cargo e admissão não são sensíveis; matrícula em texto opcional; exportação e anonimização na Fase 6).
- **ADR 0016** (rotas `/{id}` e `/{id}/sensitive`, `SensitiveRead` visível só ao Proprietário, comandos separados com `UnmappedMemberHandling.Disallow`, ordenação e filtro por sensível ignorados, histórico de HR só com `hr.view_sensitive`, `no-store`, allowlist e canário), **ADR 0018** (retenção do funcionário desligado; atendimento ao titular pelo Proprietário, story na Fase 6).
- Revisão pré-Fase 1 §2.4 (criação em página única; salário, cargo e desligamento como ações com diálogo próprio; aba "Linha do tempo"), §2.5 (CAD-04a e CAD-04b; vínculo usuário↔funcionário).
- ADR 0003, ADR 0008 (só o Proprietário concede `hr.view_sensitive`), ADR 0010 item 3.
- Padrões comuns a todos os cadastros: `docs/stories/README.md`. Moldes **cadastro em seções** (criação) e **detalhe com abas** (edição).

## Critérios de aceite
**Criação em página única**
1. **Dado** `hr.create` + `hr.view_sensitive`, **quando** abro "Novo funcionário", **então** a página única tem só o obrigatório, em duas seções: Identificação (nome, CPF, data de nascimento) e Contrato (admissão, cargo, jornada, salário base); ao escolher o cargo, o salário vem com o de referência e a jornada com a do cargo, e só mudam enquanto eu não mexi neles.
2. **Dado** os dados válidos, **quando** clico "Salvar funcionário", **então** ele é gravado com a situação Ativo, o toast diz "Funcionário salvo" e o detalhe abre com a faixa "Próximos passos" ("Completar dados pessoais", "Vincular usuário").
3. **Dado** `hr.create` sem `hr.view_sensitive`, **quando** chamo a criação, **então** a API responde 403 `forbidden` (CPF e salário são obrigatórios e sensíveis); o botão "Novo funcionário" fica oculto.
4. **Dado** um CPF já usado por outro funcionário não excluído, **quando** salvo, **então** a API responde 409 "Já existe um funcionário com este CPF."; CPF inválido responde 400 "CPF inválido." (`BrazilianDocuments`).
5. **Dado** um salário base fora da faixa do cargo, **quando** salvo, **então** a tela mostra o aviso âmbar "Salário fora da faixa do cargo ({mín}–{máx})." e permite salvar.

**Dados pessoais (aba própria)**
6. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** preencho RG, PIS, CTPS (número e série), título de eleitor, endereço (CEP com `ICepLookup`, logradouro, número, complemento, bairro, cidade, UF), telefone e e-mail pessoal e clico "Salvar dados pessoais", **então** são gravados pelo comando de dados pessoais; PIS com dígito inválido responde 400 "PIS inválido."
7. **Dado** a matrícula (texto opcional, até 20 caracteres), **quando** informo, **então** ela é única entre os funcionários não excluídos (409) e não é sensível.

**LGPD e rotas sensíveis**
8. **[Segurança]** **Dado** `hr.view` sem `hr.view_sensitive`, **quando** chamo a lista ou `GET /api/v1/hr/employees/{id}`, **então** a resposta traz só nome, matrícula, cargo, centro de trabalho, admissão, desligamento e situação; CPF, RG, PIS, CTPS, título, nascimento, endereço, telefone, e-mail pessoal e salário **não existem no JSON**; a tela mostra "Dados protegidos" com a `<Dica>` "Só quem tem a permissão Ver dados sensíveis vê estes dados."
9. **[Segurança]** **Dado** `hr.view_sensitive`, **quando** abro a aba Dados pessoais ou Contrato e salário, **então** o front chama `GET /api/v1/hr/employees/{id}/sensitive`, a leitura é registrada como `SensitiveRead` (funcionário, usuário, data, IP) na mesma requisição, a resposta leva `Cache-Control: no-store`, e só os membros do Proprietário veem esses registros; a lista nunca registra leitura nem devolve campo sensível.
10. **[Segurança]** **Dado** `hr.edit` sem `hr.view_sensitive`, **quando** chamo o comando de dados pessoais ou o de salário, **então** recebo 403 `forbidden`; um `PUT` dos dados gerais com campo sensível no corpo responde 400 (`UnmappedMemberHandling.Disallow`) e nunca anula o que está gravado.
11. **[Segurança]** **Dado** um usuário sem `hr.view_sensitive`, **quando** busca por CPF ou ordena/filtra por salário ou nascimento, **então** a busca não considera o CPF e a ordenação e o filtro são ignorados (sem oráculo); com a permissão, a busca por CPF é só exata (11 dígitos).
12. **[Segurança]** **Dado** o histórico de alterações do funcionário, **quando** um usuário sem `hr.view_sensitive` abre, **então** recebe 403; com a permissão, os campos sensíveis aparecem como `***` (`[PersonalData]`); o teste de canário confirma que CPF, PIS, salário, telefone e e-mail não aparecem em `audit.audit_log` nem no log.
13. **[Segurança]** **Dado** o teste de arquitetura, **quando** roda, **então** toda propriedade de `Employee` e `EmployeeHistoryEntry` é `[PersonalData]` ou está na allowlist de não sensíveis, e os DTOs de resumo, lista e `IEmployees` não têm propriedade sensível.

**Salário e cargo (ações com diálogo)**
14. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** clico "Reajustar salário" e informo o novo salário, a vigência e o motivo, e confirmo "Reajustar salário", **então** o salário muda e a linha do tempo ganha uma entrada `Salary` (anterior, novo, vigência, motivo, usuário) na mesma transação; o toast diz "Salário reajustado"; sem vigência ou motivo, 400.
15. **Dado** `hr.edit`, **quando** clico "Trocar cargo", escolho o cargo novo, a vigência e o motivo e confirmo "Trocar cargo", **então** grava uma entrada `Position`; com `hr.view_sensitive`, o diálogo sugere o salário de referência do cargo novo e, se aceito, grava também a entrada `Salary`; o toast diz "Cargo trocado".
16. **Dado** a aba "Linha do tempo", **quando** abro, **então** vejo as trocas de cargo (com `hr.view`) e os reajustes (só com `hr.view_sensitive`), do mais recente ao mais antigo.

**Desligamento e exclusão**
17. **Dado** `hr.edit`, **quando** clico "Registrar desligamento", informo a data (maior ou igual à admissão) e confirmo "Registrar desligamento", **então** a situação passa a Desligado, o funcionário some dos seletores e nada é apagado; o toast diz "Desligamento registrado"; o usuário vinculado não é desativado sozinho, e a faixa "Próximos passos" oferece "Desativar o usuário {nome}", visível só com `identity.edit` e executado pelo endpoint da FND-06c (regra do último Proprietário).
18. **Dado** `hr.delete` e um funcionário sem linha do tempo, sem apontamento e sem vínculo, **quando** excluo e confirmo "Excluir", **então** ele é excluído logicamente; com qualquer um deles, 409 "Funcionário com histórico não pode ser excluído. Registre o desligamento." e o diálogo oferece "Registrar desligamento".

**Vínculo com o usuário**
19. **[Segurança]** **Dado** `hr.edit` **e** `identity.edit`, **quando** clico "Vincular usuário", escolho um usuário ativo sem funcionário e confirmo "Vincular", **então** o `identity.users.employee_id` é gravado por `IUserEmployeeLink` de `Identity.Contracts`, a operação é auditada nos dois módulos e um usuário fica ligado a no máximo um funcionário (409); sem uma das duas permissões, 403; o vínculo não muda grupo nem permissão.
20. **Dado** um vínculo, **quando** clico "Desvincular usuário" e confirmo "Desvincular", com as mesmas duas permissões, **então** o `employee_id` volta a nulo e a operação é auditada.

**Contrato**
21. **Dado** Production (operador, apontamento) ou Assets (responsável), **quando** precisam do funcionário, **então** usam `IEmployees` de `HR.Contracts`, que devolve só id, nome, matrícula, cargo, centro e situação; o custo-hora vem de `IHourlyCost` por cargo (CAD-04a), nunca por funcionário.

**Permissões (3 camadas)**
22. **Dado** o módulo `hr` não liberado, **quando** chamo os endpoints, **então** recebo 403 `module_not_released` e o front mostra `ModuleNotReleased`; sem grupo que habilite `hr`, 403 `forbidden`; com `hr.view` sem `create`/`edit`/`delete`, as ações ficam ocultas por `<Can>` e a escrita responde 403 `forbidden`. Todo endpoint tem `RequireModule` e `perm:`; as rotas `/{id}/...` conferem que o filho pertence ao funcionário (IDOR).

## Regras de negócio e técnicas
- **Campos sensíveis** (`[PersonalData]`, só em `/sensitive` e nos comandos próprios): `cpf`, `rg`, `pis`, `ctps_number`, `ctps_series`, `voter_id`, `birth_date`, endereço, `phone`, `email`, `base_salary` e as entradas `Salary` da linha do tempo. **Não sensíveis:** nome, matrícula, cargo, centro, jornada, sindicato, admissão, desligamento, situação, entradas `Position`.
- **Comandos:** dados gerais, dados pessoais, reajuste de salário, troca de cargo, desligamento, vínculo e desvínculo.
- **Dinheiro:** `base_salary` em `numeric(18,2)`. **Datas de negócio** (`birth_date`, `admission_date`, `termination_date`, `effective_date`) em `date`.
- **Endereço:** campos estruturados como os do terceiro, no lugar do `address` único do 03.
- **Situação:** `Active` e `Terminated` nesta story; `OnLeave` entra com os afastamentos (CAD-04c).
- **Linha do tempo:** `employee_history` com `old_value`/`new_value` em JSON; não substitui a auditoria.
- **Retenção:** funcionário desligado fica pelo prazo trabalhista (ADR 0018); exportação e anonimização na Fase 6.
- **Ids:** Guid v7. **Soft delete** só conforme o CA 18. **Auditoria:** `IAuditable` em funcionário e linha do tempo, com nome qualificado.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `hr.view`, `hr.create`, `hr.edit`, `hr.delete`, `hr.view_sensitive`; `identity.edit` (vínculo e desativação do usuário).
- **Eventos (sem consumidor nesta fase):** `EmployeeTerminated` (`{ employeeId, terminationDate }`), `EmployeeSalaryChanged` (só id e vigência, sem valor).
- **Tabelas:** `hr.employees` (base, com endereço estruturado, `registration`, `ctps_number`, `ctps_series`, `voter_id`); `hr.employee_history` (Salary, Position); `identity.users.employee_id` (coluna nova no Identity).

## Telas e estados
- **RH → Funcionários** (`/app/rh/funcionarios`): h1 com a linha "Quem trabalha na empresa e em qual cargo."; DataTable com nome (link), matrícula, cargo, centro, admissão e situação (chips Ativo e Desligado neutro); filtros por situação, cargo e centro na URL.
- **Novo funcionário** (`/app/rh/funcionarios/novo`): página única em seções (CA 1).
- **Funcionário** (`/app/rh/funcionarios/$id`), abas na URL: Dados gerais, Dados pessoais, Contrato e salário, Linha do tempo, Histórico de alterações (última). As abas sensíveis mostram "Dados protegidos" sem `hr.view_sensitive`.
- **Ações com diálogo:** "Reajustar salário" / "Salário reajustado"; "Trocar cargo" / "Cargo trocado"; "Registrar desligamento" / "Desligamento registrado"; "Vincular usuário" / "Usuário vinculado"; "Desvincular usuário" / "Usuário desvinculado". Os diálogos saem com "Voltar".
- **Estados:** carregando; vazio ("Cadastre os funcionários para apontar horas e preparar a folha." + "Novo funcionário"); vazio filtrado; erro com "Tentar de novo"; modo leitura; "Dados protegidos".
- **Propositivo:** salário e jornada sugeridos pelo cargo; CEP preenche o endereço; faixa "Próximos passos" depois de criar e depois do desligamento.
- **400 px e teclado:** um CA de E2E para a criação e para o diálogo "Reajustar salário".
- **Ajuda:** `Ajuda/rh-funcionarios.md` (lista, `padrao: true`), `Ajuda/rh-funcionario-novo.md` e `Ajuda/rh-funcionario.md` com uma por aba (`geral`, `pessoais`, `contrato`, `linha-do-tempo`, `historico`); `<Dica>` em "Ver dados sensíveis", "PIS", "CTPS" e "Matrícula".

## Plano de testes
- **Unidade:** PIS (CA 6); salário e jornada sugeridos só enquanto não editados (CA 1); regras de exclusão (CA 18).
- **Integração (Testcontainers):** criação com e sem `hr.view_sensitive` (CA 1–3); CPF duplicado e inválido (CA 4); aviso de faixa (CA 5); dados pessoais e matrícula (CA 6, 7); JSON bruto sem sensível com `hr.view` (CA 8); `/sensitive` com `SensitiveRead` e `no-store` (CA 9); 403 e 400 nos comandos (CA 10); busca e ordenação sem oráculo (CA 11); histórico com 403, máscara e canário (CA 12); reajuste e troca de cargo na mesma transação (CA 14, 15); linha do tempo por permissão (CA 16); desligamento (CA 17); exclusão (CA 18); vínculo e desvínculo com as duas permissões (CA 19, 20); `IEmployees` (CA 21); três camadas e IDOR (CA 22).
- **Arquitetura:** allowlist de `[PersonalData]` e DTOs sem sensível (CA 13); HR não referencia `Identity.Application`/`Infrastructure`.
- **E2E (Playwright):** RH cadastra e reajusta salário; usuário só com `hr.view` não vê dados pessoais nem na resposta da rede; criação e diálogo a 400 px e só pelo teclado.

## Fora do escopo
- Dependentes, dados bancários e Pix, adicionais (insalubridade e periculosidade), vale-transporte, afastamentos e documentos extras (anexos, CNH, reservista): **CAD-04c** (início da Fase 3).
- Ponto e banco de horas: RH-01. Folha e holerite: RH-02.
- Exportação e anonimização a pedido do titular: story LGPD-01 da Fase 6 (ADR 0018).
- Funcionários do Dolibarr: não migram (ADR 0017).

## Pontos em aberto
- Decidido em 18/10/2026 (D6, D9, tabela 1.1, ADRs 0016 e 0018): quebra em base e CAD-04c; documentos CTPS e título de eleitor; campos sensíveis conforme as regras; cargo e admissão não sensíveis; matrícula em texto opcional; registro de leitura por `SensitiveRead`, visível só ao Proprietário e retido com a auditoria; exportação e anonimização na Fase 6; CPF fora da lista e busca por CPF só exata.
- Nenhum ponto em aberto.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migrations criadas (`hr` e `identity.users.employee_id`, não destrutivas).
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio, erro e "Dados protegidos".
- [ ] Ajuda da lista, da criação e de cada aba; teste de cobertura passando.
- [ ] Padrões comuns dos cadastros e próximos passos aplicados.
- [ ] Auditoria ligada, com `[PersonalData]` e `SensitiveRead`.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` conferido; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Entidades `Employee` e `EmployeeHistoryEntry` com as regras de linha do tempo, desligamento e exclusão.
- [ ] DTOs de resumo e sensível; comandos separados com `UnmappedMemberHandling.Disallow`.
- [ ] `IUserEmployeeLink` em `Identity.Contracts` (implementação no Identity) e `IEmployees` em `HR.Contracts`.
- [ ] Endpoints `/api/v1/hr/employees` (+ `/{id}/sensitive`, dados pessoais, reajuste, troca de cargo, desligamento, vínculo, desvínculo, CEP); telas e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (LGPD, corte de campos, leitura sensível, vínculo), @qa.
- Gate: CA 8, 9, 10, 11, 12 e 19 obrigatórios e nunca ignorados.

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

- **Correções feitas na validação (18/10/2026):** quebra D6 (base aqui, o resto na CAD-04c), re-estimada de G para M; criação em página única; salário, cargo e desligamento como ações com diálogo; aba "Linha do tempo"; rotas `/sensitive` e `SensitiveRead` (ADR 0016); comandos separados; vínculo com `hr.edit` e `identity.edit` e desvínculo; valores da tabela 1.1; arquivo renomeado para `CAD-04b-funcionarios-base.md`. O NO-GO de segurança da revisão fica resolvido pelos CAs 8 a 13 e 19.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da CAD-04) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 18/10/2026 | 0.3 | Decisão D6: escopo reduzido à base (dependentes, banco/Pix, adicionais, VT, afastamentos e documentos extras para a CAD-04c); D9, tabela 1.1, ADRs 0016 e 0018; ordem 13 do Marco C | River (SM) |
| 18/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
