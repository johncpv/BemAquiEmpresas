# PDV-07 — Retaguarda de vendas: lista com filtros e exportação, detalhe da venda com a trilha completa e os cards "Vendas de hoje", "Caixas abertos" e "Ticket médio" no Início

| Campo | Valor |
|---|---|
| ID | PDV-07 |
| Título | Retaguarda de vendas: lista com filtros e exportação, detalhe da venda com a trilha completa e os cards "Vendas de hoje", "Caixas abertos" e "Ticket médio" no Início |
| Tamanho | M |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 11 — PDV balcão com cupom não fiscal e offline (marco MVP) |
| Requisitos | Objetivo central do produto (visibilidade financeira) · [PRD do PDV](../01-PRD.md) seção 4.1 (Dashboard) · ADR 0028 (composição por contratos) · ADR 0045 (exportação) |
| Dependências | CX-03, PDV-05, OFF-02 |
| Bloqueia | O fechamento do marco MVP |
| Paridade com o mercado | Requisitos mínimos 8 (parte) do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | 15 (Fase 11; fecha o marco MVP) |

## User story
Como **dono**, quero **ver as vendas do dia, os caixas abertos e o ticket médio assim que entro**, e **abrir qualquer venda para saber tudo o que aconteceu com ela**, para **acompanhar a loja sem estar nela**.

## Contexto e referências
- O Início compõe cards pelo módulo `Dashboard`, que lê os outros módulos só por contrato (ADR 0028).
- Exportação segue o padrão `GET …/export` da ADR 0045, com teto de linhas.

## Critérios de aceite
### Lista e detalhe
1. **Dado** `sales.view`, **quando** abro "Vendas", **então** vejo a lista com número, data e hora, loja, caixa, operador, total, formas e os chips de estado (concluída, cancelada, devolvida; "Estoque: falta de saldo"; "Com divergência"; "Feita sem internet").
2. **Dado** os filtros, **quando** filtro por período, loja, caixa, operador, forma de pagamento e estado, **então** a lista e os totais do rodapé respeitam o filtro, e o filtro fica na URL.
3. **Dado** "Exportar", **quando** aciono, **então** recebo o arquivo no padrão da ADR 0045, com o mesmo filtro; acima do teto, a mensagem pede para reduzir o período.
4. **Dado** uma venda, **quando** abro o detalhe, **então** vejo itens (com preço da tabela e praticado), pagamentos, e a **trilha**: eventos do aparelho em ordem, aprovações de supervisor, sincronização (quando foi feita e quando chegou), estoque (movimento ou divergência), financeiro (sessão de caixa e título) e reimpressões.
5. **[Segurança]** **Dado** quem não tem `sales.view_cost`, **quando** lista, abre ou exporta vendas, **então** custo e margem não vêm (ausentes, não zerados).
6. **[Segurança]** **Dado** o CPF do cliente na venda, **quando** aparece em lista ou exportação, **então** vem mascarado; o detalhe mostra inteiro só a quem tem `sales.view`, com a leitura auditada.
7. **Dado** uma venda de outra loja, **quando** o usuário tem acesso ao módulo, **então** vê: não há restrição por loja no MVP. Isso enfraquece o multiloja e virou a pendência PP-42.

### Início
8. **Dado** um tenant com o módulo `sales` e o usuário com `sales.view`, **quando** abro o Início, **então** vejo os cards: "Vendas de hoje" (valor e quantidade, com a comparação com o mesmo dia da semana anterior), "Caixas abertos" (quantos, de quais lojas, há quanto tempo) e "Ticket médio" (hoje e nos últimos 7 dias).
9. **Dado** um card, **quando** clico, **então** vou para a lista já filtrada (vendas de hoje; sessões abertas).
10. **Dado** um caixa com vendas na fila do aparelho, **quando** vejo "Vendas de hoje", **então** o card avisa "Pode haver vendas ainda não enviadas pelos caixas." com link para as Pendências do PDV.
11. **Dado** um tenant sem o perfil de comércio, **quando** abre o Início, **então** nenhum destes cards aparece (a Konebras não muda).

### Marco
12. **Dado** o fechamento do marco MVP, **quando** concluo esta story, **então** existem os E2E dos fluxos principais (abrir caixa → vender em dinheiro e cartão → imprimir → fechar com conferência cega → ver no financeiro; e um turno sem rede com sincronização) e a rodada completa do CI está verde.

## Regras de negócio e técnicas
- Os cards leem `ISalesRevenue` e um contrato de sessões de caixa; o `Dashboard` não lê tabela do `Sales` nem do `Pos`.
- "Hoje" é o dia em `America/Sao_Paulo`.
- A lista é paginada; os totais do rodapé são do filtro, não da página.

## Permissões, eventos e tabelas
- **Permissões:** `sales.view`, `sales.view_cost`.
- **Endpoints:** `GET /api/v1/sales/sales`, `GET .../{id}`, `GET /api/v1/sales/sales/export`; cards pelo endpoint do `Dashboard`.
- **Tabelas:** nenhuma nova; índices por data e loja em `sales.sales`.

## Telas e estados
- **Loja → Vendas** (`/app/loja/vendas`, largura `full`): lista. Vazio: "Nenhuma venda no período." com "Abrir o PDV". Carregando e erro.
- **Venda** (detalhe, com abas Itens, Pagamentos e Trilha).
- **Início:** três cards. Vazio do card: "Nenhuma venda hoje ainda.".
- **Ajuda:** `Ajuda/loja-vendas.md`, `Ajuda/loja-venda.md` e `Ajuda/loja-venda-trilha.md` (novas); `Ajuda/inicio.md` atualizada.

## Plano de testes
- **Integração (Testcontainers):** filtros e totais (CA 2); custo ausente sem `view_cost` (CA 5); CPF mascarado (CA 6); cards com e sem o perfil (CA 8 e 11).
- **Vitest:** lista, chips e trilha.
- **E2E (fechamento do marco):** CA 12.

## Fora do escopo
- Relatórios por período, canal e unidade e o painel do dono no celular (PDV-09).
- Margem por venda (PDV-10).
- Restrição de acesso por loja.

## Pontos em aberto
- **PP-42:** usuário que só deve ver a própria loja. O modelo de permissões de hoje (`modulo.acao`) não tem escopo por loja; se o piloto pedir, exige ADR.

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
