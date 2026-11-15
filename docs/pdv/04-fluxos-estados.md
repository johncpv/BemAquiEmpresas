# 04 — Fluxos e máquinas de estado do PDV (proposta)

Cada seção traz o diagrama, a tabela de transições (quem pode, o que exige) e os efeitos nos outros módulos. Os estados em inglês são os valores gravados; a tela mostra o rótulo em português.

## 1. Venda

`sales.status`

```
Open ──itens──► Open ──"Pagar"──► InPayment ──Σ capturado = total──► Completed
 │  ▲                                  │   │                            │
 │  └──────── "Voltar aos itens" ──────┘   └─► PartiallyPaid ───────────┤ (split, seção 5)
 ├─► Parked (em espera) ─► Open                                         ├─► PartiallyReturned ─► Returned
 └─► Discarded (antes do pagamento)                                     └─► Cancelled (depois de paga, com aprovação)
```

| De | Para | Gatilho | Quem | Exige |
|---|---|---|---|---|
| — | `Open` | Primeiro item ou "Nova venda" | `pos.create` | Sessão de caixa aberta |
| `Open` | `Parked` | "Deixar em espera" | `pos.create` | — |
| `Parked` | `Open` | "Retomar" | `pos.create` | Mesmo caixa |
| `Open` | `Discarded` | "Descartar venda" | `pos.create` | Motivo, se já tinha itens. Fica registrada |
| `Open` | `InPayment` | "Pagar" | `pos.create` | Ao menos um item |
| `InPayment` | `Open` | "Voltar aos itens" | `pos.create` | Nenhum pagamento capturado |
| `InPayment` | `PartiallyPaid` | Primeiro pagamento capturado, com saldo a pagar | `pos.create` | — |
| `InPayment`, `PartiallyPaid` | `Completed` | Soma dos capturados = total | automático | Troco só no dinheiro |
| `Completed` | `Cancelled` | "Cancelar venda" | `pos.cancel_sale` + supervisor diferente do operador | Mesma sessão; motivo; estorno de cada pagamento. Com NFC-e, dentro do prazo legal (QC-02) |
| `Completed` | `PartiallyReturned`, `Returned` | Devolução concluída | `pos.return` | Seção 6 |

Três eixos paralelos, que **nunca travam a venda**:

| Eixo | Valores | Significado |
|---|---|---|
| `fiscal_status` | `NotRequired` · `Pending` → `Authorized` \| `AuthorizedInContingency` \| `Rejected` → `Cancelled` | Seção 7 |
| Sincronização (no aparelho) | `Local` → `Synced` \| `Divergent` \| `Quarantined` | Seção 8 |
| `stock_status` | `Pending` → `Posted` \| `Shortfall` | Baixa feita, ou falta de saldo registrada (PP-02) |

Regras:
- O preço e o desconto ficam gravados na linha. O servidor não recalcula venda sincronizada; só sinaliza divergência.
- Número `{loja}-{caixa}-{seq}`, gerado no aparelho.
- O id da venda é Guid v7 do aparelho; os eventos que a compõem levam `event_id` próprio.

Efeitos:

| Momento | Estoque | Financeiro | Fiscal |
|---|---|---|---|
| `Completed` | Baixa no local da loja, fora do caminho do caixa. Falta de saldo vira pendência ou saldo negativo, conforme a PP-02; nunca erro | Nada por venda (ver fechamento do caixa). Exceção: fiado gera título contra o cliente na hora | Loja fiscal: pedido de emissão; a venda segue `Pending`. Loja não fiscal: nada |
| `Cancelled` | Estorno pelo documento de origem | Estorna o fiado; ajusta os totais da sessão | Cancela a NFC-e dentro do prazo; fora dele, vira devolução |
| `Returned` | Entrada de devolução, ou perda se avariado | Saída de caixa, vale-troca (passivo) ou estorno de cartão registrado | NF-e de devolução ou cancelamento (QC-15) |

## 2. Caixa e turno

`pos.cash_sessions.status`

```
(sem sessão) ──Abrir: fundo de troco──► Open ──Sangria / Suprimento──► Open
Open ──"Fechar caixa"──► Counting (conferência cega) ──confirma──► Counted
Counted ──|diferença| ≤ tolerância──► Closed (quebra lançada sozinha)
Counted ──|diferença| > tolerância──► PendingReview ──aprovar + motivo──► Closed
Closed ──reabrir, com motivo (só a última sessão do caixa)──► Open
```

