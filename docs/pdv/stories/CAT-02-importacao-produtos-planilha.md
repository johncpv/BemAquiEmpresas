# CAT-02 — Importação de produtos por planilha: modelo para baixar, prévia com erros por linha, criação de item, produto, código de barras, preço e saldo inicial opcional, reexecutável sem duplicar

| Campo | Valor |
|---|---|
| ID | CAT-02 |
| Título | Importação de produtos por planilha: modelo para baixar, prévia com erros por linha, criação de item, produto, código de barras, preço e saldo inicial opcional, reexecutável sem duplicar |
| Tamanho | M |
| Status | Done (18/11/2026; branch `feat/cat-02-importacao-planilha`) |
| Fase | 10 — Fundação: perfil de negócio, catálogo, pagamentos, núcleo de vendas e spikes |
| Requisitos | Pedido do PO de 15/11/2026 (item 3) · [PRD do PDV](../01-PRD.md) seção 3.4 (passo 2 dos primeiros passos) · ADR 0045 (exportação) |
| Dependências | CAT-01 |
| Bloqueia | PERF-04 (passo "Importar produtos") |
| Ordem de execução | 5 (Fase 10) |

## User story
Como **dono de um comércio**, quero **subir a planilha dos meus produtos**, para **não cadastrar centenas de itens um a um**.

## Contexto e referências
- Importar por planilha é o passo 2 do checklist de primeiros passos (PERF-04): sem ele, a meta de primeira venda em 30 minutos não fecha.
- O saldo inicial é movimento de estoque: passa pelo `IStockMovements`, com confirmação.

## Critérios de aceite
1. **Dado** `catalog.create`, **quando** abro "Importar planilha", **então** posso baixar o modelo `.xlsx` com as colunas: código interno, nome, unidade, NCM, código de barras, preço de venda, custo, saldo inicial, categoria; a primeira aba explica cada coluna.
2. **Dado** uma planilha enviada, **quando** o sistema lê, **então** vejo a prévia com o total de linhas, quantas criam, quantas atualizam e quantas têm erro, e a lista de erros por linha com o motivo em português ("Linha 14: código de barras inválido.").
3. **Dado** a prévia com erros, **quando** confirmo, **então** só as linhas válidas entram; posso baixar as linhas com erro numa planilha para corrigir.
4. **Dado** uma linha cujo código interno ou código de barras já existe, **quando** importo de novo, **então** o produto é atualizado, não duplicado.
5. **Dado** a coluna de preço preenchida, **quando** quem importa não tem `catalog.edit_price`, **então** a prévia avisa que os preços serão ignorados e a importação segue sem eles.
6. **Dado** a coluna de saldo inicial preenchida, **quando** confirmo, **então** o sistema pede a confirmação com o verbo "Lançar saldo inicial", mostra o local de destino (padrão: "Loja") e só então gera os movimentos de entrada; reimportar a mesma planilha não lança o saldo de novo.
7. **Dado** uma planilha com mais de 20 mil linhas, **quando** envio, **então** recebo "A planilha passa do limite de 20.000 linhas. Divida em arquivos menores.".
8. **Dado** a importação concluída, **quando** termina, **então** vejo o relatório final (criados, atualizados, ignorados) e o próximo passo "Conferir preços" ou "Voltar aos primeiros passos".

## Regras de negócio e técnicas
- A leitura da planilha usa o mesmo cuidado de upload dos anexos (allowlist, tamanho).
- Importação grande roda em job (Hangfire, com `tenantId`), com progresso.
- Chave de reexecução: código interno; na falta dele, o primeiro código de barras.

## Permissões, eventos e tabelas
- **Permissões:** `catalog.create`; preço exige `catalog.edit_price`; saldo inicial exige a permissão de ajuste do Estoque que já existe.
- **Endpoints:** `GET /api/v1/catalog/import/template`, `POST /api/v1/catalog/import/preview`, `POST /api/v1/catalog/import/{id}/confirm`, `GET /api/v1/catalog/import/{id}`.
- **Tabelas:** `catalog_products.import_runs` (resumo e erros).
- **Auditoria:** importação confirmada, com os totais.

## Telas e estados
- **Importar produtos** (`/app/catalogo/produtos/importar`): enviar → prévia → confirmar → relatório. Carregando com progresso; erro de arquivo com o que fazer.
- **Ajuda:** `Ajuda/catalogo-importar.md` (nova).

## Plano de testes
- **Unidade:** validação por linha.
- **Integração (Testcontainers):** importar, reimportar sem duplicar (CA 4), saldo inicial uma vez só (CA 6), preço ignorado sem permissão (CA 5).
- **Vitest:** prévia e lista de erros.

