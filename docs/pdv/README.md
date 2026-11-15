# Plano do PDV e dos perfis de negócio (proposta)

| | |
|---|---|
| Versão | 0.1 — 15/11/2026 |
| PO | John |
| Status | **Plano proposto em 15/11/2026; construção depois do go-live da Konebras; ADRs em rascunho até o PO aprovar** |
| Origem | Pedido do PO de 15/11/2026 |
| O que é | Só planejamento. Nenhum código de produto foi alterado |

## 1. Visão geral

O BemAqui Empresas nasceu como gestão para pequena indústria sob encomenda. Este plano propõe abrir o produto para **comércio, alimentação, serviços e e-commerce**, com um PDV moderno, sem mudar o objetivo central do `CLAUDE.md`: cada venda alimenta custo, margem e caixa com dado real.

O plano se apoia em três ideias:

1. **Perfil de negócio.** Um preset em código (módulos, menu, vocabulário, dados iniciais e primeiros passos) aplicado sobre a liberação de módulos que já existe (`tenant_modules`). Habilitar uma padaria ou uma oficina vira escolher o perfil no provisionamento, sem deploy.
2. **PDV local-first.** A tela de venda grava no aparelho e sincroniza depois, com ou sem rede, pelo mesmo caminho. O offline não é adaptado depois; nasce com o primeiro commit.
3. **Reuso do que já existe.** Estoque multi-local, contas a receber, conciliação bancária, aparelho com PIN, quiosque da TV e o adaptador do EmissorFiscal já estão em produção. O PDV acrescenta módulos novos (Catálogo, Vendas, PDV, Restaurante, OS de serviço, Loja virtual) ao lado deles, pelas mesmas regras do monolito modular.

O caminho crítico não é código: é a **NFC-e no EmissorFiscal**, a **decisão sobre vender sem internet em loja fiscal**, o **hardware real** (impressoras, balanças, maquininhas) e a **hospedagem no Brasil**.

## 2. O que o PO pediu

Lista de requisitos do pedido de 15/11/2026, como foi passada ao planejamento. Os trechos entre aspas são as palavras do PO.

1. Modelos de negócio: **Comércio, Fábrica, Serviços e E-commerce**.
2. Segmentos: **supermercado, padaria, restaurante, lanchonete, hamburgueria, trailer e oficina**.
3. Perfil de negócio **fácil de habilitar**.
4. PDV **moderno e prático**.
5. **Offline desde o primeiro release.**
6. **Maquininhas de cartão externas primeiro**, com a integração já prevista.
7. Impressoras com **NFC-e ou cupom não fiscal**.
8. **CPD** (central de distribuição que abastece as lojas).
9. Painel **"Na cozinha / Pedidos prontos"**.
10. **Mesas e comandas.**
11. **Pagamento dividido por pessoa e por forma de pagamento.**
12. **Ajustes no EmissorFiscal** para o que o PDV precisar.
13. **"Não propor nada menos do que o mercado pratica."**

Onde cada pedido é atendido: [01-PRD.md](01-PRD.md), seção 2.

## 3. Índice

| Documento | O que traz |
|---|---|
| [00-benchmark-mercado.md](00-benchmark-mercado.md) | Piso e melhor do mercado por segmento, com fontes; os 38 requisitos mínimos; a **matriz de paridade** (requisito → story) |
| [01-PRD.md](01-PRD.md) | Escopo, perfis de negócio, módulos reusados e novos, escopo por segmento, regras e permissões |
| [02-arquitetura.md](02-arquitetura.md) | Como o PDV cabe no monolito modular; síntese dos seis rascunhos de ADR; os três spikes |
| [03-modelo-dados.md](03-modelo-dados.md) | Entidades novas por módulo e as ligações com Estoque, Financeiro e Fiscal |
| [04-fluxos-estados.md](04-fluxos-estados.md) | Máquinas de estado: venda, caixa, comanda e mesa, cozinha, split, devolução, documento fiscal, sincronização |
| [05-backlog.md](05-backlog.md) | Fases 10 a 17, épicos, as 87 stories, o marco MVP, esforço e paralelismo |
| [06-pendencias-riscos.md](06-pendencias-riscos.md) | Decisões do PO (PP-01 a PP-37), perguntas para a contadora (QC-01 a QC-16) e riscos |
| [07-emissor-fiscal.md](07-emissor-fiscal.md) | As 40 lacunas do EmissorFiscal para a NFC-e, o contrato da API, fases e homologação |
| [adr/](adr/README.md) | Seis rascunhos de ADR (A a F), ainda não numerados |
| [stories/](stories/README.md) | Stories detalhadas das Fases 10 e 11 (26 arquivos) |

## 4. Como ler

