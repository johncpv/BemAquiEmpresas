# IMP-02 — Impressão direta ESC/POS e gaveta: gerador único em TypeScript, `WebDeviceBridge` por WebUSB, WebSerial e Bluetooth, queda automática para o navegador e abertura de gaveta registrada

| Campo | Valor |
|---|---|
| ID | IMP-02 |
| Título | Impressão direta ESC/POS e gaveta: gerador único em TypeScript, `WebDeviceBridge` por WebUSB, WebSerial e Bluetooth, queda automática para o navegador e abertura de gaveta registrada |
| Tamanho | M |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 11 — PDV balcão com cupom não fiscal e offline (marco MVP) |
| Requisitos | Pedido do PO de 15/11/2026 (item 7) · [Rascunho B](../adr/rascunho-B-impressao-perifericos.md) itens 1, 3, 6 e 10 |
| Dependências | IMP-01; SPK-01 concluído; PP-08 (se a ponte entra) |
| Bloqueia | IMP-03 (a ponte transporta os mesmos bytes) |
| Ordem de execução | 12 (Fase 11) |

## User story
Como **operador de caixa**, quero **que o cupom saia na hora, cortado, e que a gaveta abra sozinha na venda em dinheiro**, para **não esperar o diálogo de impressão a cada venda**.

## Contexto e referências
- A impressão pelo navegador (IMP-01) abre diálogo, não corta nem abre gaveta de forma confiável.
- O que funciona em cada plataforma sai do SPK-01. Se o spike mostrar que no Windows nada disto é simples, a ponte (IMP-03) sobe para o MVP e esta story cobre o Android.

## Critérios de aceite
1. **Dado** o modelo de recibo da IMP-01, **quando** o gerador ESC/POS o converte, **então** saem bytes para 32, 42 e 48 colunas, com acentos na página de código do perfil, QR Code nativo, corte e, quando pedido, o pulso da gaveta. Um só gerador, em TypeScript, usado por qualquer adaptador.
2. **Dado** um aparelho Android com impressora Bluetooth ou USB, **quando** imprimo pelo `WebDeviceBridge`, **então** o cupom sai sem diálogo e é cortado.
3. **Dado** o Windows e um caminho direto apontado como viável pelo SPK-01, **quando** imprimo, **então** funciona como no CA 2; onde não for viável, o assistente diz "Nesta impressora, a impressão direta precisa do BemAqui Ponte." e mantém o navegador.
4. **Dado** a permissão do navegador ao dispositivo, **quando** instalo a impressora, **então** o assistente da IMP-01 ganha o passo "Autorizar a impressora", e a autorização é lembrada nas próximas vezes.
5. **Dado** a falha do caminho direto (dispositivo desconectado, permissão perdida), **quando** imprimo, **então** o app cai sozinho para o `BrowserPrintBridge` e avisa uma vez "Impressão direta indisponível. Usando a impressão do navegador.".
6. **Dado** uma venda com pagamento em dinheiro, uma sangria ou um suprimento, **quando** concluo, **então** a gaveta abre pelo pulso da impressora; em venda sem dinheiro, não abre.
7. **Dado** `pos.open_drawer`, **quando** aciono "Abrir gaveta" sem venda, **então** informo o motivo, a gaveta abre e o evento fica registrado; sem a permissão, "Chamar supervisor".
8. **Dado** o relatório de fechamento, **quando** o gerente o vê, **então** ele traz a contagem de aberturas de gaveta sem venda no turno.
9. **Dado** a lista de impressoras homologadas, **quando** abro a ajuda, **então** vejo modelo, conexão, plataforma e o perfil a escolher, como saiu do SPK-01.

## Regras de negócio e técnicas
- Os adaptadores só transportam bytes; nenhum conhece o conteúdo do recibo.
- Falha na impressora nunca trava a venda.
- A lista de modelos homologados é contrato de suporte e tem dono (PP-19).

## Permissões, eventos e tabelas
- **Permissões:** `pos.open_drawer`.
- **Eventos do aparelho:** `DrawerOpened` (com motivo e se houve venda).
- **Tabelas:** `pos.printers` (perfil, colunas, corte, gaveta).

## Telas e estados
- **Instalar impressora:** passo "Autorizar a impressora"; teste com corte e gaveta.
- **Botão "Abrir gaveta"** no painel do caixa.
- **Ajuda:** `Ajuda/pdv-impressora.md` atualizada com a lista homologada.

## Plano de testes
- **Vitest:** gerador ESC/POS contra bytes esperados, por largura e perfil (CA 1); queda para o navegador (CA 5); regra da gaveta (CA 6).
- **Manual, com o kit de hardware:** CA 2, 3 e 6, anotados na ajuda.

## Fora do escopo
- Ponte, impressora de rede, varredura da porta 9100 (IMP-03).
- Comanda de produção por estação (KDS-01).
- Balança (IMP-05).

## Pontos em aberto
- Depende do resultado do SPK-01. PP-08: aceitar ou não o instalador da ponte.

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
