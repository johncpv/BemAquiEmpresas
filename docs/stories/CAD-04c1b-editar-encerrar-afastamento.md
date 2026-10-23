# CAD-04c1b — Funcionários: editar e encerrar antes um afastamento

| Campo | Valor |
|---|---|
| ID | CAD-04c1b |
| Título | Editar o afastamento previsto ou em curso, encerrar antes o em curso (o funcionário voltou mais cedo) e cancelar o que não começou, com rastro; ação `hr.view_health` para Doença e Acidente de trabalho |
| Tamanho | P |
| Status | Done |
| Requisitos | R-RH-3 (afastamentos) · PRD §6 LGPD · R-SEC-5 · R-SEC-10 · R-PRP-1 |
| Dependências | CAD-04c1 |
| Bloqueia | RH-01b (usa a `hr.view_health` entregue aqui), RH-01c (motivo de saúde no espelho) |
| Ordem de execução | Depois da CAD-04c1 (Marco 3.1), decisão do PO de 22/10/2026 |

## User story
Como **RH**, quero **corrigir um afastamento lançado errado e encerrar antes um afastamento quando o funcionário volta mais cedo**, e como **proprietário**, quero **que o tipo Doença e o tipo Acidente de trabalho só apareçam para quem pode ver dado de saúde**, para **a situação "Afastado" e o apontamento da fábrica refletirem o dia real, sem expor dado de saúde a quem só cuida do cadastro**.

## Contexto e referências
- A CAD-04c1 só registra afastamentos (Change Log 1.0, desvio 11: "não há edição nem encerramento antecipado"). Decisão do PO de 22/10/2026: fazer agora, com a `hr.view_health`.
- PRD R-RH-3, PRD §6 LGPD; LGPD art. 11 (dado de saúde é dado pessoal sensível).
- `docs/03-modelo-dados.md` → `hr.employee_history` (`Leave` e `LeaveChange`); `docs/04-fluxos-estados.md` → "Afastamento".
- **ADR 0003** (adendo de 21/10/2026: `hr.view_health`), **ADR 0008** (adendo: `hr.view_health` reservada ao Proprietário), **ADR 0016** (adendo de 22/10/2026: dado de saúde dentro do dado sensível do RH), ADR 0019 §2 (`xmin`), ADR 0021 (mensagens no topo).
- Padrões comuns a todos os cadastros: `docs/stories/README.md`.

## Critérios de aceite
**Editar**
1. **Dado** `hr.edit` + `hr.view_sensitive` e um afastamento **previsto** (início depois de hoje) ou **em curso**, **quando** clico "Editar" na linha do tempo, mudo tipo, datas ou motivo e confirmo "Salvar afastamento", **então** grava com as mesmas validações do registro: tipo obrigatório, fim previsto antes do início 422, início antes da admissão 422, motivo até 200 (422), sobreposição com outro afastamento não cancelado 409 `leave_overlap`, funcionário desligado 409 `employee_terminated` e versão antiga 409 `concurrency_conflict` com "Recarregar"; o diálogo mostra o período e o tipo antes → depois; a mensagem no topo diz "Afastamento salvo".
2. **Dado** um afastamento **em curso**, **quando** edito, **então** o início fica hoje ou antes (o `EmployeeLeaveStarted` já saiu) e o fim previsto fica hoje ou depois (para terminar antes existe o CA 4), ambos 422 no campo; afastamento **encerrado** ou **cancelado** não se edita (409 `leave_ended` / `leave_cancelled`).
3. **[Segurança]** **Dado** um afastamento de Doença ou Acidente de trabalho (antes ou depois da edição), **quando** quem não tem `hr.view_health` tenta registrar ou editar, **então** recebe 403 `forbidden`; na tela, esses tipos não aparecem na lista e o botão "Editar" some do afastamento protegido.

