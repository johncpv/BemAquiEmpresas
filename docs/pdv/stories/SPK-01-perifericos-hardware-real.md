# SPK-01 — Spike: periféricos em hardware real (impressoras USB, rede e Bluetooth, gaveta, leitor e balança) a partir do PWA em Windows e Android, comparando o navegador com um agente mínimo em loopback

| Campo | Valor |
|---|---|
| ID | SPK-01 |
| Título | Spike: periféricos em hardware real (impressoras USB, rede e Bluetooth, gaveta, leitor e balança) a partir do PWA em Windows e Android, comparando o navegador com um agente mínimo em loopback |
| Tamanho | M |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 10 — Fundação: perfil de negócio, catálogo, pagamentos, núcleo de vendas e spikes |
| Requisitos | Pedido do PO de 15/11/2026 (itens 4, 5 e 7) · [Rascunho A](../adr/rascunho-A-offline.md) (onde roda) · [Rascunho B](../adr/rascunho-B-impressao-perifericos.md) |
| Dependências | Kit de hardware comprado (PP-19) |
| Bloqueia | IMP-02; a decisão de a IMP-03 (ponte) entrar no MVP; PP-08 e PP-09 |
| Paridade com o mercado | Requisitos mínimos prepara 2, 21 e 23 do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | Fase 10, em paralelo (prazo fechado: 1 a 2 semanas) |

## Pergunta do spike
Como **arquiteto**, quero **provar em hardware real o que o navegador alcança e o que só um agente local alcança**, para **decidir onde o PDV roda e se o instalador da ponte entra no MVP, antes de escrever a tela de venda**.

## Contexto e referências
- O rascunho B lista limites de plataforma marcados "a validar": WebUSB, WebSerial e Web Bluetooth só existem em navegadores Chromium; no Windows a impressora USB fica presa ao driver de impressão; o navegador não abre TCP 9100; página HTTPS só fala com `ws://127.0.0.1`, e o Chrome recente pede permissão de rede local.
- O `CLAUDE.md` registra o Smart App Control do Windows bloqueando DLL sem assinatura. Um serviço local sem assinatura de código pode não instalar.
- Código do spike é descartável: fica em `spikes/` ou em branch própria, fora da `main` de produto.

## Critérios de aceite (o que o spike tem de responder, com evidência)
1. **Dado** o kit (2 impressoras de 80 mm, uma USB e uma de rede; 1 Bluetooth de 58 mm; gaveta; leitor USB; balança), **quando** o PWA de teste roda em Windows com Chrome e com Edge e em Android com Chrome, **então** o relatório traz uma matriz aparelho × plataforma × caminho (WebUSB, WebSerial, Web Bluetooth, agente, `window.print`) com "funciona", "funciona com passo extra" (qual) ou "não funciona".
2. **Dado** a impressora USB no Windows com o driver do fabricante, **quando** tento WebUSB, **então** fica registrado se pega sem trocar o driver e, se não, quais passos seriam pedidos ao lojista.
3. **Dado** um agente mínimo em .NET escutando só em `127.0.0.1`, **quando** o PWA em HTTPS conecta por WebSocket, **então** fica registrado: o pedido de permissão de rede local do Chrome (texto e quando aparece), o envio em modo RAW pelo spooler, o TCP 9100 para a impressora de rede e o pulso da gaveta.
4. **Dado** o agente sem assinatura de código e depois assinado, **quando** instalo num Windows com Smart App Control ativo, **então** fica registrado o que acontece em cada caso e o custo e o prazo do certificado de assinatura.
5. **Dado** um recibo de teste (régua de 32, 42 e 48 colunas, acentos, QR Code, corte), **quando** imprimo por cada caminho, **então** há foto do resultado e o tempo entre o comando e o papel sair.
6. **Dado** o leitor em modo teclado, **quando** bipo 50 códigos seguidos, **então** nenhum se perde nem se mistura com digitação, com a captura por tempo entre teclas e sufixo Enter.
7. **Dado** a balança, **quando** leio o peso por WebSerial e pelo agente, **então** fica registrado o protocolo do modelo testado e o que falta para outros (Toledo, Filizola).
8. **Dado** tudo acima, **então** o relatório fecha com três respostas diretas: (a) plataformas suportadas na primeira versão; (b) a ponte entra no MVP ou na Fase 12; (c) lista inicial de modelos homologados.

## Regras
- Prazo fechado. Passou de duas semanas, o spike para e relata o que achou.
- Nenhuma linha do spike vai para produção.
- O que não foi testado é dito como não testado.

## Entregáveis
- Relatório em `docs/revisoes/` com a matriz, as fotos e as três respostas.
- Rascunhos A e B corrigidos com o que foi medido.
- Proposta de decisão para PP-08 e PP-09.

## Fora do escopo
- TEF e SmartPOS (PP-18).
- Assistente de impressoras com varredura de rede (IMP-03).

## Pontos em aberto
- Quais modelos entram no kit (PP-19).

## Definition of Done
- [ ] Todas as perguntas respondidas com evidência, ou marcadas "não respondida", com o motivo.
- [ ] Relatório publicado em `docs/revisoes/`.
- [ ] Rascunhos de ADR e backlog corrigidos com o que foi medido.
- [ ] Código do spike fora da `main` de produto.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