| De | Para | Gatilho | Quem | Exige |
|---|---|---|---|---|
| — | `Open` | "Abrir caixa" | `pos.create` | Nenhuma sessão `Open` no caixa; fundo de troco informado (vem do Cofre, por transferência). Na primeira versão pública, funciona sem rede |
| `Open` | `Open` | Sangria | `pos.cash_out` ou PIN de supervisor | Valor, motivo; comprovante em duas vias com assinatura de quem retira |
| `Open` | `Open` | Suprimento | `pos.cash_in` ou PIN de supervisor | Valor, motivo; comprovante |
| `Open` | `Counting` | "Fechar caixa" | Operador da sessão | Nenhuma venda `Open` ou `InPayment`; vendas `Parked` são listadas para descartar ou retomar |
| `Counting` | `Counted` | "Confirmar contagem" | Operador da sessão | Contado por forma, **sem ver o esperado**. Uma só contagem: recontar exige supervisor |
| `Counted` | `Closed` | automático | — | Diferença dentro da tolerância da loja |
| `Counted` | `PendingReview` | automático | — | Diferença acima da tolerância |
| `PendingReview` | `Closed` | "Aprovar fechamento" | `sales.approve` | Motivo. Quem aprova não é o operador da sessão |
| `Closed` | `Open` | "Reabrir caixa" | `sales.approve` | Motivo; só a última sessão do caixa; desfaz os lançamentos do fechamento |

Regras:
- **Sangria sugerida** quando o dinheiro em caixa passa do limite da loja; **obrigatória** acima do teto (a venda em dinheiro seguinte pede a sangria antes).
- **Conferência cega:** o operador digita o contado por forma (dinheiro por cédula, opcional; comprovantes de cartão por total). O esperado e a diferença só aparecem para quem tem `sales.approve`.
- **Relatório de fechamento:** vendas por forma, cancelamentos, descontos, sangrias, suprimentos, vendas feitas sem rede, quebra. Em PDF (ADR 0045) e em bobina.

Efeitos no Financeiro:

| Momento | Lançamento |
|---|---|
| Abertura | Transferência Cofre → Caixa do PDV (fundo de troco) |
| Sangria / suprimento | Transferência Caixa do PDV ↔ Cofre ou banco (`bank_transfers`) |
| `Closed` | Dinheiro: saldo na conta Caixa do caixa. Cartão: título contra a adquirente, um por adquirente, bandeira, forma e vencimento, com bruto, taxa prevista e líquido (parcelado: um por parcela). Pix conferido à mão: entrada no banco. Quebra: `bank_entries` na conta "Quebra de caixa". Tudo idempotente por sessão |

## 3. Mesa e comanda (Fase 15)

```
Mesa:    Free ─► Occupied ─► BillRequested ─► Paying ─► Free      (+ Reserved, Blocked)

Comanda: Open ──lança rodada──► Open ──"Pedir a conta" (pré-conta)──► BillRequested
         BillRequested ──pagamento parcial──► PartiallyPaid ──Σ = total──► Closed
         Open | BillRequested ──reabrir (lançar mais)──► Open
         Open ──cancelar (sem itens, ou com aprovação)──► Cancelled
```

| Operação | Quem | Regra |
|---|---|---|
| Lançar rodada | `restaurant.create` | Envia os itens novos à cozinha, por estação |
| Pedir a conta | `restaurant.create` | Imprime a pré-conta, "sem valor fiscal", com taxa de serviço e couvert |
| Retirar a taxa de serviço | `restaurant.waive_service` | Registrado |
| Transferir item, transferir mesa, juntar mesas, dividir comanda | `restaurant.transfer` | Auditado |
| Cancelar item já enviado à cozinha | `restaurant.cancel_item` | Motivo; aviso em destaque no KDS |
| Pagar | `pos.create` | Pelo `IOrderPayments`: a comanda vira uma venda (`channel = Table`). A baixa de estoque é a da venda |

- A mesa só volta a `Free` com todas as comandas `Closed`.
- Comanda por mesa, por cartão numerado (padaria) ou por nome (balcão).
- **Mesas e comandas são estado compartilhado entre aparelhos.** Com rede, o servidor manda. Sem rede, cada aparelho só acrescenta itens às comandas que ele mesmo abriu; fechar comanda de outro aparelho exige rede (PP-23).

## 4. Pedido → cozinha → pronto → entregue (Fase 15)

`restaurant.kitchen_tickets.status`, um tíquete por estação de preparo.

```
Queued ──"Iniciar"──► InPreparation ──"Pronto"──► Ready ──"Entregue"──► Delivered
   └──────────────── Cancelled (com aviso em destaque no KDS) ◄──────────────┘
```

