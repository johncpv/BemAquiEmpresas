# PERF-01 — Perfis de negócio no catálogo (`BusinessProfileCatalog`, `tenant_profiles`), liberação efetiva por plano e perfil e o estado "oculto" para módulo fora do perfil

| Campo | Valor |
|---|---|
| ID | PERF-01 |
| Título | Perfis de negócio no catálogo (`BusinessProfileCatalog`, `tenant_profiles`), liberação efetiva por plano e perfil e o estado "oculto" para módulo fora do perfil |
| Tamanho | G |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
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
- PP-03: oculto ou cadeado. Se o PO preferir o cadeado, os CA 7 a 10 caem e a story vira M.
- Os módulos novos (`catalog`, `sales`, `pos` e os demais) entram no `ModuleCatalog` nas stories de cada um; até lá o perfil `retail` só cita os que existem.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade e de integração (Testcontainers) passando; Vitest no front.
- [ ] Migration criada; permissões só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio e erro; ajuda escrita ou atualizada em `Ajuda/` no mesmo commit.
- [ ] Padrões e próximo passo propositivos avaliados; auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; story marcada no backlog; decisões em ADR.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
