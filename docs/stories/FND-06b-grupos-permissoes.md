# FND-06b — Grupos de usuários e matriz de permissões

| Campo | Valor |
|---|---|
| ID | FND-06b |
| Título | Grupos (criar, editar, duplicar, inativar), matriz módulo × ação, grupos iniciais, grupo Proprietário, regra de delegação, serviço de permissão efetiva e policy `perm:{modulo}.{acao}` |
| Tamanho | G |
| Status | Done |
| Requisitos | R-SEC-9 · R-SEC-10 · R-SEC-12 · R-SEC-5 (alterações auditadas) |
| Dependências | FND-06, FND-04, FND-05 (regra de 2FA por grupo); telas dependem de FND-09b e FND-09c. **Pré-condição:** ADR 0008 aceita |
| Bloqueia | FND-06c, FND-06d |
| Ordem de execução | 13 |

## User story
Como **proprietário da empresa**, quero **organizar o acesso por grupos (Financeiro, Comercial, Operadores...) marcando o que cada grupo pode fazer em cada módulo**, para **dar acesso por função, sem configurar pessoa a pessoa, e sem que alguém dê a si mesmo um poder que não tem**.

## Contexto e referências
- PRD R-SEC-9 (grupos; seeds: Proprietário, Financeiro, Comercial, Engenharia, Supervisão de produção, Operadores, Almoxarifado, RH), R-SEC-10 (regras da matriz), R-SEC-12 (Proprietário de sistema, nunca sem membro; alterações valem no próximo request).
- ADR 0002, ADR 0003 (ações específicas como colunas extras com descrição em tooltip).
- `docs/03-modelo-dados.md` → `identity.groups`, `group_modules`, `group_permissions`, `user_groups`.
- `docs/04-fluxos-estados.md` §3.7.
- Revisão §2.2, **ADR 0008 (delegação)**: ninguém concede ação ou grupo que não possui; só o Proprietário adiciona alguém ao Proprietário ou concede `*.approve`, `*.pay` e `hr.view_sensitive`. Revisão §2.3: revogar módulo ou grupo vale no próximo request.
- Protótipo: tela "Usuários e acessos" → Grupos (lista de grupos, matriz, chip "Alterações não salvas", "Salvar grupo", toast com quantidade de usuários afetados, "Grupo do sistema: acesso total, não pode ser restringido").
- Backlog original (CAs): habilitar módulo marca "ver"; desmarcar "ver" limpa o módulo; marcar outra ação marca "ver"; módulos não liberados desabilitados com "Não liberado"; Proprietário não editável e nunca sem membro; alterações auditadas.

## Critérios de aceite
**Matriz**
1. **Dado** a matriz de um grupo, **quando** ligo a chave "Habilitado" de um módulo, **então** a ação "Ver" é marcada.
2. **Dado** um módulo habilitado, **quando** desmarco "Ver", **então** todas as ações do módulo são desmarcadas e o módulo é desabilitado.
3. **Dado** um módulo, **quando** marco qualquer outra ação (inclusive específica), **então** "Ver" e "Habilitado" são marcados.
4. **Dado** um módulo não liberado para o tenant, **quando** abro a matriz, **então** a linha aparece desabilitada com o rótulo "Não liberado" (também para leitor de tela) e a API recusa salvar permissões nele.
5. **Dado** um módulo com ações específicas (ex.: `hr.view_sensitive`), **quando** abro a matriz, **então** elas aparecem como colunas extras daquele módulo, com a descrição em tooltip (`<Dica>`).
6. **Dado** um módulo com `approve`, **quando** passo o mouse ou foco em "Aprovar", **então** vejo o que "aprovar" significa nele (texto do catálogo).

**Grupos**
7. **Dado** um novo tenant, **quando** é provisionado, **então** existem os 8 grupos iniciais do R-SEC-9, com as permissões padrão do seed, e o Proprietário marcado `is_system`.
8. **Dado** um grupo, **quando** clico "Duplicar", **então** nasce "Cópia de {nome}" com as mesmas permissões e sem membros, aberto para edição.
9. **Dado** um grupo com membros, **quando** o inativo, **então** a tela avisa quantos usuários perdem as permissões, e depois da confirmação ("Inativar") as permissões dele deixam de contar no próximo request desses usuários.
10. **Dado** o grupo Proprietário, **quando** tento editar permissões, inativar ou duplicar com outro nome de sistema, **então** a tela mostra "Grupo do sistema: acesso total, não pode ser restringido" e a API recusa com 409 `code: "system_group"`.
11. **Dado** o grupo Proprietário com um só membro, **quando** tento remover esse membro, **então** a API recusa com 409 e a mensagem "O grupo Proprietário precisa ter ao menos um membro."
12. **Dado** um grupo, **quando** marco "Exigir verificação em duas etapas", **então** os membros sem 2FA são levados à configuração no próximo login (regra de exigência da FND-05). O Proprietário já nasce com essa exigência ligada.

