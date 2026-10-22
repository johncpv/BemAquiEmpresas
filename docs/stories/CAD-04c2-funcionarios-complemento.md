# CAD-04c2 — Funcionários: dependentes, banco/Pix, adicionais, vale-transporte e documentos

| Campo | Valor |
|---|---|
| ID | CAD-04c2 |
| Título | Complemento do cadastro do funcionário para a folha: dependentes (IR e salário-família), conta de salário (banco e Pix) com troca protegida (2FA de novo, segunda confirmação, aviso ao funcionário e ao Proprietário, quarentena de 30 dias e revisão expressa), adicionais, vale-transporte ("usa / não usa") e documentos extras |
| Tamanho | M (quebra da CAD-04c, D9) |
| Status | Ready |
| Requisitos | R-RH-1 (dependentes, dados bancários/Pix, vale-transporte, adicionais, documentos) · PRD §6 LGPD · R-SEC-5 · R-SEC-10 |
| Dependências | CAD-04c1 |
| Bloqueia | RH-02a, RH-02c (conta de salário e quarentena), LGPD-01a |
| Ordem de execução | 1 (Marco 6.2 "Folha lançada") |

## User story
Como **RH**, quero **completar o cadastro do funcionário com dependentes, conta para pagamento, adicionais, vale-transporte e documentos**, e como **proprietário**, quero **que conta bancária e documento do funcionário tenham proteção extra**, para **ter tudo o que a folha precisa sem expor o que a LGPD trata como sensível e sem desviar salário para a conta errada**.

## Contexto e referências
- PRD R-RH-1, PRD §6 LGPD.
- `docs/03-modelo-dados.md` → `hr.employees` (campos de banco/Pix, adicionais e vale-transporte), `hr.employee_dependents`, `shared.attachments`.
- **Revisão pré-Fase 3** (`docs/revisoes/2026-10-21-revisao-pre-fase3.md`): D9 (quebra da CAD-04c; esta parte não tem consumidor antes da folha), tabela 1.1 ponto 1 (vale-transporte), §2.2 CAD-04c (leitura sensível de documento, lista de anexos, aviso de Pix e titular, marca de conta alterada, exportação e anonimização).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D6 (2FA de novo ao trocar a conta de salário), D14 (proteções da conta de salário: `hr.view_sensitive`, 2FA, segunda confirmação, aviso ao funcionário e ao Proprietário, quarentena de 30 dias com revisão expressa), §4.3 (transversal: jobs com `tenantId`, anexos autorizados por registro), §4.4 padrão 8.
- **ADR 0036** (`docs/adr/0036-folha-no-financeiro.md`: conta de salário, quarentena, leitura da conta só no pagamento pela RH-02c); **ADR 0008** (adendo de 21/10/2026, D6: 2FA de novo, TOTP recente, nas ações críticas).
- **ADR 0016** (rotas `/sensitive`, `SensitiveRead`, comandos separados, `AttachmentOwnerRegistry`), **ADR 0018** (retenção e titular), **ADR 0019 §7** e adendo de 21/10/2026 (dono `EmployeeDocument`, `AttachmentOwnerRules.AuditReads`), ADR 0019 §2 (`xmin`) e §18 (notificação síncrona), ADR 0021 (mensagens no topo).
- Revisão pré-Fase 1 §2.5: banco/Pix do funcionário, dependentes menores, anexos de funcionário.
- Padrões comuns a todos os cadastros: `docs/stories/README.md`.

## Critérios de aceite
**Dependentes**
1. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** cadastro um dependente com nome, nascimento, parentesco, "Dependente de IR" e "Salário-família" e clico "Salvar dependente", **então** ele é gravado na aba Dependentes.
2. **[Segurança]** **Dado** um dependente, **quando** "Dependente de IR" está desmarcado, **então** o CPF não é pedido nem aceito (400 se enviado); marcado, o CPF é obrigatório e validado; o mesmo CPF não se repete entre os dependentes do funcionário.
3. **[Segurança]** **Dado** a rota `/api/v1/hr/employees/{employeeId}/dependents/{dependentId}`, **quando** o dependente é de outro funcionário, **então** a API responde 404 (IDOR); todo o conteúdo da aba vem só por rota que exige `hr.view_sensitive`.

