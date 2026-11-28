# EXE-02 — Modelos de etapas do projeto de serviço

| Campo | Valor |
|---|---|
| ID | EXE-02 |
| Título | Cadastro de modelos de etapas em Projetos → Modelos de etapas (letra, nome, papel Execução/Entrega/Aceite, horas previstas por unidade, cargo sugerido, checklist, "exige aprovação", marcos sugeridos); padrão do tenant e por segmento; modelo sugerido no item de serviço; seeds do perfil Serviços; contrato `IStageTemplates` com revisão |
| Tamanho | M |
| Status | Draft (revisão combinada de 28/11/2026) |
| Requisitos | [Proposta](../produto/execucao-pos-contrato.md) §5 e §6 · ENG-01 (editor de roteiro) · COM-06 P-22 · ADR 0046 (adendo PERF-04: pacote de dados por perfil) · ADR 0019 §2 (`xmin`) · R-SEC-5 |
| Dependências | EXE-01a (recurso `projects.service_execution`), EXE-RH (cargos para o tenant de serviço) |
| Bloqueia | EXE-03a, EXE-05b |
| Ordem de execução | 3 |

## User story
Como **dono de uma empresa de serviço**, quero **ter modelos de etapas prontos para o que vendo e ajustá-los ao meu jeito**, para **todo projeto novo já nascer com as etapas certas.**

## Contexto e referências (conferido no código em 28/11/2026)
- O roteiro da Engenharia (ENG-01: `routing_templates`, `routing_template_steps`, `routing_template_items`, letra A–Z com único `DEFERRABLE`) é o padrão de editor; o módulo `engineering` está fora do perfil Serviços, então o modelo do projeto vive no Commercial (ADR 0006: copiar o padrão, não referenciar).
- `QuoteLookups` (Commercial) já serve cargos ao front pelo `IPositions` no servidor (id e nome, sem custo). O mesmo padrão serve o seletor de cargo do modelo.
- Composição de custo do orçamento é **por unidade** do item (`quote_item_costs`); as horas do modelo seguem a mesma regra.

## Critérios de aceite

**Cadastro**
1. **Dado** `projects.view` e o recurso, **quando** abro Projetos → Modelos de etapas, **então** vejo a lista (nome, nº de etapas, horas por unidade somadas, segmento, chips "Padrão" e "Sugerido", "Usado em {n} projetos"), com busca e filtro "Ativos/Inativos".
2. **Dado** `projects.edit`, **quando** crio ou edito um modelo com nome (até 100, único entre os ativos e não excluídos, sem diferenciar maiúsculas), segmento opcional (os do perfil Serviços) e etapas no `RowsEditor`, **então** cada etapa tem: letra (A–Z, única no modelo, sugerida pela inicial), nome (até 60), papel (**Execução**, **Entrega**, **Aceite do cliente**), horas previstas **por unidade do item** (`numeric(18,4)`, ≥ 0), cargo sugerido (opcional), "Exige aprovação" e checklist (até 30 itens de até 200 caracteres); "Salvar modelo" → "Modelo salvo". Cada gravação sobe a `revision` do modelo.
3. **[Segurança]** **Dado** o servidor, **quando** recebe o modelo, **então** revalida e responde 422 **por linha** (`steps[n].code`, `steps[n].role`, …): ao menos uma etapa de Execução; no máximo uma Entrega e no máximo um Aceite; **Aceite exige Entrega antes** e é a última; **depois da Entrega só pode vir o Aceite** (nenhuma Execução depois da Entrega); letras únicas; horas ≥ 0; cargo existente pelo `IPositions` (inativo é aceito só se já estava gravado).
4. **Dado** o seletor de cargo, **quando** abre, **então** os cargos vêm de `GET /api/v1/stage-templates/lookups/positions` (Commercial, `projects.view`, `IPositions.ActiveAsync` no servidor, só id e nome, sem custo); cargo gravado que foi inativado aparece com o selo "Cargo inativo" e o aviso "Escolha outro cargo para os próximos projetos."
5. **Dado** os **marcos sugeridos** do modelo (`suggested_milestones` jsonb: lista de `{ percent, trigger, stageCode?, daysAfterEvent }`), **quando** edito, **então** a soma tem de dar 100% e o gatilho por etapa só aponta para letra de etapa de Execução do próprio modelo (Entrega e Aceite usam os gatilhos próprios); a EXE-05b só lê.
6. **Dado** um modelo usado em projetos, **quando** abro para editar, **então** o texto mostra "Usado em {n} projetos. Mudanças valem só para os próximos." (as etapas são copiadas, EXE-03a).

**Padrão, exclusão e concorrência**
7. **Dado** `projects.edit`, **quando** marco "Usar como padrão", **então** numa transação só (com o `xmin` do modelo e trava na linha do padrão atual) ele vira o padrão do tenant ou do segmento e o anterior deixa de ser; dois "Usar como padrão" simultâneos resultam em um padrão e um 409 `concurrency_conflict` (teste).
8. **Dado** `projects.delete`, **quando** excluo um modelo, **então** é exclusão lógica, na mesma transação remove os vínculos com itens (com auditoria); o padrão não pode ser excluído (409 "Escolha outro modelo padrão antes de excluir este.").
9. **Dado** um modelo excluído, **quando** o índice de nome e o de padrão são avaliados, **então** ignoram os excluídos (únicos parciais `WHERE NOT is_deleted`), e a `seed_key` continua gravada no excluído.

**Item de serviço**
10. **[Segurança]** **Dado** `projects.edit`, **quando** vinculo um item ao modelo (aba "Itens" do modelo), **então** o servidor valida pelo `IItems` que o item existe, está ativo e é `ItemType.Service` (422 "Só itens de serviço podem ter modelo de etapas."); o vínculo é único por item. A ficha do item mostra o modelo só para quem tem `projects.view` (sem editar ali).

