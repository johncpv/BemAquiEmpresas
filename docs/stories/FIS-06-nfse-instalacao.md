# FIS-06 — NFS-e da instalação no aceite técnico, com o processo provisório pelo portal da prefeitura

| Campo | Valor |
|---|---|
| ID | FIS-06 |
| Título | NFS-e do serviço de instalação no aceite técnico da OS de instalação (COM-06), com os itens `Service` do orçamento (venda mista, D4), ISS e retenções pela regra de prestação de serviço (FIS-02b), IBS/CBS, vínculo com a NF-e do equipamento, XML e PDF guardados e enviados ao cliente, ISS realizado na razão do projeto; emissão pelo `IFiscalEmitter` quando o provider de Caçapava existir no Emissor e, até lá, o processo provisório: emitir no portal da prefeitura e registrar a nota aqui; até a nota, o projetado usa o ISS do orçado |
| Tamanho | M |
| Status | Ready (pré-condição externa) |
| Requisitos | R-FIS-7 (NFS-e para serviços de instalação/manutenção; P-02) · R-FIS-8 (IBS/CBS) · R-PRJ-3 · R-PRJ-5 · R-COM-1 · R-PRP-2 · R-PRP-7 · R-SEC-5 |
| Dependências | FIS-01 (`IFiscalEmitter.IssueNfseAsync`, `GetAccountAsync`), FIS-02a (NFS-e em Configurações → Fiscal), FIS-02b (regra de prestação de serviço), FIS-03a (`IOutgoingInvoices`, NF-e do equipamento, fila "A emitir"), COM-06 (OS de instalação e `InstallationAccepted`), PRD-04 (`commercial.project_costs`) |
| Bloqueia | — (a GOL-01a exige as Fases 0 a 8 concluídas) |
| Ordem de execução | 15 (Marco 5.6 "Desejáveis"; antes do go-live, na ordem da fase) |

## User story
Como **financeiro**, quero **faturar a instalação quando o cliente aceita a entrega técnica, pelo sistema quando o Emissor já emitir a NFS-e de Caçapava e, até lá, registrando a nota que emiti no portal da prefeitura**, para **o faturamento e o ISS do projeto ficarem completos desde o go-live, junto com a nota do equipamento**.

## Contexto e referências
- PRD R-FIS-7 ("NFS-e para serviços de instalação/manutenção (CNAE principal da empresa) — ver pendência P-02"); `05-backlog` FIS-06.
- **Revisão pré-Fases 4 a 8** ([2026-10-21-revisao-pre-fases4-8.md](../revisoes/2026-10-21-revisao-pre-fases4-8.md)): **D4** (instalação em NFS-e no aceite técnico; parcela só cobrança; receita pela data da nota), **D5** (NFS-e não bloqueia o go-live se a contadora confirmar o processo provisório pelo portal da prefeitura; provider de Caçapava no Emissor em paralelo; até lá, o ISS do orçado no projetado), D3 (provider de Caçapava no Emissor), D6 (2FA de novo ao cancelar), D1 novo (tudo antes do go-live); tabela 3.2, pontos 70 a 73; §4.4 padrão 6.
- **ADR 0027** ([0027-integracao-emissorfiscal.md](../adr/0027-integracao-emissorfiscal.md): lacuna L-m, provider de Caçapava, cancelamento, PDF e webhook de NFS-e). **ADR 0025, adendo de 21/10/2026** ([0025-custo-real-do-projeto.md](../adr/0025-custo-real-do-projeto.md): fontes `InvoiceTax`/`InvoiceTaxReversal`). **ADR 0042** ([0042-configuracoes-fiscais-e-integracoes.md](../adr/0042-configuracoes-fiscais-e-integracoes.md)).
- `06-pendencias-riscos` **P-02** (CNAE principal de instalação 4329-1/03; CNAE de fabricação em providência pela Konebras) e P-15 (retenções no título a pagar; aqui é o serviço **prestado**).
- EmissorFiscal: contrato `POST /api/v1/nfse` (ISS e retenções, `nfeVinculada`); Caçapava (IBGE 3508504) no padrão CONAM/e-Transparência (ADR 005 de lá), autenticação por usuário e código de contribuinte da prefeitura, layout 4.00 com `Reg60_RTC` (IBS/CBS) obrigatório; portal `https://nfe.etransparencia.com.br/sp.cacapava/`. Do contador: `CodSrv`, `SRV_CTN`, `SRV_NBS`, CST e `cClassTrib` (pré-configurados em Configurações → Fiscal, FIS-02a).
- `04-fluxos-estados` §1 "OS de instalação" (`InProgress ─aceite técnico─► Completed`).