**Conta de salário (banco e Pix)**
4. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** informo banco (COMPE), agência, conta, tipo de conta, nome e CPF do titular e Pix (tipo e chave validada pelo tipo: CPF, e-mail, telefone ou aleatória) e confirmo "Salvar conta para pagamento", **então** o diálogo pede o código do 2FA de novo (TOTP recente, ADR 0008 adendo de 21/10/2026, D6) e só então grava pelo comando próprio; sem código recente, a API responde 403 `mfa_step_up_required` e nada é gravado; vale para a primeira conta e para a troca.
5. **[Segurança]** **Dado** uma chave Pix do tipo CPF diferente do CPF do funcionário, ou um CPF do titular da conta diferente do CPF do funcionário, **quando** preencho, **então** o formulário mostra o aviso "A chave Pix (ou o titular) não é o CPF do funcionário. Confira antes de salvar." e salvar exige uma **segunda confirmação**: o `ConfirmDialog` lista o que não bate ("Titular: {nome mascarado}, CPF ***.456.789-**") e só habilita "Salvar mesmo assim" depois de marcar "Conferi com o funcionário" e informar o motivo (10 a 200 caracteres); sem essa confirmação, a API responde 422 `bank_owner_mismatch_unconfirmed`; o motivo e a divergência ficam no histórico (mascarados), e o aviso fica visível na aba Pagamento e entra nos avisos do CA 6.
6. **[Segurança]** **Dado** uma troca de banco, agência, conta, titular ou chave Pix de funcionário que já tinha conta, **quando** salvo, **então** na mesma transação a troca grava histórico (auditoria mascarada), notifica todos os membros do Proprietário pelo sino ("Conta para pagamento de {funcionário} alterada por {usuário}", com "chave ou titular diferente do CPF" quando o CA 5 se aplica) e enfileira o e-mail ao funcionário (template `bank-account-changed`, job com `tenantId`, só ids nos argumentos) no e-mail pessoal vigente e, se ele mudou nos últimos 30 dias, também no anterior: "A sua conta para receber o salário foi alterada em {data}. Se não foi você, fale com o RH ou com a direção da empresa." (sem banco, conta nem chave); sem e-mail pessoal, a notificação do Proprietário diz "O funcionário não tem e-mail pessoal: avise pessoalmente."
7. **[Segurança]** **Dado** uma troca de conta (CA 6), **quando** grava, **então** a conta nova entra em **quarentena de 30 dias** (`bank_quarantine_until` = data da troca + 30 dias, ADR 0036): o cabeçalho do funcionário e a aba Pagamento mostram o chip de alerta "Conta em quarentena até {data}" com a data da troca, e o `IEmployees` expõe a data da troca, o fim da quarentena e a revisão (sem dado bancário), para a folha (RH-02c) tirar a conta do pagamento automático e mostrar "Conta alterada" no fechamento; a primeira conta de um funcionário não entra em quarentena (ela aparece como "Novo na folha" no fechamento, RH-02c).
8. **[Segurança]** **Dado** um membro do Proprietário (ou quem tem `hr.approve` + `hr.view_sensitive`) que **não** fez a troca, **quando** abre a aba Pagamento de uma conta em quarentena e clica "Revisar conta", confere os dados (mostrados mascarados, com "Mostrar" que registra `SensitiveRead`), informa como conferiu (até 200 caracteres) e confirma "Liberar conta" com o 2FA de novo, **então** a quarentena termina (`bank_reviewed_at`, `bank_reviewed_by`), o chip vira "Conta revisada em {data}" e os membros do Proprietário são notificados; quem fez a troca recebe 403 "Você não pode revisar uma troca feita por você." (exceção do Proprietário único, com a marca "autorrevisado", como a D6 do Financeiro); sem revisão, a quarentena termina sozinha no fim do prazo.
9. **[Segurança]** **Dado** a leitura dos dados bancários, **quando** acontece, **então** vem só de `/sensitive`, com `SensitiveRead` e `Cache-Control: no-store`; agência, conta, titular e chave Pix ficam mascarados também para quem tem a permissão, até clicar "Mostrar" (que registra a leitura).

**Adicionais e vale-transporte**
10. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** informo insalubridade (grau 10%, 20% ou 40%), periculosidade (sim/não) e vale-transporte ("Usa" ou "Não usa") e clico "Salvar adicionais", **então** são gravados; o valor do vale-transporte e o desconto vêm da folha (RH-02a, lançada; Fase 7, calculada), não do cadastro.

