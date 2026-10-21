# ENG-01 — Templates de roteiro

| Campo | Valor |
|---|---|
| ID | ENG-01 |
| Título | Templates de roteiro de fabricação por tipo de produto: sequência de etapas com letra (código da etapa), centro de trabalho, horas padrão, checklist da etapa, etapa de qualidade e etapa que exige aprovação; vínculo do roteiro ao produto; seed com o roteiro de exemplo Projeto → Corte → Montagem → Qualidade → Expedição; correção do `StageIndicator` |
| Tamanho | M |
| Status | Ready |
| Requisitos | R-ENG-1 (template por tipo de produto: etapas, centro, horas padrão, checklist, aprovação) · R-ENG-2 (etapas de exemplo, P-04) · R-PRP-1 (roteiro do tipo de produto como padrão) · R-PLN-1 (centro de trabalho com capacidade, já existente) · R-SEC-5 |
| Dependências | CAD-03 (centros de trabalho, `IWorkCenters`), CAD-02a (itens, `IItems`) |
| Bloqueia | PRD-01 |
| Ordem de execução | 1 (Marco 3.1 "Fábrica visível") |

## User story
Como **engenheiro (projetista)**, quero **cadastrar o roteiro padrão de cada tipo de produto, com as etapas, a letra, o centro de trabalho, as horas padrão e o checklist de cada etapa**, para **cada OS de fabricação nascer com o caminho certo na fábrica e o planejamento saber quantas horas cada equipamento consome**.

## Contexto e referências
- PRD §5.6 R-ENG-1 e R-ENG-2; §5.8 R-PLN-1 e R-PLN-3 (as horas padrão alimentam o agendamento da Fase 4); §5.15a R-PRP-1 ("roteiro do tipo de produto").
- `03-modelo-dados` → `engineering.routing_templates` (`name`, `product_type`, `is_active`) e `engineering.routing_template_steps` (`template_id`, `sequence`, `name`, `work_center_id`, `standard_hours`, `requires_approval`, `is_quality_step`, `checklist` JSON).
- `04-fluxos-estados` §1 "OS (fabricação)" (a etapa de qualidade leva a OS a `Quality`; etapa sequencial no MVP) e §3.4 (horas padrão no agendamento).
- `06-pendencias-riscos` **P-04 (decidida em 16/10/2026):** etapas Projeto → Corte → Montagem → Qualidade → Expedição; horas padrão cadastradas no template, editáveis. Pela D4, a etapa Expedição é "preparar e embalar"; a baixa do estoque é do romaneio (PRD-06b).
- **Revisão pré-Fase 3** (`docs/revisoes/2026-10-21-revisao-pre-fase3.md`): D4 (Expedição), D10 (código da etapa, campo "Letra"), D12 (primeira etapa sem BOM), D13 (`engineering.delete`), D17 (b) (correção do `StageIndicator`); tabela 1.1, itens 3 a 7; §2.3 padrão 3.
- ADR 0003 (adendo de 21/10/2026: `engineering.delete`); ADR 0023 §7 (D4 e D12 no `04-fluxos`); ADR 0016 (excluir × inativar, seed também nos tenants existentes, `IgnoreQueryFilters()`); ADR 0019 §2 (`xmin`); ADR 0021 (mensagens).
- COM-06 (Fora do escopo): "ENG-01 pode absorver o modelo padrão" da OS de instalação. Decidido: não absorver (ponto 4).
- `ModuleCatalog`: `engineering` (área Fábrica, ações `view`, `create`, `edit`, `approve` = "liberar a BOM"); esta story acrescenta `delete` (D13). Hoje nenhum grupo do seed além do Proprietário tem o módulo `engineering` (`GroupSeeder`: a Engenharia tem `projects`, `production` e `inventory`).
- **Código atual:** `src/Web/src/components/ui/stage-indicator.tsx` pinta de âmbar todas as etapas até a atual (`i <= index ? 'bg-signal'`), usa `key={stage}` (quebra com nomes repetidos) e mostra um segmento por etapa, o que não cabe a 400 px com muitas etapas (D17 b).
- Protótipo: Kanban com as colunas P Projeto, C Corte, M Montagem, Q Qualidade, E Expedição (letra da etapa no display de andar).

