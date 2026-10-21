# CAD-04c — Funcionários: dependentes, banco/Pix, adicionais, vale-transporte, afastamentos e documentos

| Campo | Valor |
|---|---|
| ID | CAD-04c |
| Título | Complemento do cadastro do funcionário para a folha: dependentes (IR e salário-família), dados bancários e Pix com troca notificada, adicionais, vale-transporte, afastamentos (LGPD art. 11) e documentos extras |
| Tamanho | M (nova, da quebra D6 da CAD-04b) |
| Status | Draft |
| Requisitos | R-RH-1 (dependentes, dados bancários/Pix, vale-transporte, adicionais, documentos) · R-RH-3 (afastamentos) · PRD §6 LGPD · R-SEC-5 · R-SEC-10 |
| Dependências | CAD-04b (funcionário base, rotas `/sensitive`, linha do tempo); FND-09d; Fase 0 (FND-07 anexos) |
| Bloqueia | PRD-03a (afastado ou desligado não aponta), PRD-04, RH-01, RH-02 |
| Ordem de execução | 1 (Marco 3.1, início da Fase 3, antes da PRD-03a), conforme a decisão D6 |

## User story
Como **RH**, quero **completar o cadastro do funcionário com dependentes, conta para pagamento, adicionais, vale-transporte, afastamentos e documentos**, e como **proprietário**, quero **que dado de saúde e de conta bancária tenha proteção extra**, para **ter tudo o que a folha precisa sem expor o que a LGPD trata como sensível**.

## Contexto e referências
- PRD R-RH-1, R-RH-3, PRD §6 LGPD. LGPD art. 11 (dado de saúde é dado pessoal sensível).
- `docs/03-modelo-dados.md` → `hr.employees` (campos de banco/Pix, adicionais e vale-transporte), `hr.employee_dependents`, `hr.employee_history` (`Leave`), `shared.attachments`.
- Decisão D6 (18/10/2026): esta parte da antiga CAD-04b vai para o início da Fase 3, onde estão os consumidores (PRD-03/04).
- **ADR 0016** (rotas `/sensitive`, `SensitiveRead`, comandos separados, `AttachmentOwnerRegistry`), **ADR 0018** (retenção e titular).
- Revisão pré-Fase 1 §2.5: afastamentos (LGPD art. 11), banco/Pix do funcionário, dependentes menores, anexos de funcionário.
- Padrões comuns a todos os cadastros: `docs/stories/README.md`.

## Critérios de aceite
**Dependentes**
1. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** cadastro um dependente com nome, nascimento, parentesco, "Dependente de IR" e "Salário-família" e clico "Salvar dependente", **então** ele é gravado na aba Dependentes.
2. **[Segurança]** **Dado** um dependente, **quando** "Dependente de IR" está desmarcado, **então** o CPF não é pedido nem aceito (400 se enviado); marcado, o CPF é obrigatório e validado; o mesmo CPF não se repete entre os dependentes do funcionário.
3. **[Segurança]** **Dado** a rota `/api/v1/hr/employees/{employeeId}/dependents/{dependentId}`, **quando** o dependente é de outro funcionário, **então** a API responde 404 (IDOR); todo o conteúdo da aba vem só por rota que exige `hr.view_sensitive`.

**Dados bancários e Pix**
4. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** informo banco (COMPE), agência, conta, tipo de conta e Pix (tipo e chave validada pelo tipo: CPF, e-mail, telefone ou aleatória) e confirmo "Salvar conta para pagamento", **então** são gravados pelo comando próprio.
5. **[Segurança]** **Dado** uma troca de banco, agência, conta ou chave Pix de funcionário já cadastrado, **quando** salvo, **então** a troca grava histórico (auditoria mascarada) e notifica todos os membros do Proprietário pelo sino ("Conta para pagamento de {funcionário} alterada por {usuário}"), na mesma transação.
6. **[Segurança]** **Dado** a leitura dos dados bancários, **quando** acontece, **então** vem só de `/sensitive`, com `SensitiveRead` e `Cache-Control: no-store`; agência, conta e chave Pix ficam mascaradas também para quem tem a permissão, até clicar "Mostrar" (que registra a leitura).

**Adicionais e vale-transporte**
7. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** informo insalubridade (grau 10%, 20% ou 40%), periculosidade (sim/não) e vale-transporte (no formato decidido pelo PO, ponto em aberto 1) e clico "Salvar adicionais", **então** são gravados.

