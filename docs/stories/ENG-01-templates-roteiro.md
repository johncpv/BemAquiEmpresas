# ENG-01 — Templates de roteiro

| Campo | Valor |
|---|---|
| ID | ENG-01 |
| Título | Templates de roteiro de fabricação por tipo de produto: sequência de etapas, centro de trabalho, horas padrão, checklist da etapa, etapa de qualidade e etapa que exige aprovação; seed com o roteiro de exemplo Projeto → Corte → Montagem → Qualidade → Expedição |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-ENG-1 (template por tipo de produto: etapas, centro, horas padrão, checklist, aprovação) · R-ENG-2 (etapas de exemplo, P-04) · R-PRP-1 (roteiro do tipo de produto como padrão) · R-PLN-1 (centro de trabalho com capacidade, já existente) · R-SEC-5 |
| Dependências | CAD-03 (centros de trabalho, `IWorkCenters`), CAD-02a (itens, `IItems`), FND-09d/09e (moldes, `rows-editor`) |
| Bloqueia | PRD-01 (a OS copia o template), PLN-01 (horas padrão), COM-01a (o `routing_template_id` no item do orçamento, campo previsto para a Fase 3) |
| Ordem de execução | 4 (Marco 3.2 "Da engenharia à OS"; primeira story do módulo `engineering`) |

## User story
Como **engenheiro (projetista)**, quero **cadastrar o roteiro padrão de cada tipo de produto, com as etapas, o centro de trabalho, as horas padrão e o checklist de cada etapa**, para **cada OS de fabricação nascer com o caminho certo na fábrica e o planejamento saber quantas horas cada equipamento consome**.

## Contexto e referências
- PRD §5.6 R-ENG-1 e R-ENG-2; §5.8 R-PLN-1 e R-PLN-3 (as horas padrão alimentam o agendamento da Fase 4); §5.15a R-PRP-1 ("roteiro do tipo de produto").
- `03-modelo-dados` → `engineering.routing_templates` (`name`, `product_type`, `is_active`) e `engineering.routing_template_steps` (`template_id`, `sequence`, `name`, `work_center_id`, `standard_hours`, `requires_approval`, `is_quality_step`, `checklist` JSON).
- `04-fluxos-estados` §1 "OS (fabricação)" (a etapa de qualidade leva a OS a `Quality`; etapa sequencial no MVP) e §3.4 (horas padrão no agendamento).
- `06-pendencias-riscos` **P-04 (decidida em 16/10/2026):** etapas Projeto → Corte → Montagem → Qualidade → Expedição; horas padrão cadastradas no template, editáveis.
- COM-06 (Fora do escopo): "ENG-01 pode absorver o modelo padrão" da OS de instalação — ver Pontos em aberto 2.
- `ModuleCatalog`: `engineering` (área Fábrica, ações `view`, `create`, `edit`, `approve` = "liberar a BOM"; **sem `delete`**). Hoje nenhum grupo do seed além do Proprietário tem o módulo `engineering` (`GroupSeeder`: a Engenharia tem `projects`, `production` e `inventory`).
- ADR 0016 (excluir × inativar, seed também nos tenants existentes, `IgnoreQueryFilters()`), ADR 0019 §2 (`xmin`), ADR 0021 (mensagens).
- Protótipo: Kanban com as colunas P Projeto, C Corte, M Montagem, Q Qualidade, E Expedição (letra da etapa no display de andar).

## Critérios de aceite
**Permissões do seed**
1. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed patch `engineering-permissions` (proposta, ADR 0016) roda, **então** o grupo Engenharia do seed ganha o módulo `engineering` com `view`, `create` e `edit`, e a Supervisão de produção ganha `engineering.view`, uma vez, com o `perm_version` dos membros incrementado; `engineering.approve` (liberar a BOM) é tratado na ENG-02.

