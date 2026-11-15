# PERF-03 — Área "Loja" no dock, rótulo de área por perfil ("Retaguarda" no lugar de "Fábrica") e vocabulário por perfil servido no `tenant-context`

| Campo | Valor |
|---|---|
| ID | PERF-03 |
| Título | Área "Loja" no dock, rótulo de área por perfil ("Retaguarda" no lugar de "Fábrica") e vocabulário por perfil servido no `tenant-context` |
| Tamanho | M |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 10 — Fundação: perfil de negócio, catálogo, pagamentos e spikes |
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