## Critérios de aceite
**Permissões do seed**
1. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed patch `engineering-permissions` (ADR 0016) roda, **então** o `ModuleCatalog` do `engineering` passa a declarar `delete` (D13, ADR 0003 adendo de 21/10/2026), o grupo Engenharia do seed ganha o módulo `engineering` com `view`, `create`, `edit` e `delete`, e a Supervisão de produção ganha `engineering.view`, uma vez, com o `perm_version` dos membros incrementado; `engineering.approve` (liberar a BOM) é da ENG-02.

**Cadastro do template**
2. **Dado** `engineering.create`, **quando** clico "Novo roteiro", informo nome (único entre os ativos, sem diferenciar maiúsculas; 422 "Já existe um roteiro com este nome."), tipo de produto (texto livre até 60, com sugestões dos tipos já usados no tenant) e as etapas, e clico "Salvar roteiro", **então** o template é gravado ativo, auditado, e abre o detalhe com a mensagem "Roteiro salvo".
3. **Dado** o editor de etapas (`rows-editor`), **quando** adiciono uma etapa, **então** informo nome (obrigatório, até 60), **letra** (código da etapa, D10: um caractere de A a Z, maiúsculo, obrigatório, sugerido pela inicial do nome e editável), centro de trabalho (combobox de `IWorkCenters.ActiveAsync`, obrigatório), horas padrão (`numeric(18,4)`, ≥ 0, com 2 casas na tela), "Etapa de qualidade" e "Exige aprovação para concluir"; posso reordenar (arrastar e também "Mover para cima/baixo" pelo teclado), e a sequência é regravada 1..n sem buracos.
4. **Dado** uma etapa, **quando** abro "Checklist da etapa", **então** cadastro de 0 a 30 itens de texto (até 120 caracteres cada), reordenáveis; o checklist é gravado como lista ordenada no template.
5. **Dado** um roteiro, **quando** salvo, **então** o servidor valida: ao menos uma etapa (422 "Inclua ao menos uma etapa."), no máximo 30 etapas, centro de trabalho ativo e existente (validado por `IWorkCenters.FindAsync`; 422 no campo `steps[n].workCenterId`), letra de A a Z e sem repetir no roteiro (422 "A letra {X} já está na etapa {n}." no campo `steps[n].code`), no máximo uma etapa de qualidade por sequência contígua e a primeira etapa não pode ser de qualidade (422 "A primeira etapa não pode ser de qualidade."); os erros aparecem no campo da linha (ADR 0021, `applyServerErrors` com caminho aninhado).
6. **Dado** o editor, **quando** vejo a primeira etapa, **então** ela tem a marca fixa "Começa sem BOM" com a `<Dica>` "A primeira etapa pode começar antes de a BOM ser liberada. As seguintes só começam com a BOM liberada (a partir da ENG-02)." (D12, ADR 0023 §7); a marca acompanha a etapa que estiver em primeiro depois de reordenar.

**Roteiro do produto**
7. **Dado** `engineering.edit` e um roteiro, **quando** abro a aba "Produtos" e vinculo itens ativos do tipo Produto acabado ou Componente (`IItems`), **então** o vínculo é gravado em `engineering.routing_template_items (template_id, item_id)` com `item_id` único (um produto tem um roteiro só); vincular um produto que já está em outro roteiro responde 422 "O produto {código} já usa o roteiro {nome}." com a ação "Abrir roteiro {nome}"; a PRD-01 usa o vínculo para sugerir o roteiro da OS.

