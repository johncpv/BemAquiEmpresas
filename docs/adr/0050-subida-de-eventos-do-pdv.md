# ADR 0050: Subida dos eventos do caixa (OFF-02): registro imutável, cadeia de hash, conferência no servidor e quarentena

- Status: Aceito (19/11/2026), com a OFF-02. Fecha os pontos em aberto da story com as propostas dela (suporte a duas versões do formato; tolerância de relógio de 2 min da ADR 0026) e registra as escolhas de implementação.
- Contexto: o caixa vende sem rede (local-first, rascunho A) e guarda tudo numa fila de saída. A subida precisa gravar cada evento uma vez só, mesmo com reenvios em paralelo, nunca recusar uma venda concluída e não confiar no aparelho (requisitos de segurança da PDV-02, PDV-03, CX-01 e IMP-01). O `IdempotencyFilter` não serve (o escopo inclui o usuário e não há usuário).

## Decisão

### 1. Protocolo
- `POST /api/v1/pos/sync/events`, esquema `Device` (ADR 0048), **único endpoint com `RequireDevice(allowRevoked: true)`**: o aparelho revogado ou substituído continua subindo o que gravou, e tudo dele entra em quarentena. Sem operador com sessão. Loja, caixa e época vêm do principal; o que o evento diz só é conferido.
- Cada evento leva `eventId`, `deviceId`, `registerId`, `deviceEpoch`, `seq` (contínuo por caixa e época, a partir de 1), `schemaVersion`, `kind` (`pos.sale`, `pos.cash`, `pos.print`), `occurredAt` e `payload` **em texto**, `prevHash` e `hash`. O hash é SHA-256 (hex) de `v1\n{eventId}\n{deviceId}\n{registerId}\n{deviceEpoch}\n{seq}\n{schemaVersion}\n{kind}\n{occurredAt}\n{prevHash}\n{SHA-256 hex do payload}` (`PosEventChain` e `event-chain.ts`, com o mesmo vetor de teste dos dois lados).
- O aparelho **sela** o evento na primeira tentativa de envio (seq, hash e o texto do payload ficam gravados no evento da fila); o reenvio manda exatamente o mesmo evento. O CPF/CNPJ sobe num bloco `documents` à parte e o nome do cliente não sobe.
- Resposta: por evento, `Accepted`, `AcceptedWithDivergence` ou `Quarantined` (todos guardados: o aparelho tira da fila), e o `seq` contíguo confirmado por época.

### 2. Gravação
- `pos.device_events`: chave no `event_id`, `INSERT … ON CONFLICT (event_id) DO NOTHING`, **imutável por trigger** (UPDATE, DELETE e TRUNCATE falham), não auditado (ele é a trilha), payload em `text` (o hash continua conferível). O texto que parece número de cartão é apagado do payload **antes** de gravar, e o evento vai para a quarentena.
- Cada evento é gravado e projetado numa **transação curta própria**, em série por caixa (`pg_advisory_xact_lock` do caixa), na ordem do `seq`. A projeção roda depois de um ponto de salvamento: falha depois de gravar volta ao ponto, e o evento fica guardado em quarentena. Isso também resolve a corrida entre a sangria e o fechamento da mesma sessão (requisito da CX-01).

