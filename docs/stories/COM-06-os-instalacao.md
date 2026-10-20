# COM-06 — OS de instalação

| Campo | Valor |
|---|---|
| ID | COM-06 |
| Título | OS de instalação criada a partir do projeto, com executor terceirizado (papel Instalador), valor combinado que gera o CP previsto do instalador com data estimada, vinculado ao projeto, etapas e checklist de entrega técnica; aceite técnico que confirma o CP, entrega o projeto e libera o recebível do marco |
| Tamanho | G (re-estimada de M: cria a primeira tabela de OS do Production e a cadeia de eventos entre três módulos) |
| Status | Ready |
| Requisitos | R-PRJ-3 (OS de instalação por item/projeto, executor terceirizado, etapas, checklist de entrega técnica) · R-FIN-4 (CP de instaladores) · R-PRJ-6 (aceite técnico libera parcela) · R-COM-4 (numeração OS) · R-COM-3 · R-PRP-2 · R-PRP-7 |
| Dependências | COM-04a (`IProjects`), COM-04b (liberação por `TechnicalAcceptance`, registro de gatilhos), COM-05 (aba Itens e OS), FIN-02 (`IPayables`), FIN-03a (OP), CAD-01a (papel Instalador), FND-09e |
| Bloqueia | PRD-01 (reaproveita `production_orders`), PNL-01 (instalações em andamento), DSH-01 (entregas) |
| Ordem de execução | 16 (Marco 2.3 "Contrato ao caixa"; fecha o ciclo) |

## User story
Como **responsável pelo projeto**, quero **abrir a OS de instalação para o instalador terceirizado, acompanhar as etapas na obra e registrar o aceite técnico do cliente**, para **a empresa responder pela instalação, pagar o instalador pelo combinado e liberar a última parcela do cliente**.

## Contexto e referências
- PRD §0 (a Konebras terceiriza a instalação mas responde por ela ao cliente final), R-PRJ-3; §4 (OS de Instalação → CP do instalador).
- Backlog COM-06: "valor gera CP do instalador vinculado ao projeto"; CA "aceite técnico dispara evento que libera o recebível vinculado".
- `03-modelo-dados` → `production.production_orders` (`type` Installation, `project_id`, `project_item_id`, `executor_party_id`), `production_order_steps` (`checklist_result`).
- `04-fluxos-estados` §1 "Projeto" (`→ Installing` OS de instalação iniciada; `→ Delivered` entrega técnica aceita); §2 (`TechnicalAcceptance` → Finance libera receivables).
- Revisão pré-Fase 2: seção 1.1 item 27 (checklist padrão validado com a Konebras antes desta story: P-22), §2.1-M13 (CP `Planned` com data estimada), §2.2 baixos (mudança do valor notifica `finance.edit`; a OP mostra "combinado × orçado"), §2.3 (texto do aceite; checklist com alvos de 56 px).
- P-15 (decidida): retenções do serviço tomado são campos opcionais do título, sem cálculo (FIN-02 CA 4).
- `ModuleCatalog`: `production` (`view`, `create`, `edit`, `approve` = "liberar na qualidade", `delete`). No seed, a Supervisão de produção tem `production.view/create/edit/approve` e a Engenharia `production.view/edit`.

## Critérios de aceite
**Permissões do seed**
1. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed patch `installation-orders-permissions` roda, **então** a Engenharia do seed ganha `production.create` (a Supervisão de produção já tem `view`, `create`, `edit` e `approve`), uma vez, com o `perm_version` dos membros incrementado.

