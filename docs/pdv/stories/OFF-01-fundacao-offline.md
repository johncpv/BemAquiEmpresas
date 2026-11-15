# OFF-01 — Fundação offline: app `/pdv` com build e service worker próprios, armazenamento local persistente atrás da porta `LocalStore` e descida do catálogo da loja (foto inicial e delta)

| Campo | Valor |
|---|---|
| ID | OFF-01 |
| Título | Fundação offline: app `/pdv` com build e service worker próprios, armazenamento local persistente atrás da porta `LocalStore` e descida do catálogo da loja (foto inicial e delta) |
| Tamanho | G |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 11 — PDV balcão com cupom não fiscal e offline (marco MVP) |
| Requisitos | Pedido do PO de 15/11/2026 (item 5: offline desde o primeiro release) · [Rascunho A](../adr/rascunho-A-offline.md) itens 1 a 5, 7 e 10 · [Fluxos](../04-fluxos-estados.md#8-sincronização-offline) |
| Dependências | PDV-01, CAT-01, PAG-01; SPK-03 concluído (escolha do armazenamento) |
| Bloqueia | PDV-02, OFF-02 |
| Paridade com o mercado | Requisitos mínimos 1 do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | 2 (Fase 11) |

## User story
Como **operador de caixa**, quero **que a tela de venda abra e ache os produtos mesmo sem internet**, para **não parar de vender quando a rede cai**.

## Contexto e referências
- O PWA atual tem um service worker só, com escopo `/fab`, que nunca guarda `/api/*`, com `skipWaiting` e `clientsClaim` ligados (`src/Web/pwa.config.ts`). O bundle já passa de 2 MiB.
- O catálogo desce de um modelo de leitura do próprio `Pos` (`pos.catalog_items`), para respeitar a regra de não ler tabela de outro módulo.

## Critérios de aceite
### App
1. **Dado** o build do front, **quando** termina, **então** existe uma entrada Vite separada para `/pdv`, com manifesto e service worker próprios de escopo `/pdv`; os de `/fab` e a rota `/tv` não mudam (teste que compara os manifestos gerados).
2. **Dado** o app `/pdv` instalado e já aberto uma vez com rede, **quando** abro sem rede, **então** a tela carrega e mostra o caixa, a loja e a faixa "Sem conexão".
3. **Dado** uma versão nova publicada, **quando** há sessão de caixa aberta, **então** o app **não** troca de versão; avisa "Atualização disponível. Será aplicada na troca de turno." e aplica quando não houver sessão aberta.
4. **Dado** um navegador sem suporte (fora de Chrome e Edge no Windows e de Chrome no Android), **quando** abro `/pdv`, **então** vejo "Este navegador não é suportado no caixa." com a lista dos suportados.

### Armazenamento local
5. **Dado** o primeiro uso, **quando** o app inicia, **então** pede armazenamento persistente (`navigator.storage.persist()`); se for negado, mostra o aviso "O navegador pode apagar dados deste caixa. Instale o aplicativo." e registra o fato na sincronização.
6. **Dado** duas abas do `/pdv` no mesmo aparelho, **quando** a segunda abre, **então** só a primeira escreve (Web Locks) e a segunda mostra "O caixa já está aberto em outra janela.".
7. **Dado** a porta `LocalStore`, **quando** o código do PDV lê ou grava dado local, **então** passa por ela; nenhuma tela usa IndexedDB direto (regra de lint).
8. **Dado** um valor em dinheiro no aparelho, **então** ele é guardado e calculado em centavos inteiros; os ids são Guid v7 gerados no aparelho.

### Descida do catálogo
9. **Dado** um evento `ProductChanged`, `PriceChanged` ou `PaymentMethodChanged`, **quando** o consumidor roda, **então** `pos.catalog_items` é atualizado com um `change_seq` novo; o consumidor é idempotente.
10. **Dado** um aparelho recém-registrado, **quando** sincroniza pela primeira vez, **então** baixa a foto das coleções da **sua loja**: itens, códigos de barras, preços com a versão da tabela, formas de pagamento, operadores da loja e impressoras.
11. **Dado** um aparelho com foto, **quando** sincroniza de novo, **então** baixa só o delta desde o seu cursor; produto desativado chega como removido.
12. **[Segurança]** **Dado** o aparelho da loja A, **quando** pede o catálogo, **então** nunca recebe item, preço ou operador só da loja B; custo e margem nunca descem para o aparelho.
13. **Dado** a faixa de estado, **quando** há fila de saída, **então** ela mostra "Sem conexão: N vendas a enviar" com a idade da mais antiga; com rede e fila vazia, mostra a hora da última sincronização.

## Regras de negócio e técnicas
- Local-first: a tela nunca espera a rede, nem quando há rede.
- O armazenamento (IndexedDB com Dexie ou SQLite-WASM em OPFS) é o escolhido no SPK-03; a porta `LocalStore` isola a escolha.
- Dado pessoal no aparelho (CPF informado na venda) fica cifrado com chave derivada da chave do aparelho.
- A lista de coleções é fechada e versionada (`schema_version`).

## Permissões, eventos e tabelas
- **Permissões:** nenhuma nova; a descida usa o esquema `Device`.
- **Endpoints:** `GET /api/v1/pos/sync/snapshot`, `GET /api/v1/pos/sync/changes?cursor=`.
- **Eventos consumidos:** `ProductChanged`, `PriceChanged`, `PaymentMethodChanged`.
- **Tabelas:** `pos.catalog_items`.

## Telas e estados
- **`/pdv`, tela inicial:** carregando ("Preparando o caixa: baixando produtos", com progresso na primeira vez); vazio ("Nenhum produto disponível para esta loja." com o que fazer); erro de primeira carga sem rede ("A primeira abertura precisa de internet.").
- **Faixa de estado** fixa no topo.
- **Ajuda:** `Ajuda/pdv-sem-conexao.md` (nova), aberta pelo botão de ajuda do `/pdv` (padrão do `FloorHelp`).

## Plano de testes
- **Vitest:** `LocalStore` (leitura, gravação, bloqueio de segunda aba); dinheiro em centavos; faixa de estado.
- **Integração (Testcontainers):** modelo de leitura idempotente (CA 9); foto e delta (CA 10 e 11); escopo por loja e ausência de custo (CA 12).
- **Build:** manifestos e escopos dos service workers (CA 1).
- **E2E no fechamento do marco:** abrir sem rede.

## Fora do escopo
- Subida de eventos e conflitos (OFF-02).
- PIN e abertura de turno sem rede (OFF-03).
- Casca Android (PAG-04).

## Pontos em aberto
- PP-09: plataformas suportadas.
- Se o SPK-03 mostrar busca lenta com 50 mil itens no IndexedDB, entra o SQLite-WASM e a story é reestimada.

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
