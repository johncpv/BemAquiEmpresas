# FIN-10b — Autoconfirmação configurável da conciliação: quem importou o extrato pode confirmar a conciliação dele, conforme o parâmetro

| Campo | Valor |
|---|---|
| ID | FIN-10b |
| Título | Parâmetro do Financeiro com três opções (Nunca / Só o Proprietário / Qualquer aprovador) para quem importou o OFX confirmar a própria conciliação; selo "Confirmada por quem importou o extrato", auditoria e histórico |
| Tamanho | P (a reavaliar na revisão) |
| Status | Draft (27/11/2026) — **a revisar** antes de entrar em execução |
| Requisitos | PRD R-FIN-8 (conciliação) · R-FIN-5 (nenhum pagamento sem OP) · R-SEC-5 · BNK-03b D13 (CA 5 e 6) · regra do produto "regras de controle de terceiros sempre configuráveis" |
| Dependências | FIN-10a (codificação do modo, step-up, concorrência, provisionamento e tela do parâmetro), BNK-03b |
| Bloqueia | — |
| Ordem de execução | Bloco "Pós-go-live: melhorias do produto" (nasceu da divisão da FIN-10 na revisão de 27/11/2026) |

## User story
Como **dono de uma empresa pequena**, quero **escolher se quem importou o extrato pode também confirmar a conciliação dele**, para **não ficar travado quando sou a única pessoa, sem esconder de ninguém que confirmei o que eu mesmo importei**.

## Contexto
- Hoje (`ReconciliationService.Commands.SegregationAsync`, BNK-03b D13/CA 5): quem importou o OFX não confirma a conciliação dele (403 `segregation_required`); "o Proprietário também não". Linhas vindas da API não têm a restrição. Não há parâmetro nem selo na conciliação.
- Proposta herdada da FIN-10 (antes da divisão): chave `finance.reconciliation_self_confirm` (mesma codificação 0/1/2 da FIN-10a), colunas `bank_reconciliations.self_confirmed boolean not null default false` e `self_approval_mode smallint null`, selo "Confirmada por quem importou o extrato" na conciliação e no histórico da linha, atalho para o parâmetro no aviso do 403 para o Proprietário, ajuda `Ajuda/financeiro-conciliacao.md`. Padrão para tenant novo pelo mesmo passo de provisionamento da FIN-10a; tenants existentes ficam em "Nunca".

## Achados da revisão de 27/11/2026 (a resolver antes de Ready)
1. **`SegregationAsync` não é só a "confirmação manual".** Ele é chamado também em `ReconciliationService.Lines.BankEntryAsync` (lançar tarifa/IOF/juros a partir da linha), `ReconciliationService.Lines.SettleAsync` (baixar título a partir da linha, `SettleFromLine`), `ReconciliationService.Cards.SettleCardsAsync` (baixa de recebíveis de cartão pela linha) e `ReconciliationService.Commands.AcceptHighAsync` (aceitar em **lote** as sugestões de alta confiança), além de `ReconcileAsync`. Todos precisam do selo e do modo gravados (e não só a conciliação manual). No lote, decidir entre **confirmação individual** (cada par autoconfirmado pede o aceite explícito, ou o lote recusa linhas do próprio importador) ou **selo por par** (cada `bank_reconciliation` gravada com `self_confirmed` e o modo), com CA e teste para cada um dos cinco caminhos.
2. **"Qualquer aprovador" na conciliação precisa de piso de segurança:** exigir **`finance.approve`** (não basta a permissão de conciliar) para quem confirma o OFX que ele mesmo importou; manter o teto `finance.ofx_bank_entry_cap` do lançamento sem OP (BNK-03b CA 6) em todos os modos; e ter **CA de segurança contra fraude de extrato** (OFX editado à mão por quem também confirma: a autoconfirmação não pode virar caminho para lançar ou baixar sem OP acima do teto, nem para conciliar uma saída sem a OP paga correspondente — R-FIN-5).

## Critérios de aceite (rascunho, a revisar)
1. **Dado** o modo "Nunca", **quando** quem importou o OFX tenta confirmar (em qualquer um dos cinco caminhos do achado 1), **então** 403 `segregation_required` como hoje.
2. **Dado** o modo "Só o Proprietário", **quando** quem importou é do grupo Proprietário e confirma, **então** grava com `self_confirmed` e o modo; quem importou e não é Proprietário continua com 403.
3. **Dado** o modo "Qualquer aprovador", **quando** quem importou tem `finance.approve`, **então** confirma o próprio OFX; sem `finance.approve`, 403 `segregation_required`.
4. **[Segurança]** Em todos os modos, o teto `ofx_bank_entry_cap` e a regra "nenhum pagamento sem OP" valem igual; a autoconfirmação nunca concilia saída sem OP paga.
5. **[Segurança]** Selo "Confirmada por quem importou o extrato" na conciliação e no histórico da linha; auditoria com o modo; desfazer e fechamento de período sem mudança.
6. Troca do parâmetro com as mesmas regras da FIN-10a (só Proprietário, código do autenticador, concorrência, aviso aos outros Proprietários).

## Fora do escopo
- Ordem de pagamento (FIN-10a).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Criada na revisão de 27/11/2026 da FIN-10, que foi dividida: a parte da conciliação veio para cá (Draft), com os achados 1 (cinco chamadas de `SegregationAsync`: `ReconcileAsync`, `BankEntryAsync`, `SettleAsync`/`SettleFromLine`, `SettleCardsAsync` e `AcceptHighAsync` em lote; selo/modo em todas e confirmação individual ou selo por par) e 2 ("Qualquer aprovador" exige `finance.approve`, mantém o teto `ofx_bank_entry_cap` e ganha CA de segurança contra fraude de extrato). Revisar antes de Ready | @po |