| Onde aparece | Regra |
|---|---|
| Estado do pedido | Derivado: `Ready` quando todos os tíquetes estão `Ready` (a estação Expedição confere) |
| Painel do cliente, coluna **"Na cozinha"** | Pedidos com tíquete `Queued` ou `InPreparation` |
| Painel do cliente, coluna **"Pedidos prontos"** | Pedidos `Ready`, até `Delivered` ou N minutos. Chamada sonora e destaque ao entrar |
| KDS | Cores por tempo (no prazo, atenção, atrasado); "desfazer" por 10 segundos; resumo de itens iguais; prioridade |
| Sem KDS (trailer) | A comanda de produção sai impressa e o caixa marca "Pronto" |
| Sem internet | KDS e painel mostram "Sem conexão. Os pedidos estão saindo na impressora."; a impressora de cozinha é o canal garantido |

- O painel é o quiosque da TV (ADR 0030) com a visão "pedidos": só leitura, sempre escuro, sem dado pessoal além da senha e, se o PO quiser, do primeiro nome (PP-24).
- O KDS é aparelho registrado do tipo KDS, porque escreve.
- A senha é curta, por loja e dia. Sem rede, cada caixa usa uma faixa própria, para não colidir.
- O aviso sai depois do commit (`AfterCommit`), não pelo outbox.

## 5. Split de pagamento

```
Total a pagar ─► Dividir: [partes iguais] | [por valor] | [por itens ou pessoa]
Parte:      Pending ──1..n pagamentos capturados──► Paid          (troco só no dinheiro)
Pagamento:  Created ─► Pending ─► Captured ─► (Voided | Refunded)     + Failed, Unknown
Venda:      InPayment ─► PartiallyPaid ─► Completed   (Σ capturados = total)
```

| Regra | Detalhe |
|---|---|
| Parte paga não se perde | Se a pessoa 2 falha, a venda fica `PartiallyPaid`, aberta. Nada é desfeito sozinho. O restante pode ser redividido |
| Pagamento capturado é imutável | Cancelar venda com pagamento capturado exige estornar cada um. No modo manual, o operador estorna na maquininha e confirma, com PIN de supervisor |
| Taxa de serviço | Rateada na proporção de cada parte |
| Centavos | O arredondamento cai na última parte |
| Modo manual | O pagamento nasce `Captured`, "atestado pelo operador", e funciona sem rede |
| `Unknown` | Existe desde o primeiro dia. É a queda de energia entre autorizar e confirmar no TEF; pede conferência humana |
| Fiscal | Uma NFC-e por venda com vários pagamentos (padrão) ou uma por pagante, quando cada um quer CPF (PP-20, QC-14) |

No MVP (PDV-03) a venda de balcão já aceita várias formas no mesmo pagamento. A divisão por pessoa e por item entra com as comandas (MESA-04), sobre a mesma estrutura de partes.

## 6. Cancelamento, devolução, estorno e troca

| Caso | Fluxo | Estoque | Financeiro | Fiscal |
|---|---|---|---|---|
| Cancelar item (venda aberta) | `pos.cancel_item` ou PIN de supervisor; motivo | — | — | — |
| Descartar venda aberta | Motivo se tinha itens; registrado | — | — | — |
| Cancelar venda paga (mesma sessão, dentro do prazo fiscal) | `pos.cancel_sale` + supervisor diferente do operador; aviso ao Proprietário | Estorno | Devolve na mesma forma: dinheiro sai do caixa; cartão é estornado na maquininha e registrado | Cancelamento da NFC-e. O prazo é curto e varia por UF (QC-02) |
| Devolução (depois) | `Requested` → `Approved` (`pos.return`) → `Completed`. Localiza a venda pelo cupom ou pelo CPF. Prazo configurável por loja | Entrada de devolução, ou perda se avariado | Dinheiro, **vale-troca** (padrão) ou estorno de cartão registrado | NF-e de devolução, a confirmar com a contadora (QC-15). Não fiscal: recibo |
| Troca | Devolução + venda nova, ligadas | As duas | A diferença é paga ou vira vale | As duas |
| Estorno de cartão (maquininha externa) | Só registro: o operador estorna na maquininha e informa o NSU | — | Baixa o título da adquirente | — |

Devolução:

```
Requested ──aprovar (pos.return)──► Approved ──reembolso + estoque──► Completed
    └──────────────── recusar ──────────────► Rejected
```

Devolução, vale-troca e fiado dependem de dado do servidor e **não funcionam sem rede**.