**Documentos extras**
11. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** informo CNH (número, categoria, validade) e certificado de reservista (opcional) e anexo documentos digitalizados (PDF, PNG ou JPG, até 10 MB), **então** são gravados; os anexos usam o dono `EmployeeDocument`, registrado por `AttachmentOwnerRegistration` com `ReadAction` e `WriteAction` = `hr.view_sensitive`, `OwnerExists` obrigatório, tipos pdf, png e jpg e até 10 MB (ADR 0019 §7, adendo de 21/10/2026), no storage com prefixo do tenant.
12. **[Segurança]** **Dado** o dono `EmployeeDocument`, **quando** alguém lista os anexos ou baixa um documento, **então** a lista só responde com `hr.view_sensitive` (sem a permissão, 403, sem revelar quantos anexos existem), o anexo é autorizado pelo registro (o `attachmentId` precisa ser do funcionário da rota; 404 para o de outro) e cada download grava leitura sensível na auditoria pelo `AttachmentOwnerRules.AuditReads` (BuildingBlocks), com quem, quando, qual documento e o IP, depois de conferir que o funcionário existe.

**LGPD, permissões e auditoria**
13. **[Segurança]** **Dado** a exportação e a anonimização do funcionário (ADR 0018, stories LGPD-01a e LGPD-01b), **quando** forem feitas, **então** incluem os dependentes, os dados bancários e Pix e os documentos (anexos e campos de CNH e reservista); a anonimização troca nome e CPF dos dependentes, conta, titular e chave Pix por marcador e apaga os anexos de documento, mantendo o que tem prazo legal (ADR 0018, retenção do funcionário desligado). Nesta story, os campos ficam `[PersonalData]` e a regra fica registrada no `docs/03-modelo-dados.md` para a LGPD-01a e a LGPD-01b.
14. **Dado** o módulo `hr` não liberado, sem grupo que o habilite ou sem `hr.view_sensitive`, **quando** chamo os endpoints desta story, **então** recebo 403 com o código da camada que barrou; o teste de arquitetura confere `RequireModule` e `perm:` em todos.
15. **[Segurança]** **Dado** os testes de allowlist e de canário (ADR 0016), **quando** rodam, **então** todas as propriedades novas são `[PersonalData]` ou estão na allowlist, e CPF de dependente, chave Pix, conta, CPF do titular e o motivo da segunda confirmação não aparecem em claro em `audit.audit_log`, no log, em `email_logs.model` nem em argumento de job.

## Regras de negócio e técnicas
- **Campos sensíveis:** todos desta story.
- **Banco/Pix:** `bank_code`, `bank_agency`, `bank_account`, `bank_account_type`, `bank_holder_name`, `bank_holder_cpf`, `pix_key_type`, `pix_key`, `bank_changed_at`, `bank_changed_by`, `bank_quarantine_until`, `bank_reviewed_at`, `bank_reviewed_by`, `bank_mismatch_reason` (`[PersonalData]`), `bank_account_version` (inteiro que sobe a cada troca, usado pela RH-02c para conferir a conta no pagamento) — desdobramento do `bank/pix` do 03.
- **Quarentena (ADR 0036):** 30 dias corridos a partir da troca; revisão expressa por quem não fez a troca (CA 8); a primeira conta não entra em quarentena.
- **2FA de novo** (D6): filtro de TOTP recente do BuildingBlocks (ADR 0008 adendo), no salvar da conta e na revisão.
- **Adicionais:** `insalubrity_pct` (10, 20 ou 40), `hazard_pay` (bool); vale-transporte `uses_transport_voucher` (bool), sem valor no cadastro.
- **Notificação** da troca de conta: `INotificationService` síncrono, na transação do comando (ADR 0019 §18), com link para a aba Pagamento do funcionário; e-mail ao funcionário pela fila `email` (FND-08), template do sistema sem dado bancário.
- **Concorrência:** `xmin` no funcionário (ADR 0019 §2), com 409 `concurrency_conflict` e "Recarregar".
- **Ids:** Guid v7. **Auditoria:** `IAuditable` em dependente e nos dados novos.

## Permissões, eventos e tabelas
- **Permissões:** `hr.view`, `hr.edit`, `hr.view_sensitive`, `hr.approve` (revisar conta, com `hr.view_sensitive`; já no `ModuleCatalog`, no seed só o Proprietário).
- **Eventos:** nenhum novo.
- **Contratos:** `IEmployees` expõe `BankAccountStatus` (data da troca, fim da quarentena, revisão, versão da conta, primeira conta ou troca), sem dado bancário, para a RH-02c; a leitura dos dados bancários para o pagamento (`IEmployeeBankAccounts`) é da RH-02c.
- **Tabelas:** `hr.employees` (+ banco/Pix e titular, quarentena e revisão, adicionais, vale-transporte, CNH, reservista), `hr.employee_dependents`; anexos em `shared.attachments` com o dono `EmployeeDocument`; template de e-mail `bank-account-changed`.

