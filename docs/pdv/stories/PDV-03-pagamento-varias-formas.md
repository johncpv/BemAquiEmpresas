# PDV-03 — Pagamento com várias formas na mesma venda e troco, com maquininha externa: máquina de estados do pagamento, campos de TEF e Pix desde já e a porta `PaymentTerminal` com `ManualTerminal`

| Campo | Valor |
|---|---|
| ID | PDV-03 |
| Título | Pagamento com várias formas na mesma venda e troco, com maquininha externa: máquina de estados do pagamento, campos de TEF e Pix desde já e a porta `PaymentTerminal` com `ManualTerminal` |
| Tamanho | M |
| Status | Review: pronta em 18/11/2026 na branch `feat/pdv-03-pagamento` (impressão do cupom com a IMP-01; ver Implementação) |
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

## Implementação (18/11/2026)

Tudo no app do caixa (`src/Web/src/pdv/payment`, `src/Web/src/pdv/sale`, `src/Web/src/pages/pdv/sale/PaymentScreen.tsx`), sobre o diário da venda da PDV-02 e a porta `LocalStore`. No servidor, só a descida do catálogo do caixa mudou: a forma de pagamento (`PosSyncPaymentMethod`) passa a levar `cardBrands` (as bandeiras da tabela da adquirente, sem o "qualquer bandeira") e `maxInstallments` (12 no crédito parcelado, ou a maior faixa cadastrada acima disso; 1 nas outras). Taxa e prazo continuam fora da descida.

| CA | Situação | Como |
|---|---|---|
| 1 | Feito | "Pagar" (F10) grava `PaymentStarted` com o total e o operador; a venda fica `InPayment` e a tela mostra o total, "Falta" e as formas da loja em botões grandes |
| 2 | Feito | `splitCash`: o pagamento é o que faltava e o excedente é troco, à parte (`receivedCents` e `changeCents`); troco em destaque enquanto digita e na venda concluída; valores rápidos: o exato e as notas acima (`quickCashOptions`) |
| 3 | Feito | Fora do dinheiro, o valor digitado acima do que falta vira o que falta, com "Só o dinheiro dá troco."; o `SaleRegister` também recusa (troco fora do dinheiro, capturado acima do que falta) |
| 4 | Feito | Bandeira da lista da forma (`cardBrands`; vazia, todas), parcelas quando `maxInstallments` > 1, NSU opcional (até 12 letras ou números) com `<Dica>`; nasce `Captured`, `integration = Manual`, `operatorAttested`. **Segurança:** nenhum campo de número, validade ou código; NSU, observação e código de autorização com 13 dígitos ou mais (mesmo separados) são recusados com "Não digite o número do cartão.", na tela e no `SaleRegister` |
| 5 | Feito | Pix: `Captured`, `operatorAttested` e `needsReconciliation`; a tela lembra "Confira o recebimento no aplicativo do banco, não na tela do cliente."; a lista no fechamento e no relatório por operador é da CX-02, a partir desse campo |
| 6 | Parcial | A soma dos capturados fecha o total: `SaleCompleted`, número `{loja}-{caixa}-{seq}` e a venda inteira na fila de saída, numa transação só; a tela mostra "Venda concluída", o troco e "Nova venda" com o foco. **"Imprimir cupom"** aparece desabilitado com a explicação: a impressão é da IMP-01 |
| 7 | Feito | Com pagamento capturado e saldo, `PartiallyPaid`: "Voltar aos itens" desabilitado com a explicação; o `SaleRegister` recusa voltar, descartar, pôr em espera e mudar itens; o estorno com supervisor é da PDV-04 |
| 8 | Feito | Sem nada capturado, "Voltar aos itens" grava `PaymentCancelled` e a venda volta a `Open` |
| 9 | Feito | Vale-troca (`StoreCredit`) e fiado (`OnAccount`): sem rede, desabilitados com "Disponível com internet."; com rede, ocultos (PDV-06 e PDV-08 não chegaram) |
| 10 | Feito | `Tender` com forma, valor, recebido, troco, parcelas, provedor, adquirente, bandeira, NSU, autorização, `txid`, id fim a fim, integração, atestado, conferência e quem confirmou (`confirmedBy`), `null` quando não se aplica |
| 11 | Feito | A tela só fala com a porta `PaymentTerminal` (`charge`, `check`); `ManualTerminal` é a única implementação. Estados `Created → Pending → Captured → (Voided \| Refunded)` mais `Failed` e `Unknown` (`canTransition`); `Pending`, `Failed` e `Unknown` têm tela ("Aguardando a maquininha…", "Pagamento não aprovado", "Não sabemos se o pagamento foi aprovado" com "Foi aprovado" / "Não foi aprovado"); o `TenderChanged` grava esses estados |
| 12 | Feito | Nada no pagamento usa a rede; dinheiro, cartão na maquininha e Pix conferido funcionam igual com e sem conexão |