**Encerrar antes**
4. **Dado** um afastamento em curso, **quando** clico "Encerrar afastamento", informo a data da volta (vem com hoje) e confirmo "Encerrar afastamento", **então** a volta precisa ficar depois do início, até o fim previsto e nunca depois de hoje (422 `returnDate`); o fim previsto passa a ser a véspera da volta; o diálogo mostra o fim previsto e a volta antes → depois; a mensagem diz "Afastamento encerrado". Previsto responde 409 `leave_not_started` (use "Cancelar afastamento"); encerrado ou cancelado, 409.
5. **Dado** o encerramento, **quando** grava, **então** a situação do funcionário é recalculada na hora (lista, detalhe e `IEmployees.Status`) e o `EmployeeLeaveEnded` vai para o outbox na mesma transação, com a marca `end_published_at`; o job diário `hr-leave-events` não publica de novo, nem no dia seguinte ao fim previsto original.

**Cancelar**
6. **Dado** um afastamento que **ainda não começou**, **quando** clico "Cancelar afastamento", informo o motivo (obrigatório, até 200, sem CID) e confirmo "Cancelar afastamento", **então** é um cancelamento lógico (`cancelled_at`): ele sai da situação, da sobreposição e do job (nenhum evento sai), e continua na linha do tempo como "Cancelado", com o motivo; a mensagem diz "Afastamento cancelado". Afastamento já iniciado não se cancela, só se encerra (409 `leave_started`).

**Rastro e dado sensível**
7. **[Segurança]** **Dado** uma edição, um encerramento ou um cancelamento, **quando** grava, **então** entra uma entrada `LeaveChange` na linha do tempo, ligada ao afastamento, com o período antes e depois, o tipo antes e depois (na edição) e o motivo do cancelamento; a auditoria grava o antes e o depois do afastamento e da entrada nova com tipo, motivo e fim previsto mascarados (`[PersonalData]`); tipo e motivo nunca vão para log nem evento (os eventos levam só id e datas, como na CAD-04c1).
8. **[Segurança]** **Dado** quem tem `hr.view_sensitive` sem `hr.view_health`, **quando** abre os afastamentos (`/leaves/sensitive`), **então** todo afastamento, de qualquer tipo, vem com `healthProtected`, sem tipo, motivo (nem o do cancelamento) nem os tipos do rastro, só com as datas, e a tela mostra "Afastado" com a dica "Dados de saúde", sem "Editar" (decisão do PO de 22/10/2026: mostrar os outros tipos deixaria inferir o de saúde). Com `hr.view_health`, vê tudo. Quem não tem `hr.view_sensitive` continua vendo só a situação (CAD-04c1 CA 4).
9. **Dado** o módulo `hr` não liberado, sem grupo que o habilite, sem `hr.edit` ou sem `hr.view_sensitive`, **quando** chamo os endpoints de editar, encerrar e cancelar, **então** recebo 403 com o código da camada que barrou; o afastamento precisa ser do funcionário da rota (404 `leave_not_found`, sem IDOR); o teste confere `RequireModule` e `perm:` nos cinco endpoints de afastamento.

**Permissão**
10. **Dado** um tenant novo ou existente, **quando** o seed patch `hr-view-health` roda, **então** `hr.view_health` ("Ver dados de saúde") está no `ModuleCatalog` do HR, o grupo RH do seed a recebe uma vez (o Proprietário já tem tudo) e o cache de permissão dos membros é invalidado; só o Proprietário concede a ação (ADR 0008).

## Regras de negócio e técnicas
- **Estado do afastamento** (calculado na leitura, `BusinessTime.Today`): `Scheduled` (hoje antes do início), `Current` (entre o início e o fim previsto), `Ended` (depois do fim), `Cancelled` (`cancelled_at` preenchido, prevalece). Regras em `EmployeeLeaveRules` (domínio, testadas sem banco).
- **Editar:** só `Scheduled` e `Current`; mesmas validações do registro; no `Current`, início ≤ hoje ≤ fim previsto. Edição sem mudança não grava nada. Se a nova data já venceu, o evento devido sai na mesma transação (`LeaveEvents.PublishDue`), uma vez só.
- **Encerrar antes:** só `Current`; início < volta ≤ fim previsto e volta ≤ hoje; `leave_end_date` = véspera da volta.
- **Cancelar:** só `Scheduled`; motivo obrigatório; `cancelled_at`; nunca publica.
- **Rastro:** entrada `LeaveChange` em `hr.employee_history` (`leave_id`, `leave_change` = `Edited`/`EndedEarly`/`Cancelled`, `old_value`/`new_value` = período `aaaa-mm-dd/aaaa-mm-dd`, `previous_leave_type`, `leave_type`, `reason` = motivo do cancelamento). Não conta como afastamento e não sai na linha do tempo comum.
- **Dado de saúde:** `Illness` e `WorkAccident`; registrar, editar de ou para esses tipos exige `hr.view_health`; encerrar e cancelar mexem só nas datas e seguem com `hr.view_sensitive`.
- **Concorrência:** `xmin` do funcionário em toda gravação de afastamento (ADR 0019 §2).

