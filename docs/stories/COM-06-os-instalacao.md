# COM-06 — OS de instalação

| Campo | Valor |
|---|---|
| ID | COM-06 |
| Título | OS de instalação criada a partir do projeto, com executor terceirizado (papel Instalador), valor combinado que gera o CP previsto do instalador vinculado ao projeto, etapas e checklist de entrega técnica; aceite técnico que confirma o CP, entrega o projeto e libera o recebível do marco |
| Tamanho | G (re-estimada de M: cria a primeira tabela de OS do Production e a cadeia de eventos entre três módulos) |
| Status | Draft |
| Requisitos | R-PRJ-3 (OS de instalação por item/projeto, executor terceirizado, etapas, checklist de entrega técnica) · R-FIN-4 (CP de instaladores) · R-PRJ-6 (aceite técnico libera parcela) · R-COM-4 (numeração OS) · R-COM-3 · R-PRP-2 · R-PRP-7 |
| Dependências | COM-04a (`IProjects`), COM-04b (liberação por `TechnicalAcceptance`), COM-05 (aba Itens e OS), FIN-02 (`IPayables`), CAD-01a (papel Instalador) |
| Bloqueia | PRD-01 (reaproveita `production_orders`), PNL-01 (instalações em andamento), DSH-01 (entregas) |
| Ordem de execução | 13 (Marco 2.3 — fecha o ciclo do contrato ao caixa) |

## User story
Como **responsável pelo projeto**, quero **abrir a OS de instalação para o instalador terceirizado, acompanhar as etapas na obra e registrar o aceite técnico do cliente**, para **a empresa responder pela instalação, pagar o instalador pelo combinado e liberar a última parcela do cliente**.

## Contexto e referências
- PRD §0 (a Konebras terceiriza a instalação mas responde por ela ao cliente final), R-PRJ-3; §4 (OS de Instalação → CP do instalador).
- Backlog COM-06: "valor gera CP do instalador vinculado ao projeto"; CA "aceite técnico dispara evento que libera o recebível vinculado".
- `03-modelo-dados` → `production.production_orders` (`type` Installation, `project_id`, `project_item_id`, `executor_party_id`), `production_order_steps` (`checklist_result`).
- `04-fluxos-estados` §1 "Projeto" (`→ Installing` OS de instalação iniciada; `→ Delivered` entrega técnica aceita); revisão pré-Fase 0 §2.1 ("`Installing` passa a valer por item"); §2 (`TechnicalAcceptance` → Finance libera receivables).
- P-15 (decidida): retenções do serviço tomado são campos opcionais do título, sem cálculo (FIN-02 CA 4).
- `ModuleCatalog`: `production` (`view`, `create`, `edit`, `approve` = "liberar na qualidade", `delete`).

## Critérios de aceite
**Criar a OS**
1. **Dado** permissão de criar OS de instalação (ver Pontos em aberto 1) e um projeto `Planned`, `InProgress` ou `Fabricated`, **quando** clico "Nova OS de instalação" no projeto, escolho os itens do projeto (padrão: todos), o instalador (terceiro ativo com papel Instalador; 422 "Escolha um terceiro com o papel Instalador."), o valor combinado (`numeric(18,2)`, > 0, padrão: o orçado de Instalação do projeto ainda sem OS), a data prevista de início e fim (`date`) e clico "Criar OS de instalação", **então** a OS nasce `Open` com número `OS-2026-00001` (`INumberSequence`, tipo `OS`), tipo `Installation`, e as etapas e o checklist padrão (CA 3).
2. **Dado** a OS criada, **quando** grava, **então** na mesma transação o Production chama `IPayables.CreateFromOriginAsync(Installer, osId, instalador, projeto, valor, sem vencimento, planned: true)` e o título a pagar nasce `Planned`, natureza Instalação, descrição "Instalação {OS} — {projeto}"; um rollback da OS desfaz o título.
3. **Dado** a OS nova, **quando** abro, **então** ela tem as etapas padrão Preparação da obra, Montagem na obra, Testes e ajustes e Entrega técnica, e o checklist de entrega técnica padrão (ver Pontos em aberto 2), editáveis enquanto a OS está `Open`.
4. **Dado** um item do projeto já coberto por outra OS de instalação não cancelada, **quando** tento incluí-lo, **então** 422 "O item {descrição} já está na {OS}."

