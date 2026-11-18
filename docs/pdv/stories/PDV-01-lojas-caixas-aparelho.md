# PDV-01 — Lojas, caixas e aparelho de PDV registrado: módulo `pos` no catálogo, aparelho com tipo, loja, caixa e época, registro com TOTP recente, chave do aparelho e esquema de autenticação `Device`

| Campo | Valor |
|---|---|
| ID | PDV-01 |
| Título | Lojas, caixas e aparelho de PDV registrado: módulo `pos` no catálogo, aparelho com tipo, loja, caixa e época, registro com TOTP recente, chave do aparelho e esquema de autenticação `Device` |
| Tamanho | G |
| Status | Review: parte 1 em produção (18/11/2026); parte 2 (CA 11 e CA 12) pronta em 18/11/2026 na branch `feat/pdv-01b-pin-operacao` |
| Fase | 11 — PDV balcão com cupom não fiscal e offline (marco MVP) |
| Requisitos | Pedido do PO de 15/11/2026 (itens 4, 5 e 8) · [Rascunho A](../adr/rascunho-A-offline.md) item 9 · [Rascunho F](../adr/rascunho-F-escala-hospedagem.md) modelo multi-loja · ADR 0026 (aparelho e PIN) · [Modelo de dados](../03-modelo-dados.md#4-pos--schema-pos) |
| Dependências | PERF-01, CAT-01, PAG-01, VND-01, PERF-04; PP-04 (16/11), PP-11, PP-12, PP-09 e PP-35 (18/11/2026) decididas |
| Bloqueia | OFF-01, CX-01 e todo o resto da Fase 11 |
| Paridade com o mercado | Requisitos mínimos 27 (lojas) do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | 1 (Fase 11) |

## User story
Como **gerente de loja**, quero **cadastrar a loja, os caixas e os aparelhos que podem vender**, para **cada venda sair de um caixa conhecido e um aparelho roubado poder ser bloqueado**.

## Contexto e referências
- Aparelho registrado + PIN de 6 dígitos + `[PinSessionAllowed]` já existem para o chão de fábrica (ADR 0026). Hoje o teto é de 10 aparelhos por tenant, o segredo gira a cada entrada por PIN e o PIN é recusado para quem tem `*.approve`.
- Esta story fecha, com a CAT-01 e a VND-01, a ADR das fronteiras dos módulos, e escreve a ADR do usuário de operação (PP-12). A fronteira proposta está em [02, seção 4.1](../02-arquitetura.md#41-fronteira-sales--pos-quem-é-dono-de-quê): loja, caixa e aparelho são do `Pos` e saem em `/api/v1/pos/…`; o módulo `sales` (da VND-01) é o código de permissão das telas de retaguarda.
- **Re-estimada de M para G** pela revisão independente de 15/11/2026: um módulo novo, o esquema `Device`, a generalização dos aparelhos, o usuário de operação e duas ADRs não cabem em M. Se a PP-12 for aprovada, o usuário de operação (CA 12) pode virar story própria.

## Critérios de aceite
### Módulos e cadastros
1. **Dado** o `ModuleCatalog`, **quando** a aplicação sobe, **então** existe `pos` (tela dedicada: `view`, `create`, `discount`, `change_price`, `cancel_item`, `cancel_sale`, `return`, `cash_out`, `cash_in`, `open_drawer`, `skip_fiscal`, `manage_devices`), ao lado do `sales` que a VND-01 criou, e os grupos do seed do Comércio recebem as ações previstas no [PRD](../01-PRD.md#63-permissões-novas).
2. **Dado** `sales.create`, **quando** crio uma loja, **então** informo código curto (único, entra no número da venda), nome, local de estoque e tabela de preço; a loja "Loja" do seed já vem ligada ao local "Loja" e à tabela "Balcão".
3. **Dado** uma loja, **quando** crio um caixa, **então** informo o número, a conta do tipo Caixa e o modo de tela (Toque ou Teclado/leitor); dois caixas não usam a mesma conta Caixa nem o mesmo número na loja.

### Aparelho
4. **[Segurança]** **Dado** `pos.manage_devices`, login completo **e TOTP confirmado nos últimos minutos** (mesma regra das ações sensíveis da ADR 0008), **quando** registro um aparelho, **então** escolho o tipo "Caixa", a loja e o caixa; o aparelho gera um par de chaves ECDSA não exportável e envia a chave pública; o registro fica auditado e **o Proprietário recebe um aviso** ("Novo caixa registrado na loja X por Fulano."). Sem TOTP recente, a tela pede o código antes.
5. **Dado** os aparelhos de fábrica existentes, **quando** a migration roda, **então** todos ficam com o tipo "Fábrica" e continuam funcionando sem mudança.
6. **Dado** um caixa que já tem aparelho ativo, **quando** registro outro aparelho nele, **então** o sistema pede confirmação ("O caixa 02 já tem um aparelho. Trocar?"), desativa o anterior e o novo começa uma **época nova** do caixa, com a sequência de vendas em 1. O número da venda leva a época (`{loja}-{caixa}.{época}-{seq}`), então as vendas que o aparelho antigo ainda não tinha enviado sobem depois sem colidir com as do novo, nem na chave nem no número.
7. **Dado** o teto de aparelhos, **quando** registro, **então** o limite é contado por tipo; o limite de 10 dos aparelhos de fábrica não muda.
8. **Dado** um aparelho revogado ou substituído, **quando** ele faz o próximo contato, **então** perde a descida do catálogo e a entrada por PIN (401, e a tela pede novo registro), mas **o endpoint de subida continua aceitando os eventos que ele já tinha gravado**, assinados com a chave dele; esses eventos entram em quarentena para revisão (OFF-02). Nenhuma venda se perde por causa da revogação.

### Autenticação do aparelho
9. **[Segurança]** **Dado** o esquema `Device`, **quando** os testes de arquitetura rodam, **então** só os endpoints marcados com o atributo próprio, todos sob `/api/v1/pos/sync/`, aceitam esse esquema; qualquer outro endpoint com ele falha o teste.
10. **[Segurança]** **Dado** uma requisição `Device`, **quando** o servidor a processa, **então** a loja e o caixa vêm do principal do aparelho, nunca do corpo; um aparelho não lê nem envia dado de outra loja (teste de IDOR).
11. **Dado** um operador com `pos.create`, **quando** entra por PIN num aparelho do tipo Caixa com rede, **então** a sessão por PIN só alcança os endpoints do PDV na lista `[PinSessionAllowed]`.
12. **Dado** a PP-12 aprovada e `identity.create`, **quando** crio um "usuário de operação", **então** ele não tem e-mail nem senha, só entra por PIN em aparelho registrado e não acessa o backoffice.

## Regras de negócio e técnicas
- Um caixa tem um aparelho ativo por vez.
- O segredo do aparelho do tipo Caixa gira a cada sincronização bem-sucedida, não a cada PIN. Para o tipo Fábrica nada muda.
- Renomear `identity.shopfloor_devices` ou só acrescentar colunas é decisão desta story, registrada na ADR; em qualquer caso a migration é aditiva.
- Loja com CNPJ próprio (estabelecimento) fica para a NFCE-07.

## Permissões, eventos e tabelas
- **Permissões:** todas as do `sales` e do `pos` (novas, por seed).
- **Endpoints:** `GET|POST|PUT /api/v1/pos/stores`, `.../stores/{id}/registers` (autorizados com `sales.*`, por serem telas de retaguarda), `GET|POST|DELETE /api/v1/identity/devices` (com tipo), `POST /api/v1/pos/sync/handshake`.
- **Tabelas:** `pos.stores`, `pos.registers` (com `device_epoch`); colunas novas em aparelhos (`kind`, `store_id`, `register_id`, `public_key`, `last_sync_at`).
- **Auditoria:** loja, caixa e aparelho.

## Telas e estados
- **Loja → Lojas e caixas** (`/app/loja/lojas`): lista de lojas com os caixas e o aparelho de cada um (último contato, chip "Sem contato há N h"). Vazio: "Nenhuma loja ainda." com "Nova loja".
- **Aparelhos** (tela existente): coluna "Tipo" e filtro por loja.
- **`/pdv` em aparelho não registrado:** "Este aparelho ainda não é um caixa." com "Registrar este aparelho" (pede login completo).
- Carregando e erro em todas.
- **Ajuda:** `Ajuda/loja-lojas-caixas.md` e `Ajuda/pdv-registrar-aparelho.md` (novas); a ajuda de Aparelhos atualizada.

## Plano de testes
- **Integração (Testcontainers):** backfill do tipo Fábrica (CA 5); registro sem TOTP recente é recusado e o aviso sai (CA 4); troca de aparelho com vendas pendentes no antigo, sem colisão (CA 6); aparelho revogado ainda sobe e não desce (CA 8); escopo por loja e IDOR (CA 10).
- **Arquitetura:** lista fechada do esquema `Device` (CA 9).
- **Vitest:** telas de lojas e de registro.

## Fora do escopo
- App `/pdv` e armazenamento local (OFF-01).
- PIN conferido sem rede (OFF-03).
- KDS, garçom e estação de impressão (tipos previstos, usados nas Fases 12 e 15).

## Pontos em aberto
- PP-12: sem o usuário de operação, o CA 12 sai e o operador de caixa precisa de e-mail (apelido).
- PP-17: teto de aparelhos por plano.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade e de integração (Testcontainers) passando; Vitest no front.
- [ ] Migration criada; permissões só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio e erro; ajuda escrita ou atualizada em `Ajuda/` no mesmo commit.
- [ ] Padrões e próximo passo propositivos avaliados; auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; story marcada no backlog; decisões em ADR.

## Implementação (parte 1, 18/11/2026)

Desenho em [ADR 0048](../../adr/0048-aparelho-do-pdv-e-esquema-device.md) (fronteiras, aparelho com tipo, esquema `Device`, época) e [ADR 0049](../../adr/0049-usuario-de-operacao.md) (usuário de operação, PP-12). Adendos nas ADRs 0008, 0026 e 0046.

| CA | Situação | Como |
|---|---|---|
| 1 | Feito | `pos` no `ModuleCatalog` (área Loja, fase 11, as 12 ações), só no perfil Comércio. Grupos do Comércio: Gerente de loja com `sales` (inclusive `approve`), `catalog` (com `edit_price`) e o `pos` menos `skip_fiscal`; Operador de caixa com `pos.view` e `pos.create`; grupo novo Fiscal de caixa com as restritas do balcão. Grupos já criados recebem pelo seed patch `pos-retail-groups`. Planos já gravados recebem `pos` e `sales` (migration `PosModuleInPlans` do catálogo) |
| 2 | Feito | `pos.stores` (código 2 a 6 letras ou números, único), local de estoque (`ILocations`) e tabela de preço (`IPriceLists`, novo no Catálogo). Pacote `pos.retail`: loja "Loja" (`LOJA`) no local "Loja" e na tabela "Balcão", com o caixa 01 na conta "Caixa PDV 01" |
| 3 | Feito | `pos.registers`: número 1 a 99 único na loja, conta do tipo Caixa (`ICashAccounts`, novo no Financeiro) única entre os caixas, modo Toque ou Teclado e leitor |
| 4 | Feito | `POST /api/v1/identity/devices` com `pos.manage_devices`, login completo e `RequireRecentTwoFactor` (403 `two_factor_required` abre o "Confirme o código"); chave ECDSA P-256 não exportável gerada no navegador, só a pública vai; auditoria `DeviceRegistered` e aviso "Novo caixa registrado na loja X por Fulano." |
| 5 | Feito | `kind` com padrão `Factory` na migration; o `/fab` só enxerga `Factory` |
| 6 | Feito | 409 `register_has_device` ("O caixa 01 já tem um aparelho (…). Trocar?"); com `replaceActive`, o anterior é revogado (`replaced`) e a época sobe, numa transação. `PosSaleNumber`: `{loja}-{caixa}.{época}-{seq}` |
| 7 | Feito | Teto por tipo: os 10 da fábrica contam só `Factory`; caixa tem um aparelho por vez. Teto por plano: PP-17 (pendente) |
| 8 | Parcial | Revogado ou substituído: aperto de mão 401 `device_revoked`; o esquema reconhece o revogado (status `Revoked`) e `RequireDevice(allowRevoked: true)` existe para a subida, mas o endpoint de subida e a quarentena são da OFF-02. A entrada por PIN do caixa é o CA 11 |
| 9 | Feito | Esquema `Device` (cookie `__Host-bq_pos` + assinatura ECDSA do método, caminho, instante e hash do corpo); `EndpointAuthorizationTests` reprova o esquema fora de `/api/v1/pos/sync/*` |
| 10 | Feito | Loja e caixa vêm do principal (`POST /api/v1/pos/sync/handshake`); teste com query de outra loja, chave trocada, segredo de outro aparelho e instante vencido |
| 11 | Feito (parte 2) | Ver "Implementação (parte 2)" abaixo |
| 12 | Feito (parte 2) | Ver "Implementação (parte 2)" abaixo |

Telas: **Loja → Lojas e caixas** (`/app/loja/lojas`, módulo `sales`), **/pdv** ("Este aparelho ainda não é um caixa." e o registro) e a lista de aparelhos de Configurações → Segurança com a coluna Tipo e o filtro por loja só no Comércio (recurso `settings.pos_devices`; a fábrica vê como antes). Ajuda: `loja-lojas-caixas.md`, `pdv-registrar-aparelho.md` e `configuracoes-seguranca-caixas.md` (só Comércio; a ajuda de Segurança da fábrica não mudou).

**Revisão de segurança do PR #227 (18/11/2026), corrigido na mesma branch:** M1 (segredo anterior aposentado; reuso revoga com aviso), B1 (rotação com UPDATE condicional), B2 (aparelho achado antes de ler o corpo; 413 acima de 4 MB), B5 (`device_clock_skew` e "Acerte o relógio deste aparelho" no `/pdv`), B6 (caixa revogado só com `pos.manage_devices`), B8 (`Down` da `PosModuleInPlans` apaga `sales`), B10 (curva P-256 pelo OID) e o teste de aparelho de um tenant no host de outro. **Anotado para depois:** B3, idempotência por (`register_id`, `device_epoch`, `seq`), obrigatória na OFF-02; B4, canonicalização do caminho assinado com `RawTarget`; B7, a chave antiga é sobrescrita antes do POST do registro; B9, CSP e origem próprias para o `/pdv`.

Fora desta parte, além dos CA 11 e 12: o `Pos` não avisa o Financeiro quando a conta de um caixa é desativada (o `IBankAccountUsageProvider` é interno do Financeiro).

## Implementação (parte 2, 18/11/2026): CA 11 e CA 12

Desenho nos adendos de 18/11/2026 da [ADR 0048](../../adr/0048-aparelho-do-pdv-e-esquema-device.md) (PIN no caixa) e da [ADR 0049](../../adr/0049-usuario-de-operacao.md) (como o usuário de operação foi implementado).

| CA | Como |
|---|---|
| 11 | **Entrada pelo aparelho, sem rota anônima nova:** `GET /api/v1/pos/sync/operators` (primeiro nome e inicial de quem pode entrar neste caixa) e `POST /api/v1/pos/sync/pin` usam o esquema `Device` (cookie e assinatura; o PIN vai dentro do corpo assinado), com o limite nomeado `pos-pin-login`. **PIN:** `identity.pos_pins`, um por usuário, PBKDF2 do `PasswordHasher` (o mesmo do PIN da fábrica), tempo uniforme, 5 erros seguidos bloqueiam 15 min, o 10º em 24 h apaga, 20 erros em 15 min no aparelho bloqueiam o caixa e avisam o Proprietário; perfil reconferido a cada entrada (`pos.create`, nenhuma ação reservada, loja do usuário de operação, funcionário ligado ativo). Tudo auditado (`PosPinLoginSucceeded`, `PosPinLoginFailed`, `PosPinLocked`, `PosPinErased`, `PosPinChanged`…), nunca o valor. **Sessão:** `amr = ["pin", "hwk"]`, família `PosPin` presa ao aparelho, 12 h, um operador por caixa; renova só com o cookie do caixa. **Lista fechada:** marcador `[PosPinSessionAllowed]` (`.AllowPosPinSession()`), separado do da fábrica; hoje `GetPosSession` (quem está no caixa) e `EndPosSession` (sair). O `PinSessionScopeMiddleware` confere tudo a cada request e responde 403 `pin_session_scope` fora da lista. **PIN de quem tem e-mail:** "PIN do caixa" em Meu perfil → Segurança (`/api/v1/pos/my-pin`, login completo e `pos.create`). Tela: `/pdv` registrado mostra os nomes, o teclado do PIN, a troca do PIN inicial, quem está no caixa e "Sair" |
| 12 | `identity.users.is_operation_user` e `operation_store_ids`; e-mail técnico `.invalid` no lugar do nulo (recomendação aceita, adendo da ADR 0049). `POST /api/v1/identity/operation-users` (nome, grupos, lojas, funcionário opcional e PIN inicial, trocado no primeiro uso), `PUT …/{id}` (nome e lojas) e `POST …/{id}/pin` ("Novo PIN"), todos com `identity.create`, só no Comércio (recurso `identity.operation_users`) e com o `pos` liberado. Grupo com ação reservada não aceita usuário de operação (criação, grupos do usuário, membros do grupo e matriz do grupo). Login por e-mail recusado, recuperação de senha e e-mail nunca saem para ele, e o `AuthService` não emite token de retaguarda para ele. Desativar (inclusive pelo desligamento do funcionário, USR-01) apaga o PIN do caixa e derruba a sessão; reativado, volta sem PIN. Tela de usuários: marca "Operação", "Sem e-mail · entra por PIN no caixa", 2FA "Não se aplica", "Novo usuário de operação", "Lojas" e "Novo PIN" |

**Fora desta parte:** o PIN de supervisor de balcão (PP-11, ADR 0048 §5) é da PDV-04, e o código de aprovação sem rede (PP-39) é da OFF-03; até lá, quem tem ação reservada (o Gerente de loja do seed tem `sales.approve`) não usa PIN no caixa. Saída por inatividade no caixa: não há regra na story; o `/pdv` mostra o fim absoluto da sessão. O seed não dá `identity.create` ao Gerente de loja: hoje quem cria o usuário de operação é o Proprietário ou quem tiver `identity.create` (decisão do PO, se o Gerente de loja deve criar).

Ajuda: `pdv-registrar-aparelho.md` (entrar por PIN), `acessos-usuarios-operacao.md` e `conta-seguranca-pin-caixa.md` (as duas só no Comércio, pelo recurso `identity.operation_users`). Migration `PosPinOperationUser` (só acréscimos).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
| 18/11/2026 | 1.0 | Parte 1 implementada (CA 1 a 10; CA 8 parcial, a subida é da OFF-02). Decisões do PO de 18/11/2026: PP-35, PP-11, PP-12 e PP-09. Recomendação aceita: a tabela de aparelhos mantém o nome e o segredo anterior do caixa vale com a assinatura (ADR 0048 §2). CA 11 e 12 na parte 2. | Claude (dev) |
| 18/11/2026 | 1.1 | Correções da revisão de segurança do PR #227 (M1, B1, B2, B5, B6, B8, B10 e o teste de outro tenant); B3, B4, B7 e B9 anotados para as próximas stories. | Claude (dev) |
| 18/11/2026 | 2.0 | Parte 2: CA 11 (PIN no caixa pelo esquema `Device`, sessão `PosPin` com `amr = ["pin", "hwk"]`, lista `[PosPinSessionAllowed]` própria, PIN do caixa em Meu perfil) e CA 12 (usuário de operação). Recomendações aceitas: e-mail técnico `.invalid` em vez de e-mail nulo; PIN do caixa por usuário, não por aparelho; quem tem `identity.create` também troca as lojas e dá PIN novo. PIN de supervisor (PP-11) fica na PDV-04. | Claude (dev) |