## Permissões, eventos e tabelas
- **Permissões:** `hr.edit` + `hr.view_sensitive` (como o registro da CAD-04c1); `hr.view_health` (nova, seed patch `hr-view-health`, reservada ao Proprietário para conceder).
- **Endpoints:** `PUT /hr/employees/{id}/leaves/{leaveId}` (`UpdateEmployeeLeave`), `POST .../{leaveId}/end` (`EndEmployeeLeave`), `POST .../{leaveId}/cancel` (`CancelEmployeeLeave`); `GET /leaves/sensitive` ganha `state`, `healthProtected`, `cancelledAt` e `changes`.
- **Eventos:** `EmployeeLeaveStarted` e `EmployeeLeaveEnded`, sem mudança de formato.
- **Tabelas:** `hr.employee_history` ganha `leave_id`, `leave_change`, `previous_leave_type` e `cancelled_at` (migration `HrLeaveChanges`, só `AddColumn` e índice).

## Telas e estados
- Aba "Linha do tempo" do funcionário: cada afastamento com o chip de estado (Previsto, Vigente, Encerrado, Cancelado), as mudanças embaixo (antes → depois) e os botões "Editar", "Encerrar afastamento" e "Cancelar afastamento" conforme o estado e a permissão.
- **Estados:** carregando, vazio e erro da lista (já existentes); "Dados protegidos" sem `hr.view_sensitive`; "Afastado" com dica "Dado de saúde" sem `hr.view_health`.
- **400 px:** diálogos de editar e encerrar em tela cheia (`size="lg"`).
- **Verbos:** "Salvar afastamento" → "Afastamento salvo"; "Encerrar afastamento" → "Afastamento encerrado"; "Cancelar afastamento" → "Afastamento cancelado".
- **Propositivo:** a edição vem com o gravado; o encerramento vem com a volta em hoje; o diálogo mostra o impacto antes → depois; a mensagem de erro de estado diz qual ação usar.
- **Ajuda:** `Ajuda/rh-funcionario-linha-do-tempo.md` (seções "Corrigir, encerrar antes ou cancelar" e "Doença e acidente de trabalho"), `rh-funcionario.md`, `rh-funcionario-historico.md` e `acessos-grupos.md`; `<Dica>` em "Data da volta", no tipo (sem `hr.view_health`) e no afastamento protegido.

## Plano de testes
- **Unidade (sem banco):** estado no dia; `IsHealth`; regras de editar, encerrar e cancelar; encerramento publica o fim uma vez e o job não repete; cancelado não publica; edição que traz o início para hoje publica o começo; catálogo, reserva ao Proprietário e seed patch (`EmployeeLeaveChangeRulesTests`). Front: tipos por permissão, ações por estado, volta e motivo (`employee.test.ts`).
- **Integração (Testcontainers):** editar previsto e em curso com as validações e o rastro (CA 1, 2); desligado (CA 1); encerrar antes, situação e eventos uma vez (CA 4, 5); cancelar, job e sobreposição (CA 6); `hr.view_health` na leitura e na escrita (CA 3, 8); auditoria, log e evento sem tipo nem motivo (CA 7); seed patch uma vez (CA 10); três camadas nos cinco endpoints (CA 9) — `EmployeesTests.LeaveChanges.cs` e `EmployeesTests.Leaves.cs`.
- **Arquitetura:** allowlist do `[PersonalData]` com as colunas novas (`PersonalDataTests`).
- **E2E (Playwright, mocks):** editar, encerrar com a volta em hoje e cancelar a 400 px, com axe; sem `hr.view_health`, só "Afastado" e sem os tipos de saúde no diálogo.

