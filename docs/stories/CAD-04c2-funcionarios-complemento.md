# CAD-04c2 — Funcionários: dependentes, banco/Pix, adicionais, vale-transporte e documentos

| Campo | Valor |
|---|---|
| ID | CAD-04c2 |
| Título | Complemento do cadastro do funcionário para a folha: dependentes (IR e salário-família), dados bancários e Pix com troca notificada, adicionais, vale-transporte ("usa / não usa") e documentos extras |
| Tamanho | M (quebra da CAD-04c, D9) |
| Status | Ready |
| Requisitos | R-RH-1 (dependentes, dados bancários/Pix, vale-transporte, adicionais, documentos) · PRD §6 LGPD · R-SEC-5 · R-SEC-10 |
| Dependências | CAD-04c1 |
| Bloqueia | RH-02 |
| Ordem de execução | Fase 6, antes da RH-02 (movida da Fase 3, D9) |

> A linha desta story entra no backlog da Fase 6 quando as fases forem juntadas. A seção Fase 6 do backlog não foi editada nesta quebra.

## User story
Como **RH**, quero **completar o cadastro do funcionário com dependentes, conta para pagamento, adicionais, vale-transporte e documentos**, e como **proprietário**, quero **que conta bancária e documento do funcionário tenham proteção extra**, para **ter tudo o que a folha precisa sem expor o que a LGPD trata como sensível**.

## Contexto e referências
- PRD R-RH-1, PRD §6 LGPD.
- `docs/03-modelo-dados.md` → `hr.employees` (campos de banco/Pix, adicionais e vale-transporte), `hr.employee_dependents`, `shared.attachments`.
- **Revisão pré-Fase 3** (`docs/revisoes/2026-10-21-revisao-pre-fase3.md`): D9 (quebra da CAD-04c; esta parte não tem consumidor antes da folha), tabela 1.1 ponto 1 (vale-transporte), §2.2 CAD-04c (leitura sensível de documento, lista de anexos, aviso de Pix e titular, marca de conta alterada, exportação e anonimização).
- **ADR 0016** (rotas `/sensitive`, `SensitiveRead`, comandos separados, `AttachmentOwnerRegistry`), **ADR 0018** (retenção e titular), **ADR 0019 §7** e adendo de 21/10/2026 (dono `EmployeeDocument`, `AttachmentOwnerRules.AuditReads`), ADR 0019 §2 (`xmin`) e §18 (notificação síncrona), ADR 0021 (mensagens no topo).
- Revisão pré-Fase 1 §2.5: banco/Pix do funcionário, dependentes menores, anexos de funcionário.
- Padrões comuns a todos os cadastros: `docs/stories/README.md`.

## Critérios de aceite
**Dependentes**
1. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** cadastro um dependente com nome, nascimento, parentesco, "Dependente de IR" e "Salário-família" e clico "Salvar dependente", **então** ele é gravado na aba Dependentes.
2. **[Segurança]** **Dado** um dependente, **quando** "Dependente de IR" está desmarcado, **então** o CPF não é pedido nem aceito (400 se enviado); marcado, o CPF é obrigatório e validado; o mesmo CPF não se repete entre os dependentes do funcionário.
3. **[Segurança]** **Dado** a rota `/api/v1/hr/employees/{employeeId}/dependents/{dependentId}`, **quando** o dependente é de outro funcionário, **então** a API responde 404 (IDOR); todo o conteúdo da aba vem só por rota que exige `hr.view_sensitive`.

**Dados bancários e Pix**
4. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** informo banco (COMPE), agência, conta, tipo de conta, nome e CPF do titular e Pix (tipo e chave validada pelo tipo: CPF, e-mail, telefone ou aleatória) e confirmo "Salvar conta para pagamento", **então** são gravados pelo comando próprio.
5. **[Segurança]** **Dado** uma chave Pix do tipo CPF diferente do CPF do funcionário, ou um CPF do titular da conta diferente do CPF do funcionário, **quando** preencho, **então** o formulário mostra o aviso "A chave Pix (ou o titular) não é o CPF do funcionário. Confira antes de salvar.", a gravação continua possível, e o aviso fica visível na aba Pagamento e entra na notificação do CA 6.
6. **[Segurança]** **Dado** uma troca de banco, agência, conta, titular ou chave Pix de funcionário já cadastrado, **quando** salvo, **então** a troca grava histórico (auditoria mascarada) e notifica todos os membros do Proprietário pelo sino ("Conta para pagamento de {funcionário} alterada por {usuário}", com "chave ou titular diferente do CPF" quando o CA 5 se aplica), na mesma transação.
7. **[Segurança]** **Dado** uma conta para pagamento alterada há menos de 30 dias, **quando** abro o funcionário (cabeçalho e aba Pagamento) ou quando a folha (RH-02) lê a conta pelo `IEmployees`, **então** aparece a marca "Conta alterada há menos de 30 dias" (chip de alerta), com a data da troca; o contrato expõe a data da última troca para a RH-02 mostrar a mesma marca.
8. **[Segurança]** **Dado** a leitura dos dados bancários, **quando** acontece, **então** vem só de `/sensitive`, com `SensitiveRead` e `Cache-Control: no-store`; agência, conta, titular e chave Pix ficam mascarados também para quem tem a permissão, até clicar "Mostrar" (que registra a leitura).

