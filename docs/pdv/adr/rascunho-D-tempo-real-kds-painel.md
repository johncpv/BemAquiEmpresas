# Rascunho D — Pedidos em tempo real: KDS e painel do cliente

> Rascunho de ADR do [plano do PDV](../README.md). "Conferido" é o que foi lido no código ou nas ADRs; "a validar" é conhecimento geral que precisa de spike ou da contadora antes de virar decisão. Os achados citados por número estão em [02-arquitetura.md](../02-arquitetura.md#1-achados-do-código-que-moldam-o-desenho).

- Status: **Proposto (rascunho)**, 15/11/2026. Ainda não numerado em `docs/adr`; vira ADR 0046 ou seguinte quando o PO aprovar ([como os rascunhos viram ADR](README.md)).
- Contexto: painel "Na cozinha / Pedidos prontos" e KDS por setor. Ver achados 7 e 8.

## Decisão proposta

1. **Pedido com tíquetes por setor.** Estados: Recebido → Em preparo → Pronto → Entregue. Senha curta por loja e dia; sem rede, cada caixa usa faixa própria, para não colidir.
2. **Painel do cliente = quiosque existente, com visão nova "pedidos".** Reaproveita integralmente a ADR 0030: `/tv#token`, cookie `__Host-bq_kiosk`, só GET, `KioskHub`, revalidação a cada 30 s, DTO com lista permitida. Mudanças:
   - escopo por loja;
   - grupo `{tenantId}:store:{storeId}:orders`;
   - `kiosk.create` da visão de pedidos exige `pos.view` em vez de `production.view`;
   - no painel, só a senha e, se o PO quiser, o primeiro nome.
3. **KDS = aparelho registrado, não quiosque.** Ele escreve ("pronto", "desfazer"), e o esquema `Kiosk` proíbe escrita por teste de arquitetura. Usa o esquema `Device` da [rascunho A](rascunho-A-offline.md), tipo KDS, com lista fechada de endpoints e `PosHub` próprio, grupos derivados das claims.
4. **Mensagem:** mantém o padrão de sinal com id e rebusca. Se o spike medir atraso visível, o `PosHub` passa a levar um corpo mínimo, sem dado pessoal.
5. **Envio depois do commit**, pelo helper `AfterCommit`, não pelo outbox: o intervalo de 5 s do outbox é lento demais para cozinha.

## Internet fora, dentro da loja

| Opção | A favor | Contra |
|---|---|---|
| **Degradar** | Simples; a cozinha continua recebendo pela impressora | KDS e painel param e mostram "Sem conexão" |
| Hub local no agente | KDS e painel seguem vivos | Página HTTPS não conecta em `ws://` de IP da LAN; exige certificado válido para nome que aponta para IP local, descoberta e segunda fonte de verdade |
| WebRTC entre aparelhos | Sem servidor local | A sinalização precisa de internet |

**Recomendação:** degradar na primeira versão. A impressora de cozinha é o canal garantido ([rascunho B](rascunho-B-impressao-perifericos.md) item 5). O KDS mostra "Sem conexão. Os pedidos estão saindo na impressora." Recomendar 4G de reserva no roteador da loja, que custa menos que o hub. Hub da loja só na F3, se houver demanda, depois de um spike de TLS na LAN.

## Consequências
- Loja que usa só KDS, sem impressora de cozinha, não tem modo sem internet. Isso precisa estar no material comercial.
- Uma instância de API sem backplane continua suficiente no início; o limite fica registrado.

## Notas da consolidação do plano
- **Stories:** KDS-01, KDS-02 e KDS-03, na Fase 15 ([backlog](../05-backlog.md)).
- Primeiro nome no painel: [PP-24](../06-pendencias-riscos.md). Restaurante sem rede: PP-23.
- **Adendo que este rascunho pede:** ADR 0030 (visão "pedidos", escopo por loja, permissão `pos.view` para criar o token).
- **Revisão de 15/11/2026.** A impressora de rede da cozinha depende da ponte (IMP-03); a KDS-01 passou a listar essa dependência.