**Execução**
5. **Dado** permissão de editar a OS, **quando** clico "Iniciar instalação" e informo a data, **então** a OS passa a `InProgress`, o evento `InstallationStarted` vai para o outbox e o consumidor do Commercial passa o projeto a `Installing` (se ainda não estiver).
6. **Dado** uma OS `InProgress`, **quando** marco uma etapa como concluída, **então** grava data e usuário; as etapas são sequenciais (a seguinte só conclui depois da anterior, `04-fluxos` §1) e a última, "Entrega técnica", só conclui pelo aceite (CA 8).
7. **Dado** o checklist de entrega técnica, **quando** marco os itens (feito, não se aplica) e anexo fotos ou o termo assinado (dono `ProductionOrder`), **então** cada marcação grava usuário e data em `checklist_result`.

**Aceite técnico**
8. **Dado** permissão de aprovar (ver Pontos em aberto 1), as etapas anteriores concluídas e todos os itens do checklist marcados, **quando** clico "Registrar aceite técnico", informo a data (≤ hoje), o nome de quem aceitou pelo cliente e confirmo no diálogo que mostra "Confirma o pagamento de {valor} ao instalador e libera a parcela {marco} de {valor} do cliente.", **então** na mesma transação: a etapa "Entrega técnica" e a OS ficam `Completed`, o Production chama `IPayables.ConfirmAsync(Installer, osId, valor, vencimento)` com o vencimento pela condição padrão do instalador (primeira parcela a partir da data do aceite; sem condição, a data do aceite — ver Pontos em aberto 3), e o evento `InstallationAccepted` vai para o outbox; o toast diz "Aceite técnico registrado".
9. **Dado** o `InstallationAccepted`, **quando** o consumidor do Commercial processa e todas as OS de instalação não canceladas do projeto estão `Completed`, **então** o projeto passa a `Delivered` e o Commercial publica `TechnicalAcceptance` e `ProjectDelivered` (`{ projectId, contractId, occurredOn }` = data do aceite); a COM-04b libera os títulos com esses gatilhos (CA do backlog).
10. **Dado** checklist incompleto ou etapa anterior aberta, **quando** tento o aceite, **então** 422 "Conclua as etapas e o checklist antes do aceite." com a lista do que falta.

**Alterações e cancelamento**
11. **Dado** uma OS `Open` ou `InProgress`, **quando** mudo o valor combinado, **então** o título `Planned` é atualizado pelo `IPayables` (mesma transação); com o título já `Open` (depois do aceite), o valor não muda pela OS (409 "Ajuste no título a pagar.").
12. **Dado** permissão de editar e uma OS `Open` (não iniciada), **quando** clico "Cancelar OS", informo o motivo e confirmo, **então** a OS fica `Cancelled` e o título `Planned` do instalador é cancelado por `IPayables.CancelFromOriginAsync`; OS `InProgress` só é cancelada com permissão de aprovar, e o título continua `Planned` para o financeiro decidir (notificação a `finance.edit`).
13. **Dado** o `ContractCancelled` (COM-04b), **quando** o consumidor do Production processa, **então** cancela as OS de instalação `Open` do projeto (e os títulos `Planned`) e lista as `InProgress` para decisão manual (`04-fluxos` §1), notificando o responsável do projeto.

**Permissões e segurança**
14. **Dado** o módulo `production` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem a ação exigida, 403 e o botão some. `RequireModule` e `perm:`; IDOR em `/production-orders/{id}/steps/{stepId}` e `/attachments/{attId}`; projeto, itens e instalador validados pelos Contracts dos donos.
15. **[Segurança]** **Dado** o aceite técnico, **quando** dois aceites chegam ao mesmo tempo, **então** só um grava (transição condicional) e o CP e o evento não são duplicados.
16. **Dado** qualquer alteração na OS, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário e data.

