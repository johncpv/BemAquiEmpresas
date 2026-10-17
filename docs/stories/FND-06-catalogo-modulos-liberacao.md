# FND-06 — Catálogo de módulos e liberação por tenant

| Campo | Valor |
|---|---|
| ID | FND-06 |
| Título | `ModuleCatalog` (módulos, ações padrão e específicas, significado de "aprovar"), `plan_modules`/`tenant_modules` e tela de liberação |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-SEC-11 · R-SEC-10 (significado de "aprovar" por módulo) · R-SEC-12 (limite pelos módulos liberados) |
| Dependências | FND-02, FND-03a (seed no provisionamento), FND-13 e FND-03b (tela no painel da plataforma) |
| Bloqueia | FND-06b, FND-06c, FND-06d, FND-14 (ajuda filtrada por módulo) |
| Ordem de execução | 12 |

## User story
Como **super admin da plataforma**, quero **liberar ou bloquear módulos para cada tenant conforme a versão contratada, sem deploy**, para **vender o sistema por versão e ligar um módulo novo para o cliente na hora**.

## Contexto e referências
- PRD R-SEC-10, R-SEC-11, R-SEC-12.
- ADR 0002 (grupos e módulos), **ADR 0003** (ações específicas em snake_case, dois níveis `modulo.acao`; `applicable_actions` com rótulo e descrição).
- `docs/03-modelo-dados.md` → `catalog.plans`, `catalog.plan_modules`, `catalog.tenant_modules` (`enabled`, `released_at`; herda do plano com exceções manuais), `identity.modules`.
- `docs/04-fluxos-estados.md` §3.7 (permissão efetiva; ordem: liberado → habilitado e ação; cache invalidado ao alterar a liberação).
- Revisão §2.1: criar a tabela `module_actions` (ADR 0003). Revisão §2.3: revogar módulo vale no próximo request.
- Protótipo: lista `MODS` com nome, ações e significado de "aprovar".

## Critérios de aceite
1. **Dado** o seed do `ModuleCatalog`, **quando** roda no provisionamento ou no deploy, **então** `identity.modules` e `identity.module_actions` ficam iguais ao catálogo do código; rodar de novo não duplica nem apaga configuração de grupo (idempotente).
2. **Dado** um módulo no catálogo, **quando** inspeciono o seed, **então** ele tem código, nome em PT-BR, área (uma das 7 do dock), fase, ordem, ações padrão aplicáveis, ações específicas (código snake_case, rótulo e descrição) e o texto do que "aprovar" significa nele (quando `approve` se aplica).
3. **Dado** o teste de arquitetura, **quando** um endpoint usa uma permissão `modulo.acao` que não existe no `ModuleCatalog`, ou usa três níveis (`modulo.entidade.acao`), **então** o teste falha.
4. **Dado** um módulo não liberado para o tenant, **quando** chamo qualquer endpoint desse módulo, **então** recebo 403 `code: "module_not_released"`, antes da checagem de grupo e ação.
5. **Dado** o super admin na aba "Módulos" do tenant (FND-03b), **quando** libera um módulo e confirma "Salvar liberação", **então** no próximo request dos usuários do tenant o módulo passa a valer, sem deploy.
6. **[Segurança]** **Dado** um módulo liberado, **quando** o super admin o bloqueia, **então** no próximo request os usuários recebem 403 `module_not_released` (cache de permissão invalidado).
7. **Dado** um plano com módulos em `plan_modules`, **quando** um tenant é provisionado com esse plano, **então** `tenant_modules` recebe os módulos do plano; exceções manuais do super admin (liberar ou bloquear a mais) ficam marcadas como exceção e sobrevivem a uma mudança do plano.
8. **Dado** a liberação, **quando** é alterada, **então** a auditoria da plataforma registra o super admin, o tenant, o módulo e antes/depois.

## Regras de negócio e técnicas
- `ModuleCatalog` em código (fonte única), lido por: seed, policy de permissão, `/me/permissions`, matriz de grupos e ajuda.
- Ações padrão: `view`, `create`, `edit`, `approve`, `delete`. Ações específicas só quando houver necessidade real (ADR 0003); nenhuma é exigida na Fase 0, mas o catálogo já declara as conhecidas: `hr.view_sensitive`, `inventory.adjust`, `finance.pay`.
- Atributo/extensão de endpoint `RequireModule("<código>")` no grupo de rotas do módulo; checagem de liberação sempre antes da de permissão.
- Cache de módulos liberados por tenant com invalidação ativa (mesmo mecanismo da FND-02).
- Módulos de código (`src/Modules/*`) não são 1:1 com módulos do catálogo: um módulo de código pode ter mais de um módulo de catálogo (ex.: Commercial → orçamentos e projetos), porque não há permissão por entidade (ADR 0003).

