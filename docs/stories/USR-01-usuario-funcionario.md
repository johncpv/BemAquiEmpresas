# USR-01 — Usuário ↔ funcionário: convite com funcionário e desligamento que desativa o login

| Campo | Valor |
|---|---|
| ID | USR-01 |
| Título | Convite com o campo opcional "Funcionário" (o vínculo nasce com o usuário convidado), "Convidar como usuário" na ficha do funcionário, coluna "Funcionário" e filtros "Sem funcionário" e "Sem grupo" na lista de usuários, e o desligamento que desativa o login vinculado |
| Tamanho | M |
| Status | Done (14/11/2026) |
| Requisitos | Decisões do PO de 14/11/2026 · FND-06c (convite, desativar, reativar) · CAD-04b CA 17 a 20 (desligamento e vínculo) · R-SEC-5 (auditoria) · R-PRP-2 e R-PRP-3 (propositivo, nada digitado duas vezes) · ADR 0006 (contratos entre módulos) · ADR 0008 (delegação) |
| Dependências | FND-06c, CAD-04b, FLH-05 (estorno do desligamento), PRD-03b (PIN do chão de fábrica) |
| Bloqueia | — |
| Ordem de execução | 1 (Pós-go-live: usuários e funcionários) |

## User story
Como **administrador do sistema e RH**, quero **convidar o funcionário já ligado à ficha dele e ter o login desligado junto com o funcionário**, para **não esquecer acesso aberto de quem saiu e saber, na lista de usuários, quem é funcionário e quem é externo**.

## Contexto e referências
- O vínculo existe desde a CAD-04b (CA 19 e 20): `identity.users.employee_id` (único, sem FK para `hr`), contrato `IUserEmployeeLink` (Identity.Contracts). Vincular e desvincular pela ficha exigem `hr.edit` + `identity.edit`; o vínculo não muda grupo nem permissão.
- Antes desta story: o convite (FND-06c) pedia só nome, e-mail e grupos; a lista de usuários não mostrava o funcionário; o desligamento (CAD-04b CA 17) só sugeria "Desativar o usuário" como próximo passo.
- O desligamento é um passo só ("Registrar desligamento", `POST /hr/employees/{id}/termination`): a situação passa a Desligado na hora e o `EmployeeTerminated` vai ao outbox. Não há rascunho. O estorno é o "Estornar desligamento" da aba Rescisão (FLH-05 CA 10), que volta o funcionário a ativo.
- A desativação manual é o `UserService.DeactivateAsync` (FND-06c CA 9): delegação do Proprietário (ADR 0008), "O grupo Proprietário precisa ter ao menos um membro ativo." e o `UserLifecycle` (security stamp novo, todas as famílias de refresh revogadas, inclusive as sessões por PIN). O PIN do chão de fábrica só entra com o usuário ativo (`PinService`), então o PIN fica sem efeito enquanto o login está desativado.

## Critérios de aceite
### Convite com funcionário
1. **Dado** `identity.create` e `hr.edit`, **quando** abro "Convidar usuário", **então** vejo o campo opcional "Funcionário" com os funcionários ativos ainda sem usuário (nem convite pendente), por nome e matrícula; "Nenhum (usuário externo)" é o padrão. Escolher o funcionário preenche o nome e, com `hr.view_sensitive`, o e-mail pessoal (se houver; a leitura fica auditada como `SensitiveRead` `Employee.InviteEmail`). Sem `hr.edit`, o campo não aparece.
2. **Dado** o convite com funcionário, **quando** envio, **então** o usuário nasce convidado já com `employee_id` (o funcionário fica reservado: sai da lista do campo e a ficha mostra o usuário com "Convite pendente"); a auditoria `UserInvited` leva o `employeeId`.
3. **Dado** um funcionário que já tem usuário (ativo, convidado ou desativado), **quando** convido com ele, **então** 409 `employee_already_linked` ("Este funcionário já tem um usuário vinculado. Desvincule antes.") e nenhum usuário é criado; funcionário desligado ou inexistente: 422 "Escolha um funcionário ativo da lista.".
4. **[Segurança]** **Dado** quem não tem `hr.edit`, **quando** manda `employeeId` no convite, **então** 403 `forbidden`; a lista do campo (`GET /hr/employees/invitable`) exige `hr.edit` na rota e `identity.create` no serviço. O convite sem funcionário continua igual (usuário externo).
5. **Dado** a ficha de um funcionário ativo sem usuário, com `hr.edit` e `identity.create`, **quando** clico "Convidar como usuário", **então** vou para Usuários com o convite aberto e o funcionário já escolhido. O mesmo vale para o "Convidar usuário" do aviso de holerite (RH-02b CA 7).