## Regras de negócio e técnicas
- **OS de instalação** em `production.production_orders` (`type = Installation`) com `contract_value` (proposta, `numeric(18,2)`), `planned_start`/`planned_end` como `date` (proposta para instalação), `accepted_on`, `accepted_by_name` (nome de quem aceitou pelo cliente, `[PersonalData]`), `cancel_reason`; itens cobertos em `production_order_project_items` (proposta).
- **Situação da OS de instalação:** `Open → InProgress → Completed`, `Cancelled` (subconjunto da máquina de OS; `Paused` e `Quality` não se aplicam).
- **Etapas** em `production_order_steps` sem centro de trabalho (`work_center_id` nulo para instalação, proposta) e sem apontamento de horas (o executor é terceiro).
- **CP do instalador** pelo `IPayables` (FIN-02 CA 10, 11), idempotente por `(Installer, osId)`; retenções preenchidas pelo financeiro no título (P-15).
- **Datas** `date`; dinheiro `numeric(18,2)`; Guid v7; numeração `OS`; auditoria `IAuditable` em OS, etapa e checklist.

## Permissões, eventos e tabelas
- **Permissões:** `production.view`, `production.create`, `production.edit`, `production.approve` (aceite técnico e cancelamento de OS iniciada) — ver Pontos em aberto 1.
- **Eventos (`Production.Contracts`):** `InstallationStarted` (`{ productionOrderId, projectId, startedOn }`), `InstallationAccepted` (`{ productionOrderId, projectId, acceptedOn }`). **Consumidos:** `ContractCancelled` (COM-04b). **Publicados pelo Commercial:** `TechnicalAcceptance`, `ProjectDelivered`.
- **Contratos:** `IProjects` (Commercial), `IPayables` (Finance, + `CancelFromOriginAsync`, proposta), `IParties`; `IInstallationOrders.ByProjectAsync` em `Production.Contracts` para a aba "Itens e OS" (COM-05).
- **Tabelas:** `production.production_orders`, `production.production_order_steps` (primeira criação, com as colunas propostas acima), `production.production_order_project_items` (**proposta**); seed do modelo padrão de etapas e checklist de instalação (**proposta**, `production.installation_templates` ou constante do sistema).

## Telas e estados
- **Projeto → Itens e OS:** "Nova OS de instalação" e a lista das OS.
- **OS de instalação** (`/app/producao/instalacoes/$id`), abas: Etapas, Checklist de entrega técnica, Documentos, Histórico de alterações; indicador de etapa no cabeçalho.
- **Lista** (`/app/producao/instalacoes`): número, projeto, instalador, previsão, etapa, situação; filtros na URL.
- **Verbos:** "Criar OS de instalação" / "OS de instalação criada"; "Iniciar instalação" / "Instalação iniciada"; "Registrar aceite técnico" / "Aceite técnico registrado"; "Cancelar OS" / "OS cancelada".
- **Estados:** carregando; vazio ("Crie a OS de instalação a partir do projeto." + "Ver projetos"); erro; modo leitura.
- **Propositivo:** valor padrão pelo orçado de instalação; instalador sugerido pelo último usado com o mesmo cliente ou cidade da obra; depois do aceite, "Próximos passos": "Ver parcela liberada" (com `finance.view`) e "Conferir título do instalador".
- **400 px:** o checklist é usado no celular na obra; botões ≥ 44 px e marcação por toque.
- **Ajuda:** `Ajuda/producao-instalacoes.md`, `Ajuda/producao-instalacao.md` (abas `etapas`, `checklist`, `documentos`, `historico`); `<Dica>` em "Aceite técnico" e "Valor combinado".

