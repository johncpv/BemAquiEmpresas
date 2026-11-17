# PERF-01 — Perfis de negócio no catálogo (`BusinessProfileCatalog`, `tenant_profiles`), liberação efetiva por plano e perfil e o estado "oculto" para módulo fora do perfil

| Campo | Valor |
|---|---|
| ID | PERF-01 |
| Título | Perfis de negócio no catálogo (`BusinessProfileCatalog`, `tenant_profiles`), liberação efetiva por plano e perfil e o estado "oculto" para módulo fora do perfil |
| Tamanho | G |
| Status | Ready for Review (implementada em 16/11/2026, branch `feat/perf-01-perfis`; PR, merge e deploy pendentes) |
| Fase | 10 — Fundação: perfil de negócio, catálogo, pagamentos, núcleo de vendas e spikes |
| Requisitos | Pedido do PO de 15/11/2026 (itens 1, 2 e 3) · [PRD do PDV](../01-PRD.md) seção 3 · ADR 0003 · ADR 0006 · FND-06 (catálogo de módulos e liberação por tenant) |
| Dependências | PP-03 decidida; FND-06 (existente) |
| Bloqueia | PERF-02, PERF-03, PERF-04, CAT-01, PDV-01 |
| Ordem de execução | 1 (Fase 10) |

## User story
Como **super admin**, quero **dizer que tipo de negócio cada empresa é**, para **o sistema mostrar a ela só o que faz sentido e eu não ter de marcar módulo por módulo**.