### Lista de usuários
6. **Dado** Usuários e acessos → Usuários, **quando** abro, **então** a coluna "Funcionário" mostra o nome do funcionário (link para a ficha com `hr.view`; "(desligado)" quando for o caso) ou "Externo" quando não há vínculo; a busca também acha pelo nome do funcionário.
7. **Dado** o filtro "Mostrar", **quando** escolho "Sem funcionário" ou "Sem grupo", **então** a lista mostra só os usuários externos ou só os que não estão em nenhum grupo; o filtro fica na URL (`?mostrar=`).

### Desligamento e login
8. **Momento:** o login é desativado quando o desligamento é **registrado** (efetivado), na mesma requisição, logo depois de o desligamento ser gravado. Não existe rascunho de desligamento; a simulação da rescisão (FLH-05 CA 1) não grava nada e não mexe no login.
9. **Dado** um funcionário com usuário vinculado ativo (ou convidado), **quando** registro o desligamento, **então** o login é desativado pelo mesmo caminho do "Desativar" de Usuários (security stamp novo, sessões e sessões por PIN revogadas, PIN sem efeito), com auditoria `DeactivatedByTermination` (quem desligou, o `employeeId`), e a conclusão diz "O login de Fulano foi desativado.".
10. **Dado** o login já desativado, **quando** desligo, **então** nada muda no login e a conclusão diz "O login de Fulano já estava desativado.". Sem usuário vinculado, nada a dizer.
11. **Dado** um usuário vinculado do grupo Proprietário, **quando** desligo, **então** o desligamento vale e o login **não** é desativado se for o último membro ativo do Proprietário (regra do grupo) ou se quem desligou não for do Proprietário (delegação, ADR 0008); a conclusão avisa o motivo, oferece "Abrir Usuários" e a auditoria grava `TerminationDeactivationSkipped`.
12. **Dado** o desligamento estornado depois ("Estornar desligamento", FLH-05 CA 10), **quando** o estorno conclui, **então** o login **não** é reativado sozinho; com o login desativado e `identity.edit`, a conclusão oferece "Reativar o login de Fulano" (o "Reativar" de Usuários, FND-06c CA 11).

## Regras de negócio e técnicas
- **Entre módulos (ADR 0006):** o HR chama o contrato `IUserEmployeeLink` (Identity.Contracts), como já fazia para vincular: `LinkedEmployeeIdsAsync` (a lista do convite) e `DeactivateForTerminationAsync` (o desligamento). Foi escolhido o contrato síncrono, não um consumidor do `EmployeeTerminated` no outbox, porque a conclusão do desligamento precisa dizer na hora o que aconteceu com o login (CA 9 a 11), e o vínculo já é feito por esse contrato. O `EmployeeTerminated` continua no outbox, sem consumidor no Identity.
- **Desativar sem duplicar regra:** a implementação do contrato chama o `UserService.DeactivateAsync` com quem está desligando como autor; as regras do Proprietário e o `UserLifecycle` são as mesmas da tela de Usuários.
- **Transação:** o desligamento é gravado antes; se a desativação falhar por regra (Proprietário), o desligamento continua valendo e a conclusão avisa. O login nunca é desativado sem o desligamento gravado.
- **Reserva do funcionário:** o vínculo é gravado na criação do usuário convidado (não no aceite), no mesmo `SaveChanges` do convite; o índice único de `employee_id` decide a corrida (409). Cancelar o convite não desfaz o vínculo; para liberar o funcionário, "Desvincular usuário" na ficha.
- **Usuário convidado vinculado:** `EmployeeOfUserAsync` continua só para usuário ativo (apontamento, ponto); o convidado não aponta até aceitar.

## Permissões, eventos e tabelas
- **Permissões:** nenhuma ação nova no `ModuleCatalog`. Convite com funcionário: `identity.create` (rota) + `hr.edit` (serviço). Lista do campo: `hr.edit` (rota) + `identity.create` (serviço); e-mail só com `hr.view_sensitive`. Desligamento: `hr.edit` (como antes). "Reativar o login": `identity.edit`.
- **Endpoints:** `GET /api/v1/hr/employees/invitable` (novo); `POST /api/v1/identity/users/invite` aceita `employeeId`; `POST /api/v1/hr/employees/{id}/termination` devolve `{ employee, login }` (`TerminationLogin`: `outcome`, `userId`, `userName`, `message`); `GET /api/v1/identity/users` traz `employeeId`, `employeeName`, `employeeTerminated`; `LinkableUser` traz `status`.
- **Eventos:** nenhum novo.
- **Tabelas:** nenhuma; sem migration (`identity.users.employee_id` e o índice único já existem).
- **Auditoria:** `UserInvited` com `employeeId`; `Deactivated` (do `UserLifecycle`) + `DeactivatedByTermination` ou `TerminationDeactivationSkipped`; `SensitiveRead` `Employee.InviteEmail` quando a lista do convite traz e-mail.