**Criar a OS**
2. **Dado** `production.create` e um projeto `Planned`, `InProgress` ou `Fabricated`, **quando** clico "Nova OS de instalação" no projeto, escolho os itens do projeto (padrão: todos), o instalador (terceiro ativo com papel Instalador; 422 "Escolha um terceiro com o papel Instalador."), o valor combinado (`numeric(18,2)`, > 0, padrão: o orçado de Instalação do projeto ainda sem OS, quando o usuário tem `projects.view_cost`), a data prevista de início e fim (`date`) e clico "Criar OS de instalação", **então** a OS nasce `Open` com número `OS-2026-00001` (`INumberSequence`, tipo `OS`), tipo `Installation`, e as etapas e o checklist padrão (CA 4).
3. **Dado** a OS criada, **quando** grava, **então** na mesma transação o Production chama `IPayables.CreateFromOriginAsync(Installer, osId, instalador, projeto, Installation, valor, sem vencimento, estimatedDueDate, …, planned: true)` com a **data estimada** = `planned_end` + a primeira parcela da condição padrão do instalador (sem condição, o próprio `planned_end`), e o título a pagar nasce `Planned` com a marca de estimativa (entra no fluxo de caixa com a `Tag` "Estimado", FIN-05 CA 5), natureza Instalação, descrição "Instalação {OS} — {projeto}"; um rollback da OS desfaz o título.
4. **Dado** a OS nova, **quando** abro, **então** ela tem as etapas padrão Preparação da obra, Montagem na obra, Testes e ajustes e Entrega técnica, e o checklist de entrega técnica vindo do **modelo configurável do tenant** (P-22, decidida em 20/10/2026: em Produção → Configurações → Modelo da OS de instalação, com `production.edit`, o tenant edita as etapas e os itens do checklist; o seed traz o modelo padrão sugerido: equipamento instalado conforme projeto, testes de segurança feitos, cliente orientado, documentação entregue, área limpa); etapas e checklist da OS continuam editáveis enquanto ela está `Open`, e mudar o modelo não altera as OS já criadas.
5. **Dado** um item do projeto já coberto por outra OS de instalação não cancelada, **quando** tento incluí-lo, **então** 422 "O item {descrição} já está na {OS}."

**Execução**
6. **Dado** `production.edit`, **quando** clico "Iniciar instalação" e informo a data, **então** a OS passa a `InProgress`, o evento `InstallationStarted` vai para o outbox e o consumidor do Commercial passa o projeto a `Installing` (se ainda não estiver).
7. **Dado** uma OS `InProgress`, **quando** marco uma etapa como concluída, **então** grava data e usuário; as etapas são sequenciais (a seguinte só conclui depois da anterior, `04-fluxos` §1) e a última, "Entrega técnica", só conclui pelo aceite (CA 9).
8. **Dado** o checklist de entrega técnica, **quando** marco os itens (feito, não se aplica) e anexo fotos ou o termo assinado (dono `ProductionOrder`: png, jpg ou pdf até 10 MB, `OwnerExists`, ClamAV), **então** cada marcação grava usuário e data em `checklist_result`; no celular, cada item tem alvo de toque de no mínimo 56 px.

**Aceite técnico**
9. **Dado** `production.approve`, as etapas anteriores concluídas e todos os itens do checklist marcados, **quando** clico "Registrar aceite técnico", informo a data (≤ hoje), o nome de quem aceitou pelo cliente e confirmo no diálogo (`size="lg"`) com o texto "Confirma o título a pagar de {valor} ao instalador (vence em {data}, segue para ordem de pagamento) e libera a parcela {marco} de {valor} do cliente." (os valores do cliente só com `finance.view`), **então** na mesma transação: a etapa "Entrega técnica" e a OS ficam `Completed`, o Production chama `IPayables.ConfirmAsync(Installer, osId, valor, vencimento)` com o vencimento pela condição padrão do instalador a partir da data do aceite (sem condição, a data do aceite), e o evento `InstallationAccepted` vai para o outbox; a mensagem de sucesso diz "Aceite técnico registrado".
10. **Dado** o `InstallationAccepted`, **quando** o consumidor do Commercial processa e todas as OS de instalação não canceladas do projeto estão `Completed`, **então** o projeto passa a `Delivered` e o Commercial publica `TechnicalAcceptance` e `ProjectDelivered` (`{ projectId, contractId, occurredOn }` = data do aceite); a COM-04b libera os títulos com esses gatilhos, mesmo que o evento chegue antes do título (registro de gatilhos, ADR 0019 §1).
11. **Dado** checklist incompleto ou etapa anterior aberta, **quando** tento o aceite, **então** o diálogo lista o que falta como pré-condição (botão desabilitado) e a API responde 422 "Conclua as etapas e o checklist antes do aceite." com a lista.