## Critérios de aceite
**A emitir**
1. **Dado** o `InstallationAccepted` (COM-06) de um projeto com itens `Service` no contrato, **quando** acontece, **então** a instalação entra na fila "A emitir" de Notas de saída (FIS-03a CA 16) e na pauta ("Notas a emitir"), e o `NextSteps` do aceite técnico oferece "Emitir NFS-e da instalação" (com `fiscal.create`).
2. **Dado** `fiscal.create` e a instalação aceita, **quando** clico "Emitir NFS-e", **então** nasce a NFS-e `Draft` com o tomador (cliente do projeto), o serviço pela regra de **prestação de serviço** ativa (FIS-02b: item 7.02 da LC 116, `CodSrv`, CTN e NBS de Configurações → Fiscal), a discriminação sugerida ("Instalação de {equipamento}, projeto {número}, OS {número}", saneada como na FIS-03a CA 4), o município da prestação (o da obra; ISS no local da obra pelo item 7.02), o valor do serviço = soma dos itens `Service` do contrato ainda não faturados, ISS, retenções e IBS/CBS calculados pela regra (nada digitado), e a NF-e do equipamento vinculada (`linked_invoice_id`) quando existir.

**Emitir pelo Emissor (quando o provider existir)**
3. **Dado** o rascunho válido, o `GET /me` do Emissor informando a NFS-e do município habilitada (lacuna L-m) e, em produção, os campos da NFS-e conferidos em Configurações → Fiscal (`ReadinessAsync`, ADR 0042), **quando** clico "Emitir NFS-e" e confirmo no `ConfirmDialog` grande com o impacto, **então** a emissão vai pelo `IFiscalEmitter.IssueNfseAsync` com o `Idempotency-Key` `nfse-{invoiceId}-{tentativa}` e o `payload_hash` (FIS-01 CA 5); autorizada, a nota guarda número, código de verificação, XML e PDF (anexos de sistema), publica `InvoiceAuthorized` com `type = NFSe` e os tributos, e o e-mail ao cliente sai pela fila; rejeitada, mostra o motivo e permite corrigir.
4. **Dado** o retorno assíncrono, **quando** o Emissor ainda não tem webhook de NFS-e, **então** o `fiscal-pending-check` (FIS-01 CA 14) consulta as NFS-e em processamento pela consulta forçada; a tela segue o padrão 6 ("Enviado / Aguardando / Demorando / Confirmado / Recusado").

**Processo provisório: portal da prefeitura (D5)**
5. **Dado** a NFS-e do município **não** habilitada no Emissor, **quando** abro a instalação a emitir, **então** a tela mostra "Até o Emissor Fiscal emitir a NFS-e de Caçapava, emita a nota no portal da prefeitura e registre aqui." com o link do portal, o rascunho com os valores para copiar (tomador, discriminação, `CodSrv`, valor, ISS) e o botão "Registrar NFS-e emitida na prefeitura".
6. **Dado** `fiscal.create` e a nota emitida no portal, **quando** clico "Registrar NFS-e emitida na prefeitura", informo número, código de verificação, data de emissão, valor do serviço e ISS **como saíram na nota da prefeitura** e anexo o PDF ou o XML da nota (obrigatório, dono `OutgoingInvoice`), e confirmo, **então** a nota fica `Authorized` com a origem **"Registrada (portal da prefeitura)"**, o ISS digitado é marcado "Digitado da nota da prefeitura" e comparado ao calculado pela regra (diferença acima de R$ 0,01 mostra o aviso "O ISS da nota é diferente do calculado ({valor}). Confira com a contadora.", sem bloquear), e o `InvoiceAuthorized` (`type = NFSe`) é publicado como na emissão; número e código repetidos no mesmo tenant: 409.
7. **Dado** a instalação aceita ainda sem NFS-e (emitida ou registrada), **quando** o projeto calcula o projetado, **então** o ISS do projetado é o do **orçado** (D5, D8); com a nota, passa a ser o realizado pela nota (CA 8).

