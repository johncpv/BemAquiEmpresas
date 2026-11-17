# PERF-02 — Provisionar com perfil de negócio e segmento, e adicionar ou remover perfil em tenant existente pela aba "Perfis"

| Campo | Valor |
|---|---|
| ID | PERF-02 |
| Título | Provisionar com perfil de negócio e segmento, e adicionar ou remover perfil em tenant existente pela aba "Perfis" |
| Tamanho | M |
| Status | Ready for Review (implementada em 17/11/2026, branch `feat/perf-02-provisionar-perfil`; PR, merge e deploy pendentes) |
| Fase | 10 — Fundação: perfil de negócio, catálogo, pagamentos, núcleo de vendas e spikes |
| Requisitos | Pedido do PO de 15/11/2026 (item 3: perfil fácil de habilitar) · [PRD do PDV](../01-PRD.md) seção 3.4 · FND-03a e FND-03b (provisionamento e tela do super admin) |
| Dependências | PERF-01 |
| Bloqueia | PERF-04 |
| Ordem de execução | 2 (Fase 10) |

## User story
Como **super admin**, quero **escolher o perfil de negócio ao criar a empresa e poder acrescentar outro depois**, para **uma empresa nova já nascer com a cara do negócio dela, em minutos**.

## Contexto e referências
- O provisionamento roda em menos de 1 minuto, com etapas retomáveis e log por etapa (`ProvisioningService`; `ProvisionDialog.tsx`).
- O detalhe do tenant é `TenantDetailPage.tsx`.

## Critérios de aceite
1. **Dado** "Nova empresa", **quando** avanço dos dados da empresa, **então** vejo o passo "Perfil de negócio" com quatro cards (Fábrica, Comércio, Prestação de serviços, E-commerce), cada um com uma linha do que libera; posso marcar mais de um e preciso marcar ao menos um.
2. **Dado** Comércio ou Prestação de serviços marcado, **quando** avanço, **então** vejo o passo "Segmento" só com os segmentos do perfil marcado; "Balcão (loja em geral)" vem escolhido para Comércio.
3. **Dado** os perfis escolhidos, **quando** chego ao passo do plano, **então** vejo o aviso de quais módulos do perfil o plano escolhido **não** cobre (vão aparecer com cadeado).
4. **Dado** o provisionamento confirmado, **quando** roda, **então** o perfil é aplicado logo depois do `ApplyPlanAsync`, há uma linha de log "Perfil aplicado" e o tempo total continua abaixo de 1 minuto.
5. **Dado** a falha numa etapa depois do perfil, **quando** retomo, **então** o perfil não é aplicado duas vezes.
6. **Dado** o detalhe de um tenant, **quando** abro a aba "Perfis", **então** vejo os perfis atuais com segmento, data e quem aplicou, e a ação "Adicionar perfil".
7. **Dado** "Adicionar perfil" com um perfil que o tenant já tem, **quando** confirmo, **então** nada muda e a mensagem diz "Esta empresa já tem o perfil Comércio.".
8. **Dado** "Remover perfil", **quando** confirmo, **então** os módulos só daquele perfil passam a `hidden`, **nenhum dado é apagado** e a confirmação diz isso; não é possível remover o último perfil.
9. **Dado** um módulo com exceção manual, **quando** adiciono ou removo um perfil, **então** a exceção continua como estava.
10. **[Segurança]** **Dado** quem não é super admin, **quando** chama os endpoints de perfil, **então** recebe 403.

## Regras de negócio e técnicas
- Adicionar perfil roda também o pacote de dados iniciais do perfil (PERF-04), idempotente por `seed_key`.
- Remover perfil não remove dados iniciais nem grupos.
- A ordem das etapas do provisionamento fica: banco → migrations → plano → **perfil** → seed → proprietário.

## Permissões, eventos e tabelas
- **Permissões:** área da plataforma (super admin), sem ação nova no `ModuleCatalog`.
- **Endpoints:** `GET /api/v1/platform/business-profiles` (catálogo); `GET|POST|DELETE /api/v1/platform/tenants/{id}/profiles`; o `POST` de provisionamento aceita `profiles[]`.
- **Tabelas:** `catalog.tenant_profiles` (`removed_at`).
- **Auditoria:** perfil adicionado e removido.

## Telas e estados
- **`ProvisionDialog`:** passos novos "Perfil de negócio" e "Segmento". Erro: "Escolha ao menos um perfil.".
- **`TenantDetailPage`, aba Perfis:** carregando; vazio não existe (todo tenant tem ao menos um perfil); erro com "Tentar de novo".
- **Ajuda:** `Ajuda/plataforma-tenant-perfis.md` (nova, aba da `plataforma-tenant.md`) e `Ajuda/plataforma-tenants.md` atualizada (passos novos do provisionamento).