### Proposta de catálogo inicial (a aprovar pelo PO; ver Pontos em aberto)
| Código | Nome | Área | Ações padrão | "Aprovar" significa |
|---|---|---|---|---|
| `dashboard` | Painel do proprietário | Visão geral | view | — |
| `quotes` | Orçamentos | Comercial | view, create, edit, approve, delete | gerar o contrato |
| `projects` | Projetos | Comercial | view, create, edit, approve, delete | aditivos de contrato |
| `parties` | Cadastros (terceiros) | Comercial | view, create, edit, delete | — |
| `engineering` | Engenharia | Fábrica | view, create, edit, approve | liberar a BOM |
| `production` | Produção | Fábrica | view, create, edit, approve | liberar na qualidade |
| `inventory` | Estoque | Fábrica | view, create, edit, approve + `adjust` | ajuste de inventário |
| `purchasing` | Compras | Fábrica | view, create, edit, approve, delete | pedido de compra dentro da alçada |
| `assets` | Patrimônio | Fábrica | view, create, edit, delete | — |
| `finance` | Caixa e pagamentos | Financeiro | view, create, edit, approve, delete + `pay` | ordem de pagamento, dentro da alçada |
| `fiscal` | Fiscal | Financeiro | view, create, edit | — |
| `hr` | RH | Pessoas | view, create, edit, approve, delete + `view_sensitive` | ajustes de ponto |
| `kiosk` | Painel da TV | Telas dedicadas | view, create | — (criar = gerar token de tela) |
| `shopfloor` | App do operador | Telas dedicadas | view, create | — (criar = apontar etapas) |
| `identity` | Usuários e acessos | Sistema | view, create, edit, delete | — |
| `settings` | Configurações | Sistema | view, edit | — |

## Permissões, eventos e tabelas
- **Tabelas:** `catalog.plans`, `catalog.plan_modules`, `catalog.tenant_modules` (+ `is_exception`), `identity.modules`, `identity.module_actions` (nova: `module_code`, `action`, `label`, `description`, `is_standard`).
- **Permissões:** o próprio catálogo. Nenhuma ação nova de tenant nesta story.
- **Eventos:** `TenantModulesChanged` (catálogo) para invalidar cache.

## Telas e estados
- **Aba "Módulos" no detalhe do tenant (FND-03b):** lista por área com chave "Liberado", origem (Plano / Exceção) e a descrição do módulo; botão "Salvar liberação"; chip "Alterações não salvas" como no protótipo; toast "Liberação salva. Vale no próximo acesso dos usuários." Estados: carregando, erro com "Tentar de novo"; vazio não se aplica (o catálogo nunca é vazio).
- **Propositivo:** ao trocar o plano, a tela mostra o que muda antes de salvar.
- **Ajuda:** conforme decisão sobre a superfície Plataforma (FND-13).

## Plano de testes
- **Unidade:** consistência do catálogo (códigos únicos, snake_case nas específicas, `approve` com significado).
- **Arquitetura:** CA 3.
- **Integração (Testcontainers):** seed idempotente rodando duas vezes (CA 1); 403 `module_not_released` antes de `forbidden` (CA 4); liberar e bloquear sem deploy, valendo no próximo request (CA 5, 6); herança do plano com exceção (CA 7).
- **E2E:** liberar um módulo na plataforma e ver o efeito no tenant (completo após FND-06d).

## Fora do escopo
- Grupos e matriz de permissões (FND-06b).
- Menu e tela "Funcionalidade não liberada nessa versão do sistema." (FND-06d).
- Cadastro e preço de planos.

## Pontos em aberto
- **Catálogo definitivo de módulos:** a tabela acima é proposta derivada do protótipo e do PRD. O PO precisa aprovar códigos, nomes, áreas, ações aplicáveis e significados de "aprovar" (o PRD só dá o exemplo de Caixa). Em particular: `parties`, `engineering`, `purchasing`, `assets`, `fiscal`, `hr`, `settings` não aparecem na matriz do protótipo com ações.
- **`settings` como módulo:** o PRD usa `settings.edit` (R-AJD-8), mas Configurações não está entre os módulos do CLAUDE.md. Confirmar.
- **Plano inicial da Konebras** e quais módulos ficam liberados na Fase 0 (só `identity` e `settings`?).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, arquitetura e integração (Testcontainers) passando.
- [ ] Migrations criadas (`plan_modules`, `tenant_modules`, `module_actions`).
- [ ] Permissões registradas no seed (`ModuleCatalog`).
- [ ] Tela com estados de carregando e erro.
- [ ] Ajuda da tela conforme decisão.
- [ ] Padrões e próximo passo propositivos avaliados.
- [ ] Auditoria ligada.
- [ ] `docs/03-modelo-dados.md` atualizado com `module_actions`; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `ModuleCatalog` com a lista aprovada pelo PO.
- [ ] `ITenantSeeder` do catálogo de módulos.
- [ ] Tabelas e migrations do catálogo e do tenant.
- [ ] `RequireModule` e checagem de liberação no pipeline.
- [ ] Cache com invalidação ativa.
- [ ] API e aba "Módulos" na plataforma.
- [ ] Teste de arquitetura de permissões.

## Quality gates
- Agentes previstos: @architect, @dev, @qa.
- Gate: catálogo aprovado pelo PO antes do início (Definition of Ready).

## Validação do PO
Checklist do @po (Pax), 16/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança da revisão §2.3 que se aplicam | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** —
- **Pré-condições para `Ready`:** Catálogo de módulos aprovado pelo PO (códigos, áreas, ações e significado de "aprovar"); módulo `settings`.
- **Prontidão:** 7/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 16/10/2026 | 0.1 | Story detalhada com ADR 0003 e a tabela `module_actions` da revisão | River (SM) |
| 16/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |

## Dev Agent Record
_A preencher pelo @dev._

## QA Results
_A preencher pelo @qa._