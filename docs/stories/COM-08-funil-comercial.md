# COM-08 — Funil comercial

| Campo | Valor |
|---|---|
| ID | COM-08 |
| Título | Funil de orçamentos por situação no período, com quantidade e valor, taxa de conversão, motivos de perda e filtro por autor |
| Tamanho | P |
| Status | Draft |
| Requisitos | R-COML-9 (funil: orçamentos por status, taxa de conversão, motivo de perda, **D**) · PRD §5.14 (Comercial: orçamentos em aberto, taxa de conversão, orçamentos vencendo) |
| Dependências | COM-01b (motivos de perda), COM-02 (revisão corrente, expirado), COM-04a (aprovado) |
| Bloqueia | DSH-01 (card Comercial) |
| Ordem de execução | 17 (Marco 2.4 — desejável, último da fase) |

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
6. **Dado** orçamentos enviados que vencem nos próximos 7 dias, **quando** abro o funil, **então** o quadro "Vencendo" lista-os com link para "Nova revisão" ou "Ajustar validade" (R-PRP-5).
7. **Dado** o módulo `quotes` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo ou sem `quotes.view`, 403 `forbidden`; desempenho com 50 mil orçamentos em menos de 1 s.

## Regras de negócio e técnicas
- Leitura no próprio Commercial, sem tabela nova; só a revisão corrente de cada número; dinheiro `numeric(18,2)`; datas pelo `BusinessTime`.

## Permissões, eventos e tabelas
- **Permissões:** `quotes.view`. **Eventos:** nenhum. **Tabelas:** nenhuma nova.

## Telas e estados
- **Comercial → Funil** (`/app/orcamentos/funil`): barras por situação (Recharts, cores dos tokens, com rótulos de texto e tabela equivalente), taxa de conversão, motivos de perda e "Vencendo".
- **Estados:** carregando; vazio ("Sem orçamentos no período." + "Ver últimos 12 meses"); erro.
- **Ajuda:** `Ajuda/comercial-funil.md`; `<Dica>` em "Taxa de conversão".

## Plano de testes
- **Unidade:** taxa de conversão e "—" (CA 2).
- **Integração (Testcontainers):** dataset fixo por situação, motivo e autor (CA 1, 3, 4); vencendo (CA 6); três camadas e desempenho (CA 7).
- **E2E (Playwright):** abrir o funil e o drill-down.

## Fora do escopo
- Card do dashboard e meta de faturamento: DSH-01 (Fase 4).
- Vendedor diferente de quem criou o orçamento: sem campo nesta fase (ver Pontos em aberto 2).

## Pontos em aberto
1. **Fórmula da conversão.** **Recomendação:** aprovados ÷ decididos (aprovados + perdidos + expirados), em quantidade e em valor; os em aberto não entram.
2. **Vendedor.** O protótipo filtra por vendedor e o 03 não tem o campo. **Recomendação:** usar o autor do orçamento no MVP; campo "Vendedor" só se o PO pedir.
3. **P-18:** ver FIN-01, ponto 5.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Tela com estados e gráfico acessível.
- [ ] Ajuda escrita; teste de cobertura passando.
- [ ] Drill-down e "Vencendo" acionáveis.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `QuoteFunnelQuery`; endpoint `/api/v1/quotes/funnel`; tela e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 1 e 2 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 19/10/2026.

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
- **Pré-condições para `Ready`:** Pontos em aberto 1 e 2 (com padrão recomendado) e P-18.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