**Decisões e desvios:**
- **Eventos a mais:** além de `PaymentStarted`, `TenderCaptured` e `SaleCompleted`, existem `PaymentCancelled` ("Voltar aos itens", CA 8) e `TenderChanged` (pagamento em `Pending`, `Failed` ou `Unknown`, das integrações futuras). Todos levam o operador.
- **Fechamento numa transação:** o último pagamento e o `SaleCompleted` entram direto na entrada `pos.sale` da fila, junto com os eventos do diário, na mesma transação que atribui o número. Se a energia cair entre gravar um pagamento e concluir, ao reabrir o caixa a venda já paga é concluída sozinha.
- **CPF (PP-26):** a venda concluída mantém o registro cifrado do documento (serve para a nota e a devolução); a fila leva só o id do registro. Teste confere que o CPF não aparece em claro na entrada da fila.
- **Rota:** o pagamento é uma tela dentro de `/pdv/` (mesmo motivo da PDV-02), não `/pdv/venda/pagamento`.
- **Tabelas `sales.sales`, `sale_parts`, `sale_payments`:** a projeção é da OFF-02 (como a story diz); nenhuma migration nesta story. Os requisitos da validação na subida foram copiados para a OFF-02 ("Requisitos vindos da PDV-03").
- **Parâmetros da loja (PP-22) e saldo (PP-02):** não entram aqui (tolerância e sangria são da CX-01 e CX-02; o bloqueio sem saldo, da PDV-05).
- Konebras (perfil fábrica): nada novo aparece; tudo está no app do caixa (módulo `pos`) e no catálogo do caixa.

**Testes:** Vitest `src/pdv/payment/payment-model.test.ts` (troco, limite fora do dinheiro, valores rápidos, soma com 1, 2 e 3 formas e centavos, estados da venda, máquina de estados do pagamento, `ManualTerminal`, campos do CA 10, número de cartão no NSU e na observação, vale-troca e fiado), `src/pdv/sale/sale-payment.test.ts` (três formas até `Completed` com número e fila, CPF fora da fila, invariante, voltar aos itens, recuperação depois da queda, `Unknown` conferido) e `src/pages/pdv/sale/PaymentScreen.test.tsx` (cartão + dinheiro na mesma venda pela tela, fiado sem rede, sem forma cadastrada). .NET: `PosTests.Forma_de_cartao_desce_com_as_bandeiras_da_tabela_e_o_teto_de_parcelas_sem_taxa` (integração) e o `OpenApiDocumentTests`. **A conferir no CI:** os testes .NET não rodaram nesta máquina (o Docker não monta o disco D:); a entrada do `openapi.json` foi escrita no formato do gerador e o cliente Orval regenerado dela. E2E de dinheiro + cartão: no fechamento do marco (plano de testes).

Ajuda: `Ajuda/pdv-pagamento.md` (nova), embutida na ajuda do app do caixa; `Ajuda/pdv-venda.md` atualizada (F10 abre o pagamento).

