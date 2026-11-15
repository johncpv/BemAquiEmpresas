# 02 — Arquitetura do PDV (proposta)

Síntese dos seis [rascunhos de ADR](adr/README.md) e de como o PDV cabe no monolito modular. Planejamento apenas: nada foi alterado no código.

**Como ler:** "conferido" é o que foi lido no código ou nas ADRs em 15/11/2026. "A validar" é conhecimento geral (navegadores, SEFAZ, adquirentes) que precisa de spike ou da contadora antes de virar decisão. As estimativas são ordem de grandeza, não medição.

## 1. Achados do código que moldam o desenho

Os rascunhos de ADR citam estes achados pelo número.

| # | Achado (conferido) | Consequência |
|---|---|---|
| 1 | **O EmissorFiscal não emite NFC-e.** Ele a declara fora de escopo (`docs/plano-finalizacao.md:310`, `PLANEJAMENTO.md:18`). A biblioteca Unimake suporta. A contingência que existe lá é SVC, de NF-e. O `IFiscalEmitter` só tem NF-e 55 e NFS-e | Caminho crítico do PDV fiscal ([07](07-emissor-fiscal.md)) |
| 2 | **O motor de estoque proíbe saldo negativo:** `CHECK quantity_on_hand >= 0` e 409 `insufficient_stock` (ADR 0023) | Venda feita sem rede não pode ser recusada depois (PP-02) |
| 3 | **A numeração central não serve ao caixa.** O `NumberSequenceService` trava uma linha por tipo e ano até o commit e não existe sem rede. Cada movimento de estoque também pega um número `MOV` | Número da venda gerado no aparelho; baixa de estoque agregada |
| 4 | **O `IdempotencyFilter` não serve para sincronizar.** O escopo inclui o usuário: reenvio em outra sessão ou por outro operador dá 422 `idempotency_key_reused`. A retenção é de 30 dias | Protocolo próprio de sincronização, por `event_id` |
| 5 | **O PWA atual é o oposto de offline.** Um service worker só, com escopo `/fab`, que nunca guarda `/api/*`; `skipWaiting` e `clientsClaim` ligados. A ADR 0026 §6 adiou o sem rede (P-23), mas já deixou regras: fila no IndexedDB, chave por ação, relógio com tolerância de 2 min, validade de 72 h | App `/pdv` novo, com build e service worker próprios |
| 6 | **O PIN é conferido no servidor.** O segredo do aparelho gira a cada entrada por PIN, em cookie HttpOnly; sessão de 12 h; teto de 10 aparelhos por tenant; PIN recusado para quem tem `*.approve` | Adendo à ADR 0026 para PIN e supervisor sem rede |
| 7 | **O quiosque é só leitura.** O esquema `Kiosk` aceita apenas GET e o hub não tem método de cliente para servidor (ADR 0030, com teste de arquitetura) | Serve ao painel do cliente. O KDS escreve, então precisa de outro principal |
| 8 | **Tempo real roda numa instância só.** Sem backplane; o `KioskConnectionRegistry` fica em memória. As mensagens levam só o id e a tela rebusca pela API | Suficiente no início; limite registrado |
| 9 | **O Inter integrado é cobrança v3 (bolepix).** O `IBankBillingProvider` não tem Pix cobrança imediata. A ADR 0032 §4 recusa credencial com escopo de pagamento. mTLS, webhook e reconsulta (ADR 0031) são reaproveitáveis | Porta `IPixChargeProvider` nova e adendo à ADR 0032 |
| 10 | **Faltam conceitos de varejo.** Não há preço de venda, lista de preços, código de barras, loja, caixa nem recebível de cartão. A ADR 0027 assume um emitente por tenant, igual ao CNPJ do tenant | Módulos novos; filial com CNPJ próprio quebra a ADR 0027 (PP-15) |
| 11 | **Limite de chamadas ao Emissor:** 4 por segundo por processo, somando todos os tenants (ADR 0027, adendo de 27/10). Do lado do Emissor, 60 por minuto por cliente | Um supermercado no pico chega perto disso sozinho |
| 12 | **Backup com RPO de 24 h**, por `pg_dump`, numa VPS compartilhada com outros projetos | Não serve ao varejo (INFRA-01) |
| 13 | **O produto está definido como ERP de indústria sob encomenda** no `CLAUDE.md` e no PRD | PRD atualizado antes das stories (PP-37) |

O que já existe e se reaproveita (conferido):

