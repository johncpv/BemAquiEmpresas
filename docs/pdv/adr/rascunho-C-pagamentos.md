# Rascunho C — Pagamentos: abstração de maquininha externa até TEF, SmartPOS e Pix

> Rascunho de ADR do [plano do PDV](../README.md). "Conferido" é o que foi lido no código ou nas ADRs; "a validar" é conhecimento geral que precisa de spike ou da contadora antes de virar decisão. Os achados citados por número estão em [02-arquitetura.md](../02-arquitetura.md#1-achados-do-código-que-moldam-o-desenho).

- Status: **Proposto (rascunho)**, 15/11/2026. Ainda não numerado em `docs/adr`; vira [rascunho A](rascunho-A-offline.md) ou seguinte quando o PO aprovar ([como os rascunhos viram ADR](README.md)).
- Contexto: começar com maquininha externa, sem retrabalho para TEF, SmartPOS e Pix dinâmico. Divisão por pessoa. Ver achado 9. O Finance não tem recebível de cartão; `SettlementSource` é só `Manual` ou `Bank`.

## Decisão proposta

1. **Toda venda tem "pagamentos" (`tenders`) com máquina de estados única:** `Created → Pending → Captured → (Voided | Refunded)`, mais `Failed` e `Unknown`. `Unknown` existe desde o dia 1: é o caso de queda de energia entre autorizar e confirmar no TEF.
2. **Campos presentes desde já**, mesmo vazios no modo manual: forma, valor, parcelas, provedor, adquirente, bandeira, NSU, código de autorização, `txid`, id fim a fim, tipo de integração (integrado ou não), quem confirmou.
3. **Duas portas, mesma máquina de estados:**
   - No aparelho (TypeScript), `PaymentTerminal`: `ManualTerminal` agora; depois `TefTerminal` (PayGo ou SiTef pelo agente) e `SmartPosTerminal` (Stone, PagBank, Cielo LIO pela casca Android).
   - No servidor (C#), `IPaymentProvider` com capacidades (`SupportsOffline`, `SupportsRefund`, `IsAsync`): `ManualProvider` agora; depois `InterPixProvider`.
4. **Manual:** o operador escolhe forma e bandeira e confirma; o pagamento nasce `Captured` com "atestado pelo operador" e funciona sem rede.
5. **Pix dinâmico:** `IPixChargeProvider` novo no Finance, ao lado do `IBankBillingProvider`.
   - Reaproveita `BankIntegration`, o handler mTLS e o webhook com reconsulta (o aviso nunca é verdade).
   - Precisa de adendo à ADR 0032 §4: aceitar escopo de **cobrança** Pix, que não é envio de Pix. Nomes de escopo a validar na documentação do Inter.
   - Confirmação chega ao caixa por SignalR, com consulta periódica de reserva. Só online.
6. **Divisão por pessoa:** a venda tem "partes" (por item, igual ou por valor). Cada parte tem um ou mais pagamentos. Invariante: soma dos capturados = total, com troco só em dinheiro.
7. **Falha parcial:** pagamento capturado é fato imutável. Se a pessoa 2 falha, a venda fica "parcialmente paga", aberta. Nada é desfeito sozinho. Cancelar venda com pagamento capturado exige estornar cada um; no manual, o operador estorna na maquininha e confirma, com PIN de supervisor.
8. **Financeiro:**
   - Tabela de taxas por adquirente, bandeira, forma e parcelas (taxa % e prazo D+N).
   - No fechamento do turno, o PDV envia ao Finance **resumos**, não uma linha por venda: um recebível por dia, adquirente, bandeira, forma e data prevista, com bruto, taxa e líquido.
   - Dinheiro vai para uma conta "Caixa da loja", com sangria, suprimento e quebra de caixa.
   - Conciliação pelo extrato já existente (ADR 0032 §7). Depois, extrato da adquirente atrás de `IAcquirerStatementProvider`.
9. **Módulo:** pagamentos ficam num módulo `Sales` (venda, itens, partes, pagamentos), usado pelo PDV agora e pelo e-commerce depois. Caixa, turno e aparelho ficam em `Pos`.

## Pontos legais a confirmar com a contadora
- Algumas UFs exigem vínculo entre o comprovante do cartão e a NFC-e, ou TEF integrado. Começar com maquininha externa pode não ser permitido em toda UF.
- Tratamento da taxa de serviço e da gorjeta.

## Consequências
- TEF tem homologação com o fornecedor e prazo comercial longo; a conversa começa antes do código.
- O `NfePaymentMethod` do `IFiscalEmitter` já tem cartões e Pix dinâmico e serve de base para o grupo de pagamento da NFC-e.

## Notas da consolidação do plano
- **Resumo para o Financeiro:** a análise de produto falava em "um lançamento por sessão e forma" e este rascunho em "um recebível por dia, adquirente, bandeira, forma e data prevista". O plano junta os dois: o fechamento da sessão gera os resumos, agrupados por adquirente, bandeira, forma e vencimento ([CX-03](../stories/CX-03-fechamento-financeiro-recebiveis.md)).
- **Módulos:** venda, partes e pagamentos em `Sales`; caixa, sessão e aparelho em `Pos` ([PRD](../01-PRD.md), seção 4).
- **Stories:** [PAG-01](../stories/PAG-01-formas-pagamento-adquirentes.md) e [PDV-03](../stories/PDV-03-pagamento-varias-formas.md) no MVP; PAG-02 a PAG-07 na Fase 16.
- **Adendo que este rascunho pede:** ADR 0032 §4 (aceitar escopo de cobrança Pix).
- Perguntas legais: QC-03 (maquininha não integrada por UF) e QC-05 (taxa de serviço e gorjeta).