- **Tem 10 minutos:** esta página, a seção 7.6 do [benchmark](00-benchmark-mercado.md#76-resultado) e as "Decisões que destravam o início" em [06-pendencias-riscos.md](06-pendencias-riscos.md#1-decisões-que-destravam-o-início).
- **Vai decidir o escopo:** [01-PRD.md](01-PRD.md) e [05-backlog.md](05-backlog.md).
- **Vai falar com a contadora:** [06-pendencias-riscos.md](06-pendencias-riscos.md), seção 3, e [07-emissor-fiscal.md](07-emissor-fiscal.md), seção 2.6.
- **Vai construir:** [02](02-arquitetura.md), [03](03-modelo-dados.md), [04](04-fluxos-estados.md), os [rascunhos de ADR](adr/README.md) e as [stories](stories/README.md).

### Marcas de confiança

O plano junta quatro análises feitas em paralelo. Cada uma marcou o que conferiu e o que supôs, e as marcas foram mantidas:

| Marca | Onde aparece | Significado |
|---|---|---|
| **[V]** / **[C]** | Benchmark | [V] verificado em fonte numerada; [C] prática corrente de mercado, não confirmada em fonte primária |
| **[C]** / **[F]** | Emissor fiscal | [C] conferido no código ou na documentação do repositório; [F] conhecimento fiscal geral, a confirmar com a contadora ou a SEFAZ |
| "Conferido" / "a validar" | Arquitetura e rascunhos de ADR | Lido no código ou nas ADRs; ou conhecimento geral que pede spike |

Atenção: `[C]` quer dizer coisas diferentes no benchmark e no documento do Emissor. **Nenhuma regra fiscal deste plano foi confirmada com a contadora.** Tudo o que é prazo, obrigatoriedade ou regra por UF está como pergunta (QC-nn), não como requisito.

### Identificadores

| Prefixo | O que é |
|---|---|
| `PERF`, `CAT`, `PAG`, `PDV`, `CX`, `IMP`, `OFF`, `CPD`, `NFCE`, `ECM`, `KDS`, `MESA`, `DLV`, `FID`, `OFI`, `INFRA` | Stories, por épico ([05-backlog.md](05-backlog.md)) |
| `SPK` | Spikes (prova técnica com prazo fechado, sem entrega de produto) |
| `PP-nn` | Pendência do plano: decisão do PO. Vira `P-31` em diante no `docs/06-pendencias-riscos.md` quando o plano for aprovado |
| `QC-nn` | Pergunta para a contadora |
| `RP-nn` | Risco do plano |
| Rascunho A a F | Rascunhos de ADR; viram ADR 0046 em diante na aprovação |

## 5. Onde as análises discordam

Dois pontos tiveram recomendações opostas. O plano **não escolheu**; apresenta os dois lados e pede a decisão.

| Tema | Posição 1 | Posição 2 | Onde decidir |
|---|---|---|---|
| Venda sem internet em loja que emite NFC-e | Arquitetura: cupom não fiscal agora e NFC-e na volta da rede, com 4G de reserva | Emissor: agente de loja com certificado A1 dedicado, que é a contingência prevista na norma | [PP-01](06-pendencias-riscos.md#pp-01--venda-sem-internet-em-loja-que-emite-nfc-e) (PO + contadora) |
| Venda com saldo de estoque insuficiente | Produto: manter a regra de saldo nunca negativo e abrir pendência | Arquitetura: permitir saldo negativo só em local de loja | [PP-02](06-pendencias-riscos.md#pp-02--venda-com-saldo-insuficiente) (PO) |

Há também um ponto em que a arquitetura ficou **abaixo do piso de mercado**: a janela máxima sem sincronizar proposta é de 72 h, e o piso é de 7 dias (PP-10).

## 6. O que acontece quando o plano for aprovado

Este diretório é uma proposta isolada. Nada fora de `docs/pdv/` foi alterado. Na aprovação:

1. As decisões do PO entram em `docs/06-pendencias-riscos.md` como P-31 em diante, com data e autor.
2. Os rascunhos A a F viram ADRs numeradas (0046 em diante) em `docs/adr/`, junto com os adendos às ADRs 0003, 0008, 0019, 0023, 0026, 0027, 0030 e 0032 ([lista](adr/README.md#adendos-a-adrs-existentes)).
3. O conteúdo de `01` a `05` é integrado aos documentos principais (`docs/01-PRD.md` a `docs/05-backlog.md`), e as stories passam para `docs/stories/`.
4. O `CLAUDE.md` é atualizado em dois pontos: a frase "para pequenas indústrias sob encomenda" e a regra "módulo não liberado aparece no menu com cadeado" (PP-03).

Até lá vale a regra do repositório: **item pendente não é implementado sem decisão registrada.**