| Já existe | Onde | Uso no PDV |
|---|---|---|
| Liberação de módulos por tenant, sem deploy | `TenantModuleService.ApplyPlanAsync` | O perfil de negócio é uma camada fina sobre isso |
| Multi-local, transferência e reservas no estoque | `Inventory.Contracts/Stock.cs` | CPD → loja; reserva do e-commerce |
| Conta do tipo `Cash`, transferências bancárias, conciliação (BNK-03b) | Finance | Caixa do PDV; sangria e suprimento; cartão conciliado pelo extrato |
| Aparelho registrado + PIN de 6 dígitos + `[PinSessionAllowed]` | ADR 0026 | Terminal de caixa, garçom, KDS |
| Token de quiosque só leitura com SignalR | ADR 0030, `KioskHub` | Painel "Na cozinha / Pedidos prontos" |
| Provisionamento em menos de 1 min, com seeds ordenados | `ProvisioningService`, `ITenantSeeder` | "Habilitar em minutos" |
| Padrão "A confirmar com a contadora" com "Marcar como conferido" | ADR 0042 | Regras fiscais e taxas que nascem do seed |
| PDF e exportação padronizados | ADR 0045 | Relatório de fechamento, listas |

Não conferido: a região da VPS (assumida nos EUA, como informado), o `OutboxProcessor` inteiro, as ADRs 0009 e 0019 além das referências, e o adaptador `EmissorFiscalHttpEmitter.cs` por inteiro.

## 2. Onde cada peça fica no monolito

```
Aparelho da loja (PWA /pdv)                 Servidor (monolito modular)                 Externos
┌──────────────────────────────┐            ┌───────────────────────────────────────┐
│ Tela de venda (React)        │            │ Pos        lojas, caixas, sessões,    │
│ LocalStore (IndexedDB)       │  eventos   │            device_events, sync,       │
│  ├ catálogo, preços, formas  │──em lote──►│            modelo de leitura do       │
│  ├ registro de eventos       │◄──delta────│            catálogo, impressoras      │
│  └ fila de saída             │            │ Sales      venda, partes, pagamentos, │
│ HardwareBridge               │            │            devolução, vale-troca      │
│  ├ AgentBridge ──ws 127.0.0.1│            │ Catalog    produto, preço, GTIN,      │
│  ├ WebDeviceBridge           │            │            ficha técnica              │
│  └ BrowserPrintBridge        │            │ Restaurant mesas, comandas, tíquetes  │
│ PaymentTerminal              │            │ Inventory  baixa agregada (contrato)  │
│  └ ManualTerminal            │            │ Finance    resumos, adquirente, Pix   │──► Banco Inter
└──────────────┬───────────────┘            │ Fiscal     IFiscalEmitter (NFC-e)     │──► EmissorFiscal ──► SEFAZ
               │                            │ Identity   aparelho com tipo, Device  │
     BemAqui Ponte (Windows)                │ Host       PosHub, KioskHub           │
     impressora, gaveta, balança, TEF       └───────────────────────────────────────┘
```

Regras do monolito que continuam valendo, sem exceção:
- Um projeto por módulo, cinco camadas, referência só a `*.Contracts`; nenhum módulo lê tabela de outro. O catálogo que desce para o aparelho vem de um **modelo de leitura do próprio `Pos`** (`pos.catalog_items`), alimentado por eventos do Catalog e do Inventory.
- Eventos entre módulos pelo outbox. Exceção já prevista no padrão: o aviso de tempo real para a cozinha usa o helper `AfterCommit`, porque o intervalo de 5 s do outbox é lento demais.
- Três camadas de acesso em todo endpoint. Os endpoints de sincronização entram numa lista fechada por atributo e teste de arquitetura, no modelo do `[PinSessionAllowed]`.
- Migrations só aditivas no Inventory e no Finance. Afrouxar o `CHECK` de saldo é migration com ADR (PP-02).

## 3. Síntese dos rascunhos

### A — Offline-first ([rascunho](adr/rascunho-A-offline.md))