**Adicionais e vale-transporte**
9. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** informo insalubridade (grau 10%, 20% ou 40%), periculosidade (sim/não) e vale-transporte ("Usa" ou "Não usa") e clico "Salvar adicionais", **então** são gravados; o valor do vale-transporte e o desconto são calculados na folha (RH-02), não no cadastro.

**Documentos extras**
10. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** informo CNH (número, categoria, validade) e certificado de reservista (opcional) e anexo documentos digitalizados (PDF, PNG ou JPG, até 10 MB), **então** são gravados; os anexos usam o dono `EmployeeDocument`, registrado por `AttachmentOwnerRegistration` com `ReadAction` e `WriteAction` = `hr.view_sensitive`, `OwnerExists` obrigatório, tipos pdf, png e jpg e até 10 MB (ADR 0019 §7, adendo de 21/10/2026).
11. **[Segurança]** **Dado** o dono `EmployeeDocument`, **quando** alguém lista os anexos ou baixa um documento, **então** a lista só responde com `hr.view_sensitive` (sem a permissão, 403, sem revelar quantos anexos existem), e cada download grava leitura sensível na auditoria pelo `AttachmentOwnerRules.AuditReads` (BuildingBlocks), com quem, quando, qual documento e o IP, depois de conferir que o funcionário existe.

**LGPD, permissões e auditoria**
12. **[Segurança]** **Dado** a exportação e a anonimização do funcionário (ADR 0018, story LGPD-01 da Fase 6), **quando** forem feitas, **então** incluem os dependentes, os dados bancários e Pix e os documentos (anexos e campos de CNH e reservista); a anonimização troca nome e CPF dos dependentes, conta, titular e chave Pix por marcador e apaga os anexos de documento, mantendo o que tem prazo legal (ADR 0018, retenção do funcionário desligado). Nesta story, os campos ficam `[PersonalData]` e a regra fica registrada no `docs/03-modelo-dados.md` para a LGPD-01.
13. **Dado** o módulo `hr` não liberado, sem grupo que o habilite ou sem `hr.view_sensitive`, **quando** chamo os endpoints desta story, **então** recebo 403 com o código da camada que barrou; o teste de arquitetura confere `RequireModule` e `perm:` em todos.
14. **[Segurança]** **Dado** os testes de allowlist e de canário (ADR 0016), **quando** rodam, **então** todas as propriedades novas são `[PersonalData]` ou estão na allowlist, e CPF de dependente, chave Pix, conta e CPF do titular não aparecem em `audit.audit_log` nem no log.

## Regras de negócio e técnicas
- **Campos sensíveis:** todos desta story.
- **Banco/Pix:** `bank_code`, `bank_agency`, `bank_account`, `bank_account_type`, `bank_holder_name`, `bank_holder_cpf`, `pix_key_type`, `pix_key`, `bank_changed_at` (desdobramento do `bank/pix` do 03).
- **Adicionais:** `insalubrity_pct` (10, 20 ou 40), `hazard_pay` (bool); vale-transporte `uses_transport_voucher` (bool), sem valor no cadastro.
- **Notificação** da troca de conta: `INotificationService` síncrono, na transação do comando (ADR 0019 §18), com link para a aba Pagamento do funcionário.
- **Concorrência:** `xmin` no funcionário (ADR 0019 §2), com 409 `concurrency_conflict` e "Recarregar".
- **Ids:** Guid v7. **Auditoria:** `IAuditable` em dependente e nos dados novos.

## Permissões, eventos e tabelas
- **Permissões:** `hr.view`, `hr.edit`, `hr.view_sensitive` (já no `ModuleCatalog`).
- **Eventos:** nenhum novo.
- **Contratos:** `IEmployees` expõe a data da última troca de conta para a RH-02 (CA 7).
- **Tabelas:** `hr.employees` (+ banco/Pix e titular, adicionais, vale-transporte, CNH, reservista), `hr.employee_dependents`; anexos em `shared.attachments` com o dono `EmployeeDocument`.

## Telas e estados
- No detalhe do funcionário (CAD-04b), abas novas antes do "Histórico de alterações": Dependentes, Pagamento e adicionais, Documentos.
- **Estados:** carregando; vazio ("Nenhum dependente cadastrado." + "Novo dependente"; "Nenhum documento anexado." + "Anexar documento"); erro; "Dados protegidos".
- **400 px:** abas com rolagem horizontal; dependentes em cartões.
- **Ajuda:** uma ajuda por aba nova, no padrão das abas que já existem: `Ajuda/rh-funcionario-dependentes.md`, `Ajuda/rh-funcionario-pagamento.md` e `Ajuda/rh-funcionario-documentos.md` (`rota: /app/rh/funcionarios/$id` e `aba`); `<Dica>` em "Dependente de IR", "Salário-família", "Titular da conta" e na marca "Conta alterada há menos de 30 dias".
- **Mensagens (ADR 0021):** sucesso na região do topo; erro dentro do diálogo, com foco; próximo passo no `NextSteps` (depois do primeiro dependente: "Registrar conta para pagamento").
- **Propositivo:** o titular vem preenchido com o nome e o CPF do funcionário; o tipo de chave Pix vem como CPF, com o CPF do funcionário sugerido.

