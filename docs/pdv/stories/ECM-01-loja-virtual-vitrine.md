# ECM-01 — Loja virtual: host próprio do tenant, identidade do comprador separada do backoffice, vitrine com categorias e variações, busca e páginas públicas com cache

| Campo | Valor |
|---|---|
| ID | ECM-01 |
| Título | Loja virtual: host próprio do tenant, identidade do comprador separada do backoffice, vitrine com categorias e variações, busca e páginas públicas com cache |
| Tamanho | G |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 14 — E-commerce (trilha seguinte à do balcão) |
| Requisitos | Pedido do PO de 15/11/2026 (item 1: E-commerce) · [PRD do PDV](../01-PRD.md) seção 4.2 (Storefront) · [Modelo de dados](../03-modelo-dados.md#11-storefront--schema-storefront-fase-14) · ADR 0040 (identidade do portal) · ADR 0009 (rotas anônimas) |
| Dependências | CAT-01, PERF-01; adendo à ADR 0009 aprovado |
| Bloqueia | ECM-02, DLV-02 |
| Ordem de execução | 1 (Fase 14, trilha do e-commerce) |

## User story
Como **dono de uma loja**, quero **uma loja virtual no meu domínio, com os produtos que eu já cadastrei**, para **vender online sem manter dois cadastros**.

## Contexto e referências
- O PO decidiu em 15/11/2026 que o balcão vem primeiro; esta é a trilha seguinte. Se o cliente já vende numa plataforma, a entrada da trilha pode ser a ECM-07 reduzida, e não esta story (PP-16).
- O portal do cliente (ADR 0040) já usa host próprio em `tenant_domains` e identidade separada do backoffice. A loja virtual segue o mesmo padrão, com `kind = Store`.
- A lista de rotas anônimas do `CLAUDE.md` é fechada: as rotas públicas da loja entram nela por adendo à ADR 0009 e no teste de arquitetura.

## Critérios de aceite
1. **Dado** o projeto `src/Modules/Storefront/`, **quando** os testes de arquitetura rodam, **então** ele tem as cinco camadas, schema `storefront` e só referencia `*.Contracts`; o módulo `storefront` está no `ModuleCatalog`, na área "Loja".
2. **Dado** um domínio cadastrado com `kind = Store`, **quando** alguém o acessa, **então** vê a loja daquele tenant; o host do backoffice não serve a loja e o host da loja não serve o backoffice.
3. **[Segurança]** **Dado** as rotas públicas da loja, **quando** os testes de arquitetura rodam, **então** todas estão na lista de rotas anônimas permitidas, só aceitam leitura e têm limite de requisições por IP; qualquer rota pública fora da lista falha o teste.
4. **[Segurança]** **Dado** um comprador que cria conta na loja, **quando** entra, **então** a identidade dele é separada da dos usuários do backoffice (outra tabela, outro cookie, outra audiência do token) e não dá acesso a nenhuma rota do backoffice.
5. **Dado** produtos marcados "vendável online", **quando** abro a vitrine, **então** vejo-os por categoria, com foto, nome, preço da tabela do canal `Online` e disponibilidade; produto sem saldo aparece como "Indisponível" ou some, por configuração.
6. **Dado** um produto com variações, **quando** abro a página dele, **então** escolho cor e tamanho, e o preço e a disponibilidade são os da variação escolhida.
7. **Dado** a busca, **quando** digito, **então** recebo produtos por nome e categoria.
8. **Dado** as páginas públicas, **quando** são servidas, **então** têm cache com invalidação por `ProductChanged` e `PriceChanged`, título e descrição por página e mapa do site.
9. **[Segurança]** **Dado** a vitrine, **quando** confiro as respostas, **então** nunca trazem custo, margem, saldo exato nem dado de outro tenant.
10. **Dado** `storefront.edit`, **quando** configuro a loja, **então** defino nome, logo, cores dentro dos tokens, políticas (troca, entrega, privacidade) e coleções em destaque.

## Regras de negócio e técnicas
- Preço e foto vêm do Catálogo (`ICatalogLookup`, `IPriceLists`); disponibilidade, do Estoque (`IStockBalances`). A loja não guarda cópia de preço.
- A disponibilidade mostrada é saldo menos reservas.
- Nenhuma cor literal: o tema da loja usa os tokens.

## Permissões, eventos e tabelas
- **Permissões:** `storefront.view`, `create`, `edit`, `approve`, `delete` (novas, por seed).
- **Endpoints:** públicos sob `/api/v1/store/…` (catálogo, produto, busca); de administração sob `/api/v1/storefront/…`.
- **Tabelas:** `storefront.store_settings`, `collections`, `collection_products`, `shoppers`; `tenant_domains.kind = Store` no catálogo.

## Telas e estados
- **Loja pública:** início, categoria, produto, busca. Vazio: "Nenhum produto disponível no momento.". Carregando e erro em todas.
- **Loja → Loja virtual** (`/app/loja/loja-virtual`): configuração e coleções. Vazio: "Sua loja virtual ainda não tem domínio." com "Configurar domínio".
- **Ajuda:** `Ajuda/loja-loja-virtual.md` (nova).

## Plano de testes
- **Arquitetura:** rotas anônimas na lista (CA 3).
- **Integração (Testcontainers):** isolamento por host (CA 2); identidade separada (CA 4); nada de custo nem de outro tenant (CA 9); cache invalidado por evento (CA 8).
- **Vitest:** página de produto com variações (CA 6).

## Fora do escopo
- Carrinho e checkout (ECM-02). Pagamento (ECM-03).
- Editor visual de tema. Blog.

## Pontos em aberto
- Emissão do certificado TLS do domínio do cliente: reusar o que o portal faz.

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