## Pendências de segurança
Da revisão de segurança da PDV-03 (18/11/2026):

| Item | Situação | O quê |
|---|---|---|
| 1 | Corrigido | `recordTender` recusa qualquer mudança de um pagamento já `Captured` ("Este pagamento já foi recebido. Ele só sai por estorno ou cancelamento, com o supervisor."). A saída de `Captured` (estorno ou cancelamento) será um método próprio da PDV-04, com quem autorizou. Teste em `sale-payment.test.ts` |
| 2 | Corrigido | `looksLikeCardNumber` normaliza antes de contar: NFKC (dígitos fullwidth viram ASCII), tira `\p{Cf}` (zero-width space, word joiner), trata qualquer não-alfanumérico entre dígitos como separador (barra, sublinhado, vírgula, asterisco, travessões) e testa `\p{Nd}{13,}` com a flag `u`. Teste em `payment-model.test.ts` com os casos da revisão |
| 3 | Registrado na OFF-02 | O que o servidor valida na subida (o aparelho não é confiável): troco coerente e totais do topo recalculados, `PaymentStarted.totalCents` igual ao total refeito, máquina de estados por `tenderId` com saída de `Captured` só pela PDV-04, `tenderId` único, venda idempotente por `saleId` e número, `integration`/`operatorAttested`/`needsReconciliation` definidos pelo servidor e campos de integração nulos no Manual, `methodType`, parcelas e bandeira conferidos, a mesma normalização do número de cartão com tamanho máximo (140 e 12), `operatorId` e `confirmedBy` nulos recusados. Em [OFF-02, "Requisitos vindos da PDV-03"](OFF-02-sincronizacao-pendencias.md#requisitos-vindos-da-pdv-03-pagamento) |
| 4 | Corrigido | Conclusão ao reabrir o caixa: o `SaleCompleted` leva como `operatorId` o `confirmedBy` do último `TenderCaptured` e marca `recoveredBy` com o operador que reabriu; `finish` exige a soma dos capturados **igual** ao total (acima do total não conclui e a venda fica para conferência); a tela avisa "Venda {número} concluída ao reabrir o caixa." com o troco, se houver. Testes em `sale-payment.test.ts` e `PaymentScreen.test.tsx` |
| 5 | Corrigido | `PaymentScreen`: guarda síncrona (`useRef`) contra o duplo envio do Confirmar (e do "Foi aprovado"/"Não foi aprovado"). Teste com dois cliques no mesmo ato: uma captura só (sem a guarda, o teste falha com duas) |
| 6 | Corrigido | Troco acima de R$ 200 no dinheiro pede um segundo toque: "Troco de R$ X: confira o valor recebido. Se estiver certo, toque em Confirmar recebimento de novo." O limite é constante da tela (`HIGH_CHANGE_CENTS`), não regra de terceiro. Teste em `PaymentScreen.test.tsx` |

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
| 18/11/2026 | 1.0 | Implementada (branch `feat/pdv-03-pagamento`): tela de pagamento no app do caixa com várias formas, troco, bandeira e NSU sem dado de cartão, Pix conferido marcado para conferência, porta `PaymentTerminal` com `ManualTerminal` e os estados `Pending`, `Failed` e `Unknown` com tela; venda concluída com número na fila de saída; bandeiras e teto de parcelas no catálogo do caixa. CA 6 parcial (cupom com a IMP-01). | Claude (dev) |
| 18/11/2026 | 1.1 | Revisão de segurança: pagamento `Captured` não muda pelo `recordTender` (saída só pela PDV-04), número de cartão normalizado (NFKC, invisíveis, qualquer separador, fullwidth), conclusão ao reabrir com o operador do último pagamento, `recoveredBy`, soma exata e aviso na tela, guarda contra duplo envio do Confirmar e confirmação de troco acima de R$ 200; requisitos do servidor (item 3) registrados na OFF-02. Ver "Pendências de segurança". | Claude (dev) |