## Plano de testes
- **Unidade:** máquina de estados da OS de instalação e etapas sequenciais (CA 5, 6, 8, 10, 12); vencimento pela condição (CA 8).
- **Integração (Testcontainers, obrigatória: cria CP):** criar OS com CP `Planned` na mesma transação e rollback (CA 1, 2); item já coberto (CA 4); iniciar e projeto `Installing` (CA 5); aceite confirma CP, publica evento e libera o recebível ponta a ponta até a COM-04b (CA 8, 9); bloqueios do aceite (CA 10); valor e cancelamento com o título (CA 11, 12); contrato cancelado (CA 13); três camadas e IDOR (CA 14); concorrência do aceite (CA 15); auditoria (CA 16).
- **Arquitetura:** Production usa só `Commercial.Contracts`, `Finance.Contracts` e `Parties.Contracts`.
- **E2E (Playwright):** criar a OS no projeto, concluir etapas e checklist no celular (400 px), registrar o aceite e ver o projeto entregue.

## Fora do escopo
- OS de fabricação, Kanban e apontamento: PRD-01 a PRD-03 (Fase 3). Painel da TV: PNL-01.
- Templates de roteiro editáveis para instalação: ENG-01 (Fase 3) pode absorver o modelo padrão.
- NFS-e do serviço de instalação: FIS-06 (P-02). Cálculo de retenções: fora do MVP (P-15).

## Pontos em aberto
1. **Módulo e permissões da OS de instalação.** O 03 põe a OS em `production`, mas quem acompanha a instalação na Konebras pode ser o comercial ou o responsável do projeto, que no seed só têm `projects`. **Recomendação:** seguir o 03 (módulo `production`, ações `create`/`edit`, aceite com `approve`) e conceder por *seed patch* `production.view/create/edit` à Engenharia e à Supervisão de produção; o Proprietário aprova o aceite. Alternativa: OS de instalação no `projects`.
2. **Etapas e checklist padrão da instalação.** Os docs não listam. **Recomendação:** as etapas do CA 3 e um checklist inicial (equipamento instalado conforme projeto, testes de segurança feitos, cliente orientado, documentação entregue, área limpa), editáveis por OS e revistos com a Konebras.
3. **Quando o CP do instalador vira `Open` e qual vencimento.** **Recomendação:** `Planned` na criação (entra no fluxo como previsão) e `Open` no aceite, vencendo pela condição padrão do instalador; alternativa: `Open` quando chegar a nota do instalador.
4. **Uma OS por projeto ou por item.** **Recomendação:** uma OS cobre um ou mais itens (padrão: todos); o projeto fica `Delivered` quando todas as OS não canceladas estão concluídas.
5. **P-18:** ver FIN-01, ponto 5.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, cadeia até o recebível), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva); seed das etapas e do checklist idempotente; *seed patch* de permissões, se aprovado.
- [ ] Permissões só do `ModuleCatalog`; dono de anexo `ProductionOrder` registrado.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda da lista e de cada aba; teste de cobertura passando.
- [ ] Confirmação com impacto financeiro e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (máquina da OS de instalação) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `IProductionDb` com `ProductionOrder` e `ProductionOrderStep` (tipo Installation).
- [ ] Integração com `IPayables` e eventos em `Production.Contracts`; consumidor do Commercial (`InstallationAccepted`, `InstallationStarted`) e do Production (`ContractCancelled`).
- [ ] Endpoints `/api/v1/production/installation-orders`; telas, checklist mobile e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (cadeia de eventos), design (checklist no celular), @qa.
- Gate: CA 2, 8, 9 e 15 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 19/10/2026.

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

- **Correções feitas na validação (19/10/2026):** re-estimada de M para G; CP do instalador pelo contrato `IPayables` na mesma transação (sem acesso a tabela do Finance); aceite publica `InstallationAccepted` e o Commercial é quem publica `TechnicalAcceptance` (como no `04-fluxos` §2); contrato cancelado com OS iniciada para decisão manual (CA 13); concorrência do aceite (CA 15).
- **Pré-condições para `Ready`:** Pontos em aberto 1 a 4 (com padrão recomendado) e P-18.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
