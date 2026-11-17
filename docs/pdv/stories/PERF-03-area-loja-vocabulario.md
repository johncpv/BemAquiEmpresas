# PERF-03 — Área "Loja" no dock, rótulo de área por perfil ("Retaguarda" no lugar de "Fábrica") e vocabulário por perfil servido no `tenant-context`

| Campo | Valor |
|---|---|
| ID | PERF-03 |
| Título | Área "Loja" no dock, rótulo de área por perfil ("Retaguarda" no lugar de "Fábrica") e vocabulário por perfil servido no `tenant-context` |
| Tamanho | M |
| Status | Done (17/11/2026), aguardando PR |
| Fase | 10 — Fundação: perfil de negócio, catálogo, pagamentos, núcleo de vendas e spikes |
| Requisitos | Pedido do PO de 15/11/2026 (item 3) · [PRD do PDV](../01-PRD.md) seção 3.2 · ADR 0005 (shell, dock e navegação) |
| Dependências | PERF-01; PP-25 |
| Bloqueia | CAT-01 (o módulo novo entra na área "Loja") |
| Ordem de execução | 3 (Fase 10) |

## User story
Como **dono de um comércio**, quero **ver "Loja" e "Retaguarda" no menu, e não "Fábrica"**, para **reconhecer o meu negócio no sistema**.

## Contexto e referências
- As áreas do dock são fixas no `ModuleAreas` (`ModuleCatalog.cs`), e uma se chama "Fábrica"; Estoque e Compras estão nela.
- `GET /api/v1/public/tenant-context` já serve nome e logo para o login.

## Critérios de aceite
1. **Dado** o `ModuleAreas`, **quando** a aplicação sobe, **então** existe a área `Store`, com o rótulo "Loja", entre "Comercial" e "Fábrica".
2. **Dado** um tenant sem o perfil `factory`, **quando** abro o dock, **então** a área que agrupa Estoque e Compras se chama "Retaguarda".
3. **Dado** um tenant com `factory` (com ou sem outros perfis), **quando** abro o dock, **então** a área continua "Fábrica".
4. **Dado** o vocabulário do perfil, **quando** o front carrega o contexto do tenant, **então** recebe um dicionário de termos; o componente de texto traduz as chaves conhecidas e mantém o termo padrão nas demais.
5. **Dado** um perfil com mais de 30 chaves de vocabulário, **quando** os testes rodam, **então** um teste falha (teto).
6. **Dado** um tenant com dois perfis que definem a mesma chave, **quando** carrego, **então** vale a ordem fixa `factory`, `retail`, `services`, `ecommerce`, e isso está documentado na ADR.
7. **Dado** um texto de ajuda com um termo do vocabulário, **quando** abro a ajuda, **então** o termo aparece como o tenant o vê.
8. **Dado** a Konebras, **quando** comparo dock, rótulos e textos antes e depois, **então** nada muda.

## Regras de negócio e técnicas
- O vocabulário é declarado em código, por perfil. O tenant não edita termo na primeira versão.
- Os termos valem para rótulos de interface. Nome de permissão, código de módulo e texto de documento fiscal não mudam.
- O dicionário não entra no `tenant-context` público além do necessário para o login; o resto vem depois da autenticação.

## Permissões, eventos e tabelas
- **Permissões:** nenhuma.
- **Endpoints:** o contexto autenticado do tenant ganha `areaLabels` e `vocabulary`.
- **Tabelas:** nenhuma.

## Telas e estados
- **Dock:** área nova e rótulo por perfil. Sem estado novo.
- **Ajuda:** o renderizador da ajuda resolve o vocabulário; `Ajuda/inicio.md` atualizada.

## Plano de testes
- **Unidade:** resolução do rótulo de área e do vocabulário por combinação de perfis (CA 2, 3 e 6); teto de chaves (CA 5).
- **Vitest:** componente de texto e renderizador da ajuda (CA 4 e 7).
- **Regressão:** retrato do dock da Konebras (CA 8).

## Fora do escopo
- Cards do Início por perfil (PERF-04 e PDV-07).
- Tradução livre pelo tenant.

## Pontos em aberto
- PP-25: a lista inicial de termos por perfil precisa da validação do PO.

## Revisão contra o código (17/11/2026, antes de codar)
- **Área como identificador.** O `area` de `/me/permissions` é o próprio texto do `ModuleAreas` ("Fábrica") e o front casa módulo e área por ele. Trocar o texto quebraria o casamento e o retrato da Konebras. Ajuste: o texto continua sendo o identificador (`Area.key` no front) e o rótulo vem à parte, em `areaLabels`.
- **"Contexto autenticado do tenant" não existia como endpoint.** O que o front carrega depois do login é o `/me/permissions` (`NavigationProvider`), com recarga pela `X-Perm-Version`. Ajuste: `areaLabels` e `vocabulary` entram nele, sem endpoint novo; o `tenant-context` público não muda (o login não usa termo).
- **Ordem fixa (CA 6) x Konebras (CA 3).** Se a Fábrica não declarasse termo, "Fábrica + Comércio" herdaria "Retaguarda" do Comércio. Ajuste: a Fábrica declara os termos padrão, então com `factory` nada muda.
- **Lista de termos (PP-25).** O teto foi decidido; a lista não. Termos óbvios como "Itens" → "Produtos" no Estoque colidem com o "produto" do Catálogo (CAT-01 liga produto a item). Ajuste: a lista inicial tem só `area.factory`; o mecanismo (teto, ordem, ajuda, hook) fica pronto para a CAT-01 e as seguintes, e a lista segue em aberto para o PO.
- **Loja vazia.** Nenhum módulo é da área "Loja" até a CAT-01; área sem módulo visível some do dock, então ela não aparece para ninguém por enquanto, e a Konebras nunca a verá (o perfil `factory` não recebe módulo novo sem decisão, ADR 0046).
- **DoD não se aplica em parte:** sem migration e sem permissão nova (nenhuma tabela); sem tela nova.

## Definition of Done
- [x] Critérios de aceite atendidos (CA 1 a 8; a área "Loja" fica oculta até ter módulo).
- [x] Testes de unidade e de integração (Testcontainers) passando; Vitest no front.
- [x] Migration criada; permissões só do `ModuleCatalog`. _Não se aplica: nenhuma tabela nem permissão._
- [x] Telas com estados de carregando, vazio e erro; ajuda escrita ou atualizada em `Ajuda/` no mesmo commit (`Ajuda/inicio.md`).
- [x] Padrões e próximo passo propositivos avaliados; auditoria ligada. _Sem escrita: nada a auditar._
- [x] OpenAPI e cliente Orval regenerados; story marcada no backlog; decisões em ADR (ADR 0046 §6).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
| 17/11/2026 | 1.0 | Implementada: `ModuleAreas.Store`, `BusinessVocabulary` (teto de 30, ordem fixa, `area.factory`), `areaLabels` e `vocabulary` em `/me/permissions`, ajuda com `{{chave|Padrão}}`, `useTerm`/`<Term>`/`useAreaLabel` no front, ADR 0046 §6. | Claude (dev) |