**Roteiro de exemplo**
8. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed roda, **então** existe o roteiro de exemplo "Roteiro padrão (exemplo)" com as etapas P Projeto, C Corte, M Montagem, Q Qualidade (etapa de qualidade) e E Expedição (P-04), horas padrão 0 (para o tenant preencher), a etapa Expedição com o checklist "Preparar e embalar para o romaneio" (D4: a baixa do estoque é do romaneio, PRD-06b), e o centro de trabalho padrão do tenant quando só houver um ativo (sem centro, as etapas ficam sem centro e o roteiro nasce **inativo**, com a faixa "Escolha o centro de trabalho de cada etapa para ativar"); o seed usa `seed_key` e não recria o roteiro que o tenant excluiu ou alterou.

**Duplicar, inativar e excluir**
9. **Dado** um roteiro, **quando** clico "Duplicar", **então** abre a criação preenchida com as etapas, as letras e os checklists, com o nome "{nome} (cópia)" (R-PRP-3), sem os produtos vinculados (cada produto tem um roteiro só, CA 7).
10. **Dado** `engineering.delete` e um roteiro sem uso (nenhuma OS nem item de orçamento ou de projeto o referencia, pelos `IRoutingTemplateUsageProvider` dos módulos que guardam o id), **quando** clico "Excluir roteiro" e confirmo, **então** a exclusão é lógica e os vínculos de produto do roteiro são apagados (o diálogo diz "{n} produtos ficam sem roteiro."); com uso, o servidor responde 409 e o diálogo oferece "Inativar" com `engineering.edit` (ADR 0016 D10). Até a PRD-01 não há provedor de uso, e todo roteiro é excluível.
11. **Dado** um roteiro inativo, **quando** abro os seletores (PRD-01), **então** ele não aparece e a sugestão pelo produto o ignora; continua visível em quem já o usa.

**Edição sem efeito retroativo**
12. **Dado** um roteiro já copiado por OS, **quando** edito etapas, letras, horas ou checklist, **então** as OS existentes não mudam (a OS guarda a própria cópia, PRD-01) e o diálogo de confirmação diz "As OS já criadas não mudam. Só as próximas usam o roteiro novo."; a lista mostra "Usado em {n} OS".

**Consulta para os outros módulos**
13. **Dado** o `IRoutingTemplates` em `Engineering.Contracts` (novo), **quando** a Produção ou o Comercial chamam `ActiveAsync()`, `FindAsync(ids)`, `StepsAsync(templateId)` ou `ForItemAsync(itemId)` (roteiro ativo vinculado ao produto, CA 7), **então** recebem o template com as etapas (nome, letra, sequência, centro, horas padrão, qualidade, exige aprovação, checklist), sem chamar tabela de outro módulo; `TotalStandardHoursAsync(templateId)` devolve a soma das horas padrão por centro, para a simulação da Fase 4.

**Indicador de etapa (D17 b, padrão de tela 3)**
14. **Dado** o `StageIndicator` (componente comum), **quando** exibido em qualquer tela, **então**: a etapa atual fica em âmbar (`--signal`), as concluídas ficam apagadas (token `muted`, nunca âmbar) e as futuras com o token da linha (`border`), como no protótipo (`.shaft i.done`, `.shaft i.now`); a letra vem do roteiro (`letters`), com a inicial do nome só como reserva; o `key` dos segmentos é o índice; acima de 8 etapas, mostra o formato compacto "M · 3/12" (letra · posição/total) sem os segmentos; o `aria-label` continua "Etapa {nome}, {n} de {total}"; o componente cabe a 400 px com 30 etapas. As telas que já usam o componente (Projeto, Projetos, Instalação, Implantação financeira, tenant do super admin) continuam corretas (teste de componente e conferência na galeria `dev/Gallery`).