## Contexto e referências
- A liberação de módulos já é por tenant e sem deploy: `tenant_modules` herda de `plan_modules`, e a exceção manual sobrevive à troca de plano (`TenantModuleService.ApplyPlanAsync`).
- Hoje o módulo não liberado aparece no menu com cadeado (`CLAUDE.md`; `isVisible` em `src/Web/src/navigation/modules.ts`). Uma padaria veria Engenharia e Produção com cadeado.
- Esta story escreve a ADR dos perfis de negócio ([lista](../adr/README.md#adrs-que-ainda-precisam-ser-escritas)), porque muda uma regra do `CLAUDE.md`.

## Critérios de aceite
### Catálogo de perfis
1. **Dado** o `BusinessProfileCatalog` em `BuildingBlocks/Modules`, **quando** a aplicação sobe, **então** ele declara os perfis `factory`, `retail`, `services` e `ecommerce` e os segmentos `counter`, `supermarket`, `bakery`, `restaurant`, `snack_bar`, `food_truck` (de `retail`) e `workshop` (de `services`), cada perfil com a lista de códigos de módulo do `ModuleCatalog`.
2. **Dado** um perfil que cita um código de módulo que não existe no `ModuleCatalog`, **quando** os testes rodam, **então** um teste de arquitetura falha com o nome do perfil e do código.
3. **Dado** o perfil `factory`, **então** ele lista exatamente os módulos que a Konebras tem hoje.

### Perfis do tenant
4. **Dado** a migration do banco de catálogo, **quando** é aplicada, **então** existe `catalog.tenant_profiles` e todo tenant existente tem o perfil `factory`, com `applied_by` do sistema.
5. **Dado** um tenant com um ou mais perfis, **quando** o perfil é aplicado, **então** `tenant_modules` passa a ter `liberado = (módulo está no plano E em algum perfil do tenant) OU exceção manual`; nenhuma linha marcada como exceção manual é alterada.
6. **Dado** um tenant com `retail` e `ecommerce`, **quando** consulto os módulos, **então** vale a união dos dois perfis.

### Estado oculto
7. **Dado** um módulo fora de todos os perfis do tenant e sem exceção manual, **quando** chamo `GET /me/permissions`, **então** ele vem com o estado `hidden`; não aparece no dock, na busca nem no "Pergunte ao BemAqui".
8. **Dado** um módulo dentro do perfil e fora do plano, **quando** abro o menu, **então** ele aparece com cadeado e a tela `ModuleNotReleased`, como hoje.
9. **[Segurança]** **Dado** um módulo oculto, **quando** chamo a rota da API ou abro a URL direto, **então** a API responde 403 `module_not_released` e o front mostra a tela padrão com "Funcionalidade não liberada nessa versão do sistema.". Oculto é só apresentação; a camada 1 continua sendo quem barra.
10. **Dado** uma exceção manual que libera um módulo fora do perfil, **quando** abro o menu, **então** o módulo aparece normalmente.

### Regressão
11. **Dado** o tenant da Konebras antes e depois da migration, **quando** comparo `GET /me/permissions` e o menu de cada grupo do seed, **então** são idênticos (teste de regressão com o retrato gravado antes).

## Regras de negócio e técnicas
- `tenant_modules` continua sendo a **única** tabela lida pela camada 1. O perfil é uma entrada do cálculo, não uma quarta camada.
- Aplicar perfil é idempotente e roda dentro do `ApplyPlanAsync`, depois do plano.
- Segmento não altera a liberação: só é lido pelos seeds (PERF-04) e pelos padrões de tela.
- O cache de permissões efetivas é invalidado quando o perfil do tenant muda.

## Permissões, eventos e tabelas
- **Permissões:** nenhuma ação nova. Perfil é administrado pelo super admin (área da plataforma).
- **Endpoints:** `GET /me/permissions` ganha o estado `hidden` por módulo. Os de administração do perfil são da PERF-02.
- **Tabelas:** `catalog.tenant_profiles` ([modelo](../03-modelo-dados.md#1-platform-banco-de-catálogo)); migration do catálogo com backfill.
- **Auditoria:** aplicar e remover perfil.

## Telas e estados
- **Dock e busca:** módulo `hidden` não é renderizado. Sem tela nova.
- **Ajuda:** `Ajuda/inicio.md` ganha um parágrafo "Por que não vejo todos os módulos"; a ajuda da tela `ModuleNotReleased` não muda.

## Plano de testes
- **Unidade:** cálculo da liberação efetiva (plano × perfis × exceção), com a tabela-verdade completa.
- **Integração (Testcontainers):** backfill `factory`; união de perfis; exceção manual preservada; `hidden` em `/me/permissions`; 403 na rota direta de módulo oculto (CA 9).
- **Regressão:** retrato da navegação da Konebras (CA 11).
- **Arquitetura:** perfil só cita código existente (CA 2).
- **Vitest:** `isVisible` com os três estados (liberado, cadeado, oculto).

## Fora do escopo
- Telas de provisionamento e a aba Perfis (PERF-02).
- Área "Loja" e vocabulário (PERF-03).
- Dados iniciais (PERF-04).

## Pontos em aberto
- PP-03: **decidida em 16/11/2026: oculto** (o cadeado fica para "no perfil, fora do plano"). Registrada na [ADR 0046](../../adr/0046-perfis-de-negocio-e-modulo-oculto.md).
- Os módulos novos (`catalog`, `sales`, `pos` e os demais) entram no `ModuleCatalog` nas stories de cada um; até lá o perfil `retail` só cita os que existem.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade e de integração (Testcontainers) passando; Vitest no front.
- [x] Migration criada; permissões só do `ModuleCatalog`.
- [x] Telas com estados de carregando, vazio e erro (sem tela nova); ajuda atualizada em `Ajuda/inicio.md` no mesmo commit.
- [x] Padrões e próximo passo propositivos avaliados (sem tela nova; a PERF-02 traz a aba Perfis); auditoria ligada.
- [x] OpenAPI e cliente Orval regenerados; story marcada no backlog; decisões em ADR (0046).

## O que foi feito (16/11/2026)
**Revisão antes de codar (story × código):**
- A story não dizia o que vale para um tenant **sem perfil**. Decidido: sem perfil = sem filtro (o comportamento de antes, só plano e exceção, com cadeado). É rede de segurança; a migration dá `factory` a todos e o provisionamento aplica `factory` até a PERF-02 trazer a escolha. O último perfil ativo não pode ser removido.
- A exceção manual era "diferente do plano" (`SaveAsync`). Passou a ser "diferente do herdado (plano E perfil)", senão liberar à mão um módulo fora do perfil (CA 10) não ficaria marcado como exceção e cairia no próximo recálculo.
- O "oculto" não cabe em `tenant_modules` (que só tem `enabled`) sem mudar a tabela que a camada 1 lê. Ele é calculado no tenant resolvido (`ResolvedTenant.HiddenModules` = não liberado e fora de todo perfil) e entra no hash da versão de permissões só quando existe, para a versão da Konebras não mudar.
- A ajuda e o "Pergunte ao BemAqui" já filtravam por módulo liberado (`HelpAccess`): o oculto some deles sem código novo.
- `CatalogTenantProfile` já existia com outro sentido (documento e ambiente fiscal do tenant): a entidade nova se chama `TenantProfile` no domínio e `tenant_profiles` no banco, sem colisão.
- A PRD diz que restaurante e KDS só aparecem nos segmentos de alimentação, mas a story diz que segmento não altera a liberação. Ficou a regra da story; a ADR 0046 §4 prevê adendo na Fase 15.
- `applied_by` "do sistema" = nulo, com a origem da auditoria dizendo "Sistema · migration PERF-01".

**Entregue:**
- `BusinessProfileCatalog` (`BuildingBlocks/Modules`): 4 perfis, 7 segmentos, `factory` = os 16 módulos de hoje; regras puras de liberação herdada e dos três estados (`ModuleVisibility`).
- `catalog.tenant_profiles` (migration `TenantProfiles` do catálogo, só acréscimo): índice único parcial entre os não removidos com `NULLS NOT DISTINCT`, trigger de NOTIFY e backfill `factory` auditado. `tenant_modules` não é tocada.
- `TenantModuleService`: `ApplyPlanAsync` com plano × perfis; `ApplyProfileAsync`, `RemoveProfileAsync` e `GetProfilesAsync` (idempotentes, auditados; os endpoints são da PERF-02); `ModuleRelease` ganhou `inProfile`. Provisionamento aplica `factory`.
- `GET /me/permissions` e `GET /modules` com `hidden`; front: `isVisible` esconde o oculto do dock, do Início e da busca; a rota direta cai em `ModuleNotReleased` (o oculto chega como não liberado).
- [ADR 0046](../../adr/0046-perfis-de-negocio-e-modulo-oculto.md); `CLAUDE.md` (camada 1) e ADR 0002 atualizados; `Ajuda/inicio.md` com "Por que não vejo todos os módulos".

**Testes:** unidade (tabela-verdade plano × perfil × exceção, união, `factory` = 16 módulos, perfis e segmentos); arquitetura (perfil só cita módulo do catálogo, com perfil e código na mensagem); integração (regressão da Konebras com retrato de `/me/permissions` de um usuário de cada grupo do seed antes e depois do backfill; união, cadeado, oculto, 403 `module_not_released` na rota direta, ajuda sem o oculto, exceção manual preservada, último perfil, auditoria); Vitest (liberado, cadeado e oculto).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
| 16/11/2026 | 1.0 | Implementada: revisão contra o código (tenant sem perfil, exceção relativa ao herdado, oculto no tenant resolvido), ADR 0046, migration com backfill `factory` e regressão da Konebras. | Claude (dev) |