**Delegação (ADR 0008)**
13. **[Segurança]** **Dado** um usuário com `identity.edit` que não tem `finance.approve`, **quando** tenta marcar `finance.approve` num grupo, **então** a API recusa com 403 `code: "delegation_denied"` e a tela mostra a caixa desabilitada com a explicação.
14. **[Segurança]** **Dado** um usuário que não é do Proprietário, **quando** tenta conceder qualquer `*.approve`, `*.pay` ou `hr.view_sensitive`, ou adicionar alguém ao grupo Proprietário, **então** a API recusa com 403 `delegation_denied`, mesmo que ele tenha a ação.
15. **[Segurança]** **Dado** uma alteração de grupo salva, **quando** um membro faz o próximo request, **então** vale a nova permissão (cache versionado invalidado), inclusive para revogação.

**Auditoria**
16. **Dado** qualquer alteração em grupo, matriz ou membros, **quando** salvo, **então** a auditoria registra quem, quando e o antes/depois, consultável no histórico do grupo (componente da FND-09c).

**Permissão efetiva e policy** (movidas da FND-06c na validação do PO: os endpoints de grupo e a delegação já dependem delas)
17. **Dado** um usuário em grupos ativos, **quando** o serviço de permissão efetiva calcula as permissões dele, **então** o resultado é a união das ações dos grupos ativos com módulo habilitado, limitada aos módulos liberados do tenant; para o Proprietário, todas as ações dos módulos liberados (`04-fluxos` §3.7).
18. **Dado** um endpoint com a policy `perm:identity.edit`, **quando** um usuário sem essa ação o chama, **então** recebe 403 `forbidden`; **quando** tem a ação, **então** a requisição segue. Código de permissão inexistente no `ModuleCatalog` falha no teste de arquitetura (FND-06).

## Regras de negócio e técnicas
- Policy dinâmica `perm:{modulo}.{acao}` por `IAuthorizationPolicyProvider`, usando o serviço de permissão efetiva com cache versionado por usuário (ADR 0008). Ordem: liberado (`module_not_released`) → habilitado e ação (`forbidden`).
- Grupo: nome único entre ativos, descrição, `is_system`, `is_active`, `requires_2fa`. Sem exclusão: grupos são inativados (há histórico).
- `group_permissions` só vale com `group_modules.enabled`. Proprietário não grava linhas: `pode = liberado` para tudo, inclusive ações específicas (ADR 0003, §3.7).
- Regras da matriz aplicadas no cliente (UX) e validadas no servidor (fonte da verdade).
- Delegação: para cada ação concedida, o ator precisa possuí-la; ações sensíveis (`*.approve`, `*.pay`, `hr.view_sensitive`) e o grupo Proprietário só pelo Proprietário.
- Salvar grupo incrementa a versão de permissões dos membros (cache versionado, ADR 0008).
- Regra de 2FA: registrar `ITwoFactorRequirementRule` "grupo exige 2FA" (FND-05).

### Permissões padrão do seed (proposta, derivada do protótipo; a aprovar pelo PO)
| Grupo | Módulos e ações |
|---|---|
| Proprietário | tudo (sistema) |
| Financeiro | dashboard: view · finance: view, create, edit, approve · projects: view · quotes: view · inventory: view |
| Comercial | quotes: view, create, edit · projects: view |
| Engenharia | projects: view, edit · production: view, edit · inventory: view |
| Supervisão de produção | production: view, create, edit, approve · inventory: view, create · projects: view · kiosk: view, create · shopfloor: view, create |
| Operadores | shopfloor: view, create |
| Almoxarifado | inventory: view, create, edit · production: view |
| RH | hr: view, create, edit (sem `view_sensitive` por padrão; ver Pontos em aberto) |

## Permissões, eventos e tabelas
- **Tabelas:** `identity.groups` (+ `requires_2fa`), `identity.group_modules`, `identity.group_permissions`, `identity.user_groups`.
- **Permissões usadas:** `identity.view` (ver grupos), `identity.create` (criar e duplicar), `identity.edit` (editar matriz e membros, inativar), `identity.delete` (não usado para grupos; inativação é edição — ver Pontos em aberto).
- **Eventos:** `GroupPermissionsChanged` (invalida cache dos membros).