**Permissões e segurança**
15. **Dado** o módulo `engineering` não liberado, **quando** chamo qualquer endpoint desta story, **então** 403 `module_not_released` e a tela `ModuleNotReleased`; sem grupo que habilite o módulo, 403 `forbidden` e o botão some do menu; sem `engineering.create`, `edit` ou `delete`, os botões somem e a API responde 403. `RequireModule` e `perm:` em todo endpoint (teste de arquitetura).
16. **[Segurança]** **Dado** as rotas `/api/v1/engineering/routing-templates/{id}/steps/{stepId}` e `/routing-templates/{id}/items/{itemId}`, **quando** a etapa ou o vínculo é de outro template, **então** 404 (IDOR pai/filho); o corpo é desserializado com `UnmappedMemberHandling.Disallow` e `seed_key`, `is_system` ou ids de etapa de outro template são recusados.
17. **[Segurança]** **Dado** duas pessoas editando o mesmo roteiro, **quando** a segunda salva com a versão antiga, **então** 409 `concurrency_conflict` "O roteiro mudou desde que você abriu. Recarregue." com "Recarregar" (ADR 0019 §2).
18. **Dado** qualquer alteração no roteiro, nas etapas ou nos vínculos de produto, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário e data.

## Regras de negócio e técnicas
- **Tabelas** (03, com os acréscimos desta story): `routing_templates` (+ `seed_key`, `is_deleted`, `created_at`; `product_type` texto livre até 60), `routing_template_steps` (+ `code` `char(1)`, a "Letra", único por `(template_id, code)`; `checklist` como JSON ordenado `[{"sequence":1,"text":"..."}]`, como no 03) e `routing_template_items` (`template_id`, `item_id` com índice único). Horas `numeric(18,4)`.
- **Letra da etapa (D10):** é o código que o Kanban (PRD-02) usa para as colunas e o display de andar mostra. A mesma letra em roteiros diferentes cai na mesma coluna; a `<Dica>` do campo diz isso.
- **Sequência contígua** regravada pelo servidor; reordenar não troca ids.
- **Cópia, não referência:** a OS copia etapas, letras e checklist (PRD-01). O template só serve de ponto de partida; o histórico de execução nunca depende dele.
- **Primeira etapa sem BOM (D12):** o roteiro só marca a regra (CA 6); o bloqueio no início de etapa é ligado pela ENG-02 na PRD-03a.
- **Modelo da instalação (ponto 4):** não é absorvido; a OS de instalação continua com o modelo próprio (COM-06). Os dois compartilham só os componentes visuais (`rows-editor`, editor de checklist, `StageIndicator`).
- **Roteiro no item do orçamento (ponto 6):** a coluna anulável `commercial.quote_items.routing_template_id` entra junto com a PRD-01, sem tela nesta fase.
- **Provedor de uso:** `IRoutingTemplateUsageProvider` em `Engineering.Contracts`, implementado pela Produção (OS) e pelo Comercial (item do orçamento e do projeto), os dois na PRD-01.
- **Ids** Guid v7; soft delete; auditoria `IAuditable` em template, etapa e vínculo; `xmin`.
- **Fora da fronteira:** o centro de trabalho é só o id (`production.work_centers`), validado por `IWorkCenters` (`Production.Contracts`); o produto é só o id, validado por `IItems`; o Engineering não referencia tabela de outro módulo.

## Permissões, eventos e tabelas
- **Permissões:** `engineering.view`, `engineering.create`, `engineering.edit` (já no `ModuleCatalog`) e `engineering.delete` (novo, D13). Seed patch `engineering-permissions` (CA 1).
- **Eventos:** nenhum (o template não tem efeito em outro módulo).
- **Contratos novos:** `IRoutingTemplates` (com `ForItemAsync`) e `IRoutingTemplateUsageProvider` em `Engineering.Contracts`.
- **Tabelas:** `engineering.routing_templates`, `engineering.routing_template_steps`, `engineering.routing_template_items` (primeira migration do schema `engineering`).