## Telas e estados
- No detalhe do funcionário (CAD-04b), abas novas antes do "Histórico de alterações": Dependentes, Pagamento e adicionais, Documentos.
- **Padrões comuns das Fases 4 a 8:** 8 (bloco "Dados protegidos" padrão para quem não tem `hr.view_sensitive`).
- **Estados:** carregando; vazio ("Nenhum dependente cadastrado." + "Novo dependente"; "Nenhum documento anexado." + "Anexar documento"; "Nenhuma conta para pagamento." + "Registrar conta para pagamento"); erro; "Dados protegidos"; conta em quarentena (CA 7) com "Revisar conta" para quem pode.
- **Diálogos:** 2FA de novo no salvar e na revisão; segunda confirmação (CA 5) no `ConfirmDialog` com a lista do que não bate.
- **400 px:** abas com rolagem horizontal; dependentes em cartões; diálogos em tela cheia; botões de `ErrorState`/`EmptyState` com 44 px.
- **Ajuda:** uma ajuda por aba nova, no padrão das abas que já existem: `Ajuda/rh-funcionario-dependentes.md`, `Ajuda/rh-funcionario-pagamento.md` (explica a quarentena, a revisão e o aviso ao funcionário) e `Ajuda/rh-funcionario-documentos.md` (`rota: /app/rh/funcionarios/$id` e `aba`); `<Dica>` em "Dependente de IR", "Salário-família", "Titular da conta", "Conta em quarentena" e "Revisar conta".
- **Mensagens (ADR 0021):** sucesso na região do topo; erro dentro do diálogo, com foco; próximo passo no `NextSteps` (depois do primeiro dependente: "Registrar conta para pagamento"; depois de uma troca: "Revisar conta" para quem pode).
- **Propositivo:** o titular vem preenchido com o nome e o CPF do funcionário; o tipo de chave Pix vem como CPF, com o CPF do funcionário sugerido.

## Plano de testes
- **Unidade:** Pix por tipo (CA 4); aviso e segunda confirmação de chave ou titular diferente do CPF (CA 5); quarentena de 30 dias e primeira conta (CA 7); quem pode revisar (CA 8); CPF de dependente só com IR (CA 2).
- **Integração (Testcontainers):** dependentes, IDOR e CPF (CA 1–3); conta com 2FA de novo, sem código recente 403 (CA 4); segunda confirmação obrigatória, 422 sem ela (CA 5); notificação ao Proprietário e e-mail ao funcionário (atual e anterior) na fila, com `tenantId` e sem dado bancário (CA 6); quarentena exposta no `IEmployees` (CA 7); revisão por outro usuário, recusa a quem trocou, Proprietário único com a marca, 2FA (CA 8); máscara e `SensitiveRead` (CA 9); adicionais e vale-transporte (CA 10); anexos com `EmployeeDocument`, lista sem permissão, anexo de outro funcionário e leitura sensível no download (CA 11, 12); três camadas (CA 14); canário e allowlist (CA 13, 15).
- **E2E (Playwright):** trocar a conta para pagamento com chave Pix de outro CPF, passar pelo 2FA e pela segunda confirmação, ver a quarentena, a notificação no sino do Proprietário, e revisar com outro usuário.

## Fora do escopo
- Afastamentos e situação: CAD-04c1 (Fase 3).
- Cálculo do vale-transporte, férias e folha: RH-02a e Fase 7.
- Uso da conta no pagamento (contrato sensível, exclusão do pagamento automático, conferência da versão na OP): RH-02c.
- Telas e serviço de exportação e anonimização: LGPD-01a e LGPD-01b; esta story só deixa o dado marcado e a regra registrada (CA 13).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, D9 e tabela 1.1, item 1):
1. **Escopo e fase:** dependentes, banco/Pix, adicionais, vale-transporte e documentos na Fase 6, antes da RH-02a (D9).
2. **Vale-transporte:** só "usa / não usa" no cadastro; o valor fica na folha (CA 10).

Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, D6 e D14, ADR 0036):
3. **Proteções da conta de salário:** trocar exige `hr.view_sensitive` e 2FA de novo (CA 4); segunda confirmação quando titular ou chave Pix não batem com o CPF (CA 5); aviso ao funcionário no e-mail pessoal e ao Proprietário (CA 6); quarentena de 30 dias que tira a conta nova do pagamento automático (CA 7) e revisão expressa por quem não fez a troca (CA 8).