**Afastamentos (LGPD art. 11)**
8. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** clico "Registrar afastamento", informo tipo (lista do ponto em aberto 2), início, fim previsto e motivo e confirmo "Registrar afastamento", **então** grava uma entrada `Leave` na linha do tempo e a situação mostra "Afastado" entre o início e o fim (calculada na leitura por `BusinessTime.Today`, nunca gravada, como o "Vencido" do Financeiro); afastamentos sobrepostos respondem 409; a mensagem de sucesso no topo diz "Afastamento registrado" (ADR 0021).
9. **[Segurança]** **Dado** um usuário com `hr.view` sem `hr.view_sensitive`, **quando** vê o funcionário afastado (lista, detalhe, `IEmployees`), **então** recebe só a situação "Afastado", sem tipo, motivo nem datas; tipo, datas e motivo vêm só de `/sensitive`. O `EmployeeSummary` do `IEmployees` (`HR.Contracts`) ganha `Status` (`Active`/`OnLeave`/`Terminated`), sem tipo nem motivo, para a PRD-03a recusar apontamento de afastado.
10. **[Segurança]** **Dado** o campo motivo, **quando** preencho, **então** ele aceita até 200 caracteres, mostra a orientação "Não informe CID nem diagnóstico." e o texto nunca vai para log nem para evento.
11. **[Segurança]** **Dado** os eventos de afastamento (`EmployeeLeaveStarted`, `EmployeeLeaveEnded`), **quando** publicados, **então** levam só o id do funcionário e as datas, nunca o tipo nem o motivo.

**Documentos extras**
12. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** informo CNH (número, categoria, validade) e certificado de reservista (opcional) e anexo documentos digitalizados (PDF, PNG ou JPG, até 10 MB), **então** são gravados; os anexos usam o dono `EmployeeDocument`, registrado por `AttachmentOwnerRegistration` com `ReadAction` e `WriteAction` = `view_sensitive`, `OwnerExists` obrigatório (ADR 0019 §7), tipos pdf, png e jpg e até 10 MB.
13. **[Segurança]** **Dado** o `AttachmentOwnerRegistry` (regras por dono da ADR 0019 §7), **quando** o dono `EmployeeDocument` é registrado, **então** leitura e escrita exigem `hr.view_sensitive`, o download registra `SensitiveRead` e verifica que o funcionário existe; sem a permissão, 403.

**Permissões e auditoria**
14. **Dado** o módulo `hr` não liberado, sem grupo que o habilite ou sem `hr.view_sensitive`, **quando** chamo os endpoints desta story, **então** recebo 403 com o código da camada que barrou; o teste de arquitetura confere `RequireModule` e `perm:` em todos.
15. **[Segurança]** **Dado** os testes de allowlist e de canário (ADR 0016), **quando** rodam, **então** todas as propriedades novas são `[PersonalData]` ou estão na allowlist, e CPF de dependente, chave Pix, conta e motivo de afastamento não aparecem em `audit.audit_log` nem no log.

## Regras de negócio e técnicas
- **Campos sensíveis:** todos desta story, inclusive a situação detalhada do afastamento.
- **Banco/Pix:** `bank_code`, `bank_agency`, `bank_account`, `bank_account_type`, `pix_key_type`, `pix_key` (desdobramento do `bank/pix` do 03).
- **Adicionais:** `insalubrity_pct` (10, 20 ou 40), `hazard_pay` (bool); vale-transporte conforme o ponto em aberto 1.
- **Situação:** acrescenta `OnLeave`, derivada do afastamento vigente na leitura (`BusinessTime.Today`); a coluna `status` continua gravando só `Active`/`Terminated`.
- **Notificação** da troca de conta: `INotificationService` síncrono, na transação do comando (ADR 0019 §18), com link para a aba Pagamento do funcionário.
- **Concorrência:** `xmin` no funcionário (ADR 0019 §2), com 409 `concurrency_conflict` e "Recarregar".
- **Ids:** Guid v7. **Auditoria:** `IAuditable` em dependente e nos dados novos.

## Permissões, eventos e tabelas
- **Permissões:** `hr.view`, `hr.edit`, `hr.view_sensitive` (já no `ModuleCatalog`).
- **Eventos:** `EmployeeLeaveStarted` e `EmployeeLeaveEnded` (só id e datas).
- **Tabelas:** `hr.employees` (+ banco/Pix, adicionais, vale-transporte, CNH, reservista), `hr.employee_dependents`, `hr.employee_history` (`Leave`).