**Efeitos, cancelamento e segurança**
8. **Dado** o `InvoiceAuthorized` com `type = NFSe`, **quando** o Commercial processa (inbox), **então** grava na razão o ISS e o IBS/CBS marcados como custo (fonte `InvoiceTax`, ADR 0025 adendo), idempotente; e o `IOutgoingInvoices` passa a contar a NFS-e no faturamento pela data da nota (receita do DRE da FIN-08 e "a faturar" da DSH-01b).
9. **Dado** a necessidade de cancelar uma NFS-e, **quando** o Emissor ainda não cancela NFS-e (lacuna L-m) ou a nota foi registrada do portal, **então** "Cancelar NFS-e" mostra "Cancele no portal da prefeitura e registre aqui." com "Registrar cancelamento feito na prefeitura" (`fiscal.approve`, motivo, número do protocolo e o **código do autenticador, 2FA de novo, D6**), que marca a nota `Cancelled` (origem manual), lança o reverso `InvoiceTaxReversal` e publica `InvoiceCancelled`, sem apagar nada; com o cancelamento disponível no Emissor, segue o fluxo da FIS-03b.
10. **Dado** o módulo `fiscal` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; emitir e registrar sem `fiscal.create`, cancelar ou registrar cancelamento sem `fiscal.approve`: 403 e o botão some; `RequireModule` e `perm:`; IDOR em `/outgoing-invoices/{id}`; OS, projeto e itens validados pelos Contracts dos donos; anexos só por endpoint autorizado.
11. **[Segurança]** **Dado** as credenciais da prefeitura, **quando** são configuradas, **então** ficam no Emissor (ADR 0027); o BemAqui não recebe nem guarda a senha web nem os códigos de usuário e contribuinte da prefeitura (teste: nenhum campo desses na API do BemAqui).
12. **[Segurança]** **Dado** a NFS-e, **quando** existe com número (emitida ou registrada), **então** nunca é apagada; XML ou PDF guardados 5 anos; histórico com quem emitiu ou registrou e quem cancelou; dois registros simultâneos da mesma instalação: só um grava (`xmin` e índice único parcial de NFS-e ativa por OS de instalação).

## Regras de negócio e técnicas
- Mesma tabela `fiscal.outgoing_invoices` com `type = NFSe` (03): `service` (jsonb: item da LC 116, `CodSrv`, CTN, NBS, município da prestação, ISS, retenções, IBS/CBS), `linked_invoice_id` (a NF-e do equipamento), `installation_order_id`, `verification_code`, `origin` (`Emitter`/`PortalRegistered`), `iss_typed` (só no registro do portal).
- **Venda mista (D4):** os itens `Service` do orçamento viram NFS-e no aceite técnico; a parcela do contrato é só cobrança.
- **ISS prestado** e retenções vêm da regra na emissão; no registro do portal, o ISS é o da nota da prefeitura, comparado ao calculado.
- **Projetado:** ISS do orçado até a nota (D5).

## Permissões, eventos e tabelas
- **Permissões:** `fiscal.create` (emitir e registrar), `fiscal.edit` (rascunho), `fiscal.approve` (cancelar e registrar cancelamento; 2FA de novo).
- **Eventos:** consome `InstallationAccepted` (COM-06, para a fila); publica `InvoiceAuthorized` e `InvoiceCancelled` com `type = NFSe`.
- **Tabelas:** `fiscal.outgoing_invoices` (`type = NFSe`, `service`, `linked_invoice_id`, `installation_order_id`, `verification_code`, `origin`, `iss_typed`); índice único parcial de NFS-e ativa por OS de instalação.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 6 (emissão pelo Emissor; `EnvironmentBanner`); 7 (cancelamento registrado com 2FA no `ConfirmDialog`).
- **Fiscal → Notas de saída** com o filtro "NFS-e" e a fila "A emitir" com as instalações aceitas; "Emitir NFS-e" no aceite técnico da OS de instalação e no projeto; tela do rascunho com a área "Para copiar no portal da prefeitura" no processo provisório.
- **Verbos:** "Emitir NFS-e" / "NFS-e enviada"; "Registrar NFS-e emitida na prefeitura" / "NFS-e registrada"; "Registrar cancelamento feito na prefeitura" / "Cancelamento registrado".
- **Estados:** carregando; processo provisório (CA 5); rascunho; processando com estado de demora; rejeitada com o motivo; campos da NFS-e não conferidos em produção (link para Configurações → Fiscal → NFS-e); registrada com a marca "Registrada (portal da prefeitura)".
- **Propositivo:** a instalação aceita entra sozinha na fila e na pauta; valores prontos para copiar no portal; o `NextSteps` da NF-e do equipamento oferece "Emitir NFS-e da instalação" depois do aceite técnico.
- **400 px:** como a NF-e; a área "Para copiar" com botão "Copiar" de 44 px por campo.
- **Ajuda:** `Ajuda/fiscal-nfse.md` (prevista, com a seção "Processo provisório pelo portal da prefeitura"); `<Dica>` em "Município da prestação", "Item da LC 116", "Retenção do ISS" e "Registrada (portal da prefeitura)".

