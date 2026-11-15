# Rascunho B — Impressão e periféricos

> Rascunho de ADR do [plano do PDV](../README.md). "Conferido" é o que foi lido no código ou nas ADRs; "a validar" é conhecimento geral que precisa de spike ou da contadora antes de virar decisão. Os achados citados por número estão em [02-arquitetura.md](../02-arquitetura.md#1-achados-do-código-que-moldam-o-desenho).

- Status: **Proposto (rascunho)**, 15/11/2026. Ainda não numerado em `docs/adr`; vira ADR 0046 ou seguinte quando o PO aprovar ([como os rascunhos viram ADR](README.md)).
- Contexto: térmica 58/80 mm ESC/POS por USB, rede e Bluetooth; DANFE NFC-e ou recibo; cozinha por setor; gaveta; leitor; balança; configuração simples.

## Opções

| Opção | A favor | Contra |
|---|---|---|
| Impressão do navegador (`window.print`, CSS 80 mm) | Funciona em qualquer lugar com driver instalado | Abre diálogo, não corta nem abre gaveta de forma confiável, lento |
| WebUSB, WebSerial ou Web Bluetooth direto | Sem instalar nada; bom em Android | Driver no Windows; não alcança impressora de rede; só Chromium |
| **Agente local (`ws://127.0.0.1`)** | USB pelo spooler em modo RAW, TCP 9100, serial, balança, gaveta; base para TEF e para o hub da loja | Instalador, assinatura de código, atualização própria |
| Impressora de rede pelo servidor | Nada | O servidor não alcança a LAN da loja; inviável |
| QZ Tray ou similar | Pronto | Licença comercial e certificado pago para impressão silenciosa; mesmo custo de instalação do agente; não resolve TEF |

## Decisão proposta

1. **Porta única `HardwareBridge` no front**, com três adaptadores escolhidos em tempo de execução:
   - `AgentBridge` (padrão no Windows);
   - `WebDeviceBridge` (WebUSB, WebSerial, Bluetooth; padrão no Android);
   - `BrowserPrintBridge` (último recurso).
2. **Agente "BemAqui Ponte"**: serviço Windows em .NET, escutando só em loopback, pareado com o aparelho registrado por código de uso único, com conferência de `Origin`. Precisa de assinatura de código: o próprio `CLAUDE.md` registra o Smart App Control bloqueando DLL sem assinatura.
3. **Um só gerador ESC/POS em TypeScript.** O recibo sai de um modelo estruturado (linhas, colunas 32/42/48, QR nativo, corte, gaveta) e vira bytes. O agente só transporta bytes.
4. **Dois modelos de comprovante:**
   - DANFE NFC-e: dados e QR devolvidos pelo Emissor. O aparelho não monta QR fiscal porque não tem o CSC nem o certificado.
   - Recibo não fiscal: com o texto "Não é documento fiscal".
   - Reimpressão é evento registrado.
5. **Cozinha por setor:** item → setor de produção → impressora(s). O pedido gera `print_jobs` com id idempotente. Quem imprime é a estação da loja (o agente ou o próprio caixa) e confirma. Sem internet, o caixa que lançou o pedido imprime direto pelo próprio agente na impressora de rede.
6. **Gaveta:** pulso pela impressora (ESC p), só em venda com dinheiro, sangria ou suprimento. Abertura avulsa exige permissão e vira evento.
7. **Leitor:** modo teclado, sem API. O app captura por tempo entre teclas e sufixo Enter.
8. **Balança:**
   - Etiqueta EAN-13 com prefixo 2 é só software: formato configurável por loja (dígitos do código, peso ou preço).
   - Leitura direta no caixa: serial, pelo agente ou por WebSerial; protocolos por perfil (Toledo, Filizola; a validar).
9. **Configuração simples:** assistente em três passos.
   - "Procurar impressoras": o agente lista o spooler, USB e varre a porta 9100 na sub-rede.
   - Escolher o perfil por modelo, de uma lista curada.
   - "Imprimir teste" com régua de colunas, acento, QR, corte e gaveta.
   - A configuração fica no servidor, por caixa, e desce na sincronização.
10. **Reserva:** falha na impressora não trava a venda. Oferece reimprimir, outra impressora, enviar por WhatsApp ou e-mail (ADR 0041), ou `window.print`.

## Consequências
- A lista de modelos homologados é contrato de suporte e precisa de dono.
- Android com impressora de rede só funciona com a casca nativa.

## Notas da consolidação do plano
- A análise de produto colocava o agente local só na fase de supermercado. Este rascunho o trata como padrão no Windows. O plano adota o meio-termo: o MVP sai com a porta `HardwareBridge` e os adaptadores de navegador ([IMP-01](../stories/IMP-01-cupom-nao-fiscal-impressora.md), [IMP-02](../stories/IMP-02-impressao-direta-escpos-gaveta.md)); a ponte é a IMP-03, na Fase 12. **Se o [SPK-01](../stories/SPK-01-perifericos-hardware-real.md) mostrar que a impressora USB no Windows não funciona de forma simples pelo navegador, a IMP-03 sobe para o MVP.**
- Decisão do PO sobre aceitar um instalador assinado: [PP-08](../06-pendencias-riscos.md). Lista de hardware homologado: PP-19.
- O texto do recibo não fiscal aparece nos insumos de duas formas ("SEM VALOR FISCAL" e "Não é documento fiscal"). O PO fixou "Não é documento fiscal" em 15/11/2026 (PP-05).