## Telas e estados
- No detalhe do funcionário (CAD-04b), abas novas antes do "Histórico de alterações": Dependentes, Pagamento e adicionais, Documentos; os afastamentos entram na "Linha do tempo" e no diálogo "Registrar afastamento".
- **Estados:** carregando; vazio ("Nenhum dependente cadastrado." + "Novo dependente"); erro; "Dados protegidos".
- **400 px:** abas com rolagem horizontal; dependentes em cartões; diálogo de afastamento em tela cheia.
- **Ajuda:** uma ajuda por aba nova, no padrão das abas que já existem: `Ajuda/rh-funcionario-dependentes.md`, `Ajuda/rh-funcionario-pagamento.md` e `Ajuda/rh-funcionario-documentos.md` (`rota: /app/rh/funcionarios/$id` e `aba`); a `Ajuda/rh-funcionario-linha-do-tempo.md` ganha "Afastamentos"; `<Dica>` em "Dependente de IR", "Salário-família" e "Motivo".
- **Mensagens (ADR 0021):** sucesso na região do topo; erro dentro do diálogo, com foco; próximo passo no `NextSteps` (depois do primeiro dependente: "Registrar conta para pagamento").

## Plano de testes
- **Unidade:** Pix por tipo (CA 4); CPF de dependente só com IR (CA 2); situação derivada do afastamento (CA 8).
- **Integração (Testcontainers):** dependentes, IDOR e CPF (CA 1–3); banco/Pix, notificação e máscara (CA 4–6); adicionais (CA 7); afastamento sobreposto, situação para `hr.view` e eventos sem tipo (CA 8, 9, 11); motivo limitado e fora do log (CA 10); anexos com `EmployeeDocument` (CA 12, 13); três camadas (CA 14); canário (CA 15).
- **E2E (Playwright):** registrar afastamento e ver só "Afastado" com um usuário de `hr.view`.

## Fora do escopo
- Férias e cálculo de folha: RH-02 e Fase 7.
- Exportação e anonimização: LGPD-01 (Fase 6).

## Pontos em aberto
1. **Vale-transporte** (pré-condição, PO): só "usa/não usa" (proposta) ou valor diário, ou linhas e tarifas.
2. **Tipos de afastamento** (pré-condição, PO): proposta de doença, acidente de trabalho, licença-maternidade, licença-paternidade, serviço militar e outros. Férias ficam para a folha.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração, arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Abas com estados; ajuda escrita; teste de cobertura passando.
- [ ] Padrões comuns dos cadastros aplicados.
- [ ] Auditoria ligada, com `[PersonalData]` e `SensitiveRead`.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` conferido; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `EmployeeDependent`, dados bancários/Pix, adicionais, afastamento e documentos.
- [ ] Comandos separados e rotas `/sensitive`; notificação de troca de conta.
- [ ] Dono `EmployeeDocument` no `AttachmentOwnerRegistry`.
- [ ] Abas e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (LGPD art. 11, conta bancária), @qa.
- Gate: CA 5, 9, 10, 13 e 15 obrigatórios.

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

- **Correções feitas na validação:** —
- **Pré-condições para `Ready`:** pontos em aberto 1 (vale-transporte) e 2 (tipos de afastamento), a decidir pelo PO antes do início da Fase 3. Não fazem parte da tabela 1.1 aprovada.
- **Prontidão:** 10/10 na checklist. **Veredito:** GO condicional. Status `Draft` até as duas decisões.

**Revisão na preparação da Fase 3 (21/10/2026, @sm River e @po Pax)**, conferida contra o código atual (`BemAqui.HR.Domain.Employee`, `EmployeesContract`, `AttachmentOwnerRules`, ajudas `rh-funcionario-*`):
- O código não tem nenhum campo desta story (`EmployeeStatus` só com `Active`/`Terminated`, `EmployeeHistoryKind` só com `Salary`/`Position`): o escopo continua válido.
- Ajustes de coerência: "toast" trocado pela mensagem no topo (ADR 0021, CA 8); `OnLeave` calculado na leitura e `Status` no `EmployeeSummary` do `IEmployees`, para a PRD-03a (CA 9); regras do dono `EmployeeDocument` com `ReadAction`/`WriteAction` e `OwnerExists` (ADR 0019 §7, CA 12 e 13); ajuda em um arquivo por aba, como as abas atuais; notificação síncrona (ADR 0019 §18); `xmin`; 400 px; `Bloqueia` cita a PRD-03a.
- Checklist reaplicada: 10/10. Veredito mantido: **GO condicional**, `Draft` até as decisões 1 e 2.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story criada pela quebra D6 da CAD-04b, com os CAs de segurança da revisão pré-Fase 1 §2.5 | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist): GO condicional, `Draft` | Pax (PO) |
| 21/10/2026 | 0.3 | Revisão na preparação da Fase 3 contra o código atual: mensagens da ADR 0021, `OnLeave` calculado e `Status` no `IEmployees`, regras do dono de anexo (ADR 0019 §7), ajuda por aba, notificação síncrona, `xmin`, 400 px, ordem 1 do Marco 3.1 | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, GO condicional, `Draft` até as decisões 1 e 2 | Pax (PO) |