## 7. Documento fiscal

`fiscal_status` da venda; o documento em si fica em `fiscal.outgoing_invoices`.

```
NotRequired                                   (loja não fiscal, ou pos.skip_fiscal)
Pending ──autorizada──► Authorized ──cancelamento no prazo──► Cancelled
   │  └──SEFAZ fora, Emissor em contingência──► AuthorizedInContingency ──transmitida──► Authorized
   └──rejeitada──► Rejected ──corrigir e reemitir (fila da retaguarda)──► Pending
```

| Situação | O que acontece |
|---|---|
| Loja com modo `AlwaysNfce` | Toda venda pede NFC-e. Vender sem ela exige `pos.skip_fiscal` |
| Loja com modo `Ask` | O operador escolhe por venda. Quando o recibo não fiscal é permitido é pergunta da contadora, não escolha do operador (PP-05, QC-13) |
| Loja com modo `NeverFiscal` | Recibo não fiscal sempre, com o texto de "sem valor fiscal" |
| A SEFAZ demora | A venda **não espera** além de um teto (proposta do Emissor: 8 a 10 s; depois, `202`). Segue `Pending` e o cupom sai quando a autorização chegar, ou sai o recibo com a NFC-e por e-mail |
| Internet ok, SEFAZ fora | O Emissor emite em contingência e devolve XML assinado e QR; o caixa imprime o DANFE de contingência; a transmissão é do Emissor |
| **Loja sem internet** | **Em aberto: PP-01.** Recibo não fiscal agora e NFC-e depois, ou agente de loja que assina a contingência |
| Rejeição | Vai para a fila de correção da retaguarda; o caixa não trata |
| Cancelamento | Dentro do prazo legal, com `pos.cancel_sale` e PIN de supervisor (PP-11). Fora do prazo: NF-e de devolução (`NfePurpose.Return` já existe) |

Idempotência: `nfce-{saleId}-{tentativa}`, com hash do payload, igual à NF-e. Série por caixa; numeração do Emissor.

## 8. Sincronização offline

No aparelho, cada evento passa por:

```
Local (gravado no LocalStore) ──lote enviado──► Sent ──resposta do servidor──► Synced
                                                   ├──aceito com divergência──► Divergent  (vale; gera pendência)
                                                   └──em quarentena──► Quarantined (não se perde; revisão humana)
sem resposta ou erro de rede ──► continua Local; reenvio com o mesmo event_id
```

| Passo | Regra |
|---|---|
| Gravação | Toda ação grava primeiro no `LocalStore` e entra na fila de saída, com ou sem rede |
| Lote | `POST /api/v1/pos/sync/events`, assinado com a chave do aparelho (esquema `Device`). Funciona sem operador presente |
| Idempotência | `ON CONFLICT (event_id) DO NOTHING`. Reenviar não duplica |
| Sequência | `seq` por caixa + hash do evento anterior. Lacuna ou adulteração é denunciada |
| Resposta | Até qual `seq` foi confirmado e, por evento: aceito, aceito com divergência, em quarentena |
| **Venda concluída nunca é recusada** | Conflito vira pendência para conferência humana |
| Descida | Foto inicial + delta por cursor (`change_seq`): itens, códigos de barras, preços, operadores, formas de pagamento, impressoras e, na Fase 15, mesas |

Regras de conflito:

| Conflito | Regra |
|---|---|
| Estoque | A venda sempre vale (PP-02) |
| Preço | Vale o que o aparelho tinha. O evento leva o preço e a `price_list_version`; o servidor não reprecifica. Versão vencida além do limite vira divergência. Tabela nova entra com `valid_from`, para o aparelho baixar antes |
| Número da venda | Do aparelho. Um caixa tem um aparelho ativo por vez |
| Relógio | O instante do aparelho é informativo; o servidor grava `received_at`. A data fiscal é a do servidor. Diferença além da tolerância vira divergência |
| Aparelho revogado | A revogação só vale no próximo contato. Os eventos de aparelho revogado entram em quarentena; não são descartados |
| Janela máxima sem sincronizar | Depois dela o aparelho não abre turno. Proposta da arquitetura: 72 h. Piso de mercado: 7 dias. Decisão: PP-10 |
| Versão do app | O servidor aceita eventos de versões antigas por um prazo definido (`schema_version`). O app só atualiza na troca de turno |

Faixa fixa na tela de venda quando há fila: **"Sem conexão: N vendas a enviar"**, com a idade da mais antiga. Alerta quando a fila envelhece.
