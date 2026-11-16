# PDV-01 — Lojas, caixas e aparelho de PDV registrado: módulo `pos` no catálogo, aparelho com tipo, loja, caixa e época, registro com TOTP recente, chave do aparelho e esquema de autenticação `Device`

| Campo | Valor |
|---|---|
| ID | PDV-01 |
| Título | Lojas, caixas e aparelho de PDV registrado: módulo `pos` no catálogo, aparelho com tipo, loja, caixa e época, registro com TOTP recente, chave do aparelho e esquema de autenticação `Device` |
| Tamanho | G |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 11 — PDV balcão com cupom não fiscal e offline (marco MVP) |
| Requisitos | Pedido do PO de 15/11/2026 (itens 4, 5 e 8) · [Rascunho A](../adr/rascunho-A-offline.md) item 9 · [Rascunho F](../adr/rascunho-F-escala-hospedagem.md) modelo multi-loja · ADR 0026 (aparelho e PIN) · [Modelo de dados](../03-modelo-dados.md#4-pos--schema-pos) |
| Dependências | PERF-01, CAT-01, PAG-01, VND-01, PERF-04; PP-04, PP-11 e PP-12 decididas |
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

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