- **Onde roda:** PWA instalado + agente local opcional. Um só código React. Windows com Chrome ou Edge e Android com Chrome. Casca Android fina (Capacitor) só quando entrar SmartPOS. iOS fora do caixa.
- **App `/pdv` separado**, com entrada Vite e service worker próprios. Atualização só na troca de turno, nunca com `skipWaiting` no meio de uma venda.
- **Local-first desde o primeiro commit:** a tela de venda nunca espera a rede, nem quando há rede.
- **Armazenamento:** IndexedDB (Dexie) atrás de uma porta `LocalStore`, com `navigator.storage.persist()` e Web Locks. SQLite-WASM em OPFS como alternativa se o SPK-03 mostrar busca lenta com 50 mil itens.
- **Dinheiro em centavos inteiros** no TypeScript; ids Guid v7 gerados no aparelho.
- **Subida:** eventos de venda imutáveis, em lote, por `POST /api/v1/pos/sync/events`. Cada evento leva `event_id`, `device_id`, `register_id`, `seq` por caixa, `schema_version`, instante do aparelho e hash do anterior. O servidor grava com `ON CONFLICT (event_id) DO NOTHING` e responde, por evento: aceito, aceito com divergência ou em quarentena.
- **Descida:** foto inicial + delta por cursor (`change_seq`), só da loja do aparelho.
- **Autenticação sem rede:** chave ECDSA não exportável por aparelho, lote assinado, esquema `Device` próprio. Verificador do PIN baixado só para os operadores da loja. Risco aceito: PIN de 6 dígitos com acesso físico ao aparelho é quebrável; por isso o que se faz sem rede é limitado.
- **Fases sem retrabalho:** F1 (piloto interno: vende com rede e não trava se cair) → F2 (primeira versão pública: abrir turno e PIN sem rede, delta de catálogo, tela de pendências) → F3 (comandas sem rede limitadas, opcional).

### B — Impressão e periféricos ([rascunho](adr/rascunho-B-impressao-perifericos.md))

- **Porta única `HardwareBridge`** no front, com três adaptadores: `AgentBridge` (padrão no Windows), `WebDeviceBridge` (WebUSB, WebSerial, Bluetooth; padrão no Android) e `BrowserPrintBridge` (último recurso).
- **Agente "BemAqui Ponte":** serviço Windows em .NET, só em loopback, pareado por código de uso único, com conferência de `Origin`. Precisa de assinatura de código.
- **Um só gerador ESC/POS em TypeScript.** O agente só transporta bytes.
- **Dois comprovantes:** DANFE NFC-e (dados e QR devolvidos pelo Emissor; o aparelho não monta QR fiscal) e recibo não fiscal.
- **Cozinha por setor:** item → setor → impressora(s), com `print_jobs` de id idempotente.
- **Leitor em modo teclado**, sem API. **Balança:** a etiqueta EAN-13 é só software; a leitura direta é serial, pelo agente ou por WebSerial.
- **Falha na impressora não trava a venda.**

Limites de plataforma a validar no SPK-01: WebUSB, WebSerial e Web Bluetooth só existem em navegadores Chromium; no Windows a impressora USB fica presa ao driver; o navegador não abre TCP 9100; página HTTPS só fala com `ws://127.0.0.1`.

### C — Pagamentos ([rascunho](adr/rascunho-C-pagamentos.md))

- **Máquina de estados única para o pagamento:** `Created → Pending → Captured → (Voided | Refunded)`, mais `Failed` e `Unknown`. `Unknown` existe desde o primeiro dia: é a queda de energia entre autorizar e confirmar no TEF.
- **Campos de TEF presentes desde já**, vazios no modo manual: parcelas, provedor, adquirente, bandeira, NSU, autorização, `txid`, id fim a fim, tipo de integração, quem confirmou.
- **Duas portas:** no aparelho, `PaymentTerminal` (`ManualTerminal` agora; `TefTerminal` e `SmartPosTerminal` depois); no servidor, `IPaymentProvider` (`ManualProvider` agora; `InterPixProvider` depois).
- **Pix dinâmico:** `IPixChargeProvider` no Finance, reaproveitando mTLS, webhook e reconsulta. Só online.
- **Split:** partes (por item, igual ou por valor), cada uma com um ou mais pagamentos.
- **Financeiro por resumo**, no fechamento do turno.

### D — Tempo real: KDS e painel ([rascunho](adr/rascunho-D-tempo-real-kds-painel.md))

- **Painel do cliente = quiosque existente** com a visão "pedidos" e escopo por loja.
- **KDS = aparelho registrado**, tipo KDS, com `PosHub` próprio e lista fechada de endpoints.
- **Sem internet, degradar:** a impressora de cozinha é o canal garantido; o KDS mostra "Sem conexão. Os pedidos estão saindo na impressora." Recomendar 4G de reserva. Loja que usa só KDS, sem impressora de cozinha, não tem modo sem internet; isso precisa estar no material comercial.