**Seeds**
11. **Dado** um tenant com o perfil Serviços (provisionamento, aba "Perfis" ou `migrate`), **quando** o pacote `projects.services` roda, **então** nascem os quatro modelos da proposta §6 com os **marcos sugeridos** da mesma tabela e o chip "Sugerido — ajuste ao seu jeito", idempotentes pela `seed_key`; "Implantação de software" vira padrão **só se o tenant ainda não tem padrão**; o que o tenant editou não é sobrescrito; o que ele excluiu **não volta** (nem pelo pacote nem pelo botão "Usar os sugeridos").
12. **Dado** a Konebras (`factory`), **quando** o deploy roda, **então** nenhum modelo nasce, a migration só cria tabelas (não toca nenhuma linha existente) e as rotas respondem 403 `module_not_released`; `GET` dos projetos dela é idêntico (regressão).

**Contrato e auditoria**
13. **Dado** o contrato `IStageTemplates` (Commercial.Contracts), **quando** a EXE-03a/05b leem, **então** recebem modelo com `revision`, etapas (código, nome, papel, horas por unidade, cargo) e marcos sugeridos; nunca custo.
14. **Dado** qualquer gravação, **quando** acontece, **então** vai para a auditoria (`IAuditable`) e o histórico mostra quem mudou o quê (teste).

## Permissões, eventos e tabelas
- **Permissões:** `projects.view` (ver e lookups), `projects.edit` (criar, editar, padrão, vincular item), `projects.delete` (excluir). Nenhuma ação nova.
- **Tabelas (schema `commercial`, novas):** `stage_templates` (name, kind `Service`, segment, is_default, is_active, revision, suggested_milestones jsonb, seed_key, is_deleted, deleted_at, created_at, xmin; únicos parciais de nome e de padrão por `coalesce(segment,'')` `WHERE NOT is_deleted`; único de `seed_key`), `stage_template_steps` (template_id, sequence, code `char(1)`, name, role, standard_hours_per_unit `numeric(18,4)`, position_id, requires_approval, checklist jsonb; único `(template_id, code)` DEFERRABLE), `stage_template_items` (template_id, item_id único). Migration só `CREATE TABLE`.
- **Pacote:** `projects.services` em `BusinessProfileSeeds.ByProfile[services]`.

## Telas e estados
- **Lista** `/app/projetos/modelos`: vazio "Nenhum modelo de etapas ainda." com "Criar modelo" e "Usar os sugeridos"; carregando; erro com "Tentar de novo".
- **Editor**: layout do roteiro, sem centro de trabalho; total de horas por unidade no rodapé; prévia do indicador com as letras; erros na linha.
- **Celular (400 px):** etapas em cartões com "Mover para cima"/"Mover para baixo" (sem arrastar); checklist recolhido.
- **Ajuda:** `Ajuda/projetos-modelos-de-etapas.md` (`rota: /app/projetos/modelos`, `modulo: projects`, `recurso: projects.service_execution`); `<Dica>` em "Papel", "Horas por unidade" e "Marcos sugeridos".

## Plano de testes
- **Unidade:** validação do CA 3 (cada regra, 422 na linha certa); marcos sugeridos (CA 5).
- **Integração (Testcontainers):** CRUD, revisão; padrão único e corrida de dois "Usar como padrão"; exclusão com vínculos na mesma transação; seed idempotente, não toma o padrão existente e não ressuscita excluído; vínculo só com item `Service`; lookup sem custo; 403 e regressão da Konebras; auditoria.
- **Front (Vitest):** editor (regras de papel, mover no celular), "Cargo inativo", vazio com "Usar os sugeridos".

## Tarefas
- [ ] Domain/Application: `StageTemplate`, validação, `StageTemplateService`, `IStageTemplates`, lookup de cargos.
- [ ] Infrastructure: migration; pacote `projects.services` com os quatro modelos e marcos.
- [ ] Endpoints: `/api/v1/stage-templates` (GET, POST, PUT, DELETE), `/{id}/default`, `/{id}/items`, `/lookups/positions`, com `RequireProfileFeature`; OpenAPI e Orval.
- [ ] Front e ajuda; docs `03-modelo-dados`.

## Dev Notes
- Editor e validação: `src/Modules/Engineering/` (ENG-01) e `src/Web/src/pages/engineering/`; padrão de lookup: `src/Modules/Commercial/BemAqui.Commercial.Application/QuoteLookups.cs`; cargos: `src/Modules/HR/BemAqui.HR.Contracts/PositionsContracts.cs`; itens: `IItems` (Inventory.Contracts); pacote: `BusinessProfileSeeds`, `IProfileSeedPackage`.

## Fora do escopo
- Etapas no projeto (EXE-03a). Etapas em paralelo e dependências. Modelo para a OS da oficina (OFI-01).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Story escrita a partir da proposta aprovada pelo PO | Claude |
| 27/11/2026 | 0.2 | Dependência ajustada para EXE-01a | Claude |
| 28/11/2026 | 0.3 | **Revisão combinada de 28/11/2026:** lookup de cargos pelo Commercial (sem custo); `suggested_milestones` já nesta migration com os marcos do seed; Aceite exige Entrega e nada de Execução depois da Entrega; seed não toma o padrão nem ressuscita excluído; troca de padrão e exclusão numa transação com teste de corrida; "Cargo inativo"; vínculo só com item `Service`; revisão do modelo e `IStageTemplates`; regressão da Konebras; mover etapas no celular; "Usado em {n} projetos". **Decisão do mantenedor** (o PO pode rever): horas previstas por **unidade** do item, como a composição do orçamento. | Claude |
