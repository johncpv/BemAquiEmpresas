# Rascunho E — Acoplamento fiscal: NFC-e pelo EmissorFiscal ou recibo não fiscal

> Rascunho de ADR do [plano do PDV](../README.md). "Conferido" é o que foi lido no código ou nas ADRs; "a validar" é conhecimento geral que precisa de spike ou da contadora antes de virar decisão. Os achados citados por número estão em [02-arquitetura.md](../02-arquitetura.md#1-achados-do-código-que-moldam-o-desenho).

- Status: **Proposto (rascunho)**, 15/11/2026. Ainda não numerado em `docs/adr`; vira ADR 0046 ou seguinte quando o PO aprovar ([como os rascunhos viram ADR](README.md)).
- Contexto: o certificado A1 fica só no cofre do Emissor (ADR 0027 §3) e o aparelho não pode tê-lo. Ver achados 1 e 11.

## Decisão proposta

1. **Venda e documento fiscal são coisas separadas.** A venda fecha no aparelho na hora. O documento é um pedido ao servidor, com estado próprio: `NotRequired`, `Pending`, `Authorized`, `AuthorizedInContingency`, `Rejected`, `Cancelled`.
2. **Tipo de comprovante por loja e por venda:** NFC-e ou recibo não fiscal. Quando o recibo não fiscal é permitido é pergunta da contadora, não escolha do operador.
3. **Lacunas novas no EmissorFiscal** (no modelo da tabela da ADR 0027 §5):
   - NFC-e modelo 65;
   - CSC e QR Code gerados lá;
   - série por caixa;
   - DANFE NFC-e devolvida como dados estruturados;
   - contingência offline (tpEmis 9) feita pelo Emissor quando a SEFAZ cai;
   - cancelamento de NFC-e e cancelamento por substituição;
   - limite de chamadas por chave, não por IP.
4. **`IFiscalEmitter` ganha `IssueNfceAsync`, `GetNfceAsync`, `CancelNfceAsync`.** O cálculo de imposto continua no `TaxCalculator`, no servidor. O aparelho não calcula imposto.
5. **Idempotência:** `nfce-{saleId}-{tentativa}`, com `payload_hash`, igual à NF-e. A numeração é do Emissor, com **uma série por caixa**.
6. **Item apto para o PDV:** NCM, CFOP, CSOSN ou CST e CEST são validados no cadastro, pela prévia, não na venda. Item incompleto não desce para o caixa. Sem isso, venda feita sem rede falha na emissão depois.
7. **Cancelar e devolver:**
   - Cancelamento dentro do prazo legal (a confirmar por UF) com ação nova `pos.cancel_sale` e PIN de supervisor. Hoje `fiscal.approve` é reservada ao Proprietário e pede 2FA de novo; isso não funciona no balcão e pede adendo à ADR 0008.
   - Fora do prazo: NF-e de devolução (`NfePurpose.Return` já existe).
8. **CPF na nota** é dado pessoal: cifrado no aparelho, marcado `[PersonalData]`, fora de log e outbox (regra da ADR 0027 §4).

## Contingência

| Cenário | O que fazer |
|---|---|
| Internet ok, SEFAZ fora | O Emissor emite em contingência offline e devolve XML assinado e QR. O caixa imprime a DANFE de contingência. Transmissão depois, pelo Emissor. |
| Loja sem internet | Ninguém na loja pode assinar. Opções abaixo. |

Opções para a loja sem internet:
- **(a) Recibo não fiscal agora e NFC-e emitida na volta da rede.** Simples e mantém o requisito do certificado. **Ressalva legal:** entregar mercadoria sem documento fiscal pode ser infração, e a SEFAZ rejeita data de emissão atrasada, então a nota sai com a data da reconexão.
- **(b) Bloquear a venda** quando o documento é obrigatório. Seguro juridicamente; contraria o offline-first.
- **(c) Assinador local na loja**, com cópia do A1 no agente. Permite contingência offline de verdade; contraria o requisito do PO.
- **(d) 4G de reserva**, para reduzir o cenário ao mínimo. Não elimina.

**Recomendação:** (d) como orientação padrão de implantação, mais (a) como política por loja, com janela máxima e relatório "NFC-e emitidas depois da venda". Ligar só depois de a contadora aprovar por UF. Se ela reprovar (a), a escolha fica entre (b) e reabrir (c) com o PO.

Perguntas para a contadora, por UF dos clientes-alvo:
- A contingência offline de NFC-e é aceita? Qual o prazo de transmissão?
- Qual o prazo de cancelamento?
- Há obrigação de TEF integrado?
- A opção (a) é aceitável? Com que limite?
- Há equipamento fiscal específico ainda exigido?
- IBS/CBS na NFC-e.

## Consequências
- O PDV fiscal não existe antes das lacunas do Emissor. O PDV não fiscal pode sair antes.
- Filial com CNPJ próprio exige emitente por loja; hoje é um por tenant ([rascunho F](rascunho-F-escala-hospedagem.md)).

## Notas da consolidação do plano
- **Divergência registrada.** A recomendação deste rascunho para a loja sem internet é (d) + (a): 4G de reserva e recibo não fiscal agora com NFC-e na volta da rede. A análise do EmissorFiscal ([07](../07-emissor-fiscal.md), seção 2.6) recomenda o oposto: agente de loja com A1 dedicado, que é a contingência prevista na norma. As duas posições estão na [PP-01](../06-pendencias-riscos.md#pp-01--venda-sem-internet-em-loja-que-emite-nfc-e); a decisão do PO está logo abaixo.
- As "lacunas novas no EmissorFiscal" do item 3 estão detalhadas (41 lacunas) em [07-emissor-fiscal.md](../07-emissor-fiscal.md).
- **Stories:** NFCE-00 a NFCE-07, na Fase 13.
- **Adendos que este rascunho pede:** ADR 0027 (NFC-e, série por caixa, emitente por loja) e ADR 0008 (cancelamento no balcão com PIN de supervisor, PP-11).
- **Decisão do PO em 15/11/2026: opção (c) deste rascunho, o assinador local na loja** (é a opção (b) da PP-01). A recomendação original deste rascunho, (d) + (a), foi vencida: o PO quer contingência offline de verdade. Um servidor de assinatura por loja, chave não exportável, certificado separado do cofre, série própria de contingência com alerta, inventário por loja. A opção (a) fica como modo degradado, desligado por padrão. O "requisito do PO" citado no texto (certificado só no cofre) foi revisto por ele; pede adendo à ADR 0027 §3.
- **Revisão de 15/11/2026.** (1) O caso "internet ok, SEFAZ fora" não tinha lacuna no Emissor: é a **lacuna 41**, e a story dona do estado `AuthorizedInContingency` é a NFCE-08. (2) "A data fiscal é a do servidor" é proposta: QC-17. (3) A lista de campos do "item apto" precisa da contadora. (4) O tipo de comprovante por loja foi decidido pelo PO: "NFC-e" ou "só cupom não fiscal", por escolha e responsabilidade da empresa, com aceite do Proprietário (PP-05).