**Alterações e cancelamento**
12. **Dado** uma OS `Open` ou `InProgress`, **quando** mudo o valor combinado, **então** o título `Planned` é atualizado pelo `IPayables.UpdatePlannedAsync` (mesma transação) e os membros com `finance.edit` são notificados ("Valor da instalação {OS} mudou de {antes} para {depois} por {usuário}."); com o título já `Open` (depois do aceite), o valor não muda pela OS (409 "Ajuste no título a pagar.").
13. **Dado** `production.edit` e uma OS `Open` (não iniciada), **quando** clico "Cancelar OS", informo o motivo e confirmo, **então** a OS fica `Cancelled` e o título `Planned` do instalador é cancelado por `IPayables.CancelFromOriginAsync`; OS `InProgress` só é cancelada com `production.approve`, e o título continua `Planned` para o financeiro decidir (notificação a `finance.edit`; com a OS cancelada, o Finance libera o cancelamento do título com `finance.edit`, ADR 0019 §4, consultando `IInstallationOrders.StatusAsync`).
14. **Dado** o `ContractCancelled` (COM-04b), **quando** o consumidor do Production processa, **então** cancela as OS de instalação `Open` do projeto (e os títulos `Planned`) e lista as `InProgress` para decisão manual (`04-fluxos` §1), notificando o responsável do projeto.

**Ordem de pagamento (§2.2 baixos)**
15. **Dado** uma OP com um título de origem `Installer` (FIN-03a), **quando** o aprovador abre a OP, **então** o item mostra "Combinado {valor} · Orçado {valor}" (orçado de Instalação do projeto, por `IProjects`, só para quem tem `projects.view_cost`), com o aviso âmbar quando o combinado passa do orçado.

**Permissões e segurança**
16. **Dado** o módulo `production` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem a ação exigida, 403 e o botão some. `RequireModule` e `perm:`; IDOR em `/production-orders/{id}/steps/{stepId}` e `/attachments/{attId}`; projeto, itens e instalador validados pelos Contracts dos donos.
17. **[Segurança]** **Dado** o aceite técnico, **quando** dois aceites chegam ao mesmo tempo, **então** só um grava (transição pela entidade rastreada com `xmin`) e o CP e o evento não são duplicados.
18. **Dado** qualquer alteração na OS, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário e data.

## Regras de negócio e técnicas
- **OS de instalação** em `production.production_orders` (`type = Installation`) com `contract_value` (`numeric(18,2)`), `planned_start`/`planned_end` como `date`, `accepted_on`, `accepted_by_name` (nome de quem aceitou pelo cliente, `[PersonalData]`), `cancel_reason`; itens cobertos em `production_order_project_items`.
- **Situação da OS de instalação:** `Open → InProgress → Completed`, `Cancelled` (subconjunto da máquina de OS; `Paused` e `Quality` não se aplicam).
- **Etapas** em `production_order_steps` sem centro de trabalho (`work_center_id` nulo para instalação) e sem apontamento de horas (o executor é terceiro).
- **CP do instalador** pelo `IPayables` (FIN-02 CA 14, 15), idempotente por `(Installer, osId)`, `Planned` com data estimada (ADR 0019 §17) até o aceite; retenções preenchidas pelo financeiro no título (P-15).
- **Aceite com `production.approve`:** no seed, a Supervisão de produção já tem essa ação (ela "libera na qualidade"), e portanto também registra o aceite técnico; o aceite não move dinheiro (o CP segue para OP aprovada).
- **Datas** `date`; dinheiro `numeric(18,2)`; Guid v7; numeração `OS`; auditoria `IAuditable` em OS, etapa e checklist; concorrência pelo `xmin`.