**Cadastro do template**
2. **Dado** `engineering.create`, **quando** clico "Novo roteiro", informo nome (único entre os ativos, sem diferenciar maiúsculas; 422 "Já existe um roteiro com este nome."), tipo de produto (texto até 60, ver Pontos em aberto 1) e as etapas, e clico "Salvar roteiro", **então** o template é gravado ativo, auditado, e abre o detalhe com a mensagem "Roteiro salvo".
3. **Dado** o editor de etapas (`rows-editor`), **quando** adiciono uma etapa, **então** informo nome (obrigatório, até 60), centro de trabalho (combobox de `IWorkCenters.ActiveAsync`, obrigatório), horas padrão (`numeric(18,4)`, ≥ 0, com 2 casas na tela), "Etapa de qualidade" e "Exige aprovação para concluir"; posso reordenar (arrastar e também "Mover para cima/baixo" pelo teclado), e a sequência é regravada 1..n sem buracos.
4. **Dado** uma etapa, **quando** abro "Checklist da etapa", **então** cadastro de 0 a 30 itens de texto (até 120 caracteres cada), reordenáveis; o checklist é gravado como lista ordenada no template.
5. **Dado** um roteiro, **quando** salvo, **então** o servidor valida: ao menos uma etapa (422 "Inclua ao menos uma etapa."), no máximo 30 etapas, centro de trabalho ativo e existente (validado por `IWorkCenters.FindAsync`; 422 no campo `steps[n].workCenterId`), no máximo uma etapa de qualidade por sequência contígua e a primeira etapa não pode ser de qualidade (422 "A primeira etapa não pode ser de qualidade."); os erros aparecem no campo da linha (ADR 0021, `applyServerErrors` com caminho aninhado).
6. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed roda, **então** existe o roteiro de exemplo "Roteiro padrão (exemplo)" com as etapas Projeto, Corte, Montagem, Qualidade (etapa de qualidade) e Expedição (P-04), horas padrão 0 (para o tenant preencher) e o centro de trabalho padrão do tenant quando só houver um ativo (sem centro, as etapas ficam sem centro e o roteiro nasce **inativo**, com a faixa "Escolha o centro de trabalho de cada etapa para ativar"); o seed usa `seed_key` e não recria o roteiro que o tenant excluiu ou alterou.

**Duplicar, inativar e excluir**
7. **Dado** um roteiro, **quando** clico "Duplicar", **então** abre a criação preenchida com as etapas e os checklists, com o nome "{nome} (cópia)" (R-PRP-3).
8. **Dado** um roteiro sem uso (nenhuma OS nem item de orçamento o referencia, pelos `IRoutingTemplateUsageProvider` dos módulos que guardam o id), **quando** clico "Excluir" e confirmo, **então** a exclusão é lógica; com uso, o servidor responde 409 e o diálogo oferece "Inativar" (ADR 0016 D10). Como o módulo não tem a ação `delete`, a exclusão exige `engineering.edit` (ver Pontos em aberto 3).
9. **Dado** um roteiro inativo, **quando** abro os seletores (PRD-01, COM-01a), **então** ele não aparece; continua visível em quem já o usa.

**Edição sem efeito retroativo**
10. **Dado** um roteiro já copiado por OS, **quando** edito etapas, horas ou checklist, **então** as OS existentes não mudam (a OS guarda a própria cópia, PRD-01) e o diálogo de confirmação diz "As OS já criadas não mudam. Só as próximas usam o roteiro novo."; a lista mostra "Usado em {n} OS".

**Consulta para os outros módulos**
11. **Dado** o `IRoutingTemplates` em `Engineering.Contracts` (novo), **quando** a Produção ou o Comercial chamam `ActiveAsync()`, `FindAsync(ids)` ou `StepsAsync(templateId)`, **então** recebem o template com as etapas (nome, sequência, centro, horas padrão, qualidade, exige aprovação, checklist), sem chamar tabela de outro módulo; `TotalStandardHoursAsync(templateId)` devolve a soma das horas padrão por centro, para a simulação da Fase 4.

**Permissões e segurança**
12. **Dado** o módulo `engineering` não liberado, **quando** chamo qualquer endpoint desta story, **então** 403 `module_not_released` e a tela `ModuleNotReleased`; sem grupo que habilite o módulo, 403 `forbidden` e o botão some do menu; sem `engineering.create`/`edit`, os botões somem e a API responde 403. `RequireModule` e `perm:` em todo endpoint (teste de arquitetura).
13. **[Segurança]** **Dado** a rota `/api/v1/engineering/routing-templates/{id}/steps/{stepId}`, **quando** a etapa é de outro template, **então** 404 (IDOR pai/filho); o corpo é desserializado com `UnmappedMemberHandling.Disallow` e `seed_key`, `is_system` ou ids de etapa de outro template são recusados.
14. **[Segurança]** **Dado** duas pessoas editando o mesmo roteiro, **quando** a segunda salva com a versão antiga, **então** 409 `concurrency_conflict` "O roteiro mudou desde que você abriu. Recarregue." com "Recarregar" (ADR 0019 §2).
15. **Dado** qualquer alteração no roteiro ou nas etapas, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário e data.