## Telas e estados
- **Padrões de tela da Fase 3:** 3 (indicador de etapa, corrigido aqui), 6 (tag "Sugerido" no centro sugerido).
- **Fábrica → Engenharia → Roteiros** (`/app/engenharia/roteiros`; entrada no `modules.ts`: `engineering` ganha as abas "Roteiros" e, na ENG-02, "BOMs"): h1 "Roteiros de fabricação" com a linha "O caminho de cada produto na fábrica: etapas, centros e horas padrão."; colunas nome, tipo de produto, etapas (letras no `StageIndicator`), horas padrão totais, produtos vinculados, situação, uso; filtros na URL (situação, tipo).
- **Detalhe** (`/app/engenharia/roteiros/$id`): cabeçalho com nome, chip e o `StageIndicator` com a letra de cada etapa; abas na URL: Etapas (editor de linhas), Produtos, Histórico de alterações. O checklist abre em painel lateral por etapa.
- **Verbos:** "Salvar roteiro" / "Roteiro salvo"; "Duplicar"; "Vincular produto" / "Produto vinculado"; "Inativar roteiro" / "Roteiro inativado"; "Excluir roteiro" / "Roteiro excluído".
- **Estados:** carregando (skeleton); vazio ("Nenhum roteiro cadastrado. Comece pelo roteiro de exemplo ou crie um novo." + "Novo roteiro"); vazio da aba Produtos ("Nenhum produto usa este roteiro. Vincule para a OS nascer com ele." + "Vincular produto"); erro com "Tentar de novo"; modo leitura sem `engineering.edit`, com a faixa que explica por quê.
- **Propositivo:** novo roteiro vem com as 5 etapas de exemplo e as letras (P-04) e o centro sugerido pelo último usado em cada etapa com o mesmo nome, com a tag "Sugerido"; letra sugerida pela inicial do nome; total de horas ao vivo no rodapé fixo (`aria-live`); depois de salvar, `NextSteps`: "Vincular produtos" (quando não há nenhum), "Ver OS sem roteiro" (com `production.view`, a partir da PRD-01) e "Duplicar para outro produto".
- **400 px:** a lista mostra nome, etapas e situação; as linhas do editor viram cartões com "Remover etapa {n}"; o checklist em tela cheia; o `StageIndicator` no formato compacto quando passar de 8 etapas.
- **Ajuda:** `Ajuda/engenharia-roteiros.md` (lista) e `Ajuda/engenharia-roteiro.md` (abas `etapas` com `padrao: sim`, `produtos` e `historico`); a ajuda explica que a Expedição é "preparar e embalar" e que a baixa é no romaneio (D4); `<Dica>` em "Letra" ("Código da etapa no quadro da produção. A mesma letra em roteiros diferentes fica na mesma coluna."), "Horas padrão" ("Horas de trabalho esperadas para uma unidade nesta etapa; o planejamento usa este número."), "Etapa de qualidade", "Exige aprovação" e "Começa sem BOM" (CA 6).

## Plano de testes
- **Unidade:** validação do roteiro (CA 5: sem etapa, primeira de qualidade, centro ausente, letra repetida ou inválida, limites); regravação da sequência e marca da primeira etapa (CA 3, 6); `StageIndicator` (CA 14: atual em âmbar, concluídas apagadas, compacto acima de 8, letra do roteiro, nomes repetidos, 30 etapas a 400 px).
- **Integração (Testcontainers):** seed patch de permissões uma vez só, com `delete` no catálogo (CA 1); CRUD com centro validado pelo `IWorkCenters` (CA 2–5); vínculo de produto único e 422 (CA 7); seed do roteiro de exemplo idempotente, nos tenants existentes, com letras e checklist da Expedição, sem recriar o excluído e inativo sem centro (CA 8); duplicar sem produtos (CA 9); excluir com `engineering.delete`, vínculos apagados, e 409 com uso (CA 10); inativo fora do seletor e do `ForItemAsync` (CA 11); `IRoutingTemplates` (CA 13); três camadas e IDOR (CA 15, 16); concorrência (CA 17); auditoria (CA 18).
- **Arquitetura:** Engineering só referencia `Production.Contracts` e `Inventory.Contracts`; `*.Contracts` não referencia outro `*.Contracts`; todo endpoint com `RequireModule` e `perm:`.
- **E2E (Playwright):** criar roteiro a partir do exemplo, reordenar etapa pelo teclado, trocar uma letra, preencher checklist, vincular produto e salvar; tela a 400 px.