## Permissões, eventos e tabelas
- **Permissões:** `production.view`, `production.create`, `production.edit`, `production.approve` (aceite técnico e cancelamento de OS iniciada); seed patch `installation-orders-permissions` (Engenharia + `production.create`).
- **Eventos (`Production.Contracts`):** `InstallationStarted` (`{ productionOrderId, projectId, startedOn }`), `InstallationAccepted` (`{ productionOrderId, projectId, acceptedOn }`). **Consumidos:** `ContractCancelled` (COM-04b). **Publicados pelo Commercial:** `TechnicalAcceptance`, `ProjectDelivered`.
- **Contratos:** `IProjects` (Commercial, + orçado por natureza para quem tem `projects.view_cost`), `IPayables` (Finance), `IParties`; `IInstallationOrders.ByProjectAsync` e `StatusAsync` em `Production.Contracts`.
- **Tabelas:** `production.production_orders`, `production.production_order_steps` (primeira criação, com as colunas acima), `production.production_order_project_items` (nova); modelo padrão de etapas e checklist de instalação como constante do sistema (seed idempotente), revisto com a Konebras (P-22).

## Telas e estados
- **Projeto → Itens e OS:** "Nova OS de instalação" e a lista das OS.
- **Fábrica → Instalações** (`/app/producao/instalacoes`; entrada no `modules.ts` na área Fábrica): número, projeto, instalador, previsão, etapa, situação; filtros na URL.
- **OS de instalação** (`/app/producao/instalacoes/$id`): cabeçalho com número, chip, projeto e `StageIndicator` das etapas da OS; abas na URL: Etapas, Checklist de entrega técnica, Documentos, Histórico de alterações.
- **Verbos:** "Criar OS de instalação" / "OS de instalação criada"; "Iniciar instalação" / "Instalação iniciada"; "Registrar aceite técnico" / "Aceite técnico registrado"; "Cancelar OS" / "OS cancelada".
- **Estados:** carregando; vazio ("Crie a OS de instalação a partir do projeto." + "Ver projetos"); erro; modo leitura.
- **Propositivo:** valor padrão pelo orçado de instalação; instalador sugerido pelo último usado com o mesmo cliente ou cidade da obra; depois do aceite, "Próximos passos": "Ver parcela liberada" (com `finance.view`) e "Conferir título do instalador".
- **400 px:** o checklist é usado no celular na obra: itens com alvo de 56 px, marcação por toque e foto pela câmera ("Escolher arquivo"); a lista mostra número, projeto, previsão e situação.
- **Ajuda:** `Ajuda/producao-instalacoes.md`, `Ajuda/producao-instalacao.md` (abas `etapas`, `checklist`, `documentos`, `historico`); `<Dica>` em "Aceite técnico", "Valor combinado" e "Estimado".

## Plano de testes
- **Unidade:** máquina de estados da OS de instalação e etapas sequenciais (CA 6, 7, 9, 11, 13); data estimada e vencimento pela condição (CA 3, 9).
- **Integração (Testcontainers, obrigatória: cria CP):** seed patch (CA 1); criar OS com CP `Planned` estimado na mesma transação e rollback (CA 2, 3); item já coberto (CA 5); iniciar e projeto `Installing` (CA 6); aceite confirma CP, publica evento e libera o recebível ponta a ponta até a COM-04b, inclusive com o evento antes do título (CA 9, 10); bloqueios do aceite (CA 11); valor com notificação e cancelamento com o título (CA 12, 13); contrato cancelado (CA 14); combinado × orçado na OP (CA 15); três camadas e IDOR (CA 16); concorrência do aceite (CA 17); auditoria (CA 18).
- **Arquitetura:** Production usa só `Commercial.Contracts`, `Finance.Contracts` e `Parties.Contracts`.
- **E2E (Playwright):** criar a OS no projeto, concluir etapas e checklist no celular (400 px, alvos de 56 px), registrar o aceite e ver o projeto entregue.

