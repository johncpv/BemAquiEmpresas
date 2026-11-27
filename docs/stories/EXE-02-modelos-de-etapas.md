# EXE-02 — Modelos de etapas do projeto de serviço

| Campo | Valor |
|---|---|
| ID | EXE-02 |
| Título | Cadastro de modelos de etapas em Projetos → Modelos de etapas (letra, nome, papel Execução/Entrega/Aceite, horas previstas, cargo sugerido, checklist, "exige aprovação"); modelo padrão do tenant e por segmento; modelo sugerido no item de serviço; seeds do perfil Serviços (implantação de software, consultoria, obra/instalação, manutenção programada) |
| Tamanho | M |
| Status | Draft |
| Requisitos | [Proposta](../produto/execucao-pos-contrato.md) §5 e §6 · ENG-01 (editor de roteiro: letra, horas, checklist, aprovação) · COM-06 P-22 (modelo configurável do tenant) · ADR 0046 (adendo PERF-04: pacote de dados por perfil) · R-SEC-5 |
| Dependências | EXE-01 (recurso `projects.service_execution`) |
| Bloqueia | EXE-03, EXE-05 |
| Ordem de execução | 2 (Execução depois do contrato, antes da F2 do Conecta) |

## User story
Como **dono de uma empresa de serviço**, quero **ter modelos de etapas prontos para o que eu vendo e ajustá-los ao meu jeito**, para **todo projeto novo já nascer com as etapas certas, sem montar do zero.**

## Contexto e referências
- O roteiro da Engenharia (ENG-01) é o modelo de etapas da **OS de fabricação** e depende de centro de trabalho; o módulo `engineering` está fora do perfil Serviços. Esta story cria o modelo do **projeto de serviço**, no módulo `projects`, com o mesmo jeito de editar (letra A–Z, horas, checklist, aprovação) para o usuário reconhecer.
- Decisão do PO (27/11/2026): modelos configuráveis, com o sistema propondo um modelo que atenda ao modelo de operação do cliente.

## Critérios de aceite
1. **Dado** `projects.view` e o recurso `projects.service_execution`, **quando** abro Projetos → Modelos de etapas, **então** vejo a lista (nome, nº de etapas, horas previstas somadas, segmento, chips "Padrão" e "Sugerido"), com busca e filtro "Ativos/Inativos".
2. **Dado** `projects.edit`, **quando** crio ou edito um modelo com nome (até 100, único entre os ativos sem diferenciar maiúsculas), segmento opcional (os do perfil Serviços) e etapas no `RowsEditor`, **então** cada etapa tem: letra (A–Z, única no modelo, sugerida pela inicial do nome), nome (até 60), papel (**Execução**, **Entrega** ou **Aceite do cliente**), horas previstas (`numeric(18,4)`, ≥ 0), cargo sugerido (opcional, pelo `IPositions`), "Exige aprovação" e checklist (até 30 itens de até 200 caracteres); "Salvar modelo" → "Modelo salvo".
3. **[Segurança]** **Dado** o servidor, **quando** recebe o modelo, **então** revalida: ao menos uma etapa; no máximo uma etapa "Aceite do cliente" e, se houver, é a última; no máximo uma etapa "Entrega" e ela vem antes do aceite; letras únicas; cargo existente e ativo; responde 422 no campo da linha (`steps[n].code` etc.) mesmo que o front tenha validado.
4. **Dado** um modelo, **quando** marco "Usar como padrão" (`projects.edit`), **então** ele vira o padrão do tenant (ou do segmento escolhido) e o anterior deixa de ser; um padrão por segmento e um geral.
5. **Dado** um item `Service` do estoque, **quando** abro Projetos → Modelos de etapas → modelo → aba "Itens" e vinculo o item (ou, na ficha do item, o campo "Modelo de etapas sugerido" para quem tem `projects.view`), **então** o vínculo é único por item (`stage_template_items`) e é a primeira sugestão para orçamentos com esse item (usado na EXE-05).
6. **Dado** um modelo usado em projeto, **quando** edito, **então** o projeto existente **não** muda (as etapas foram copiadas, EXE-03); a tela avisa "Mudanças valem para os próximos projetos."
7. **Dado** `projects.delete`, **quando** excluo um modelo, **então** é exclusão lógica; o padrão não pode ser excluído (409 "Escolha outro modelo padrão antes de excluir este."); vínculos com itens são removidos com auditoria.
8. **Dado** um tenant com o perfil Serviços (provisionamento, aba "Perfis" ou `migrate`), **quando** o pacote `projects.services` roda (`IProfileSeedPackage`, ADR 0046 PERF-04), **então** nascem os quatro modelos da proposta §6 ("Implantação de software" como padrão geral; "Consultoria", "Obra/instalação", "Manutenção programada") com o chip "Sugerido — ajuste ao seu jeito", idempotentes pela `seed_key`; o que o tenant excluiu não volta e o que editou não é sobrescrito. O tenant `bemaqui` recebe os quatro no deploy.
9. **Dado** a Konebras (`factory`), **quando** o deploy roda, **então** nenhum modelo nasce e a tela/rotas respondem 403 `module_not_released` (recurso por perfil).
10. **Dado** qualquer gravação, **quando** acontece, **então** vai para a auditoria (`IAuditable`) e o histórico do modelo mostra quem mudou o quê.