Decisões da revisão aplicadas: §4.3 (anexo autorizado pelo registro e storage com prefixo do tenant, CA 11, 12; job de e-mail com `tenantId`, CA 6); §4.4 padrão 8.

**Pré-condição externa:** nenhuma.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração, arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Abas com estados; ajuda escrita; teste de cobertura passando.
- [ ] Padrões comuns dos cadastros aplicados.
- [ ] Auditoria ligada, com `[PersonalData]`, `SensitiveRead` e `AuditReads` no dono `EmployeeDocument`.
- [ ] 2FA de novo no salvar e na revisão da conta (ADR 0008 adendo).
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado (titular, `bank_changed_at`, quarentena e revisão, `bank_account_version`, `uses_transport_voucher`, regra da LGPD-01a/01b); story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `EmployeeDependent`, dados bancários/Pix com titular, quarentena e revisão, adicionais e documentos.
- [ ] Comandos separados e rotas `/sensitive`; aviso e segunda confirmação de CPF; 2FA de novo; notificação e e-mail da troca de conta.
- [ ] `BankAccountStatus` no `IEmployees`.
- [ ] Dono `EmployeeDocument` no `AttachmentOwnerRegistry`, com `AuditReads`.
- [ ] Abas e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (conta bancária, quarentena, documentos, LGPD), @qa.
- Gate: CA 4, 5, 6, 7, 8, 9, 12 e 15 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas do CLAUDE.md) que se aplicam | OK (ajustado: CAs 4 a 8 da D14) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: primeira da Fase 6, Marco 6.2) |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (18/10/2026, primeira validação da CAD-04c):** —
- **Correções da revalidação pré-Fase 3 (21/10/2026):** story nova pela quebra da CAD-04c (D9), com os CAs de dependentes, banco/Pix, adicionais e documentos da mãe; vale-transporte só "usa / não usa" (ponto 1); CAs de segurança da §2.2: aviso de chave Pix ou titular diferente do CPF, marca de conta alterada, lista de anexos só com `hr.view_sensitive` e leitura sensível no download pelo `AuditReads`, dependentes, conta e documentos na exportação e anonimização da ADR 0018; dono `EmployeeDocument` pelo adendo da ADR 0019 §7; movida para a Fase 6.
- **Correções da revalidação (21/10/2026):** D14 e D6 aplicadas à conta de salário: 2FA de novo no salvar (CA 4), segunda confirmação com motivo quando titular ou chave não batem (CA 5), e-mail ao funcionário no endereço atual e no anterior (CA 6), a marca de 30 dias virou quarentena exposta no `IEmployees` (CA 7) e entrou a revisão expressa por outro usuário (CA 8, novo); anexo autorizado pelo registro (CA 12); canário com o motivo e os argumentos de job (CA 15); ordem 1 do Marco 6.2 e bloqueio da RH-02a, RH-02c e LGPD-01a; nomes LGPD-01a/01b; CAs renumerados (8 a 14 viraram 9 a 15).
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | CAD-04c: story criada pela quebra D6 da CAD-04b, com os CAs de segurança da revisão pré-Fase 1 §2.5 | River (SM) |
| 18/10/2026 | 0.2 | CAD-04c: validação do PO (checklist): GO condicional, `Draft` | Pax (PO) |
| 21/10/2026 | 0.3 | CAD-04c: revisão na preparação da Fase 3 contra o código atual (ADR 0021, `OnLeave` calculado e `Status` no `IEmployees`, regras do dono de anexo, ajuda por aba, notificação síncrona, `xmin`, 400 px) | River (SM) |
| 21/10/2026 | 0.4 | CAD-04c: revalidação do PO: 10/10, GO condicional, `Draft` até as decisões 1 e 2 | Pax (PO) |
| 21/10/2026 | 0.5 | Quebra da CAD-04c (D9): dependentes, banco/Pix, adicionais, vale-transporte (ponto 1) e documentos; CAs de segurança da §2.2 (aviso de CPF, marca de 30 dias, `AuditReads`, lista de anexos, ADR 0018); dono `EmployeeDocument` (adendo da ADR 0019 §7); Fase 6, antes da folha (RH-02a) | River (SM) |
| 21/10/2026 | 0.6 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 21/10/2026 | 0.7 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D6, D14, ADR 0036, §4.3, §4.4 padrão 8): conta de salário com 2FA de novo, segunda confirmação, aviso ao funcionário, quarentena de 30 dias e revisão expressa; nova ordem (Marco 6.2, ordem 1); CAs renumerados | River (SM) |
| 21/10/2026 | 0.8 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