## Fora do escopo
- Importação pelo XML da nota do fornecedor (CPD-01).
- Variações pela planilha (a primeira versão importa produto simples; a grade é criada na tela).
- Fotos em lote.

## Pontos em aberto
- Nenhum.

## Implementação (18/11/2026)
Decisões em [ADR 0047 §7](../../adr/0047-catalogo-de-produtos.md).
- **Tela:** `/app/catalogo/produtos/importar` (enviar → prévia → confirmar → relatório; o id da importação fica em `?id=`, para o link do sino e o recarregar). "Importar planilha" na lista de produtos e no vazio dela.
- **CA 1:** `GET /api/v1/catalog/import/template`: a primeira aba, "Como preencher", explica as 9 colunas com exemplo; a aba "Produtos" tem o cabeçalho, com código, NCM e código de barras em formato texto.
- **CA 2 e 7:** `POST /api/v1/catalog/import/preview` (multipart): só `.xlsx` (allowlist dos anexos), até 10 MB e 150 MB descompactados; colunas pelo cabeçalho. A prévia grava `catalog_products.import_runs` e devolve o total, quantas criam, quantas atualizam e quantas têm erro, com as mensagens "Linha N: …" (as primeiras 200), as categorias novas e os avisos. Mais de 20 mil linhas: 422 com a mensagem do CA.
- **CA 3:** `POST /api/v1/catalog/import/{id}/confirm` grava só as válidas, cada linha na sua transação (falha ao gravar vira linha com erro e as outras seguem); `GET /api/v1/catalog/import/{id}/errors` baixa as linhas com erro (número da linha e motivo, mais as colunas do modelo), que voltam pela importação depois de corrigidas. Confirma uma vez só (409 na segunda).
- **CA 4:** chave = código interno (código do item no Estoque, pelo contrato novo `IItems.FindByCodesAsync`); sem ele, o código de barras. Linha sem nenhum dos dois é erro. Atualiza nome, categoria, código de barras novo e preço; unidade e NCM do produto existente ficam com o Estoque.
- **CA 5:** sem `catalog.edit_price`, a prévia avisa que os preços serão ignorados (a coluna nem é conferida) e a importação segue sem eles; a permissão é conferida de novo ao confirmar.
- **CA 6:** com saldo, a tela pede "Lançar saldo inicial" com o local (sugerido: o de nome "Loja", senão o padrão do Estoque), ou "Importar sem o saldo". Exige `inventory.adjust` (403 sem ela) e local ativo (422). Movimento `OpeningBalance` com origem `CatalogImport` e o produto como `source_id`: reimportar devolve o movimento já lançado e o relatório mostra "já tinha o saldo". O motor exige custo maior que zero na entrada, então a linha com saldo pede o custo.
- **CA 8:** relatório com criados, atualizados e ignorados, o saldo lançado, "Conferir preços" (lista só com os sem preço), "Voltar aos primeiros passos" (Início) e "Importar outra planilha".
- **Job:** acima de 500 linhas válidas (`Catalog:ImportSyncMaxRows`), `ProductImportJob` (Hangfire, `tenantId` + ids), blocos de 200 com o progresso gravado e retomada; a tela mostra a barra de progresso e o sino avisa quem confirmou.
- **Auditoria:** `Imported` ao confirmar, com as linhas e os registros previstos; produtos, códigos, preços e categorias seguem auditados pelo interceptor. A importação não é `IAuditable` (as linhas não vão para a auditoria).
- **Konebras:** o Catálogo segue oculto e não liberado para ela; nenhuma tela ou rota nova aparece.
- **Pendências:** nenhuma pendência sem decisão foi implementada. A PP-22 (parâmetros da loja) não se aplica; o local "Loja" ainda não existe como cadastro (nasce com as lojas do PDV), por isso a sugestão cai no local padrão.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade e de integração (Testcontainers) passando; Vitest no front.
- [x] Migration criada; permissões só do `ModuleCatalog`.
- [x] Telas com estados de carregando, vazio e erro; ajuda escrita ou atualizada em `Ajuda/` no mesmo commit.
- [x] Padrões e próximo passo propositivos avaliados; auditoria ligada.
- [x] OpenAPI e cliente Orval regenerados; story marcada no backlog; decisões em ADR.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
| 18/11/2026 | 1.0 | Implementada (seção "Implementação"): importação por planilha com prévia, linhas com erro para baixar, reimportação sem duplicar, saldo inicial uma vez só e job com progresso; `import_runs`, `IItems.FindByCodesAsync`, `StockSources.CatalogImport`; ajuda `catalogo-importar`; ADR 0047 §7. | Claude (dev) |
