# PDV-04 — Desconto por item e no total com limite por grupo, troca de preço, cancelamento de item e de venda paga, e aprovação por PIN de supervisor com motivo e trilha

| Campo | Valor |
|---|---|
| ID | PDV-04 |
| Título | Desconto por item e no total com limite por grupo, troca de preço, cancelamento de item e de venda paga, e aprovação por PIN de supervisor com motivo e trilha |
| Tamanho | M |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 11 — PDV balcão com cupom não fiscal e offline (marco MVP) |
| Requisitos | [PRD do PDV](../01-PRD.md) seções 6.4 e 6.5 · [Fluxos](../04-fluxos-estados.md#6-cancelamento-devolução-estorno-e-troca) · ADR 0008 e ADR 0026 (exceção a registrar) · PP-11 |
| Dependências | PDV-03, CX-01; PP-11 e PP-38 decididas; PP-22 (valores padrão) |
| Bloqueia | OFF-03 |
| Paridade com o mercado | Requisitos mínimos 6 do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | 11 (Fase 11) |

## User story
Como **dono**, quero **que desconto alto, troca de preço e cancelamento só passem com a senha de um supervisor e fiquem registrados**, para **ninguém fazer isso sozinho no caixa**.

## Contexto e referências
- A ADR 0008 exige TOTP recente para estornar, e a ADR 0026 recusa PIN para quem tem `*.approve`. No balcão as duas são impraticáveis: esta story depende da exceção nomeada da PP-11.
- Nesta story a aprovação é conferida no servidor (com rede). Sem rede vem na OFF-03.

## Critérios de aceite
### Desconto e preço
1. **Dado** um item ou o total, **quando** dou desconto em % ou em valor até o meu limite (o da loja ou o do meu grupo, conforme a PP-38), **então** ele é aplicado, com motivo obrigatório escolhido de uma lista (com "Outro" e texto).
2. **Dado** um desconto acima do meu limite e sem `pos.discount`, **quando** tento, **então** vejo "Chamar supervisor" com o resumo (item, de quanto para quanto).
3. **Dado** `pos.change_price` ou a aprovação de um supervisor, **quando** troco o preço de um item, **então** a linha guarda o preço da tabela e o praticado, com o motivo.
4. **Dado** um desconto que deixa o item abaixo do custo, **quando** a venda chega ao servidor, **então** entra no relatório "Vendas abaixo do custo" (o custo não desce para o aparelho).

### Cancelamento
5. **Dado** uma venda aberta, **quando** removo um item sem `pos.cancel_item`, **então** preciso de "Chamar supervisor"; com a permissão, removo com motivo.
6. **Dado** uma venda `Completed` da sessão atual, **quando** aciono "Cancelar venda" com `pos.cancel_sale`, **então** ainda assim preciso da aprovação de um supervisor **diferente de mim**, com motivo; a venda passa a `Cancelled`.
7. **Dado** o cancelamento de venda com pagamento em dinheiro, **quando** concluo, **então** o valor sai do dinheiro em caixa da sessão; com cartão na maquininha externa, a tela instrui "Estorne na maquininha e informe o NSU do estorno." e só conclui depois da confirmação do operador.
8. **Dado** uma venda de sessão anterior, **quando** tento cancelar, **então** recebo "Esta venda é de outro caixa. Use a devolução." (PDV-06).
9. **Dado** o cancelamento de uma venda paga, **quando** concluído, **então** o Proprietário recebe uma notificação com venda, valor, operador, supervisor e motivo.

### Supervisor
10. **Dado** "Chamar supervisor", **quando** o supervisor digita o PIN no mesmo aparelho, **então** o servidor confere que ele tem a permissão da ação, grava `supervisor_approvals` (ação, operador, supervisor, motivo, antes e depois, venda) e libera **só aquela ação**; a sessão do operador continua a mesma.
11. **[Segurança]** **Dado** o operador que também é supervisor, **quando** tenta aprovar a própria ação, **então** recebe "Outra pessoa precisa aprovar.".
12. **[Segurança]** **Dado** cinco PINs errados, **quando** ocorre o quinto, **então** o PIN daquele usuário naquele aparelho bloqueia, como hoje (ADR 0026).
13. **Dado** `sales.view`, **quando** abro "Aprovações de supervisor", **então** vejo o relatório por operador, por supervisor e por tipo, com exportação; e o indicador "cancelamentos acima de X% das vendas do turno" por caixa.

## Regras de negócio e técnicas
- Onde mora o limite de desconto é a PP-38: por loja (recomendado; parâmetro do `Pos`) ou por grupo (campo no Identity; nesse caso vale o maior entre os grupos do operador). A primeira versão do plano dizia as duas coisas; a story não sai de `Draft` sem a decisão. **Decidida em 15/11/2026: por loja.**
- O supervisor nunca aprova a própria operação.
- Cancelar item, descontar e cancelar venda entram no `audit.audit_log`, além dos eventos do aparelho.
- O PIN de supervisor é a exceção nomeada às ADRs 0008 e 0026; o TOTP continua valendo na retaguarda.

## Permissões, eventos e tabelas
- **Permissões:** `pos.discount`, `pos.change_price`, `pos.cancel_item`, `pos.cancel_sale`.
- **Endpoints:** `POST /api/v1/pos/supervisor-approvals` (na lista do PIN); `GET /api/v1/pos/supervisor-approvals`, `GET .../export` (retaguarda, autorizados com `sales.view`); o limite de desconto na tela da loja ou do grupo, conforme a PP-38.
- **Eventos do aparelho:** `DiscountApplied`, `PriceOverridden`, `LineRemoved`, `SaleCancelled`, `SupervisorApproved`. **Evento de domínio:** `SaleCancelled`.
- **Tabelas:** `pos.supervisor_approvals`; o limite de desconto em `pos.stores` ou no grupo, conforme a PP-38.

## Telas e estados
- **Diálogo "Chamar supervisor":** resumo da ação, lista dos supervisores da loja, teclado de PIN. Erro: PIN errado com as tentativas restantes.
- **Loja → Aprovações de supervisor** (`/app/loja/aprovacoes`): relatório. Vazio: "Nenhuma aprovação no período.". Carregando e erro.
- **Ajuda:** `Ajuda/pdv-supervisor.md` e `Ajuda/loja-aprovacoes.md` (novas).

## Plano de testes
- **Unidade:** resolução do limite de desconto (conforme a PP-38); desconto em % e em valor com centavos.
- **Integração (Testcontainers):** aprovação grava a trilha (CA 10); supervisor igual ao operador é recusado (CA 11); cancelamento ajusta a sessão (CA 7) e dispara estoque e aviso (CA 9).
- **Vitest:** diálogo do supervisor.

## Fora do escopo
- PIN conferido sem rede (OFF-03).
- Cancelamento de NFC-e (NFCE-03).
- Devolução (PDV-06).

## Pontos em aberto
- PP-11: sem a exceção, a aprovação teria de ser por TOTP e a story precisa ser redesenhada.
- PP-38: onde mora o limite. PP-22: o valor padrão (sugestão: 5%) e o X% do alerta de cancelamentos.

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