## Fora do escopo
- OS de fabricação, Kanban e apontamento: PRD-01 a PRD-03 (Fase 3). Painel da TV: PNL-01.
- Templates de roteiro editáveis para instalação: ENG-01 (Fase 3) pode absorver o modelo padrão.
- NFS-e do serviço de instalação: FIS-06 (P-02). Cálculo de retenções: fora do MVP (P-15).

## Pontos em aberto
Decididos pelo PO em 20/10/2026 (seção 1.1, item 27):
1. OS de instalação no módulo `production`, com seed patch para a Engenharia (a Supervisão já tem as ações); aceite com `production.approve`.
2. Etapas padrão do CA 4; checklist configurável por tenant com modelo padrão sugerido no seed (P-22, decidida em 20/10/2026).
3. CP `Planned` com data estimada na criação e `Open` no aceite, vencendo pela condição padrão do instalador.
4. Uma OS cobre um ou mais itens (padrão: todos); o projeto fica `Delivered` quando todas as OS não canceladas estão concluídas.
5. P-18 decidida (ADR 0021).

**Pré-condição externa:** nenhuma (P-22 decidida: modelo configurável com padrão sugerido).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, cadeia até o recebível), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva); seed das etapas e do checklist idempotente; seed patch de permissões.
- [ ] Permissões só do `ModuleCatalog`; dono de anexo `ProductionOrder` registrado com `OwnerExists`.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda da lista e de cada aba; teste de cobertura passando.
- [ ] Confirmação com impacto financeiro e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (máquina da OS de instalação) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `IProductionDb` com `ProductionOrder` e `ProductionOrderStep` (tipo Installation).
- [ ] Integração com `IPayables` (estimativa, confirmação, atualização, cancelamento) e eventos em `Production.Contracts`; consumidor do Commercial (`InstallationAccepted`, `InstallationStarted`) e do Production (`ContractCancelled`).
- [ ] "Combinado × orçado" no item da OP (Finance lendo `IProjects`).
- [ ] Endpoints `/api/v1/production/installation-orders`; telas, checklist mobile e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (cadeia de eventos), design (checklist no celular), @qa.
- Gate: CA 3, 9, 10 e 17 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 20/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 2).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação (19/10/2026):** re-estimada de M para G; CP do instalador pelo `IPayables` na mesma transação; aceite publica `InstallationAccepted` e o Commercial publica `TechnicalAcceptance`; contrato cancelado com OS iniciada para decisão manual; concorrência do aceite.
- **Correções da revalidação (20/10/2026):** seed patch só com o que falta (Engenharia + `production.create`, CA 1) e a observação de que a Supervisão já registra o aceite; CP `Planned` com data estimada (M13, CA 3); texto do aceite da revisão de design (CA 9); mudança de valor notifica o financeiro e a OP mostra combinado × orçado (CA 12, 15); título órfão de OS cancelada (CA 13); anexos com tipos e tamanho do dono (ADR 0019 §7); checklist com alvos de 56 px; Instalações no menu; checklist padrão como pré-condição externa (P-22). CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Pré-condição externa explícita: P-22 (checklist validado com a Konebras).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 20/10/2026 | 0.3 | Decisões do PO (seção 1.1) e correções da revisão pré-Fase 2 (§2.1-M13, A3, §2.2 baixos, §2.3); pré-condição P-22; CAs renumerados | River (SM) |
| 20/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` com pré-condição externa | Pax (PO) |
| 20/10/2026 | 0.5 | P-22 decidida pelo PO: modelo de etapas e checklist configurável por tenant, com padrão sugerido no seed (CA 4); sem pré-condição externa | River (SM) |
