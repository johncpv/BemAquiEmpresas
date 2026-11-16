# PAG-01 — Formas de pagamento e adquirentes no Financeiro: tipo, conta de destino, adquirente como terceiro, tabela de taxas por bandeira, forma e parcelas, prazo D+N e histórico

| Campo | Valor |
|---|---|
| ID | PAG-01 |
| Título | Formas de pagamento e adquirentes no Financeiro: tipo, conta de destino, adquirente como terceiro, tabela de taxas por bandeira, forma e parcelas, prazo D+N e histórico |
| Tamanho | M |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 10 — Fundação: perfil de negócio, catálogo, pagamentos, núcleo de vendas e spikes |
| Requisitos | Pedido do PO de 15/11/2026 (itens 6 e 11) · [Rascunho C](../adr/rascunho-C-pagamentos.md) item 8 · [Modelo de dados](../03-modelo-dados.md#6-finance-extensões) · ADR 0042 |
| Dependências | — |
| Bloqueia | PDV-03, CX-03, PERF-04 |
| Paridade com o mercado | Requisitos mínimos 28 (base) do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | 6 (Fase 10) |

## User story
Como **financeiro**, quero **cadastrar as formas de pagamento da loja com a taxa e o prazo de cada maquininha**, para **o fluxo de caixa mostrar quando e quanto eu recebo de verdade**.

## Contexto e referências
- O Financeiro não tem recebível de cartão; `SettlementSource` é só `Manual` ou `Bank`.
- O `NfePaymentMethod` do `IFiscalEmitter` já tem cartões e Pix e serve de base para o código fiscal da forma.

## Critérios de aceite
1. **Dado** `finance.create`, **quando** crio uma forma de pagamento, **então** informo nome, tipo (Dinheiro, Pix, Débito, Crédito à vista, Crédito parcelado, Vale, Vale-troca, Fiado), conta de destino e, nos tipos de cartão, a adquirente (um terceiro).
2. **Dado** uma forma do tipo cartão, **quando** cadastro as taxas, **então** informo por bandeira e por número de parcelas a taxa em % e o prazo em dias (D+N), com a data de início da vigência.
3. **Dado** uma taxa em vigor, **quando** altero, **então** a anterior é encerrada e a nova entra com vigência a partir da data informada; nenhuma taxa passada é editada; o histórico mostra quem mudou e quando.
4. **Dado** as formas do seed, **quando** abro a tela, **então** taxa e prazo zerados aparecem com o chip "A confirmar" e a ação "Marcar como conferido", auditada.
5. **Dado** `IPaymentMethods` em `Finance.Contracts`, **quando** outro módulo consulta, **então** recebe as formas ativas com tipo, se dá troco (só Dinheiro), a adquirente e a taxa e o prazo vigentes numa data, por bandeira e parcelas.
6. **Dado** uma forma usada em alguma venda, **quando** tento excluir, **então** ela só pode ser desativada.
7. **Dado** uma forma do tipo Dinheiro, **quando** salvo, **então** a conta de destino tem de ser do tipo Caixa; nos demais tipos, o sistema sugere a conta bancária principal.
8. **Dado** a adquirente, **quando** crio pela tela da forma, **então** o cadastro rápido cria o terceiro com o papel "Adquirente", sem sair da tela.

## Regras de negócio e técnicas
- Taxa `decimal(9,4)`; valores `decimal(18,2)`.
- A forma guarda o código fiscal do meio de pagamento, usado pela NFC-e na Fase 13.
- Nenhum campo de forma ou de taxa guarda dado de cartão de cliente (regra 14 do PRD).
- A forma de pagamento é do Financeiro; o PDV só lê pelo contrato e recebe a lista na sincronização.

## Permissões, eventos e tabelas
- **Permissões:** as do `finance` que já existem (`view`, `create`, `edit`). Nenhuma nova.
- **Endpoints:** `GET|POST /api/v1/finance/payment-methods`, `GET|PUT /api/v1/finance/payment-methods/{id}`, `GET|POST .../fees`, `POST .../confirm`.
- **Eventos:** `PaymentMethodChanged` (atualiza o que desce para os caixas).
- **Tabelas:** `finance.payment_methods`, `finance.acquirer_fees`.
- **Auditoria:** forma e taxa são `IAuditable`; "Marcar como conferido" auditado.

## Telas e estados
- **Financeiro → Formas de pagamento** (`/app/financeiro/formas-de-pagamento`): lista com chip de "A confirmar"; vazio com "Nova forma de pagamento".
- **Forma de pagamento:** formulário com a seção "Taxas e prazos" (tabela por bandeira e parcelas).
- Carregando e erro em ambas.
- **Ajuda:** `Ajuda/financeiro-formas-de-pagamento.md` (nova). `<Dica>` em "D+N" ("Em quantos dias o dinheiro cai na conta depois da venda.").

## Plano de testes
- **Unidade:** taxa vigente numa data; vigências que não se sobrepõem.
- **Integração (Testcontainers):** histórico de taxa (CA 3); contrato (CA 5); desativar em vez de excluir (CA 6); conta Caixa para Dinheiro (CA 7).
- **Vitest:** tabela de taxas.

## Fora do escopo
- Gerar o recebível de cartão (CX-03).
- Extrato da adquirente (PAG-05).
- Pix dinâmico (PAG-02).

## Pontos em aberto
- QC-03: em algumas UFs o grupo de cartão da NFC-e pede o CNPJ da credenciadora; o cadastro da adquirente como terceiro já guarda o CNPJ.

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
