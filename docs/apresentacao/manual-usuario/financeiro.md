# Manual do Financeiro

**Grupo:** Financeiro (do sistema). **Ações principais:** `finance.view`, `finance.create`, `finance.edit`, `finance.pay`; notas fiscais (`fiscal.view`, `fiscal.create`, `fiscal.edit`); custo e margem de orçamentos e projetos. **Precisa de:** 2FA (você paga).

Você cobra, monta as ordens de pagamento, paga o que foi aprovado, cuida do extrato e emite as notas. Você **não aprova** pagamento: quem aprova é o Proprietário ou quem estiver na alçada.

## Rotina

### Todo dia

| Tarefa | Tela | Ajuda |
|---|---|---|
| Ver a pauta: ordens aprovadas para pagar, títulos que vencem em 3 dias sem ordem | Início `/app` | [inicio](../../../Ajuda/inicio.md) |
| Baixar recebimentos que não vieram pelo boleto | Financeiro → A receber `/app/financeiro/receber` | [financeiro-receber-titulo-baixas](../../../Ajuda/financeiro-receber-titulo-baixas.md) |
| Emitir e enviar boleto com Pix | Título a receber → aba **Boleto** | [financeiro-receber-titulo-boleto](../../../Ajuda/financeiro-receber-titulo-boleto.md) |
| Montar ordem de pagamento dos títulos que vencem | Financeiro → A pagar `/app/financeiro/pagar` → **Criar ordem de pagamento** | [financeiro-pagar](../../../Ajuda/financeiro-pagar.md) · [financeiro-ordem-nova](../../../Ajuda/financeiro-ordem-nova.md) |
| Pagar as ordens aprovadas | Ordens de pagamento `/app/financeiro/ordens-de-pagamento` → **Pagar** | [financeiro-ordem-de-pagamento](../../../Ajuda/financeiro-ordem-de-pagamento.md) |
| Emitir a NF-e do romaneio expedido | Financeiro → Fiscal → saída `/app/fiscal/saida` | [fiscal-saida](../../../Ajuda/fiscal-saida.md) · [fiscal-nota-saida](../../../Ajuda/fiscal-nota-saida.md) |
| Registrar a NFS-e da instalação (processo provisório) | `/app/fiscal/saida/nfse/$id` | [fiscal-nfse](../../../Ajuda/fiscal-nfse.md) |

### Toda semana

| Tarefa | Tela | Ajuda |
|---|---|---|
| Importar o extrato (API do Inter ou OFX) | Financeiro → Contas e caixas → extrato `/app/financeiro/contas/$id/extrato` | [financeiro-contas-extrato-banco](../../../Ajuda/financeiro-contas-extrato-banco.md) |
| Conferir a inadimplência e os lembretes da régua | Inadimplência `/app/financeiro/inadimplencia`; título → aba **Lembretes** | [financeiro-inadimplencia](../../../Ajuda/financeiro-inadimplencia.md) · [financeiro-receber-titulo-lembretes](../../../Ajuda/financeiro-receber-titulo-lembretes.md) |
| Olhar o fluxo de caixa no cenário ajustado | Fluxo de caixa `/app/financeiro/fluxo-de-caixa` | [financeiro-fluxo-de-caixa](../../../Ajuda/financeiro-fluxo-de-caixa.md) |
| Conferir os títulos que nasceram dos recebimentos e da folha | A pagar | [compras-recebimento-financeiro](../../../Ajuda/compras-recebimento-financeiro.md) |

### Todo mês

| Tarefa | Tela | Ajuda |
|---|---|---|
| Gerar os títulos das contas recorrentes (aluguel, energia) | Financeiro → Recorrentes `/app/financeiro/recorrentes` | [financeiro-recorrentes](../../../Ajuda/financeiro-recorrentes.md) |
| Pagar salários e guias da folha fechada pelo RH | Ordens de pagamento | [financeiro-ordens-de-pagamento](../../../Ajuda/financeiro-ordens-de-pagamento.md) |
| Deixar a conciliação pronta para o Proprietário fechar | Conciliação `/app/financeiro/conciliacao` | [financeiro-conciliacao](../../../Ajuda/financeiro-conciliacao.md) |
| Conferir o DRE com a contadora | DRE `/app/financeiro/dre` | [financeiro-dre](../../../Ajuda/financeiro-dre.md) |

## Entre as telas

- **Ninguém digita a parcela do contrato.** As parcelas nascem na aprovação do orçamento. As de evento ficam **previstas** até a fabricação, a entrega ou o aceite técnico acontecer; aí ganham vencimento e aparecem na sua pauta.
- **Ninguém digita a conta a pagar da compra.** O recebimento (manual ou pela nota) cria os títulos, com os vencimentos da nota quando é pela NF-e. Seu trabalho é conferir e mandar para ordem.
- **O boleto não muda o título.** Valor, situação e vencimento só mudam com a baixa. O aviso do banco baixa o título sozinho; se não baixou, confira em Configurações → Integrações → Eventos antes de baixar à mão.
- **Conciliação em dupla:** quem importou o OFX não concilia aquele arquivo. Combine com o Proprietário (ou outra pessoa do Financeiro) quem concilia.
- **Salários:** a conta do funcionário é lida na hora do pagamento, só para quem paga. Se a conta mudou depois do envio, a ordem pede nova aprovação.
- **Nota fiscal:** você emite e pode corrigir por carta de correção. Cancelar é do Proprietário; use **Pedir cancelamento** com o motivo dentro do prazo de 24 h.

## Os 5 erros mais comuns

1. **Baixar à mão um título que o boleto já baixou.** Gera recebimento em dobro. Antes da baixa manual, abra a aba **Boleto** e veja a situação.
2. **Pagar fora da ordem de pagamento.** Toda saída para fornecedor passa por ordem aprovada. Tarifa e IOF entram pela conciliação, com **Lançar sem OP**.
3. **Pagar com valor diferente do aprovado.** Se o valor passou, use **Reenviar para aprovação**. Não divida em várias ordens pequenas: o sistema soma 30 dias do fornecedor.
4. **Emitir a NF-e de novo enquanto ela processa.** Espere "Aguardando a autorização…" terminar; a conferência continua sozinha.
5. **Usar sempre "Receitas a classificar" ou "Despesas a classificar".** O DRE fica sem sentido. Escolha a conta certa do plano; na dúvida, pergunte à contadora uma vez e use sempre a mesma.
