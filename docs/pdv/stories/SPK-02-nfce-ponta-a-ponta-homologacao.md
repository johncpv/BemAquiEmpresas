# SPK-02 — Spike: NFC-e modelo 65 de ponta a ponta em homologação pelo EmissorFiscal, com CSC e QR Code, série por caixa, contingência com a SEFAZ fora e medição de latência da VPS atual e de São Paulo

| Campo | Valor |
|---|---|
| ID | SPK-02 |
| Título | Spike: NFC-e modelo 65 de ponta a ponta em homologação pelo EmissorFiscal, com CSC e QR Code, série por caixa, contingência com a SEFAZ fora e medição de latência da VPS atual e de São Paulo |
| Tamanho | M |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 10 — Fundação: perfil de negócio, catálogo, pagamentos e spikes |
| Requisitos | Pedido do PO de 15/11/2026 (itens 7 e 12) · [Rascunho E](../adr/rascunho-E-acoplamento-fiscal.md) · [07-emissor-fiscal.md](../07-emissor-fiscal.md) lacuna 2 e fase 0 |
| Dependências | CSC e idCSC de homologação do emitente de teste (tenant `demo`, ADR 0027 §6); acesso ao repositório do EmissorFiscal |
| Bloqueia | NFCE-00; INFRA-01 (dados de latência); PP-14, PP-31, PP-33 |
| Paridade com o mercado | Requisitos mínimos prepara 2 e 3 do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | Fase 10, em paralelo (prazo fechado: 1 a 2 semanas) |

## Pergunta do spike
Como **arquiteto**, quero **autorizar uma NFC-e em homologação pelo EmissorFiscal e medir quanto tempo o caixa esperaria**, para **saber se a biblioteca atende, quanto trabalho há no Emissor e se a hospedagem atual serve**.

## Contexto e referências
- O Emissor não tem nenhuma linha de NFC-e. O suporte da Unimake.DFe foi inferido só dos nomes de símbolos na DLL (`Servicos.NFCe`, `InfNFeSupl`, `MontarQrCodeNFCe`, `CSCIDToken`); as assinaturas e o comportamento não foram conferidos [C].
- O spike roda no repositório do EmissorFiscal (`spikes/Spike.Nfe` é o modelo), em container Linux, como manda o `CLAUDE.md` de lá.
- As regras fiscais citadas no documento do Emissor com [F] não são premissa do spike: são perguntas que ele ajuda a responder.

## Critérios de aceite (o que o spike tem de responder, com evidência)
1. **Dado** o emitente de teste com CSC de homologação, **quando** monto, assino e transmito uma NFC-e modelo 65 com um item e pagamento em dinheiro, **então** ela é autorizada e o relatório guarda a chave, o protocolo e o XML.
2. **Dado** a nota autorizada, **quando** leio o QR Code com o aplicativo de consulta da SEFAZ, **então** a consulta abre; fica registrada a versão do QR Code que a biblioteca gerou e se o `infNFeSupl` (`qrCode` e `urlChave`) saiu completo.
3. **Dado** uma venda com CPF, uma com cartão (`tpIntegra=2`) e uma com troco, **quando** transmito, **então** fica registrado o que foi aceito e as rejeições, com o código.
4. **Dado** a SEFAZ simulada como fora do ar, **quando** tento emitir em contingência (`tpEmis=9`) **com o certificado no servidor**, **então** fica registrado se a biblioteca monta o XML e o QR de contingência e o que a transmissão posterior devolve.
5. **Dado** a emissão normal, **quando** meço 30 emissões a partir da VPS atual e 30 a partir de uma máquina em São Paulo, **então** o relatório traz p50 e p95 por etapa (montagem, assinatura, transmissão, resposta) nos dois lugares.
6. **Dado** a VPS atual, **quando** transmito para a SEFAZ de SP, **então** fica registrado se há alguma restrição a IP de fora do país.
7. **Dado** uma nota autorizada, **quando** cancelo dentro e fora do prazo, **então** ficam registrados o prazo aplicado em SP e a rejeição fora dele.
8. **Dado** tudo acima, **então** o relatório fecha com: (a) a biblioteca atende ou não, e o que falta; (b) esforço revisto das fases 1 e 2 do Emissor; (c) recomendação sobre a hospedagem; (d) as perguntas QC-01 a QC-16 entregues à contadora, com o que o spike já respondeu.

## Regras
- Só ambiente de homologação. Nenhuma nota com valor fiscal.
- O spike não decide a PP-01 (venda sem internet); só informa o que é tecnicamente possível com o certificado no servidor.
- As duas correções da NF-e de hoje (lacunas 25 e 5) não esperam o spike.

## Entregáveis
- Relatório em `docs/revisoes/` (cópia no repositório do Emissor).
- Rascunho E e a tabela de lacunas do [07](../07-emissor-fiscal.md) corrigidos.
- Proposta de decisão para PP-14, PP-31 e PP-33.

## Fora do escopo
- Agente de loja assinando offline (depende da PP-01).
- DANFE em PDF.

## Pontos em aberto
- Quem executa o trabalho no EmissorFiscal (PP-13).

## Definition of Done
- [ ] Todas as perguntas respondidas com evidência, ou marcadas "não respondida", com o motivo.
- [ ] Relatório publicado em `docs/revisoes/`.
- [ ] Rascunhos de ADR e backlog corrigidos com o que foi medido.
- [ ] Código do spike fora da `main` de produto.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