## Fora do escopo
- Atestado médico e abono de ausência: RH-01b (usa a mesma `hr.view_health`).
- Reabrir afastamento encerrado ou cancelado.
- Perda do PIN do tablet para quem ganha `hr.view_health` (ADR 0008 e 0026, adendos): fica com a story do PIN (PRD-03b), que ainda não existe no código.

## Pontos em aberto
Decididos pelo PO em 22/10/2026: fazer agora a edição e o encerramento antes; `hr.view_health` em ação específica (ADR 0003), com seed no Proprietário e no RH; aplicar a regra nas telas e endpoints de afastamento da CAD-04c1; a RH-01b usa a mesma ação.

**Pré-condição externa:** nenhuma.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração, arquitetura e E2E escritos; unidade, front e E2E passando (integração e os testes de arquitetura que carregam todas as DLLs rodam na fila do CI/Docker).
- [x] Migration criada (não destrutiva: só `AddColumn` e índice).
- [x] Permissão só do `ModuleCatalog`, com seed patch.
- [x] Linha do tempo com estados; ajuda atualizada.
- [x] Padrões comuns dos cadastros aplicados (verbos, impacto antes → depois, 409 com "Recarregar", 400 px).
- [x] Auditoria ligada, com `[PersonalData]` e `SensitiveRead`.
- [ ] OpenAPI e cliente Orval regenerados (editados à mão no `openapi.json` e gerados pelo Orval; falta regravar com `BQ_UPDATE_OPENAPI=1`).
- [x] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `EmployeeLeaveRules` (estado, saúde, editar, encerrar, cancelar) e colunas novas em `EmployeeHistoryEntry`.
- [x] Comandos `UpdateLeaveAsync`, `EndLeaveAsync` e `CancelLeaveAsync`; leitura com estado, rastro e proteção de saúde.
- [x] `hr.view_health` no catálogo, seed patch `hr-view-health` e reserva ao Proprietário.
- [x] Diálogos, linha do tempo e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (LGPD art. 11), @qa.
- Gate: CA 3, 5, 7, 8 e 9 obrigatórios.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 22/10/2026 | 0.1 | Story criada pela decisão do PO de 22/10/2026 (editar e encerrar antes; `hr.view_health`) | @dev |
| 22/10/2026 | 1.1 | Decisões do PO: (1) confirmado: registrar Doença ou Acidente de trabalho exige `hr.view_health`; (2) confirmado: encerrar e cancelar afastamento de saúde não exigem `hr.view_health`; (3) quem não tem `hr.view_health` não vê o tipo nem o motivo de **nenhum** afastamento, de nenhum tipo (rota sensível, rastro e telas mostram só "Afastado" e as datas), para não deixar inferir o de saúde; com a ação, vê tudo. Sem mudança de contrato: o `healthProtected` passa a vir `true` em todos para quem não tem a ação. Na tela, sem a ação, "Editar" some de todos os afastamentos (o tipo não é visível); o servidor continua aceitando editar tipo que não é de saúde | @dev |
| 22/10/2026 | 1.0 | Implementada (@dev). Desvios: (1) registrar afastamento de Doença ou Acidente de trabalho também exige `hr.view_health` (quem não vê o tipo não grava o tipo), além de editar de ou para esses tipos; (2) encerrar e cancelar o afastamento de saúde não exigem `hr.view_health`, porque mexem só nas datas, que o RH sem a ação vê; (3) no afastamento em curso, o início não pode passar para depois de hoje e o fim não pode ficar antes de hoje (o começo já foi publicado; terminar antes é pelo encerramento); (4) o rastro guarda período e tipo antes e depois, mas não o motivo anterior (minimização; a auditoria registra que mudou, mascarado); (5) a edição não publica evento novo: o `EmployeeLeaveStarted` já publicado continua com as datas antigas (sem consumidor nesta fase); (6) "Excluir" virou "Cancelar afastamento" (cancelamento lógico com `cancelled_at`), com o verbo do padrão de cancelamento; (7) a proteção de saúde vale para o afastamento que foi de saúde em qualquer versão; (8) a perda do PIN para quem ganha `hr.view_health` fica para a story do PIN (não há PIN no código); (9) `openapi.json` editado à mão, regravar com `BQ_UPDATE_OPENAPI=1`. | @dev |