### E — Acoplamento fiscal ([rascunho](adr/rascunho-E-acoplamento-fiscal.md))

- **Venda e documento fiscal separados.** Estados do documento: `NotRequired`, `Pending`, `Authorized`, `AuthorizedInContingency`, `Rejected`, `Cancelled`.
- **`IFiscalEmitter` ganha `IssueNfceAsync`, `GetNfceAsync`, `CancelNfceAsync`.** O imposto continua calculado no servidor.
- **Idempotência** `nfce-{saleId}-{tentativa}` com hash do payload. **Uma série por caixa**, numerada pelo Emissor.
- **Item apto para o PDV** validado no cadastro, não na venda.
- **Loja sem internet:** ponto em aberto, com duas recomendações opostas ([PP-01](06-pendencias-riscos.md#pp-01--venda-sem-internet-em-loja-que-emite-nfc-e)).

### F — Escala, multi-loja e hospedagem ([rascunho](adr/rascunho-F-escala-hospedagem.md))

- **Ingestão em dois tempos:** o lote grava eventos e vendas numa transação curta e responde; estoque, financeiro e fiscal vêm depois, por consumidores idempotentes.
- **Estoque agregado:** uma perna por item, por caixa e janela de tempo, com rastreio até as vendas. Evita cerca de 400 mil movimentos por dia por loja grande (estimativa) e a disputa pela numeração `MOV` e pela linha do item que guarda o custo médio.
- **Sem auditoria por linha de venda:** o registro de eventos é a trilha.
- **Medir antes de particionar.**
- **Multi-loja:** estabelecimento (loja) como conceito novo; emitente fiscal por estabelecimento; local de estoque ligado à loja.
- **Hospedagem:** levar API, PostgreSQL e EmissorFiscal para São Paulo, em VPS dedicada, com WAL contínuo, antes da versão pública.

## 4. Onde os insumos discordavam e o que o plano adotou

| Tema | Análise de produto | Análise de arquitetura | Plano |
|---|---|---|---|
| Venda sem internet em loja fiscal | (a) cupom agora, NFC-e depois | (d) 4G + (a); a do Emissor recomenda agente com A1 | **Não decidido:** PP-01 |
| Saldo insuficiente na venda | Pendência, sem negativar | Negativo permitido em local de loja | **Não decidido:** PP-02 |
| Projetos de módulo | `Sales` único, com os códigos `sales` e `pos` | `Sales` + `Pos` | Dois projetos (PP-04) |
| Preço e código de barras | Módulo `Catalog` próprio | Preço no `Sales`, GTIN no item do Estoque | Módulo `Catalog` (PP-04) |
| Número da venda | `PDV{nn}-{seq}` | `{loja}-{caixa}-{seq}` | `{loja}-{caixa}-{seq}`, que não colide entre lojas |
| Chave de idempotência | `client_sale_id` por venda | Registro de eventos com `event_id` e cadeia de hash | Eventos; o id da venda é gerado no aparelho |
| Baixa de estoque | Por linha, no `SaleCompleted` | Agregada por item, caixa e janela | Fora do caminho do caixa, por consumidor idempotente; a agregação é confirmada no SPK-03 |
| Resumo para o financeiro | Um por sessão e forma | Um por dia, adquirente, bandeira, forma e vencimento | No fechamento da sessão, agrupado por adquirente, bandeira, forma e vencimento |
| Agente local | Fase de supermercado | Padrão no Windows | Fase 12 (IMP-03); sobe para o MVP se o SPK-01 mandar |
| Nomes | `terminals`, `cash_sessions` | `registers`, `shifts` | `registers` (caixa) e `cash_sessions` (sessão de caixa) |
| Janela sem sincronizar | Não citava | 72 h | 72 h fica abaixo do piso de mercado: PP-10 |

## 5. Os três spikes

Rodam em paralelo com a Fase 10, uma a duas semanas cada. São os que mais reduzem risco.

| Spike | O que prova | O que decide |
|---|---|---|
| [SPK-01](stories/SPK-01-perifericos-hardware-real.md) Periféricos em hardware real | Três impressoras comuns (USB, rede, Bluetooth), gaveta, balança e leitor, a partir do PWA em Windows e Android. WebUSB e WebSerial contra um agente mínimo em loopback, com o pedido de permissão de rede local do Chrome e a assinatura de código | Rascunhos A (onde roda) e B; se a ponte entra no MVP |
| [SPK-02](stories/SPK-02-nfce-ponta-a-ponta-homologacao.md) NFC-e de ponta a ponta em homologação | Modelo 65 no Emissor, CSC e QR, série por caixa, contingência quando a SEFAZ cai. Mede o tempo a partir da VPS atual e de uma máquina em São Paulo e confere restrição de IP. Leva as perguntas à contadora | Rascunho E e a hospedagem |
| [SPK-03](stories/SPK-03-nucleo-local-first-carga.md) Núcleo local-first sob carga | IndexedDB com 50 mil itens em tablet barato e PC antigo; registro de eventos e fila; lote com 20 caixas simulados; baixa agregada de estoque; queda de energia e aba fechada no meio da venda | Armazenamento e as regras do rascunho F |

## 6. Roteiro técnico, em ordem

1. Spikes 1, 2 e 3 (em paralelo).
2. Fechar os rascunhos A a F e os adendos; atualizar PRD, arquitetura, modelo de dados e backlog principais.
3. Fundação de dados: perfis, catálogo com códigos de barras e tabela de preço, formas de pagamento, módulos `sales` e `pos` no `ModuleCatalog`, modelo de leitura `pos.catalog_items`.
4. Identidade: aparelho com tipo e loja, esquema `Device`, lista permitida com teste de arquitetura.
5. App `/pdv`: build e service worker próprios, `LocalStore`, eventos, fila, sincronização.
6. Venda de balcão com `ManualTerminal`, recibo não fiscal e `HardwareBridge` (F1, piloto interno).
7. Sessão de caixa, sangria, fechamento e resumos para o Finance, com taxas e D+N.
8. Sem rede completo: PIN local, abrir turno, delta de catálogo, Pendências do PDV (F2, primeira versão pública).
9. Migração de hospedagem e WAL contínuo, **antes** de o item 8 ir a cliente.
10. Agente Ponte com assistente de impressoras; cozinha por setor.
11. NFC-e pelo Emissor, cancelamento e devolução (depende do SPK-02 e das lacunas).
12. Painel do cliente e KDS; mesas, comandas e split por pessoa (online).
13. Pix dinâmico pelo Inter; TEF pelo agente; SmartPOS pela casca Android.
14. Supermercado: carga, balança no caixa, promoções.
15. E-commerce sobre o `Sales` (trilha paralela, pode começar depois do item 3).

O detalhamento por story está em [05-backlog.md](05-backlog.md).

## 7. Arquivos de referência no código

| Arquivo | Por que importa |
|---|---|
| `src/BuildingBlocks/BemAqui.BuildingBlocks/Modules/ModuleCatalog.cs` | Módulos, áreas e ações; recebe os módulos novos, a área "Loja" e o `BusinessProfileCatalog` ao lado |
| `src/Modules/Platform/BemAqui.Platform.Application/Modules/TenantModuleService.cs` e `.../Provisioning/ProvisioningService.cs` | Liberação efetiva e etapas do provisionamento, onde o perfil entra |
| `src/Web/src/navigation/modules.ts` | Menu, `isVisible` (estado "oculto") e `MODULE_UI` dos módulos novos |
| `src/Web/pwa.config.ts` | Service worker atual; o `/pdv` ganha o próprio |
| `src/BuildingBlocks/BemAqui.BuildingBlocks/Web/IdempotencyFilter.cs` | Por que a sincronização não o usa |
| `src/BuildingBlocks/BemAqui.BuildingBlocks/Persistence/Numbering/NumberSequenceService.cs` | Por que a numeração da venda é do aparelho |
| `src/Modules/Inventory/BemAqui.Inventory.Contracts/Stock.cs` | `IStockMovements`, origens e tipos; ponto da PP-02 |
| `src/Modules/Finance/BemAqui.Finance.Contracts/ReceivablesContracts.cs` | Hoje só lê; falta o contrato de escrita e a origem `Sale` |
| `src/Modules/Finance/BemAqui.Finance.Application/Billing/BankBillingContracts.cs` | Vizinho do futuro `IPixChargeProvider` |
| `src/Modules/Fiscal/BemAqui.Fiscal.Contracts/OutgoingInvoiceContracts.cs` e `.../Emitter/IFiscalEmitter.cs` | `InvoiceType.NFCe` e os métodos novos |
| `src/Host/BemAqui.Host/Realtime/KioskHub.cs` | Base do painel do cliente |
| `src/Web/src/pages/shopfloor/OfflineGate.tsx` | Como o `/fab` trata a falta de rede hoje |
| ADRs 0023, 0026, 0027, 0030 e 0032 | As que recebem adendo |
