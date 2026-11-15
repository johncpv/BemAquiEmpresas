# PDV-03 — Pagamento com várias formas na mesma venda e troco, com maquininha externa: máquina de estados do pagamento, campos de TEF e Pix desde já e a porta `PaymentTerminal` com `ManualTerminal`

| Campo | Valor |
|---|---|
| ID | PDV-03 |
| Título | Pagamento com várias formas na mesma venda e troco, com maquininha externa: máquina de estados do pagamento, campos de TEF e Pix desde já e a porta `PaymentTerminal` com `ManualTerminal` |
| Tamanho | M |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 11 — PDV balcão com cupom não fiscal e offline (marco MVP) |
| Requisitos | Pedido do PO de 15/11/2026 (itens 6 e 11) · [Rascunho C](../adr/rascunho-C-pagamentos.md) itens 1 a 4, 6 e 7 · [Fluxos](../04-fluxos-estados.md#5-split-de-pagamento) |
| Dependências | PDV-02, PAG-01, VND-01 |
| Bloqueia | CX-02, IMP-01, OFF-02, PDV-04 |
| Paridade com o mercado | Requisitos mínimos 10 (formas diferentes na mesma venda) do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | 4 (Fase 11) |

## User story
Como **operador de caixa**, quero **receber parte em dinheiro e parte no cartão e ver o troco na hora**, para **fechar a venda sem fazer conta**.

## Contexto e referências
- O PO pediu maquininha externa primeiro, com a integração já prevista. Por isso o pagamento nasce com o tipo de integração e os campos que o TEF e o Pix vão preencher depois.
- A divisão por pessoa e por item (MESA-04) usa a mesma estrutura de "partes"; aqui a venda tem uma parte só.

## Critérios de aceite
1. **Dado** uma venda com itens, **quando** aciono "Pagar", **então** a venda passa a `InPayment` e vejo o total, as formas de pagamento da loja e o valor que falta.
2. **Dado** a forma Dinheiro, **quando** informo o valor recebido maior que o que falta, **então** vejo o troco em destaque e o pagamento é gravado com o valor da venda e o troco à parte; há botões de valor rápido (exato e as notas acima).
3. **Dado** uma forma que não é dinheiro, **quando** informo valor maior que o que falta, **então** recebo "Só o dinheiro dá troco." e o valor é limitado ao que falta.
4. **Dado** uma forma de cartão, **quando** confirmo, **então** escolho a bandeira (lista da forma) e as parcelas, quando houver; o NSU é opcional; o pagamento nasce `Captured`, com `integration = Manual` e "atestado pelo operador". **[Segurança]** A tela nunca pede número de cartão, validade ou código de segurança, e os campos NSU e observação recusam sequência que pareça número de cartão ("Não digite o número do cartão.").
5. **Dado** a forma Pix sem integração, **quando** confirmo, **então** o pagamento nasce `Captured`, marcado "Pix conferido pelo operador" e **pendente de conferência contra o extrato**; a tela lembra "Confira o recebimento no aplicativo do banco, não na tela do cliente."; esses pagamentos são listados um a um no fechamento (CX-02) e no relatório por operador.
6. **Dado** dois ou mais pagamentos, **quando** a soma dos capturados é igual ao total, **então** a venda passa a `Completed`, recebe o número `{loja}-{caixa}-{seq}` e a tela oferece "Imprimir cupom" e "Nova venda".
7. **Dado** um pagamento capturado e saldo a pagar, **quando** confiro, **então** a venda está `PartiallyPaid`; "Voltar aos itens" fica bloqueado e o pagamento capturado só sai por estorno com supervisor (PDV-04).
8. **Dado** `InPayment` sem nenhum pagamento capturado, **quando** uso "Voltar aos itens", **então** a venda volta a `Open`.
9. **Dado** as formas Fiado e Vale-troca, **quando** estou sem rede, **então** aparecem desabilitadas com "Disponível com internet."; com rede e sem a story correspondente pronta, ficam ocultas.
10. **Dado** o registro do pagamento, **quando** confiro o dado, **então** existem os campos forma, valor, troco, parcelas, provedor, adquirente, bandeira, NSU, código de autorização, `txid`, id fim a fim, tipo de integração e quem confirmou, vazios quando não se aplicam.
11. **Dado** a porta `PaymentTerminal`, **quando** leio o código, **então** a tela de pagamento só fala com ela; o `ManualTerminal` é a única implementação e os estados `Pending`, `Failed` e `Unknown` existem e têm tela, mesmo sem uso no modo manual.
12. **Dado** qualquer estado de rede, **quando** pago em dinheiro, cartão na maquininha ou Pix conferido, **então** funciona igual.

## Regras de negócio e técnicas
- Invariante: soma dos pagamentos capturados = total da venda; troco só em dinheiro.
- Pagamento capturado é fato imutável: não se edita, só se estorna.
- Estados do pagamento: `Created → Pending → Captured → (Voided | Refunded)`, mais `Failed` e `Unknown`.
- Dinheiro em centavos no aparelho.

## Permissões, eventos e tabelas
- **Permissões:** `pos.create`.
- **Eventos do aparelho:** `PaymentStarted`, `TenderCaptured`, `SaleCompleted`.
- **Tabelas (projeção no servidor, pela OFF-02):** `sales.sales`, `sale_lines`, `sale_parts`, `sale_payments`.

## Telas e estados
- **Pagamento** (`/pdv/venda/pagamento`): total, "Falta", formas em botões grandes, lista dos pagamentos já feitos. Vazio não se aplica. Erro: "Nenhuma forma de pagamento ativa nesta loja." com o caminho para cadastrar.
- **Concluída:** "Venda concluída", troco em destaque, "Imprimir cupom", "Nova venda" (foco padrão).
- **Ajuda:** `Ajuda/pdv-pagamento.md` (nova). `<Dica>` em "NSU".

## Plano de testes
- **Vitest:** troco (CA 2 e 3); invariante da soma com 1, 2 e 3 formas e centavos; máquina de estados do pagamento (CA 7, 8 e 11).
- **Integração (Testcontainers), com a OFF-02:** a venda projetada bate com os eventos.
- **E2E no fechamento do marco:** dinheiro + cartão na mesma venda.

## Fora do escopo
- Split por pessoa e por item (MESA-04).
- Pix dinâmico, TEF, SmartPOS (PAG-02 a PAG-04).
- Fiado (PDV-08) e vale-troca (PDV-06).

## Pontos em aberto
- QC-03: se alguma UF proíbe maquininha não integrada ou exige dados do cartão na nota. Não afeta o MVP não fiscal.

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