## Telas e estados
- **Convite** (`InviteDialog`): campo "Funcionário" (select) com dica e o texto de apoio "Opcional. Deixe em branco para quem não é funcionário (ex.: a contadora)."; vazio: "Todos os funcionários ativos já têm usuário.".
- **Usuários** (`/app/acessos/usuarios`): coluna "Funcionário" e filtro "Mostrar".
- **Ficha do funcionário** (Dados gerais → Usuário do sistema): "Convidar como usuário" (botão principal) ao lado de "Vincular usuário"; chip "Convite pendente" ou "Login desativado" no usuário vinculado.
- **Registrar desligamento:** o diálogo avisa que o login é desativado; a conclusão mostra a mensagem do login (verde) ou o aviso do Proprietário (âmbar) acima dos próximos passos.
- **Rescisão → Estornar desligamento:** próximo passo "Reativar o login de Fulano".
- **Ajuda:** `acessos-usuarios.md`, `rh-funcionario.md`, `rh-rescisao.md` atualizadas.

## Plano de testes
- **Integração (Testcontainers), `EmployeesTests.UserLink`:** convite com funcionário grava o vínculo e reserva o funcionário (CA 1, 2, 6); funcionário já vinculado é recusado (CA 3); sem `hr.edit` não vincula nem lista, e o externo continua (CA 4); o desligamento desativa o login vinculado com auditoria e sessões revogadas (CA 9), mantém o Proprietário quando quem desliga não é do Proprietário (CA 11) e não diz nada sem usuário.
- **Vitest:** `users-filter.test.ts` (filtro "Mostrar" e o texto da coluna, CA 6 e 7).
- Acesso por rota e cobertura da ajuda ficam com as varreduras (`EndpointAuthorizationTests`, teste de cobertura).

## Fora do escopo
- Reativar o login sozinho no estorno do desligamento (decisão do PO: só como próximo passo).
- Desfazer o desligamento sem rescisão calculada (não existe estorno sem a rescisão; continua como hoje).
- Apagar o PIN do chão de fábrica no desligamento: a desativação manual não apaga, e o PIN não entra com o login desativado; reativar devolve o PIN, como na desativação manual.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de integração (Testcontainers) e Vitest passando.
- [x] Permissões só do `ModuleCatalog` (nenhuma nova).
- [x] Telas com estados; ajuda das telas tocadas atualizada.
- [x] Auditoria ligada.
- [x] OpenAPI e cliente Orval regenerados; story marcada no backlog.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 14/11/2026 | 1.0 | Story escrita e implementada a partir das decisões do PO de 14/11/2026: (1) o desligamento desativa o login vinculado automaticamente, na efetivação (o registro do desligamento; não há rascunho), pelo caminho da desativação manual; o estorno não reativa sozinho e oferece "Reativar o login"; o último membro ativo do Proprietário não é desativado e a conclusão avisa; (2) pode existir usuário externo, o vínculo continua opcional. | Claude (dev) |
| 14/11/2026 | 1.0 | **Recomendação (aguarda PO):** login de Proprietário vinculado a funcionário desligado por quem **não** é do Proprietário também não é desativado (regra de delegação da ADR 0008, já aplicada no "Desativar" manual: "Só quem é do grupo Proprietário pode desativar outro Proprietário."); a conclusão avisa e oferece "Abrir Usuários". A decisão do PO citava só o último membro; foi aplicada a regra mais conservadora, que reusa a existente. | Claude (dev) |
| 14/11/2026 | 1.0 | **Recomendação (aguarda PO):** no convite, o e-mail pessoal do funcionário só é preenchido para quem tem `hr.view_sensitive` (o e-mail é dado pessoal da rota `/sensitive`, CAD-04b CA 9), com `SensitiveRead`; sem a ação, quem convida digita o e-mail. | Claude (dev) |
| 14/11/2026 | 1.0 | Vínculo gravado na criação do usuário convidado (não no aceite), para reservar o funcionário; cancelar o convite não desfaz o vínculo (desvincular pela ficha). | Claude (dev) |