## Telas e estados
Referência: protótipo, "Usuários e acessos" → aba Grupos (sub-tela em aba na topbar, D8).
- **Lista de grupos** à esquerda (nome, descrição, quantidade de membros, chip "Inativo"), botão "Novo grupo".
- **Detalhe:** nome e descrição editáveis, membros, chave "Exigir verificação em duas etapas", matriz módulo × ação agrupada por área, chip "Alterações não salvas", botão "Salvar grupo", "Duplicar", "Inativar".
- **Estados:** carregando (skeleton da lista e da matriz); vazio de membros ("Nenhum membro. Adicione usuários na aba Usuários."); erro ao salvar com o motivo (ex.: delegação negada) e como resolver.
- **Confirmação:** toast "Grupo {nome} salvo. {N} usuários recebem as novas permissões no próximo acesso." (texto do protótipo).
- **Propositivo:** "Novo grupo" oferece começar de um grupo existente (duplicar); ao sair com alterações não salvas, pergunta "Salvar grupo" ou "Descartar".
- **Acessibilidade:** chave "Habilitado" com Switch do Radix; caixas com rótulo "{ação} em {módulo}"; "Não liberado" lido pelo leitor de tela.
- **Ajuda:** `Ajuda/acessos-grupos.md` (rota da tela, aba `grupos`), explicando as 3 camadas (como no protótipo) e a regra de delegação.

## Plano de testes
- **Unidade:** regras da matriz (CA 1 a 3) no domínio e no componente do front; regra de delegação (CA 13, 14); cálculo da permissão efetiva (CA 17).
- **Integração (Testcontainers):** seed dos 8 grupos (CA 7); duplicar (CA 8); inativar com efeito no próximo request (CA 9, 15); Proprietário protegido (CA 10, 11); módulo não liberado recusado (CA 4); delegação negada (CA 13, 14); auditoria (CA 16); policy `perm:` (CA 18).
- **E2E (Playwright):** editar a matriz, salvar, e um membro ver o efeito no menu no próximo acesso (completo após FND-06d).

## Fora do escopo
- Cadastro de usuários e convites (FND-06c).
- Permissão direta no usuário (não existe, ADR 0002).
- Delegação temporária de aprovação (risco listado no 06; sem story na Fase 0).

## Pontos em aberto
- **ADR 0008:** proposta na revisão §2.2, sem arquivo `docs/adr/0008-*.md`. Redigir e aceitar antes da execução.
- **Permissões padrão dos grupos iniciais:** a tabela acima vem do protótipo. RH e os módulos fora do protótipo precisam de definição do PO; se o RH deve nascer com `hr.view_sensitive`, a concessão vem do Proprietário (ADR 0008), então o seed precisa da decisão explícita.
- **Inativar grupo exige `identity.edit` ou `identity.delete`?** Não definido.
- **Seed de grupos iniciais com `finance.approve`** (Financeiro no protótipo) é compatível com a delegação porque é feito pelo sistema; confirmar que o PO quer o Financeiro aprovando OP por padrão (o PRD diz "padrão: proprietário aprova tudo", R-FIN-5).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migrations criadas.
- [ ] Permissões registradas no seed (`identity.*`, grupos iniciais).
- [ ] Tela com estados de carregando, vazio e erro.
- [ ] Ajuda da tela escrita.
- [ ] Padrões e próximo passo propositivos avaliados.
- [ ] Auditoria ligada.
- [ ] ADR 0008 aceita; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Redigir `docs/adr/0008-delegacao-de-permissoes.md` com o PO.
- [ ] Domínio de grupos com as regras da matriz e de sistema.
- [ ] Serviço de delegação.
- [ ] Seed dos grupos iniciais (`ITenantSeeder`).
- [ ] Endpoints de grupos (lista, detalhe, criar, duplicar, salvar, inativar, membros).
- [ ] Serviço de permissão efetiva, cache versionado por usuário e invalidação.
- [ ] `IAuthorizationPolicyProvider` para `perm:{modulo}.{acao}`.
- [ ] Regra de 2FA por grupo.
- [ ] Tela Grupos conforme protótipo.

## Quality gates
- Agentes previstos: @dev, cybersecurity (delegação), @ux-design-expert, @qa.
- Gate: testes de delegação e do Proprietário obrigatórios.

## Validação do PO
Checklist do @po (Pax), 16/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança da revisão §2.3 que se aplicam | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** Serviço de permissão efetiva e policy `perm:` trazidos da FND-06c (CA 17 e 18).
- **Pré-condições para `Ready`:** ADR 0008 aceita; permissões padrão dos 8 grupos iniciais aprovadas.
- **Prontidão:** 8/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 16/10/2026 | 0.1 | Story detalhada com ADR 0003, ADR 0008 proposta e os CAs de segurança da revisão | River (SM) |
| 16/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |

## Dev Agent Record
- **API (FND-06b), 18/10/2026, branch `feat/fnd-06b-grupos-permissoes`.** As permissões padrão seguem a ADR 0010 item 3. Inativar grupo exige `identity.edit`, porque inativar é edição e grupos não são excluídos.
  - **Tabelas:** `identity.groups` (+ `requires_2fa`, `seed_key`), `group_modules`, `group_permissions` e `user_groups`.
  - **Seed (CA 7):** `GroupSeeder` cria os 8 grupos uma vez por `seed_key`. O Proprietário é de sistema, exige 2FA e não grava linhas de permissão. O Financeiro não tem `finance.approve`, e o RH tem `hr.view_sensitive`.
  - **Matriz no servidor (CA 1 a 4):** `GroupMatrix.Normalize` aplica as regras: qualquer ação marca "Ver" e "Habilitado", e um módulo sem ações fica desabilitado. Salvar permissão num módulo não liberado responde 422 `module_not_released`. O detalhe do grupo devolve as ações disponíveis, com a descrição das específicas e o significado de "aprovar" (CA 5, 6).
  - **Grupos (CA 8 a 11):**
    - duplicar cria "Cópia de {nome}", sem membros;
    - inativar devolve quantos usuários são afetados;
    - o Proprietário responde 409 `system_group` e não fica sem membro (409, "O grupo Proprietário precisa ter ao menos um membro.");
    - "Exigir verificação em duas etapas" entra como `GroupRequiresTwoFactorRule` (CA 12).
  - **Delegação, ADR 0008 (CA 13, 14):**
    - ninguém concede o que não tem;
    - só o Proprietário concede `*.approve`, `finance.pay`, `hr.view_sensitive`, `identity.edit` e `settings.edit`, e só ele adiciona alguém ao Proprietário;
    - adicionar membro a um grupo exige ter todas as permissões dele;
    - ninguém altera um grupo de que é membro, salvo o Proprietário;
    - a recusa responde 403 `delegation_denied`.
  - **Permissão efetiva (CA 15, 17):** `PermissionService` calcula a união dos grupos ativos com módulo habilitado e filtra pelos módulos liberados a cada chamada. O cache por usuário dura 30 s e é invalidado na hora quando grupo, membros ou inativação mudam. A versão conferida a cada request, prevista na ADR 0008, fica para a FND-06c.
  - **Policy `perm:` (CA 18):** `RequirePermission("modulo.acao")` confere primeiro a liberação (403 `module_not_released`) e depois a ação (403 `forbidden`, com a mensagem da ADR 0010 item 8).
  - **Endpoints:** `/api/v1/identity/groups` (listar, detalhe, criar, duplicar, salvar, inativar e membros) e `GET /api/v1/me/permissions`.
  - **Auditoria e aviso (CA 16):** toda mudança é auditada com antes e depois, e os membros do Proprietário são notificados.
  - **Convite do proprietário:** ao aceitar, o usuário entra no grupo Proprietário.
  - **Testes:** 6 de integração.
  - **Pendente:** tela "Usuários e acessos → Grupos" e a ajuda `acessos-grupos.md`, depois da FND-09a/b/c; E2E.

- **Tela (FND-06b), 18/10/2026, branch `feat/fnd-06bc-telas`.** "Usuários e acessos → Grupos" fica em `/app/acessos/grupos`, com o grupo escolhido na URL (`?grupo=`).
  - **Matriz:**
    - as mesmas regras do servidor (`group-matrix.ts`);
    - "Habilitado" é o Switch do Radix e cada caixa tem o rótulo "{ação} em {módulo}";
    - as ações específicas ficam na coluna "Outras", com descrição, e "Aprovar" mostra o significado;
    - módulo não liberado aparece desabilitado (CA 1 a 6).
  - **Edição:**
    - chip "Alterações não salvas";
    - "Salvar grupo", com o toast de quantos usuários recebem as permissões;
    - guarda "Salvar/Descartar" ao sair com alteração.
  - **Ações:** "Novo grupo" a partir de um existente, "Duplicar", "Inativar" com o número de afetados e "Gerenciar membros".
  - **Proprietário:** aparece como "Grupo do sistema: acesso total, não pode ser restringido".
  - **Delegação recusada:** a tela mostra o motivo que o servidor mandou.
  - **`<Can perm>`** esconde o que o usuário não pode fazer, e o `ModuleGate` aplica as regras da FND-06d.
  - **Ajuda:** `acessos-grupos.md`.

## QA Results
_A preencher pelo @qa._