### 3. Conferência e quarentena
- **Quarentena** (o evento fica só no registro, sem efeito): aparelho revogado ou substituído, caixa ou loja desativados, evento que diz ser de outro aparelho/caixa/época, sequência quebrada (lacuna, seq repetido ou hash que não fecha; vale "a partir dali" enquanto a pendência de sequência do anterior estiver aberta), versão do formato fora do suporte (**suportadas: a atual e a anterior**), dado de cartão, venda que não passa no replay, operador sem sessão por PIN naquele aparelho naquela hora, permissão que a pessoa não tinha no instante, loja sem o modo não fiscal aceito na versão vigente do texto no instante da venda (pelo histórico da auditoria da loja), sessão de caixa desconhecida, de outro caixa ou fechada, segunda abertura e horário fora da janela.
- **Divergência aceita** (o efeito acontece): preço desatualizado (vale o preço do aparelho; o limite é a versão vigente da tabela), relógio do aparelho a mais de **2 min** do servidor (medido pelo instante assinado do envio; as duas datas ficam na venda e o financeiro do MVP usa a do servidor, `CompletedAt` = instante do aparelho corrigido pelo desvio), soma dos pagamentos diferente do total, pagamento `Unknown`/`Pending` sem captura e sangria acima do dinheiro calculado da sessão (o `aboveExpected` do aparelho é só indício; o servidor recalcula fundo + dinheiro das vendas da sessão + suprimentos − sangrias, com `pos.session_sales`).
- O **replay da venda** roda no servidor a partir dos eventos (`PosSaleReplay`), sem usar os totais do aparelho: remoção do último item e desfazer do último lançamento só para o último lançamento de fato e com `pos.stores.free_remove_last` (a regra da loja, não a do aparelho); outra remoção ou redução exige `authorizedBy` com `pos.cancel_item` no instante; estorno de pagamento capturado exige `authorizedBy` com `pos.cancel_sale`; máquina de estados por `tenderId`; troco = recebido − valor; valor nunca acima do que faltava; total do `PaymentStarted` igual ao das linhas; campos de integração vazios no manual; forma, tipo, parcelas e bandeira conferidos no modelo de leitura do caixa.
- **Liberar da quarentena** (com `sales.approve` e motivo, auditado) projeta o evento sem refazer a conferência que o segurou; a pendência fecha na mesma transação do efeito.

### 4. Operador e permissão no instante
- **Sessão por PIN:** a família de refresh tokens `PosPin` do usuário no aparelho, do primeiro token à primeira revogação ou ao fim das 12 h, com a folga de 2 min (`IPosOperatorHistory.HadPinSessionAsync`).
- **Permissão no instante:** `identity.permission_snapshots`, um retrato por usuário e versão de permissão (`perm_version`), gravado quando a versão é avaliada pela primeira vez e logo depois de cada mudança de grupo ou vínculo (`BumpAsync`). Vale o retrato com `valid_from` mais recente antes do instante; sem retrato anterior, o mais antigo conhecido. Limite aceito: mudança feita por fora do `BumpAsync` passa a valer no retrato quando for avaliada (o próximo request de quem olhar a pessoa). A sessão por PIN do supervisor que autoriza no caixa chega com a PDV-04; até lá, quem autoriza é conferido só pela permissão.

### 5. Dado pessoal
- `sales.sale_customer_documents`: CPF/CNPJ cifrado com o key ring (propósito com o id da linha, `SecretKind.CustomerDocument`) e **índice cego** HMAC-SHA256 com a chave do tenant (`sales.blind_index_keys`, cifrada, `SecretKind.BlindIndexKey`). Sem chave estrangeira para a venda: o documento é guardado mesmo com a venda em quarentena. Fora dos eventos, do log e do outbox; apagado em 5 anos (job de expurgo, `[PersonalDataPurge]`), o que não quebra a cadeia de hash.

### 6. Aviso de caixa sem enviar vendas
- "Caixa 02 sem enviar vendas há N horas." para quem tem `sales.approve`, uma vez por caixa e por período: quando o aparelho faz contato e o evento mais antigo da fila passou do aviso da loja (`pos.stores.sync_alert_hours`, padrão 4 h), ou pelo job de 30 em 30 minutos, para caixa com sessão aberta e sem contato.

## Consequências
- O caixa nunca perde venda por regra de servidor: o pior caso é a quarentena, visível em "Pendências do PDV".
- O Fiscal (QC-17), o estoque (PDV-05) e o fechamento (CX-02 e CX-03) reagem pelo outbox, como antes.
- Aparelhos com o app anterior a esta story mandam remoção autorizada sem `authorizedBy`: essas vendas caem em quarentena até atualizar (o PDV ainda não está em produção).
- Particionamento de `device_events` fica para depois (fora do escopo da story).
