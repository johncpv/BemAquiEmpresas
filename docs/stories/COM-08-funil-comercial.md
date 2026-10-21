# COM-08 — Funil comercial

| Campo | Valor |
|---|---|
| ID | COM-08 |
| Título | Funil de orçamentos por situação no período, com quantidade e valor, taxa de conversão, motivos de perda e filtro por autor |
| Tamanho | P |
| Status | Done |
| Requisitos | R-COML-9 (funil: orçamentos por status, taxa de conversão, motivo de perda, **D**) · PRD §5.14 (Comercial: orçamentos em aberto, taxa de conversão, orçamentos vencendo) |
| Dependências | COM-01b (motivos de perda), COM-02 (revisão corrente, expirado, prorrogação), COM-04a (aprovado), FND-09e (`Chart`, tokens de gráfico) |
| Bloqueia | DSH-01 (card Comercial) |
| Ordem de execução | 20 (Marco 2.4 "Controles e D"; desejável, último da fase) |

## User story
Como **proprietário**, quero **ver quantos orçamentos estão em cada etapa, quantos viram contrato e por que perdemos os outros**, para **saber onde o comercial precisa melhorar**.

## Contexto e referências
- PRD R-COML-9 (D) e §5.14 (card Comercial do dashboard, Fase 4).
- `04-fluxos-estados` §1 "Orçamento" (situações); COM-01b (lista fixa de motivos de perda); COM-02 (só a revisão corrente conta).
- Protótipo: painel "Comercial — orçamentos dos últimos 90 dias".

## Critérios de aceite
1. **Dado** `quotes.view` e o período (padrão: últimos 90 dias, pela data de criação da revisão 0), **quando** abro "Funil", **então** vejo, por situação da revisão corrente (Rascunho, Enviado, Aprovado, Perdido, Expirado), a quantidade e o valor (preço total), com os números do dataset fixo de teste.
2. **Dado** o período, **quando** a taxa de conversão é calculada, **então** é aprovados ÷ (aprovados + perdidos + expirados), em quantidade e em valor, com uma casa decimal; sem orçamento decidido, mostra "—" (ver Pontos em aberto 1).
3. **Dado** os perdidos do período, **quando** abro "Motivos de perda", **então** vejo cada motivo da lista fixa com quantidade, valor e %, do maior para o menor, e o detalhe dos "Outro".
4. **Dado** o filtro por autor (quem criou o orçamento), **quando** escolho, **então** todos os números são do autor; o filtro fica na URL.
5. **Dado** um número do funil, **quando** clico, **então** a lista de orçamentos abre filtrada por aquela situação e período (drill-down).
6. **Dado** orçamentos enviados que vencem nos próximos 7 dias, **quando** abro o funil, **então** o quadro "Vencendo" lista-os com link para "Prorrogar validade" (COM-02, D16) ou "Nova revisão" (R-PRP-5).
7. **Dado** o módulo `quotes` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo ou sem `quotes.view`, 403 `forbidden`; desempenho com 50 mil orçamentos em menos de 1 s.
8. **[Segurança]** **Dado** os filtros e a ordenação do funil, **quando** a consulta roda, **então** o SQL só usa parâmetros, a ordenação aceita só as colunas da allowlist (422 fora dela) e os valores são de preço (nunca de custo ou margem).

## Regras de negócio e técnicas
- Leitura no próprio Commercial, sem tabela nova; só a revisão corrente de cada número; dinheiro `numeric(18,2)`; datas pelo `BusinessTime`.

## Permissões, eventos e tabelas
- **Permissões:** `quotes.view`. **Eventos:** nenhum. **Tabelas:** nenhuma nova.

## Telas e estados
- **Comercial → Funil** (`/app/orcamentos/funil`; entrada no `modules.ts` na área Comercial): barras por situação (`Chart` da FND-09e, tokens `--chart-*`, com rótulos de texto e tabela equivalente acessível por teclado), taxa de conversão, motivos de perda e "Vencendo".
- **400 px:** as barras ocupam a largura; a tabela equivalente mostra situação, quantidade e valor.
- **Estados:** carregando; vazio ("Sem orçamentos no período." + "Ver últimos 12 meses"); erro.
- **Ajuda:** `Ajuda/comercial-funil.md`; `<Dica>` em "Taxa de conversão".

## Plano de testes
- **Unidade:** taxa de conversão e "—" (CA 2).
- **Integração (Testcontainers):** dataset fixo por situação, motivo e autor (CA 1, 3, 4); vencendo (CA 6); três camadas e desempenho (CA 7); allowlist de ordenação (CA 8).
- **E2E (Playwright):** abrir o funil e o drill-down; tela a 400 px.

## Fora do escopo
- Card do dashboard e meta de faturamento: DSH-01 (Fase 4).
- Vendedor diferente de quem criou o orçamento: sem campo nesta fase.

## Pontos em aberto
Decididos pelo PO em 20/10/2026 (seção 1.1, item 29):
1. Conversão = aprovados ÷ decididos (aprovados + perdidos + expirados), em quantidade e em valor; os em aberto não entram.
2. Autor do orçamento no lugar de "vendedor" no MVP; campo "Vendedor" só se o PO pedir.
3. P-18 decidida (ADR 0021).

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers) e E2E passando.
- [x] Permissões só do `ModuleCatalog`.
- [x] Tela com estados e gráfico acessível.
- [x] Ajuda escrita; teste de cobertura passando.
- [x] Drill-down e "Vencendo" acionáveis.
- [x] OpenAPI e cliente Orval regenerados.
- [x] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `QuoteFunnelQuery`; endpoint `/api/v1/quotes/funnel`; tela e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 1 e 2 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 20/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 2).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação (19/10/2026):** só a revisão corrente conta (sem inflar o funil com revisões); fórmula de conversão explícita; "vendedor" do protótipo trocado pelo autor, com ponto em aberto.
- **Correções da revalidação (20/10/2026):** "Vencendo" leva a "Prorrogar validade" (D16, CA 6); SQL com allowlist e só valores de preço (CA 8, D7); gráfico com o `Chart` e os tokens da FND-09e; navegação e 400 px; ordem 20.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 20/10/2026 | 0.3 | Decisões do PO (D16, seção 1.1) e ajustes da revisão pré-Fase 2 (§2.2 baixos, §2.3) | River (SM) |
| 20/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 21/10/2026 | 1.0 | Implementada. Desvios: as barras do funil são barras horizontais próprias (cores `--chart-*`, rótulo de texto e tabela equivalente), porque o `Chart` da FND-09e só desenha linhas; a allowlist de ordenação vale para o quadro "Vencendo" (validade, preço, número), o único com ordenação; "Vencendo" e os detalhes de "Outro" mostram até 50 linhas; o filtro de autor lista quem já criou orçamento (revisão 0); "Prorrogar validade" e "Nova revisão" abrem o diálogo direto no orçamento (`?acao=`); a lista de orçamentos ganhou `from`, `to` e `authorId` para o drill-down; período máximo de 3 anos | @dev |