## Fora do escopo
- BOM da OS e o bloqueio por BOM no início de etapa: ENG-02. Cópia do roteiro para a OS e etapas editáveis na OS: PRD-01.
- Modelo da OS de instalação (COM-06): continua em Produção → Modelo da OS de instalação (ponto 4).
- Tela do roteiro no item do orçamento: fora desta fase; a coluna entra na PRD-01 (ponto 6).
- Roteiro alternativo por centro, etapas paralelas e terceirizadas (R-PRD-10, pós-MVP).
- Simulação de prazo do orçamento: PLN-05 (Fase 4).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, tabela 1.1, itens 3–7):
1. Tipo de produto em texto livre com sugestões, mais `routing_template_items(template_id, item_id único)` ligando o roteiro ao produto (CA 2, 7).
2. Não absorver o modelo da instalação; compartilhar só os componentes visuais (Regras).
3. Exclusão com `engineering.delete` (D13, ADR 0003 adendo de 21/10/2026) (CA 1, 10).
4. `routing_template_id` anulável no item do orçamento, junto com a PRD-01, sem tela (Regras).
5. Seed patch `engineering-permissions` aprovado (CA 1).

Decisões da revisão aplicadas: D10 (campo "Letra", CA 3 e 5), D12 (primeira etapa sem BOM, CA 6), D4 (Expedição "preparar e embalar", CA 8), D17 (b) (`StageIndicator`, CA 14).

**Pré-condição externa:** nenhuma.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras` (ADR 0020); seed do roteiro de exemplo idempotente; seed patch `engineering-permissions` com `delete` no `ModuleCatalog`.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] `StageIndicator` corrigido (D17 b) sem quebrar as telas que já o usam.
- [ ] Telas com estados de carregando, vazio e erro, inclusive a 400 px.
- [ ] Ajuda da lista e de cada aba escrita no mesmo commit; teste de cobertura passando.
- [ ] Padrões preenchidos e próximos passos avaliados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado (`code` da etapa, `routing_template_items`, colunas novas); story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `IEngineeringDb` com `RoutingTemplate`, `RoutingTemplateStep` e `RoutingTemplateItem`; configuração EF `internal` no schema `engineering`.
- [ ] `RoutingTemplateService` (criar, editar, duplicar, vincular produto, inativar, excluir) e validador FluentValidation.
- [ ] `RoutingTemplateSeeder` (exemplo P-04 com letras e Expedição D4) e `SeedPatch` `engineering-permissions`; `delete` no `ModuleCatalog`.
- [ ] `IRoutingTemplates` e `IRoutingTemplateUsageProvider` em `Engineering.Contracts`.
- [ ] Endpoints `/api/v1/engineering/routing-templates`; páginas, `modules.ts` e ajudas.
- [ ] `StageIndicator`: cores, compacto acima de 8, `key` pelo índice, testes e galeria.

## Quality gates
- Agentes previstos: @dev, @architect (primeiro schema do Engineering e contrato com a Produção), design (editor de etapas, checklist e `StageIndicator`), @qa.
- Gate: CA 5, 7, 8, 12, 14, 15 e 16 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: primeira story do Marco 3.1) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** roteiro do seed inativo quando não há centro único (CA 6), em vez de nascer com centro inventado; exclusão sem a ação `delete` registrada como ponto em aberto (3), sem decidir; aviso de que editar o roteiro não muda as OS já criadas (CA 10).
- **Correções da revalidação (21/10/2026):** pontos 3 a 7 decididos (vínculo `routing_template_items` com produto único, CA 7; `engineering.delete` no catálogo e no seed, CA 1 e 10; coluna do orçamento na PRD-01); campo "Letra" com validação (D10, CA 3 e 5); marca "Começa sem BOM" na primeira etapa (D12, CA 6); Expedição "preparar e embalar" no seed (D4, CA 8); correção do `StageIndicator` (D17 b, padrão 3, CA 14); `ForItemAsync` no contrato (CA 13); cabeçalho pela nova ordem (Marco 3.1, ordem 1); CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO (D4, D10, D12, D13, D17 b, tabela 1.1 itens 3–7) e correções da revisão pré-Fase 3 (§2.3 padrão 3); nova ordem (Marco 3.1, ordem 1); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