## Regras de negócio e técnicas
- **Papel da etapa** comanda o projeto na EXE-03: concluir a etapa "Entrega" = registrar entrega; concluir "Aceite do cliente" = registrar aceite. Modelo sem essas etapas usa os botões da EXE-01.
- **Horas previstas × cargo sugerido** alimentam "Preencher mão de obra pelas etapas" no orçamento (EXE-05) e o previsto da aba Horas (EXE-04).
- `kind` do modelo nasce só com `Service` (reservado `ServiceOrder` para a OFI-01 reaproveitar o cadastro, sem tela agora).

## Permissões, eventos e tabelas
- **Permissões:** `projects.view` (ver), `projects.edit` (criar, editar, padrão, vincular item), `projects.delete` (excluir). Nenhuma ação nova.
- **Tabelas (schema `commercial`, novas):** `stage_templates` (name, kind, segment, is_default, is_active, seed_key, is_deleted, deleted_at, created_at, xmin; único parcial do padrão por `coalesce(segment,'')` entre não excluídos), `stage_template_steps` (template_id, sequence, code `char(1)`, name, role `Execution/Delivery/Acceptance`, standard_hours, position_id, requires_approval, checklist jsonb; único `(template_id, code)` DEFERRABLE como na ENG-01), `stage_template_items` (template_id, item_id único).
- **Pacote de dados:** `projects.services` em `BusinessProfileSeeds.ByProfile[services]`.

## Telas e estados
- **Lista** `/app/projetos/modelos`: vazio "Nenhum modelo de etapas ainda." com "Criar modelo" e "Usar os modelos sugeridos" (roda o pacote sob demanda); carregando; erro com "Tentar de novo".
- **Editor** `/app/projetos/modelos/$id`: mesmo layout do editor de roteiro (ENG-01) sem centro de trabalho; total de horas no rodapé; prévia do indicador de etapa com as letras.
- **400 px:** cada etapa vira cartão; checklist recolhido.
- **Ajuda:** nova `Ajuda/projetos-modelos-de-etapas.md` (`rota: /app/projetos/modelos`, `modulo: projects`, `recurso: projects.service_execution`); `<Dica>` em "Papel" e "Horas previstas".

## Plano de testes
- **Unidade:** validação do modelo (CA 3), sugestão de letra.
- **Integração (Testcontainers):** CRUD e padrão único; pacote idempotente (rodar duas vezes, excluir e rodar de novo); 403 na Konebras.
- **Front (Vitest):** editor com regras de papel; estado vazio com "Usar os modelos sugeridos".

## Tarefas
- [ ] Domain/Application: `StageTemplate`, `StageTemplateService`, validação; `IStageTemplates` em `Commercial.Contracts` (lido pela EXE-03/05).
- [ ] Infrastructure: migration aditiva; pacote `projects.services` com os quatro modelos.
- [ ] Endpoints: `GET/POST/PUT/DELETE /api/v1/stage-templates`, `POST .../{id}/default`, `PUT .../{id}/items` com `RequireProfileFeature`; OpenAPI e Orval.
- [ ] Front: lista, editor (reaproveitar o `RowsEditor` e a tela do roteiro), campo no item, ajuda.
- [ ] Docs: `03-modelo-dados` (commercial).

## Dev Notes
- **Reaproveitar:** editor e validação do roteiro em `src/Modules/Engineering/` (ENG-01: `routing_templates`, `routing_template_steps`, `routing_template_items`) e `src/Web/src/pages/engineering/` (copiar o padrão, não referenciar o módulo: regra de arquitetura ADR 0006); modelo da OS de instalação (`installation_template`, COM-06).
- **Pacote por perfil:** `BuildingBlocks/Tenancy/IProfileSeedPackage`, `BuildingBlocks/Modules/BusinessProfileSeeds`, `ProfileSeedRunner`.
- **Cargos:** `src/Modules/HR/BemAqui.HR.Contracts/PositionsContracts.cs` (sem salário).

## Fora do escopo
- Etapas no projeto (EXE-03). Dependência entre etapas fora da sequência, etapas em paralelo e subtarefas (sequencial na v1, como a OS). Modelo para a OS de serviço da oficina (OFI-01).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Story escrita a partir da proposta aprovada pelo PO (27/11/2026) | Claude |