## Plano de testes
- **Unidade:** Pix por tipo (CA 4); aviso de chave ou titular diferente do CPF (CA 5); marca de 30 dias (CA 7); CPF de dependente só com IR (CA 2).
- **Integração (Testcontainers):** dependentes, IDOR e CPF (CA 1–3); banco/Pix, aviso, notificação, marca e máscara (CA 4–8); adicionais e vale-transporte (CA 9); anexos com `EmployeeDocument`, lista sem permissão e leitura sensível no download (CA 10, 11); três camadas (CA 13); canário e allowlist (CA 12, 14).
- **E2E (Playwright):** trocar a conta para pagamento com chave Pix de outro CPF, ver o aviso, a marca e a notificação no sino do Proprietário.

## Fora do escopo
- Afastamentos e situação: CAD-04c1 (Fase 3).
- Cálculo do vale-transporte, férias e folha: RH-02 e Fase 7.
- Telas e serviço de exportação e anonimização: LGPD-01 (Fase 6); esta story só deixa o dado marcado e a regra registrada (CA 12).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, D9 e tabela 1.1, item 1):
1. **Escopo e fase:** dependentes, banco/Pix, adicionais, vale-transporte e documentos na Fase 6, antes da RH-02 (D9).
2. **Vale-transporte:** só "usa / não usa" no cadastro; o valor fica na folha.

**Pré-condição externa:** nenhuma.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração, arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Abas com estados; ajuda escrita; teste de cobertura passando.
- [ ] Padrões comuns dos cadastros aplicados.
- [ ] Auditoria ligada, com `[PersonalData]`, `SensitiveRead` e `AuditReads` no dono `EmployeeDocument`.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado (titular, `bank_changed_at`, `uses_transport_voucher`, regra da LGPD-01); story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `EmployeeDependent`, dados bancários/Pix com titular, adicionais e documentos.
- [ ] Comandos separados e rotas `/sensitive`; aviso de CPF; notificação de troca de conta.
- [ ] Dono `EmployeeDocument` no `AttachmentOwnerRegistry`, com `AuditReads`.
- [ ] Abas e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (conta bancária, documentos, LGPD), @qa.
- Gate: CA 5, 6, 8, 11 e 14 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK (ajustado: sem o R-RH-3, que ficou na CAD-04c1) |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas do CLAUDE.md) que se aplicam | OK (ajustado: CAs 5, 7, 11 e 12 da §2.2) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: Fase 6, antes da RH-02) |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (18/10/2026, primeira validação da CAD-04c):** —
- **Correções da revalidação (21/10/2026):** story nova pela quebra da CAD-04c (D9), com os CAs de dependentes, banco/Pix, adicionais e documentos da mãe; vale-transporte só "usa / não usa" (ponto 1, CA 9); CAs de segurança da §2.2: aviso de chave Pix ou titular diferente do CPF (CA 5), marca "Conta alterada há menos de 30 dias" (CA 7), lista de anexos só com `hr.view_sensitive` e leitura sensível no download pelo `AuditReads` (CA 11), dependentes, conta e documentos na exportação e anonimização da ADR 0018 (CA 12); dono `EmployeeDocument` pelo adendo da ADR 0019 §7; movida para a Fase 6.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | CAD-04c: story criada pela quebra D6 da CAD-04b, com os CAs de segurança da revisão pré-Fase 1 §2.5 | River (SM) |
| 18/10/2026 | 0.2 | CAD-04c: validação do PO (checklist): GO condicional, `Draft` | Pax (PO) |
| 21/10/2026 | 0.3 | CAD-04c: revisão na preparação da Fase 3 contra o código atual (ADR 0021, `OnLeave` calculado e `Status` no `IEmployees`, regras do dono de anexo, ajuda por aba, notificação síncrona, `xmin`, 400 px) | River (SM) |
| 21/10/2026 | 0.4 | CAD-04c: revalidação do PO: 10/10, GO condicional, `Draft` até as decisões 1 e 2 | Pax (PO) |
| 21/10/2026 | 0.5 | Quebra da CAD-04c (D9): dependentes, banco/Pix, adicionais, vale-transporte (ponto 1) e documentos; CAs de segurança da §2.2 (aviso de CPF, marca de 30 dias, `AuditReads`, lista de anexos, ADR 0018); dono `EmployeeDocument` (adendo da ADR 0019 §7); Fase 6, antes da RH-02 | River (SM) |
| 21/10/2026 | 0.6 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