## Plano de testes
- **Unidade:** montagem a partir da OS e dos itens `Service`, ISS, retenções e IBS/CBS (CA 2); comparação do ISS digitado (CA 6).
- **Integração (Testcontainers + `FakeFiscalEmitter`):** fila e pauta pelo `InstallationAccepted` (CA 1); emitir com o provider habilitado, autorizar pelo job e e-mail (CA 3, 4); processo provisório quando o `GET /me` não habilita (CA 5); registrar a nota do portal com anexo, duplicidade e aviso de ISS (CA 6); projetado com o ISS do orçado e depois o realizado (CA 7, 8); razão e `IOutgoingInvoices` (CA 8); cancelamento registrado com 2FA e reverso (CA 9); três camadas e IDOR (CA 10); nenhuma credencial da prefeitura no BemAqui (CA 11); nunca apagar e concorrência (CA 12).
- **Homologação da prefeitura (manual, quando o provider existir):** uma NFS-e no ambiente de homologação de Caçapava, no tenant `demo`.
- **E2E (Playwright):** aceite técnico → fila "A emitir" → registrar a NFS-e do portal → ver o ISS no projeto; tela a 400 px.

## Fora do escopo
- NFS-e recorrente de contrato de manutenção (Fase 10). Serviço tomado: fora do MVP.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.2, pontos 70–73):
1. Não bloqueia o go-live: até o provider de Caçapava existir, o processo provisório é emitir no portal da prefeitura e registrar aqui (D5; CA 5, 6); agora antes do go-live, na ordem da fase (D1 novo).
2. P-02 e venda mista: instalação em NFS-e no aceite técnico, pelos itens `Service` do orçamento (D4; CA 1, 2).
3. Provider de Caçapava no Emissor, planejado já (D3, lacuna L-m; CA 3).
4. Código do serviço: 7.02 pré-configurado, com 14.06 como alternativa na `<Dica>` (FIS-02a; CA 2).

Decisões da revisão aplicadas: D5 (ISS do orçado no projetado até a nota, CA 7); D6 (2FA de novo no cancelamento registrado, CA 9); ADR 0025 adendo (`InvoiceTax`/`InvoiceTaxReversal`, CA 8, 9); novo requisito (campos da NFS-e conferidos em produção, CA 3); §4.4 padrão 6.

**Pré-condição externa:** provider da NFS-e de Caçapava no EmissorFiscal (lacuna L-m, dono PO), com as credenciais web da prefeitura (Konebras); códigos da contadora (`CodSrv`, CTN, NBS, CST, `cClassTrib`; pré-configurados em Configurações → Fiscal); confirmação do processo provisório pela contadora. Não bloqueiam o `Ready`: o registro pelo portal funciona sem o provider.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers + fake) e E2E passando; NFS-e de homologação registrada quando o provider existir.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda escrita; teste de cobertura passando.
- [ ] Efeito fiscal com confirmação; fila e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (NFS-e no aceite técnico; registro pelo portal) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `IssueNfseAsync`/`GetNfseAsync` no adaptador; NFS-e na `OutgoingInvoice` com as duas origens.
- [ ] Consumidor do `InstallationAccepted` para a fila; registro do portal com anexo; cancelamento registrado.
- [ ] Botões na OS de instalação e no projeto; ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, contadora (processo provisório e códigos).
- Gate: CA 2, 3, 6, 7, 8, 9 e 11 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK (ajustado) |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: antes do go-live, ordem 15, antes da FIN-09) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** cancelamento registrado manualmente enquanto o EmissorFiscal não cancela NFS-e; credenciais da prefeitura só no EmissorFiscal; a importância da NFS-e para a Konebras levada ao ponto 1.
- **Correções da revalidação (21/10/2026):** pontos 70 a 73 decididos; NFS-e no aceite técnico pelos itens `Service` (D4; CA 1, 2); processo provisório pelo portal com "Registrar NFS-e emitida na prefeitura" (D5; CA 5, 6); ISS do orçado no projetado até a nota (CA 7); `InvoiceTax`/`InvoiceTaxReversal` e faturamento pela data da nota (CA 8, 9); 2FA de novo no cancelamento registrado (D6; CA 9); provider de Caçapava como pré-condição externa (D3); antes do go-live, na ordem da fase (D1 novo); CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externas: provider de Caçapava no Emissor, credenciais da prefeitura e códigos da contadora.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D1 novo, D3, D4, D5, D6, tabela 3.2 pontos 70–73, §4.4); processo provisório pelo portal; antes do go-live, na ordem da fase (15, Marco 5.6); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