## Regras de negócio e técnicas
- **Tabelas** (03, com acréscimos marcados como proposta): `routing_templates` (+ `seed_key`, `is_deleted`, `created_at`; `product_type` texto até 60) e `routing_template_steps` (+ `checklist` como JSON ordenado `[{"sequence":1,"text":"..."}]`, como no 03). Horas `numeric(18,4)`.
- **Sequência contígua** regravada pelo servidor; reordenar não troca ids.
- **Cópia, não referência:** a OS copia etapas e checklist (PRD-01). O template só serve de ponto de partida; o histórico de execução nunca depende dele.
- **Provedor de uso:** `IRoutingTemplateUsageProvider` em `Engineering.Contracts`, implementado pela Produção (PRD-01) e pelo Comercial quando o item do orçamento ganhar o campo (Pontos em aberto 4). Até lá, só a Produção.
- **Ids** Guid v7; soft delete; auditoria `IAuditable` em template e etapa; `xmin`.
- **Fora da fronteira:** o centro de trabalho é só o id (`production.work_centers`), validado por `IWorkCenters` (`Production.Contracts`); o Engineering não referencia tabela da Produção.

## Permissões, eventos e tabelas
- **Permissões:** `engineering.view`, `engineering.create`, `engineering.edit` (já no `ModuleCatalog`). Seed patch **proposto** `engineering-permissions` (CA 1).
- **Eventos:** nenhum (o template não tem efeito em outro módulo).
- **Contratos novos:** `IRoutingTemplates` e `IRoutingTemplateUsageProvider` em `Engineering.Contracts`.
- **Tabelas:** `engineering.routing_templates`, `engineering.routing_template_steps` (primeira migration do schema `engineering`).

## Telas e estados
- **Fábrica → Engenharia → Roteiros** (`/app/engenharia/roteiros`; entrada no `modules.ts`: `engineering` ganha as abas "Roteiros" e, na ENG-02, "BOMs"): h1 "Roteiros de fabricação" com a linha "O caminho de cada produto na fábrica: etapas, centros e horas padrão."; colunas nome, tipo de produto, etapas (letras no `StageIndicator`), horas padrão totais, situação, uso; filtros na URL (situação, tipo).
- **Detalhe** (`/app/engenharia/roteiros/$id`): cabeçalho com nome, chip e o `StageIndicator` com a letra de cada etapa; abas na URL: Etapas (editor de linhas), Histórico de alterações. O checklist abre em painel lateral por etapa.
- **Verbos:** "Salvar roteiro" / "Roteiro salvo"; "Duplicar" ; "Inativar roteiro" / "Roteiro inativado"; "Excluir roteiro" / "Roteiro excluído".
- **Estados:** carregando (skeleton); vazio ("Nenhum roteiro cadastrado. Comece pelo roteiro de exemplo ou crie um novo." + "Novo roteiro"); erro com "Tentar de novo"; modo leitura sem `engineering.edit`, com a faixa que explica por quê.
- **Propositivo:** novo roteiro vem com as 5 etapas de exemplo (P-04) e o centro sugerido pelo último usado em cada etapa com o mesmo nome; total de horas ao vivo no rodapé fixo (`aria-live`); depois de salvar, `NextSteps`: "Ver OS sem roteiro" (com `production.view`, a partir da PRD-01) e "Duplicar para outro produto".
- **400 px:** a lista mostra nome, etapas e situação; as linhas do editor viram cartões com "Remover etapa {n}"; o checklist em tela cheia.
- **Ajuda:** `Ajuda/engenharia-roteiros.md` (lista) e `Ajuda/engenharia-roteiro.md` (abas `etapas` com `padrao: sim`, e `historico`); `<Dica>` em "Horas padrão" ("Horas de trabalho esperadas para uma unidade nesta etapa; o planejamento usa este número."), "Etapa de qualidade" e "Exige aprovação".

