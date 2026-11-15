# CAT-02 — Importação de produtos por planilha: modelo para baixar, prévia com erros por linha, criação de item, produto, código de barras, preço e saldo inicial opcional, reexecutável sem duplicar

| Campo | Valor |
|---|---|
| ID | CAT-02 |
| Título | Importação de produtos por planilha: modelo para baixar, prévia com erros por linha, criação de item, produto, código de barras, preço e saldo inicial opcional, reexecutável sem duplicar |
| Tamanho | M |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 10 — Fundação: perfil de negócio, catálogo, pagamentos e spikes |
| Requisitos | Pedido do PO de 15/11/2026 (item 3) · [PRD do PDV](../01-PRD.md) seção 3.4 (passo 2 dos primeiros passos) · ADR 0045 (exportação) |
| Dependências | CAT-01 |
| Bloqueia | PERF-04 (passo "Importar produtos") |
| Ordem de execução | 6 (Fase 10) |

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
- Importação pelo XML da nota do fornecedor (CPD-05).
- Fotos em lote.

## Pontos em aberto
- Nenhum.

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
