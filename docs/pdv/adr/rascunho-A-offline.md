# Rascunho A — PDV offline-first: onde roda, armazenamento local e sincronização

> Rascunho de ADR do [plano do PDV](../README.md). "Conferido" é o que foi lido no código ou nas ADRs; "a validar" é conhecimento geral que precisa de spike ou da contadora antes de virar decisão. Os achados citados por número estão em [02-arquitetura.md](../02-arquitetura.md#1-achados-do-código-que-moldam-o-desenho).

- Status: **Proposto (rascunho)**, 15/11/2026. Ainda não numerado em `docs/adr`; vira ADR 0046 ou seguinte quando o PO aprovar ([como os rascunhos viram ADR](README.md)).
- Contexto: o PDV precisa vender sem rede desde a primeira versão pública, em PC de caixa Windows e em tablet Android, com periféricos. Ver achados 3 a 6.
- Complementa: 0026 (aparelho e PIN), 0009, 0019 (idempotência), 0023.

## Opções: onde o PDV roda

| Opção | A favor | Contra |
|---|---|---|
| PWA no navegador, sem instalar | Zero instalação | Armazenamento sujeito a limpeza; periféricos só por WebUSB, WebSerial e Web Bluetooth |
| **PWA instalado + agente local opcional** | Um só código React; armazenamento persistente; o agente resolve o que o navegador não alcança | Um instalador a mais no Windows; assinatura de código |
| Tauri ou Electron | TCP, serial e USB nativos num pacote | Segundo canal de atualização; cookies `__Host-` e service worker mudam de comportamento; TEF ainda pede componente nativo |
| App Android nativo | Único caminho para SDK de SmartPOS | Segundo código de interface |

Limites de plataforma (a validar no spike 1):
- WebUSB, WebSerial e Web Bluetooth só existem em navegadores Chromium. No iOS não há nenhum dos três.
- No Windows, a impressora USB fica presa ao driver de impressão; WebUSB só pega com troca de driver, o que não é "configuração simples".
- O navegador não abre TCP 9100, então impressora de rede não é alcançável sem intermediário.
- Página HTTPS só fala com `ws://127.0.0.1`. Um IP da LAN é bloqueado como conteúdo misto. O Chrome recente pede permissão de rede local.

## Decisão proposta

1. **Um app React novo, `/pdv`, com build e service worker próprios.** Entrada Vite separada: o bundle atual já passa de 2 MiB e o plugin gera um service worker só. Atualização só na troca de turno, nunca com `skipWaiting` no meio de uma venda.
2. **Alvos suportados:**
   - Windows com Chrome ou Edge, PWA instalado, mais o agente local ([rascunho B](rascunho-B-impressao-perifericos.md)).
   - Android com Chrome, PWA instalado.
   - Casca Android fina (Capacitor) só quando entrar SmartPOS ([rascunho C](rascunho-C-pagamentos.md)).
   - iOS fica fora do caixa; serve no máximo para lançar pedido online.
3. **Local-first desde o primeiro commit.** A tela de venda nunca espera a rede, nem quando há rede. Toda ação grava no armazenamento local e entra numa fila de saída no aparelho.
4. **Armazenamento: IndexedDB** (Dexie, Apache 2.0) atrás de uma porta `LocalStore`, com `navigator.storage.persist()` e Web Locks para um só escritor por aparelho. SQLite-WASM em OPFS fica como alternativa se o spike 3 mostrar busca lenta com 50 mil itens.
5. **Dinheiro em centavos inteiros no TypeScript.** Ids Guid v7 gerados no aparelho; `crypto.randomUUID` é v4, então precisa de gerador v7.
6. **Protocolo de subida:** eventos de venda imutáveis, em lote, por `POST /api/v1/pos/sync/events`.
   - Cada evento leva `event_id`, `device_id`, `register_id`, `seq` por caixa, `schema_version`, instante do aparelho e hash do evento anterior. A cadeia de hash denuncia lacuna ou adulteração.
   - O servidor grava em `pos.device_events` (imutável por trigger, como `stock_movements`) com `ON CONFLICT (event_id) DO NOTHING` e projeta em `sales.sales`.
   - A resposta confirma até qual `seq` e diz, por evento: aceito, aceito com divergência ou em quarentena.
   - **Venda concluída nunca é recusada.** Conflito vira pendência para conferência humana.
   - O `IdempotencyFilter` continua só para comandos que exigem rede (ex.: criar cobrança Pix).
7. **Protocolo de descida:** foto inicial mais delta por cursor.
   - Fonte: um modelo de leitura do próprio módulo (`pos.catalog_items`, com `change_seq`), alimentado por eventos do Catalog (produto, preço) e do Inventory (item). Respeita a regra de não ler tabela de outro módulo.
   - Coleções: itens, códigos de barras, preços, operadores, formas de pagamento, mesas, impressoras.
8. **Regras de conflito:**
   - **Estoque:** a venda sempre vale; ver [rascunho F](rascunho-F-escala-hospedagem.md) sobre saldo negativo.
   - **Preço:** vale o que o aparelho tinha. O evento leva o preço e a `price_list_version`; o servidor nunca reprecifica. Versão vencida além do limite vira divergência. Lista nova entra com `valid_from`, para o aparelho baixar antes.
   - **Número da venda:** `{loja}-{caixa}-{seq}`, gerado no aparelho, fora do `INumberSequence`. Um caixa tem um aparelho ativo por vez; o servidor devolve o último `seq` ao registrar um aparelho novo.
   - **Relógio:** o instante do aparelho é informativo; o servidor grava `received_at`. A data fiscal é do servidor.
   - **Mesas e comandas:** são estado compartilhado entre aparelhos. Online, o servidor manda. Sem rede, cada aparelho só acrescenta itens às comandas que ele mesmo abriu; fechar comanda de outro aparelho exige rede.
9. **Autenticação sem rede** (adendo à 0026):
   - **Registro do aparelho:** login completo de quem tem `pos.manage_devices`. Generalizar `identity.shopfloor_devices` com `kind` (Fábrica, Caixa, KDS, Garçom, Estação de impressão), `store_id` e `register_id`. O teto passa a ser por tipo e plano.
   - **Chave do aparelho:** ECDSA não exportável (WebCrypto). O lote de sincronização vai assinado e usa um esquema `Device` próprio, restrito a `/api/v1/pos/sync/*` por atributo e teste de arquitetura, no modelo do `[PinSessionAllowed]`. A sincronização funciona sem operador presente.
   - **Segredo do aparelho:** gira a cada sincronização bem-sucedida, não a cada PIN.
   - **PIN sem rede:** verificador derivado no servidor e baixado só para os operadores da loja, protegido pela chave do aparelho. Bloqueio local por tentativas. **Risco aceito:** 6 dígitos com acesso físico ao aparelho é quebrável. Por isso o que se pode fazer sem rede é limitado.
   - **Supervisor sem rede:** cancelar item ou dar desconto acima do limite pede PIN de supervisor, vira evento e é revisto na sincronização. A regra "perfil sensível não tem PIN" precisa de exceção nomeada, como a `production.approve_quality`.
   - **Revogação:** só vale no próximo contato. Compensação: janela máxima sem sincronizar (proposta: 72 h); depois disso o aparelho não abre turno. Eventos de aparelho revogado entram em quarentena, não são descartados.
10. **Escopo por loja:** o aparelho só baixa dados da sua loja. O servidor tira `store_id` do principal do aparelho, nunca do corpo.
11. **Não disponível sem rede:** Pix dinâmico, TEF, autorização de NFC-e, limite de crédito do cliente, saldo de outra loja, cadastro e troca de preço, troca ou devolução de venda de outro caixa, relatórios, primeiro login, registro de aparelho, KDS e painel em tempo real.

## Fases sem retrabalho

| Fase | Entrega | O que já é definitivo |
|---|---|---|
| F1, piloto interno | Vende com rede; abrir turno e entrar exigem rede; queda no meio da venda não trava | Armazenamento local, eventos, fila, lote de sincronização, numeração por caixa, porta de periféricos |
| F2, primeira versão pública | Abrir turno e PIN sem rede, delta de catálogo, tela de pendências, limites | Só acréscimo sobre a F1 |
| F3 | Comandas sem rede limitadas; hub da loja se houver demanda | Opcional |

**Diferença de esforço (estimativa):** fazer a F1 local-first custa de 30% a 40% a mais no núcleo do PDV (frente e sincronização) do que um PDV online simples. A F2 soma de 5 a 8 semanas-pessoa. Fazer online simples e adaptar depois significa reescrever a tela de venda, a numeração e a ingestão; eu não recomendo.

## Consequências
- A perda do aparelho perde as vendas ainda não enviadas. Mitigação: sincronizar logo, contador visível de vendas não enviadas e cópia do log em disco pelo agente.
- O servidor precisa aceitar eventos de versões antigas do app por um prazo definido.
- Tela nova: Pendências do PDV.

## Notas da consolidação do plano
- **Janela sem sincronizar (item 9, "Revogação"):** a proposta de 72 h fica abaixo do piso de mercado, que é de 7 dias (Hiper; [benchmark](../00-benchmark-mercado.md), requisito 1). Virou a [PP-10](../06-pendencias-riscos.md): parâmetro por loja com teto de 7 dias.
- **Onde roda e agente local:** dependem do [SPK-01](../stories/SPK-01-perifericos-hardware-real.md) e das PP-08 e PP-09.
- **Armazenamento local:** IndexedDB ou SQLite-WASM sai do [SPK-03](../stories/SPK-03-nucleo-local-first-carga.md).
- **Nomes de tabela:** este rascunho usa `pos.device_events` e `pos.catalog_items`; o [modelo de dados](../03-modelo-dados.md) mantém esses nomes.
- **Stories:** [OFF-01](../stories/OFF-01-fundacao-offline.md), [OFF-02](../stories/OFF-02-sincronizacao-pendencias.md), [OFF-03](../stories/OFF-03-caixa-supervisor-sem-rede.md) e [PDV-01](../stories/PDV-01-lojas-caixas-aparelho.md).
- **Adendos que este rascunho pede:** ADR 0026 (aparelho com tipo e loja, PIN sem rede, exceção nomeada para supervisor), ADR 0019 (a sincronização não usa o `IdempotencyFilter`).
- **Revisão de 15/11/2026.** (1) O número da venda e a chave dos eventos ganham a **época do aparelho** (`{loja}-{caixa}.{época}-{seq}`), para que a troca de aparelho não colida com vendas ainda não enviadas pelo antigo; o aparelho revogado mantém só a subida. (2) O verificador do PIN não pode ser "protegido pela chave do aparelho": a chave ECDSA só assina; é preciso uma chave de cifra não extraível própria. (3) Faltavam limites de valor e de quantidade para aprovação offline e um piso de horário do servidor para a janela. (4) O mesmo PIN valer online e offline é a pendência PP-39. (5) O registro do aparelho exige TOTP recente e avisa o Proprietário. (6) O CPF não entra no evento imutável. Tudo isso está nas stories PDV-01, OFF-02 e OFF-03.
- "F1" e "F2" deste rascunho são o "piloto interno" e o "MVP" do [README](../README.md#marcos).