## Plano de testes
- **Unidade:** validação do roteiro (CA 5: sem etapa, primeira de qualidade, centro ausente, limites); regravação da sequência (CA 3).
- **Integração (Testcontainers):** seed do roteiro de exemplo idempotente, nos tenants existentes, sem recriar o excluído e inativo sem centro (CA 6); seed patch de permissões uma vez só (CA 1); CRUD com centro validado pelo `IWorkCenters` (CA 2–5); duplicar (CA 7); excluir sem uso e 409 com uso (CA 8); inativo fora do seletor (CA 9); `IRoutingTemplates` (CA 11); três camadas e IDOR (CA 12, 13); concorrência (CA 14); auditoria (CA 15).
- **Arquitetura:** Engineering só referencia `Production.Contracts` e `Inventory.Contracts`; `*.Contracts` não referencia outro `*.Contracts`; todo endpoint com `RequireModule` e `perm:`.
- **E2E (Playwright):** criar roteiro a partir do exemplo, reordenar etapa pelo teclado, preencher checklist e salvar; tela a 400 px.

## Fora do escopo
- BOM da OS: ENG-02. Cópia do roteiro para a OS e etapas editáveis na OS: PRD-01.
- Modelo da OS de instalação (COM-06): continua em Produção → Modelo da OS de instalação (Pontos em aberto 2).
- Roteiro alternativo por centro, etapas paralelas e terceirizadas (R-PRD-10, pós-MVP).
- Simulação de prazo do orçamento: PLN-05 (Fase 4).

## Pontos em aberto
1. **Chave do "tipo de produto"** (PO): o 03 tem `product_type` como texto. Proposta: texto livre com sugestões dos já usados, mais o vínculo opcional a itens do tipo Produto acabado (`routing_template_items`, proposta), para a PRD-01 sugerir o roteiro pelo item do projeto. Alternativa: usar a categoria do item.
2. **Modelo da OS de instalação** (PO): a COM-06 diz que a ENG-01 "pode absorver" o modelo. Proposta: **não absorver** nesta story; a instalação não tem centro nem horas e o modelo já está em produção.
3. **Excluir sem a ação `delete`** (PO/arquiteto): o `engineering` não tem `delete` no `ModuleCatalog`. Proposta: a exclusão lógica de roteiro sem uso usa `engineering.edit`; alternativa: incluir `delete` no catálogo (como a ADR 0016 fez com `inventory` e `production`), o que exige seed e ADR.
4. **Roteiro no item do orçamento** (PO): o 03 diz "`routing_template_id` entra na Fase 3" em `quote_items`. Proposta: o campo entra junto com a PRD-01 (o item do projeto herda do item do orçamento), sem mudar a tela do orçamento nesta fase; a simulação da PLN-05 é quem precisa dele no orçamento.
5. **Permissões do seed** (PO): seed patch proposto do CA 1 (Engenharia com `view/create/edit`, Supervisão com `view`).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras` (ADR 0020); seed do roteiro de exemplo idempotente; seed patch de permissões.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio e erro, inclusive a 400 px.
- [ ] Ajuda da lista e de cada aba escrita no mesmo commit; teste de cobertura passando.
- [ ] Padrões preenchidos e próximos passos avaliados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado (colunas propostas); story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `IEngineeringDb` com `RoutingTemplate` e `RoutingTemplateStep`; configuração EF `internal` no schema `engineering`.
- [ ] `RoutingTemplateService` (criar, editar, duplicar, inativar, excluir) e validador FluentValidation.
- [ ] `RoutingTemplateSeeder` (exemplo P-04) e `SeedPatch` `engineering-permissions`.
- [ ] `IRoutingTemplates` e `IRoutingTemplateUsageProvider` em `Engineering.Contracts`.
- [ ] Endpoints `/api/v1/engineering/routing-templates`; páginas, `modules.ts` e ajudas.

## Quality gates
- Agentes previstos: @dev, @architect (primeiro schema do Engineering e contrato com a Produção), design (editor de etapas e checklist), @qa.
- Gate: CA 5, 6, 10, 12 e 13 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026.

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

- **Correções feitas na validação:** roteiro do seed inativo quando não há centro único (CA 6), em vez de nascer com centro inventado; exclusão sem a ação `delete` registrada como ponto em aberto (3), sem decidir; aviso de que editar o roteiro não muda as OS já criadas (CA 10).
- **Pré-condições para `Ready`:** pontos em aberto 1, 3 e 5.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
