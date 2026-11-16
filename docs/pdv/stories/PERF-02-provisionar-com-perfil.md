# PERF-02 — Provisionar com perfil de negócio e segmento, e adicionar ou remover perfil em tenant existente pela aba "Perfis"

| Campo | Valor |
|---|---|
| ID | PERF-02 |
| Título | Provisionar com perfil de negócio e segmento, e adicionar ou remover perfil em tenant existente pela aba "Perfis" |
| Tamanho | M |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
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