## Plano de testes
- **Integração (Testcontainers):** provisionar com dois perfis; retomar sem duplicar (CA 5); adicionar idempotente (CA 7); remover só oculta (CA 8); exceção preservada (CA 9).
- **Vitest:** passos do diálogo e a regra do segmento por perfil.

## Fora do escopo
- O cliente criar a própria conta (PP-27).
- Limites comerciais por plano (PP-17).

## Pontos em aberto
- PP-17: se o plano limitar caixas ou lojas, o aviso do CA 3 passa a mostrar também os limites.

## Implementação (17/11/2026): o que a revisão contra o código ajustou
- **Etapa "Perfil" no log.** O perfil virou uma etapa própria do provisionamento (`ProvisioningSteps.Profile`), logo depois do registro: ela aplica o plano e os perfis escolhidos (o `ApplyProfileAsync` já roda o `ApplyPlanAsync`) e grava a linha "Perfil aplicado: Comércio · Padaria." (CA 4). A ordem fica registro → **plano e perfil** → usuário do banco → banco → estrutura → dados iniciais → proprietário. O plano já era aplicado no registro, antes do banco existir, então "perfil antes do seed" (o que importa para a PERF-04) vale; a story falava em "migrations → plano → perfil", o que não refletia o código.
- **Retomada (CA 5).** A etapa concluída não roda de novo; se ela falhar, a retomada reaplica sem duplicar (aplicar é idempotente). A retomada pela tela não traz a escolha de perfis: se nada tiver sido gravado, aplica Fábrica (o comportamento de antes), e o super admin corrige na aba Perfis. Tenants provisionados antes desta story mostram a etapa como "Sem registro".
- **Endpoints.** `GET /platform/business-profiles`, `GET /platform/plans/modules` (o aviso do CA 3 precisa dos módulos de cada plano), `GET|POST /platform/tenants/{tenantId}/profiles` e `DELETE /platform/tenants/{tenantId}/profiles/{perfil}?segment=`. Mesma sessão de super admin e mesmo filtro de origem da aba Módulos. O `POST /platform/tenants` aceita `profiles[]`; sem o campo vale Fábrica, e lista vazia é 422 "Escolha ao menos um perfil.".
- **CA 10.** Sem a sessão do super admin a resposta é **401** (não 403), como todas as rotas da plataforma; no host do tenant a rota não existe (404).
- **CA 7.** "Já tem" compara perfil **e** segmento: Comércio · Padaria numa empresa com Comércio · Balcão é um perfil novo (é assim que se troca o segmento). A mensagem cita o segmento quando há ("Esta empresa já tem o perfil Comércio · Balcão.").
- **Quem aplicou (CA 6).** O provisionamento grava o super admin que pediu (`applied_by`); a aba mostra o e-mail dele ou "Sistema". O histórico (aplicado e removido, com data e quem fez) é a auditoria visível; as entradas `ProfileApplied` e `ProfileRemoved` do catálogo levam agora também o IP.
- **Segmento de Serviços.** Pode ficar "Em geral" (sem segmento) ou Oficina; Comércio exige segmento.
- **Sem migration.** `tenant_profiles` (PERF-01) já tinha tudo; nenhuma tabela mudou.
- **Fora desta story:** dados iniciais por perfil ao adicionar (PERF-04); a CLI `tenant provision` continua aplicando Fábrica.

## Definition of Done
- [x] Critérios de aceite atendidos (CA 10 com 401, ver acima).
- [x] Testes de integração (Testcontainers) passando; Vitest no front.
- [x] Sem migration (a tabela é da PERF-01); permissões: área da plataforma, nada novo no `ModuleCatalog`.
- [x] Telas com estados de carregando e erro (o vazio não existe); ajuda escrita (`Ajuda/plataforma-tenant-perfis.md`) e atualizada (`plataforma-tenants.md`, `plataforma-tenant.md`) no mesmo commit.
- [x] Padrões e próximo passo propositivos avaliados (Fábrica e Balcão já marcados, aviso do plano); auditoria ligada e visível.
- [x] OpenAPI e cliente Orval regenerados; story marcada no backlog; sem decisão nova de arquitetura (ADR 0046 cobre).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
| 17/11/2026 | 1.0 | Implementada: etapa "Perfil" no provisionamento, passos no `ProvisionDialog`, aba Perfis com histórico e os endpoints da plataforma. | Claude (dev) |
