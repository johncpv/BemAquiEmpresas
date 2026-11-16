# 07 — O que o EmissorFiscal precisa mudar para a NFC-e

Parte do [plano do PDV](README.md). O EmissorFiscal é outro repositório (`D:\GIT\EmissorFiscal`); este documento é o pedido de trabalho para lá, no modelo da tabela de lacunas da ADR 0027.

Análise de lacunas e plano de trabalho, sem alteração de código. Data-base: 15/11/2026, branch `finalizacao` do `D:\GIT\EmissorFiscal`.

**Legenda de confiança**
- **[C]** verificado por leitura do código ou da documentação do repositório.
- **[F]** conhecimento fiscal geral, não documentado no repositório. Precisa de confirmação (contadora, Portal Nacional da NF-e ou SEFAZ da UF) antes de virar regra no código.
- Esforço para um desenvolvedor: **P** até 3 dias, **M** 4 a 8 dias, **G** mais de 8 dias.

**Resumo executivo**
- O núcleo do Emissor (cofre do A1, assinatura, idempotência, numeração, outbox e webhook, guarda do XML) serve à NFC-e sem ser reescrito.
- A biblioteca já instalada (Unimake.DFe 20260722) tem símbolos de NFC-e: namespace `Servicos.NFCe`, `InfNFeSupl`, `MontarQrCodeNFCe`, `CSCIDToken`, `VersaoQRCodeNFCe`, grupo de cartão e configuração para as 27 UFs. Conferi só os nomes na DLL, não as assinaturas nem o comportamento.
- Hoje não existe nenhuma linha de NFC-e no código; `docs/plano-finalizacao.md` §5 a declara fora de escopo.
- Três pontos mudam o desenho, não só o contrato:
  1. **Contingência offline exige assinatura no momento da venda.** O QR Code offline depende do XML assinado, então quem está sem internet precisa de um certificado por perto.
  2. **A reconciliação atual reenvia o mesmo XML assinado.** Na NFC-e normal isso tende a ser rejeitado por data de emissão atrasada [F].
  3. **Os limites atuais não comportam supermercado**: 60 requisições por minuto por cliente, instância única, PDF via Chromium por nota.
- Há um bloqueio anterior a tudo: a dívida **E14** (painel preso ao tenant da WSP, "não cadastrar 2º cliente") impede vender o PDV a vários clientes.

---

## 0. Dois defeitos que afetam a NF-e de hoje (corrigir já, sem esperar o PDV)

Estes dois pontos apareceram na análise da NFC-e, mas atingem a **NF-e modelo 55 que já está em produção**. Não dependem de nenhuma decisão deste plano.

| Lacuna | Defeito | Quem é afetado hoje | Confiança | Esforço |
|---|---|---|---|---|
| **25** | `MapPis` e `MapCofins` só tratam os CST 01 e 02 como alíquota; todo o resto vira `PISNT`/`COFINSNT`. CST 49 ou 99 pede o grupo `PISOutr`/`COFINSOutr`, então a nota reprovaria no XSD (erro 215) | Qualquer emitente que use CST 49 ou 99 (é o caso típico do Simples Nacional). A Konebras está no regime normal, então pode não ter batido no problema ainda | [C] sobre o código (linhas 297-334 do `NfeRequestMapper.cs`); [F] sobre o leiaute | P |
| **5** | O enum de `indPres` não tem o valor 5 e chama o 9 de "PresencialForaEstabelecimento"; a mensagem do validador (linha 85) diz "5 (NFC-e em entrega a domicílio)". Pelo leiaute conhecido: 4 = NFC-e com entrega a domicílio, 5 = presencial fora do estabelecimento, 9 = não presencial, outros | Quem emite NF-e com `indPres` 5 ou 9 pode estar vendo o rótulo trocado na tela e na mensagem de validação. O efeito no XML enviado não foi conferido | [C] sobre o código; [F] sobre o leiaute. **Conferir no MOC antes de mexer** | P |

Recomendação: abrir as duas correções no EmissorFiscal agora, como a fase 0 da tabela da seção 4.1 já prevê. No backlog deste plano elas estão dentro da [NFCE-00](05-backlog.md).

---

## 1. Estado atual: o que existe e se reaproveita

| Capacidade | Onde está [C] | Serve à NFC-e? |
|---|---|---|
| Cofre do A1 (AES-GCM, chave-mestra fora do banco), conferência do CNPJ por OID ICP-Brasil | `src/EmissorFiscal.Application/Services/CertificateService.cs`, `src/EmissorFiscal.Infrastructure/Security/AesGcmCertificateProtector.cs` | Sim. Ressalva: a linha 72 exige CNPJ do certificado igual ao do emitente, o que impede usar o A1 da matriz na filial (lacuna 22) |
| Assinatura separada da transmissão; XML assinado gravado antes de falar com a SEFAZ | `INfeProvider.SignAsync` / `TransmitAsync` em `src/EmissorFiscal.Providers/Nfe/Unimake/UnimakeNfeProvider.cs`; `FiscalDocument.SignedXml` | Sim, com parâmetro de modelo: hoje fixa `TipoDFe.NFe` e `Servico.NFeAutorizacao` (linhas 47-48, 271-288) |
| Transmissão síncrona (`indSinc=1`, lote de 1 nota) | `UnimakeNfeProvider.cs` linhas 54 e 84-88 | Sim, é o modo da NFC-e |
| Validação XSD antes de gastar número | `src/EmissorFiscal.Providers/Nfe/Unimake/NfeSchemaValidator.cs` (`nfe_v4.00.xsd` embutido) | Provavelmente sim: a NFC-e usa o mesmo leiaute 4.00 [F] |
| Idempotência por índice único (cliente + chave) | `NfeIssuanceService.IssueAsync`; `FiscalDocumentConfiguration.cs` (`ix_documents_client_id_idempotency_key`) | Sim. Ressalva: repetição com payload diferente devolve o documento antigo sem comparar (lacuna 23) |
| Numeração transacional por emitente + tipo + série + ambiente (`SELECT ... FOR UPDATE`) | `src/EmissorFiscal.Infrastructure/Persistence/NumberSequenceAllocator.cs`, `NumberSequence.cs` | Sim. `DocumentType` entra na chave, então a série da 65 fica independente da 55 |
| Inutilização, lacunas e guarda do `procInutNFe` | `NfeEventService.InvalidateRangeAsync` / `ListGapsAsync`, `NumberInvalidation.cs`, `InutilizacaoDeNumeracao.cs` | Parcial: filtros e provider fixos em `DocumentType.Nfe` / `ModeloDFe.NFe` |
| Cancelamento com prazo no domínio | `src/EmissorFiscal.Domain/PrazoDeCancelamento.cs` (24 h fixas, Portaria CAT 162/08) | Não como está: o prazo da NFC-e é outro |
| Reconciliação de pendentes, com proteção contra o cStat 656 | `NfeReconciliationService.cs`, `SituacaoDaNota.cs`, `NfeReconciliationJob.cs` (30 s, lote de 20) | Parcial: a regra de reenvio e a contingência SVC não valem para a 65 |
| Webhook assinado (`t=…,v1=…`), outbox com `FOR UPDATE SKIP LOCKED`, 8 tentativas | `PublicacaoDeEventos.cs`, `WebhookPublisher.cs`, `OutboxDispatcher.cs` | Sim; faltam os eventos `nfce.*` |
| ICMS: todos os CST e CSOSN (101, 102, 103, 201, 202, 203, 300, 400, 500, 900), com `pCredSN` | `NfeRequestMapper.Icms.cs`, `NfeRequestValidator.Icms.cs` | Sim. O Simples já está mapeado no ICMS |
| CRT 1, 2, 3 e 4 no emitente | `Enums.cs` (`TaxRegime`), `NfeRequestMapper.MapEmitente` | Sim |
| IBS/CBS por item e total | `NfeRequestMapper.Grupos.cs` (`MapIbsCbs`, `MapTotaisIbsCbs`), `NfeOptions.ExigirIbsCbs` | Sim; exigido só do regime normal (`ValidarIbsCbsObrigatorio`) |
| `infRespTec` com hash CSRT | `NfeRequestMapper.MapResponsavelTecnico`, `ResponsavelTecnicoOptions` em `NfeOptions.cs` | Sim. Ressalva: a configuração é global, com um único CSRT |
| Enum `tPag` com Pix 17 e 20 | `src/EmissorFiscal.Domain/NfeEnums.cs` | Parcial: `PagamentoRequest` só tem `meio` e `valor` |
| Horário legal por UF | `src/EmissorFiscal.Domain/HorarioDaUf.cs` | Sim, essencial para vários estados |
| Escopos, `/me`, limite por cliente, bloqueio de licença | `ApiScopes.cs`, `ClientSelfEndpoints.cs`, `ClientRateLimiter.cs` | Sim, com escopos novos e limite revisto |
| DANFE A4 em HTML (Zeus) e PDF (Gotenberg) | `ZeusHtmlDanfeRenderer.cs`, `GotenbergPdfConverter.cs`, `DanfeService.cs` | Não: é A4 da modelo 55. Reaproveita-se só a interface `IDanfeRenderer` e o conversor |
| Multi-tenant por RLS | `TenantConnectionInterceptor.cs`, `ApiKeyAuthenticationHandler.cs` | Sim, mas com a dívida E14 aberta |

Do lado do BemAqui Empresas [C]:
- O adaptador `src/Modules/Fiscal/BemAqui.Fiscal.Infrastructure/Emissor/EmissorFiscalHttpEmitter.cs` fala só NF-e e NFS-e.
- `EmissorFiscalRateGate.cs` é um token bucket com `TokenLimit = 1`, ou seja, enfileira as chamadas ao Emissor.
- A ADR 0027 fixa: um tenant do Emissor por tenant do BemAqui, numeração do Emissor, A1 só no cofre do Emissor.
- Não há menção a PDV ou NFC-e em `docs/` do BemAqui.

---

## 2. Lista de lacunas

São 41: as 40 da análise original e a **41**, acrescentada pela revisão independente de 15/11/2026 (seção 2.2).

### 2.1 Contrato, mapeamento e autorização

| # | O quê | Por quê | Onde | Esf. | Depende de |
|---|---|---|---|---|---|
| 1 | **`DocumentType.Nfce = 65`** e modelo como parâmetro em builder, transmissor, eventos e consulta | Hoje tudo fixa o modelo 55 [C]. O modelo está nas posições 21-22 da chave; a consulta deve escolher o serviço por ele | `Enums.cs`; `NfeRequestMapper.cs` linhas 139 e 428; `UnimakeNfeProvider.MontarConfiguracao`; `UnimakeNfeEventProvider.cs` linhas 72 e 209; `NfeEventService.cs` linha 319; `SituacaoDaNota.cs` linha 261 | M | Spike da lacuna 2 |
| 2 | **Spike da Unimake para NFC-e**: `Servicos.NFCe.Autorizacao`, `TipoDFe.NFCe`, CSC na `Configuracao`, geração do `infNFeSupl` | Conferi só os nomes na DLL. Falta provar em container Linux que a lib monta `qrCode` e `urlChave` e qual versão de QR Code gera | `spikes/Spike.Nfe` como modelo; `UnimakeNfeProvider.cs` | P | Nada; fazer primeiro |
| 3 | **Contrato `NfceRequest`** em rota própria (`POST /api/v1/nfce`), reaproveitando `ItemNfeRequest` e `ImpostosItemRequest` | A NFC-e não tem destinatário obrigatório nem cobrança, e seu transporte é restrito. Forçar no `NfeRequest` exigiria afrouxar validações da 55 (`destinatario` é obrigatório em `NfeRequestValidator.cs` linha 110) | Novos `Models/NfceRequest.cs`, `Validation/NfceRequestValidator.cs`, `Endpoints/NfceEndpoints.cs` | M | 1 |
| 4 | **Grupo `ide` da 65**: `mod=65`, `tpImp` 4 ou 5, `idDest=1`, `indFinal=1`, `tpNF=1`, `finNFe=1`, `indPres` 1 ou 4, sem `dhSaiEnt`, sem `NFref` | Regras de validação do MOC para modelo 65 [F: lista a confirmar]. Hoje `TpImp` é fixo em retrato, `IdDest` sai da UF do destinatário e `DhSaiEnt` sempre vai na saída [C] | `NfeRequestMapper.MapIde` (linhas 134-172) | M | 3 |
| 5 | **Rótulos de `indPres` a conferir** | O enum não tem o valor 5 e chama o 9 de "PresencialForaEstabelecimento"; a mensagem do validador (linha 85) diz "5 (NFC-e em entrega a domicílio)" [C]. Pelo leiaute que conheço, 4 = NFC-e com entrega a domicílio, 5 = presencial fora do estabelecimento, 9 = não presencial, outros [F] | `NfeEnums.cs`, `NfeIntermediador.cs`, `NfeRequestValidator.cs` | P | Leitura do MOC |
| 6 | **Consumidor opcional ("CPF na nota")**: `dest` ausente, ou só CPF/CNPJ/`idEstrangeiro`; `indIEDest=9`, sem IE; nome e endereço opcionais (endereço exigido com `indPres=4`) | Ajuste SINIEF 19/16 [F]. Identificação obrigatória acima de um valor, que varia por UF [F: confirmar]. Hoje `MapDestinatario` exige endereço completo e razão social [C] | `NfeRequestMapper.MapDestinatario`; validador novo | P | 3 |
| 7 | **Homologação**: o primeiro item deve levar `xProd` = "NOTA FISCAL EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL" | Regra da NFC-e em homologação [F]. Hoje só o `xNome` do destinatário é trocado (linha 205) [C] | `NfeRequestMapper.MapItem` | P | 4 |
| 8 | **CSC e idCSC por emitente e por ambiente**, cifrados | O QR Code versão 2 usa hash com o CSC, emitido pela SEFAZ por contribuinte e por ambiente [F]. `Issuer` não tem esses campos [C]; o padrão a copiar é o de `EncryptedMunicipalUser` | `Issuer.cs`, `IssuerModels.cs`, `IssuerService.cs`, migration, tela do painel, `GET /me` | M | Nada |
| 9 | **QR Code e `infNFeSupl`** (`qrCode` + `urlChave`), versão por UF | Versão 2: Manual de Padrões do DANFE NFC-e e QR Code [F]. Versão 3: lembro de uma NT de 2025 que dispensa o CSC no online e usa assinatura no offline; número e cronograma por UF **a confirmar** [F]. As URLs variam por UF e ambiente (a DLL tem `UrlQrCode*` e `UrlChave*`) | `UnimakeNfeProvider.SignAsync`; `NfeProcXml.cs`; testes com vetores do manual | M | 2 e 8 |
| 10 | **Latência da emissão síncrona** | Por nota, hoje [C]: consulta de idempotência, emitente, transação de numeração, decifragem do PFX, serialização e XSD, duas montagens de `Configuracao` com `CertificadoBase64`, `Task.Run` sobre chamada síncrona, três `SaveChanges`, e o limitador consultando o banco a cada requisição. Não medido | `NfeIssuanceService.TransmitirAsync`; `UnimakeNfeProvider.MontarConfiguracao`; `ClientRateLimiter.cs` linhas 44-47 | M | Medição no spike |
| 11 | **Prazo máximo de resposta ao PDV** (sugestão: 8 a 10 s, depois `202`) | O caixa não pode esperar a SEFAZ. Hoje o tempo é o da lib, sem teto próprio [C] | `NfeIssuanceService`; `Program.cs` | P | 12 |
| 12 | **Reconciliação própria da 65: sem reenvio do XML e sem SVC** | A NFC-e normal tem tolerância curta entre `dhEmi` e recepção (5 minutos, rejeição por emissão atrasada) [F: confirmar código e tolerância]. `NfeReconciliationService` reenvia o mesmo XML e `TentarContingenciaAsync` monta `tpEmis` 6 ou 7, que não existem na NFC-e [C + F]. Para a 65: consultar pela chave; se a SEFAZ não conhece, encerrar como não autorizada | `NfeReconciliationService.cs`, `NfeIssuanceService.TentarContingenciaAsync`, `ContingencyPolicy.cs` | M | 1 |

### 2.2 Contingência offline

| # | O quê | Por quê | Onde | Esf. | Depende de |
|---|---|---|---|---|---|
| 13 | **Recepção de NFC-e já assinada em contingência** (`tpEmis=9`, `dhCont`, `xJust`), com fila de transmissão | O Emissor central não consegue assinar o que acontece com a loja sem internet. Precisa receber o XML assinado na borda, validar (XSD, CNPJ, série reservada, assinatura), guardar e transmitir no prazo | Novo `POST /api/v1/nfce/contingencia`; reaproveita `SignedXml`, `ContingencyPending`, `NextReconcileAt` | G | Decisão de arquitetura (§2.6) |
| 14 | **Prazo de transmissão e alerta** | Até o fim do primeiro dia útil seguinte ou 24 h, conforme a UF [F: confirmar por UF]. Precisa de relógio por documento e alerta antes de vencer | Job novo sobre a fila; `INotificationSender` | P | 13 |
| 15 | **Série de contingência por PDV, numerada na borda** | Sem internet não há alocador central. O `tpEmis` entra na chave, e reusar o mesmo número com outro `tpEmis` gera duplicidade [F]. Proposta: série exclusiva de contingência por terminal, em que o agente é o único dono; o Emissor só registra e recusa emissão online nela | `NumberSequence` (marca de série delegada); cadastro de ponto de venda (lacuna 21) | M | 21 |
| 16 | **Rejeição tardia da nota offline** | A venda já aconteceu e o cliente saiu com o DANFE. Se a SEFAZ rejeitar depois, é preciso corrigir e retransmitir, ou tratar o número. O procedimento é fiscal, não só técnico | Estados novos; webhook `nfce.contingencia.rejeitada`; runbook | M | 13 e contadora |
| 17 | **EPEC da NFC-e (`tpEmis=4`)** | Só cobre SEFAZ fora do ar com internet funcionando; não resolve PDV sem rede. Lembro de estar disponível em poucas UFs, SP entre elas [F: confirmar]. A DLL tem `ContingenciaEPEC` e `RecepcaoEPEC` | Provider novo | M | Opcional, fase posterior |
| **41** | **Emissão central em contingência (`tpEmis=9`) quando a loja tem internet e a SEFAZ está fora** (acrescentada pela revisão de 15/11/2026) | A lacuna 12 tira a SVC da NFC-e e encerra a nota sem resposta como não autorizada; as lacunas 13 a 15 tratam de nota assinada **na borda**. Faltava o caso mais comum: o Emissor, que tem o A1 no cofre, assina em contingência, devolve XML e QR ao caixa e transmite depois. Precisa de: série (a normal do caixa ou uma de contingência central, a decidir, porque o `tpEmis` entra na chave [F]); relógio de prazo de transmissão por documento, com alerta (reusa a lacuna 14); cancelamento por substituição da nota normal que ficou sem resposta (lacuna 18); estado e evento próprios (`nfce.contingencia.emitida`); texto de contingência no bloco `impressao`. Se a UF aceita `tpEmis=9` e qual o prazo: [F], QC-01 | `NfeIssuanceService.TentarContingenciaAsync`, `ContingencyPolicy.cs`, `NfeReconciliationService.cs`; resposta do `POST /api/v1/nfce` com `tipoEmissao = 9` | M | 1, 12, 18; contadora (QC-01) |

### 2.3 Eventos, numeração e impressão

| # | O quê | Por quê | Onde | Esf. | Depende de |
|---|---|---|---|---|---|
| 18 | **Cancelamento com prazo por modelo e UF** e **cancelamento por substituição** (evento 110112) | NFC-e: 30 minutos pelo Ajuste SINIEF 19/16, com variação por UF [F]. Substituição: para a nota normal que ficou sem resposta e foi refeita em contingência; nem toda UF aceita [F]. Hoje há 24 h fixas [C] | `PrazoDeCancelamento.cs`, `NfeEventService.CancelAsync`, `UnimakeNfeEventProvider.cs`, `NfeEventModels.cs` | M | 1 |
| 19 | **Inutilização da 65 e política de número rejeitado** | A dispensa do Ajuste SINIEF 40/2026 (AC, BA, SP) está no repositório para a NF-e; se vale para a NFC-e, **confirmar** [F]. Hoje todo número rejeitado fica queimado [C]; em volume de varejo isso gera muitas lacunas. Avaliar reaproveitar o número quando a rejeição é conclusiva | `InutilizacaoDeNumeracao.cs`, `NfeEventService.cs`, `NfeIssuanceService.cs` | M | Contadora |
| 20 | **DANFE NFC-e para 58 mm e 80 mm** em três saídas: (a) dados estruturados + texto do QR Code na resposta, para o PDV imprimir em ESC/POS; (b) PDF sob demanda, para segunda via e e-mail; (c) "mensagem eletrônica" (`tpImp=5`) | O renderizador atual é A4 da modelo 55 [C]. Chromium por venda é lento demais para o caixa e não existe offline. A ADR 001 registra que `Zeus.Danfe.QuestPdf` só tem DANFE de NFC-e; é candidata, com licença e teste em Linux a conferir. Em contingência o papel leva texto próprio e, em algumas UFs, duas vias [F] | Novos `INfceDanfeRenderer` e bloco `impressao` na resposta; `DanfeService.cs`; `GotenbergPdfConverter` (hoje preso ao `@page` A4) | G | 9 |
| 21 | **Cadastro de ponto de venda** (terminal ligado à série normal e à de contingência) | `NumberSequence` já separa por série [C], mas nada impede dois caixas na mesma série nem reserva série para a borda. O alocador é correto sob concorrência; série por caixa só reduz disputa do bloqueio e organiza lacunas | Entidade nova, migration, validação na emissão | M | Nada |
| 22 | **Filial com o A1 da matriz** | O Emissor recusa certificado com CNPJ diferente do emitente (`CertificateService.cs` linha 72) [C]. A SEFAZ costuma aceitar A1 de mesma raiz de CNPJ [F: confirmar]. Sem isso, cada loja precisa do próprio A1 | `CertificateService.cs`, `Certificate.cs` | P | Decisão do PO |
| 23 | **Idempotência com hash do payload** | Repetir a chave com payload diferente devolve a nota antiga em silêncio [C]. No PDV, reuso acidental da chave esconderia uma venda | `NfeIssuanceService.IssueAsync`, `FiscalDocument` | P | Nada |

### 2.4 Tributação, pagamento e campos do varejo

| # | O quê | Por quê | Onde | Esf. | Depende de |
|---|---|---|---|---|---|
| 24 | **CST e CSOSN permitidos na NFC-e** | O MOC restringe os códigos na 65; lembro de CSOSN 102, 103, 300, 400, 500 e 900 e de CST 00, 20, 40, 41, 60 e 90 [F: confirmar a lista]. Hoje o validador aceita todos [C]. Sem ICMS destacado nos CSOSN 102 e 500 já é o comportamento do mapeador [C] | `NfceRequestValidator` | P | 3 |
| 25 | **PIS/COFINS do Simples (CST 49 e 99) e por quantidade (03)** | `MapPis` e `MapCofins` só tratam 01 e 02 como alíquota; todo o resto vira `PISNT`/`COFINSNT` [C, linhas 297-334]. CST 49 ou 99 exige o grupo `PISOutr`/`COFINSOutr`, então hoje reprovaria no XSD (erro 215) [F sobre o leiaute]. Afeta também a NF-e de cliente do Simples | `NfeRequestMapper.cs` | P | Nada; corrigir já |
| 26 | **Grupo de pagamento completo**: `vTroco`, `xPag` (para `tPag` 99), grupo `card` (`tpIntegra`, CNPJ da credenciadora, `tBand`, `cAut`), CNPJ do recebedor e terminal | `PagamentoRequest` só tem `meio` e `valor`, e `MapPagamentos` não emite troco nem cartão [C]. Regra do leiaute: soma dos pagamentos menos `vNF` = troco [F]. Algumas UFs exigem o grupo de cartão [F: confirmar quais]. A DLL tem os campos. Com maquininha externa vai `tpIntegra=2`; o contrato já nasce com os campos do TEF | `NfeRequest.cs` (`PagamentoRequest`), `NfeEnums.cs` (falta 91), `NfeRequestMapper.MapPagamentos`, validador | M | 3 |
| 27 | **Tributos aproximados (`vTotTrib`)** por item e total | Lei 12.741/2012 para venda a consumidor [F]. Não existe no contrato [C] | `ItemNfeRequest`, `MapItem`, `MapTotais` | P | 3 |
| 28 | **Código de benefício fiscal (`cBenef`)** | Exigido por várias UFs conforme o CST [F: lista por UF a confirmar]. Não existe no contrato [C] | `ItemNfeRequest`, `MapItem` | P | 3 |
| 29 | **IBS/CBS na NFC-e e no Simples** | O mapeador serve [C]. A exigência hoje é só do regime normal [C]. Para Simples e MEI, e para a 65 especificamente, a obrigatoriedade e a data **precisam de confirmação** [F] | `NfeRequestValidator.Grupos.cs`, `NfeOptions.cs` | P | Contadora e NT vigente |
| 30 | **CFOP e GTIN** | A NFC-e aceita um conjunto restrito de CFOP de venda interna e a SEFAZ valida GTIN em cadastro central [F]. Hoje só se valida o formato [C] | Validador novo; `SefazRejectionCatalog.cs` com os códigos da 65 | P | 3 |

### 2.5 Escala, segurança, plataforma e testes

| # | O quê | Por quê | Onde | Esf. | Depende de |
|---|---|---|---|---|---|
| 31 | **Limite de requisições para volume de caixa** | Padrão de 60 por minuto por cliente (`ClientModels.cs`), contado em memória e por instância [C]. Um supermercado com dez caixas passa disso. Rever unidade (por emitente ou por terminal), isentar a emissão do teto global ou subir muito o padrão | `ClientRateLimiter.cs`, `ClientApplication.cs` | P | Nada |
| 32 | **Mais de uma instância** | Limitador em memória e jobs de reconciliação escritos para instância única [C; não conferi se o lote da reconciliação usa `SKIP LOCKED`, o outbox usa]. Hoje a API é ponto único de falha do caixa | `NfeReconciliationJob.cs`, `ClientRateLimiter.cs`, `docker-compose.yml` | M | Medição |
| 33 | **Lote só para contingência** | A autorização síncrona da NFC-e é de uma nota por vez; lote não ganha nada no caminho online [F]. Serve para o agente subir N notas offline de uma vez, com concorrência por emitente para não cair em consumo indevido (656) | Endpoint da lacuna 13 | P | 13 |
| 34 | **Armazenamento** | Cada documento guarda `RequestPayload`, `SignedXml` e `AuthorizedXml` [C]. Estimativa minha, não medida: dezenas de GB por loja de supermercado por ano. Avaliar descartar o `SignedXml` após autorizar, compressão e particionamento por mês | `FiscalDocumentConfiguration.cs`, `RetencaoDeDados.cs` | M | Medição |
| 35 | **CSRT por UF** | `infRespTec` já sai [C], mas com um único `IdCsrt`/`Csrt` global. O CSRT é fornecido por cada SEFAZ à software house; várias UFs exigem o grupo na NFC-e e algumas o hash [F: confirmar quais] | `NfeOptions.cs` (`ResponsavelTecnicoOptions`), `NfeRequestMapper.MapResponsavelTecnico` | P | Cadastro nas SEFAZ |
| 36 | **Escopos `nfce:emitir` e `nfce:eventos`** | Separar do `nfe:*`: a chave do PDV é a mais exposta e não deve emitir modelo 55. `nfce:emitir` entra em `BlockedByLicense`; decidir o que acontece com a loja se a licença bloquear | `ApiScopes.cs`, `ScopePolicies.cs`, painel, `docs/integracao/README.md` | P | Nada |
| 37 | **Eventos de webhook `nfce.*`** | Hoje só `nfe.*` e `nfse.*` [C]. Propostos: `nfce.autorizada`, `nfce.rejeitada`, `nfce.cancelada`, `nfce.contingencia.recebida`, `nfce.contingencia.rejeitada`, `nfce.contingencia.prazo`. Como o caixa recebe resposta síncrona, recomendar assinar só os assíncronos | `PublicacaoDeEventos.cs`, `SituacaoDaNota.cs`, guia | P | 1 |
| 38 | **Dívida E14 (painel preso ao tenant da WSP)** | Documentada em `docs/plano-finalizacao.md` e no `CLAUDE.md` como bloqueio ao segundo cliente [C]. O PDV é produto para muitos clientes | `ApiKeyAuthenticationHandler.cs`, `EmissorFiscalClientHttp.cs` | M | Pré-requisito comercial |
| 39 | **Diferenças por UF em tabela de configuração, não em `if`** | Prazo de cancelamento, contingência aceita, versão do QR Code, exigência de `infRespTec`/CSRT, `cBenef`, limite de identificação, inutilização. **SP**: a substituição do SAT pela NFC-e e as datas não estão no repositório; **confirmar**, e confirmar também se SP aceita `tpEmis=9` [F]. **SC e CE** têm histórico de exigências próprias de software e equipamento; **confirmar antes de prometer** [F] | Novo `NfceRegrasPorUf` no domínio, no padrão de `InutilizacaoDeNumeracao` e `HorarioDaUf` | M | Pesquisa por UF |
| 40 | **Testes e homologação por UF** | Ver §2.7 | `tests/EmissorFiscal.UnitTests`, `Dockerfile.tests` | G, contínuo | Clientes-piloto |

### 2.6 Quem assina offline: opções e recomendação

O ponto de partida é técnico: na contingência offline o DANFE precisa do QR Code, e o QR Code offline deriva do XML assinado (digest na versão 2, assinatura na versão 3) [F]. Sem certificado ao alcance do caixa não existe NFC-e em contingência; existe apenas um recibo.

| Opção | Como funciona | A favor | Contra e ressalva legal |
|---|---|---|---|
| **(a) Fila + recibo "pendente"** | O PDV imprime comprovante não fiscal e enfileira; o Emissor emite como nota normal quando a rede volta | Nada novo no Emissor; o A1 não sai do cofre | **Não é contingência fiscal.** O consumidor sai sem documento fiscal. A nota normal emitida depois leva `dhEmi` da transmissão, não da venda, ou é rejeitada por atraso [F]. Na prática é venda sem documento no ato; o risco de autuação é do cliente. Só com parecer escrito da contadora |
| **(b) Agente de assinatura na loja** | Um serviço local (por loja, ou por caixa em loja pequena) guarda um certificado, monta, assina e numera a NFC-e `tpEmis=9`, imprime o DANFE de contingência e envia o XML ao Emissor, que transmite e guarda | É a contingência prevista na norma; funciona sem internet; o servidor de loja cobre a queda de link, que é o caso comum | **Há um certificado do CNPJ fora do cofre, e ele não tem alcance limitado** (ver "O risco do certificado na loja", abaixo). É software a mais para distribuir, assinar e atualizar. Depende de a UF aceitar `tpEmis=9` [F] |
| **(c) EPEC** | Registra um evento resumido na SEFAZ e transmite a nota depois | Sem certificado na borda | **Precisa de internet**, então não atende "PDV offline". Disponível em poucas UFs [F] |

> **Divergência entre as análises.** A recomendação abaixo é a da análise do Emissor. A análise de arquitetura do PDV ([rascunho E](adr/rascunho-E-acoplamento-fiscal.md)) recomenda o contrário: recibo não fiscal agora e NFC-e na volta da rede, com 4G de reserva, e deixa o assinador local como última opção. As duas posições estão lado a lado na [PP-01](06-pendencias-riscos.md#pp-01--venda-sem-internet-em-loja-que-emite-nfc-e).

> **Decidido pelo PO em 15/11/2026: (b).** Contingência offline de verdade, com certificado instalado no computador da loja e as proteções abaixo: **um servidor de assinatura por loja, não por caixa**; chave não exportável; certificado separado do que fica no cofre central, revogável sem parar o resto; série própria de contingência, com alerta de uso fora dela; inventário por loja. A opção (a) fica só como modo degradado, desligado por padrão. Ficam abertas apenas as perguntas à contadora por UF (se a UF aceita `tpEmis=9` e o prazo de transmissão) e a confirmação com a certificadora sobre os tipos de certificado. **As lacunas 13 a 16 e 21 passam a escopo firme.**

**Recomendação desta análise: (b)**, na forma de agente de loja com A1 dedicado, e o Emissor central como receptor, transmissor e guardião (lacunas 13 a 16).
- **O risco do certificado na loja (texto corrigido pela revisão de 15/11/2026).** A análise original dizia que o certificado da loja teria "alcance e revogação controlados". Isso não se sustenta: um A1 e-CNPJ **não tem escopo técnico**. Quem copia o arquivo e a senha assina **qualquer** documento em nome do CNPJ (nota de qualquer modelo e série, procuração eletrônica, acesso a portais), não só a série de contingência. Isto é conhecimento geral, **não conferido com uma Autoridade Certificadora**; confirmar se existe tipo de certificado restrito a documento fiscal antes de decidir.
- Por isso a opção (b) só é aceitável com estas proteções, todas obrigatórias:
  1. **Chave não exportável:** o certificado é instalado no TPM do equipamento ou no repositório de certificados do Windows com a chave marcada como não exportável. O arquivo `.pfx` e a senha nunca ficam em disco na loja, e o agente nunca os lê.
  2. **Certificado próprio da loja**, diferente do que está no cofre do Emissor, para que revogar um não derrube o outro [F: emitir mais de um A1 por CNPJ é prática comum; confirmar com a AC].
  3. **Inventário por loja:** qual certificado (número de série, validade) está em qual equipamento, quem instalou e quando; conferido a cada sincronização do agente.
  4. **Procedimento e custo de revogação** escritos: quem revoga, em quanto tempo, quanto custa reemitir, e o que a loja faz enquanto isso (cai no modo degradado).
  5. **Alerta de uso fora da série:** o Emissor avisa quando chega nota assinada por certificado de loja fora da série de contingência reservada àquele ponto de venda, ou de modelo diferente de 65. Isso detecta uso indevido **só do que passa pelo Emissor**; uso do certificado fora do sistema não é detectável.
- O requisito original do PO ("o PDV não guarda o A1") fica atendido só em parte: o A1 do cofre não sai dele, mas passa a existir **outro certificado do mesmo CNPJ dentro da loja**. Isso precisa do aceite explícito do PO.
- A opção (a) pode existir como modo degradado, desligado por padrão e habilitado por cliente mediante aceite formal. Não deve ser o desenho da primeira versão.
- A opção (c) fica para depois, só onde a UF oferecer.

O agente em si é entrega do PDV (BemAqui). No Emissor entram: o endpoint de recepção, a reserva de série, a distribuição de configuração ao agente (URLs do QR Code, idCSC e, se a versão 2 ainda valer na UF, o CSC) e a regra por UF.

### 2.7 Estratégia de testes e homologação

- **Unidade e XSD** [C para o padrão existente]: repetir `NfeRequestMapperTests` e `IcmsCoberturaTests` para a 65: `ide`, consumidor ausente, pagamento com troco e cartão, CSOSN, PIS/COFINS 49 e 99, `infNFeSupl`. Rodar em container Linux, como manda o `CLAUDE.md`.
- **QR Code**: testes com os vetores de exemplo do manual oficial, para versão 2 e, se confirmada, 3; online e offline.
- **Contrato**: fixture do payload do PDV, no padrão de `DolibarrPayloadContratoTests`, e suíte de contrato do lado do BemAqui.
- **Ciclo completo com SEFAZ falsa**: timeout com prazo de resposta, rejeição, duplicidade, reconciliação sem reenvio, recepção de contingência, prazo vencendo.
- **Carga**: latência p50 e p95 por etapa e disputa da numeração com N caixas na mesma série e em séries separadas.
- **Homologação por UF**: exige CNPJ credenciado, com IE e CSC de homologação naquela UF. Não dá para homologar "em tese" num estado sem estabelecimento. Caminho: primeiro SP com o emitente de teste do tenant `demo` (ADR 0027 §6); depois um cliente-piloto por UF, priorizando por autorizadora (as UFs que usam SVRS se comportam de forma parecida; a lista exata vem da configuração da Unimake e do Portal Nacional [F]).
- **Roteiro por UF**: venda simples, com CPF, com cartão, cancelamento no prazo, cancelamento por substituição, contingência offline com transmissão posterior, rejeição proposital, inutilização se aplicável, DANFE lido pelo aplicativo da SEFAZ.

---

## 3. Esboço do contrato da API para NFC-e

Nomes em pt-BR, coerentes com `NfeRequest` e `NfeResponse`. Campos marcados com `*` não existem hoje.

**Emissão online**

```http
POST /api/v1/nfce                      (escopo nfce:emitir)
X-Api-Key: <chave>
Idempotency-Key: pdv-03-venda-000184
```

```json
{
  "emitente": "12345678000190",
  "serie": 3,
  "pontoDeVenda": "CX-03",
  "dataEmissao": "2026-11-15T10:42:07-03:00",
  "indicadorPresenca": 1,
  "consumidor": {
    "cpf": "12345678909",
    "nome": "FULANO DE TAL",
    "email": "fulano@exemplo.com.br",
    "endereco": null
  },
  "itens": [
    {
      "codigo": "7891000100103",
      "descricao": "LEITE UHT INTEGRAL 1L",
      "ncm": "04012010",
      "cest": "1701600",
      "cfop": "5102",
      "unidadeComercial": "UN",
      "quantidade": 2,
      "valorUnitario": 5.49,
      "valorTotal": 10.98,
      "valorDesconto": 0,
      "codigoBarras": "7891000100103",
      "codigoBeneficioFiscal": null,
      "valorTributosAproximados": 1.87,
      "impostos": {
        "origem": 0,
        "icms": { "cst": "102" },
        "pis": { "cst": "49" },
        "cofins": { "cst": "49" },
        "ibsCbs": null
      }
    }
  ],
  "pagamentos": [
    {
      "meio": 3,
      "valor": 10.98,
      "descricao": null,
      "cartao": {
        "tipoIntegracao": 2,
        "cnpjCredenciadora": null,
        "bandeira": null,
        "autorizacao": null,
        "cnpjRecebedor": null,
        "terminal": null
      }
    }
  ],
  "troco": 0,
  "informacoesComplementares": "Operador 12",
  "impressao": { "formato": "dados", "largura": 80 }
}
```

Notas:
- `pontoDeVenda`*, `consumidor`* (opcional por inteiro), `codigoBeneficioFiscal`*, `valorTributosAproximados`*, `descricao`* e `cartao`* no pagamento, `troco`* e `impressao`* são novos.
- `tipoOperacao`, `finalidade`, `consumidorFinal`, `naturezaOperacao`, `transporte`, `cobranca` e `notasReferenciadas` não existem neste contrato: o serviço os fixa.
- `tipoIntegracao`: 1 = TEF integrado, 2 = maquininha não integrada. Na primeira versão do PDV vai 2 e os demais campos do cartão ficam nulos, salvo UF que os exija [F].
- `impressao.formato`: `dados` (padrão), `pdf` ou `nenhum`; `largura`: 58 ou 80.

**Resposta** (mesmos códigos HTTP da NF-e: 201 autorizada, 202 sem desfecho, 422 rejeitada)

```json
{
  "id": "019fca35-…",
  "situacao": 2,
  "chave": "35261112345678000190650030000001841123456786",
  "serie": 3,
  "numero": 184,
  "protocolo": "135260000000001",
  "autorizadaEm": "2026-11-15T10:42:08-03:00",
  "rejeicao": null,
  "criadaEm": "2026-11-15T13:42:07Z",
  "tipoEmissao": 1,
  "qrCode": "https://…?p=3526…|2|1|1|…",
  "urlConsulta": "https://…/consulta",
  "impressao": {
    "largura": 80,
    "emitente": { "razaoSocial": "…", "cnpj": "…", "inscricaoEstadual": "…", "endereco": "…" },
    "itens": [ { "codigo": "…", "descricao": "…", "quantidade": 2, "unidade": "UN", "valorUnitario": 5.49, "valorTotal": 10.98 } ],
    "totais": { "quantidadeItens": 1, "valorProdutos": 10.98, "desconto": 0, "valorAPagar": 10.98, "troco": 0 },
    "pagamentos": [ { "descricao": "Cartão de Crédito", "valor": 10.98 } ],
    "consumidor": "CPF 123.456.789-09",
    "chaveFormatada": "3526 1112 …",
    "mensagemFiscal": null,
    "tributosAproximados": "Trib aprox R$ 1,87 Fonte: IBPT",
    "contingencia": false
  }
}
```

`tipoEmissao`*, `qrCode`*, `urlConsulta`* e `impressao`* são novos; os demais campos já existem em `NfeResponse`.

**Demais rotas propostas**

| Rota | Escopo | Observação |
|---|---|---|
| `GET /api/v1/nfce/{chave}` · `/xml` · `/danfe?largura=80` | `consultar` | DANFE em PDF sob demanda |
| `POST /api/v1/nfce/{chave}/consultar` | `consultar` | Mesma proteção contra o 656 |
| `POST /api/v1/nfce/{chave}/cancelamento` | `nfce:eventos` | `{ "justificativa": "…", "chaveSubstituta": null }`; com `chaveSubstituta`, vira cancelamento por substituição |
| `POST /api/v1/nfce/inutilizacao` · `GET /api/v1/nfce/numeracao/lacunas` | `nfce:eventos` · `consultar` | Como na NF-e |
| `POST /api/v1/nfce/contingencia` | `nfce:emitir` | `{ "emitente", "pontoDeVenda", "notas": [ { "xmlAssinado": "<base64>" } ] }`; idempotente pela chave de acesso; responde 202 com a situação de cada nota |
| `GET /api/v1/nfce/pontos-de-venda/{id}/configuracao` | `nfce:emitir` | Para o agente: série normal, série de contingência, ambiente, URLs e versão do QR Code, idCSC |

---

## 4. Plano em fases e decisões

### 4.1 Fases

| Fase | Conteúdo (lacunas) | Semanas | Paralelo? |
|---|---|---|---|
| **0. Spike e decisões** | Lacuna 2 (Unimake NFC-e em Linux, QR Code, medição de latência); correção da 25; rótulos da 5; tabela inicial por UF (39) com a contadora | 1 a 1,5 | A pesquisa por UF corre junto com o spike |
| **1. NFC-e online** | 1, 3, 4, 6, 7, 8, 9, 11, 12, 24, 26, 27, 28, 30, 36, 37 | 4 a 5 | Com duas pessoas: contrato, validador e pagamento de um lado; provider, CSC e QR Code do outro |
| **2. Eventos e impressão** | 18, 19, 20, 23, **41** | 2 a 3 | Paralela à fase 1 depois da lacuna 1; a impressão só depende da 9 |
| **3. Contingência offline (escopo firme desde 15/11/2026)** | 13, 14, 15, 16, 21, 33, mais o contrato com o agente, o inventário de certificados por loja e o alerta de uso fora da série | 3 a 4 | O agente (BemAqui) anda junto, depois de fechado o contrato da fase 0 |
| **4. Escala e plataforma** | 10, 31, 32, 34, 35, 38, 22 | 2 a 3 | Independente; a 38 pode começar já |
| **5. Homologação por UF** | 40 e ajustes da 39; EPEC (17) e 29 conforme confirmação | 2 para SP, depois contínuo | Por cliente-piloto |

Total aproximado: 12 a 16 semanas com uma pessoa, 8 a 10 com duas. É estimativa sem medição; não inclui o agente de loja nem o PDV, nem a espera por credenciamento, CSC e CSRT nas SEFAZ.

Ordem que não pode inverter: fase 0 antes de tudo; lacuna 12 antes de qualquer teste real de NFC-e; lacuna 38 antes do segundo cliente; a fase 3 dependia da decisão 1 abaixo, que o PO tomou em 15/11/2026 (assinatura na loja): ela passa a escopo firme e pode começar assim que a fase 0 fechar o contrato com o agente de loja, em paralelo com as fases 1 e 2.

### 4.2 Decisões para o PO

As decisões abaixo estão consolidadas, com as do produto e da arquitetura, em [06-pendencias-riscos.md](06-pendencias-riscos.md) (PP-01, PP-09, PP-13 a PP-15 e PP-29 a PP-33). A lista original fica aqui para rastreio.

1. **Arquitetura offline**: aceitar a opção (b), com A1 dedicado na loja? Se não, aceitar o risco fiscal da opção (a), por escrito? **Decidido em 15/11/2026: (b), com um servidor de assinatura por loja.**
2. **Agente por loja ou por caixa** (**decidido em 15/11/2026: por loja**), e em que plataformas o PDV roda (Windows, Android, navegador). Navegador puro não assina offline.
3. **Caminho da venda**: PDV → BemAqui → Emissor (dois saltos, como na ADR 0027) ou PDV → Emissor direto com chave por loja. Define latência e ponto de falha.
4. **Meta de latência e volume**: tempo máximo no caixa, vendas por minuto no pico, número de lojas no primeiro ano. Sem isso a fase 4 é chute.
5. **UFs do primeiro lançamento**, em ordem. Cada UF custa pesquisa, credenciamento e piloto.
6. **Filial**: A1 por loja ou A1 da matriz para todas (lacuna 22).
7. **Licença bloqueada**: a loja para de vender com nota? Sugestão: carência, como já se faz com cancelamento e consulta.
8. **Responsável técnico**: qual CNPJ assina como software house e quem faz o cadastro do CSRT em cada SEFAZ.
9. **Biblioteca do DANFE em PDF**: `Zeus.Danfe.QuestPdf` (conferir licença) ou modelo HTML próprio com Gotenberg.
10. **Número rejeitado na NFC-e**: queimar e inutilizar, como hoje, ou reaproveitar quando a rejeição é conclusiva.
11. **Resolver a E14 agora**, antes do PDV, e por qual dos dois caminhos já descritos em `docs/plano-finalizacao.md`.

### 4.3 Perguntas para a contadora

Consolidadas em [06-pendencias-riscos.md](06-pendencias-riscos.md), seção "Perguntas para a contadora" (QC-01 a QC-17).

1. Por UF de interesse: prazo de cancelamento da NFC-e, se aceita contingência offline (`tpEmis=9`), prazo de transmissão e se exige duas vias.
2. SP: situação e datas da troca do SAT pela NFC-e, e a contingência aceita lá.
3. Valor a partir do qual o consumidor deve ser identificado, por UF.
4. Para clientes do Simples e MEI: CSOSN por tipo de produto (102, 500 e os demais), CST de PIS/COFINS (49, 99 ou outro) e se IBS/CBS já se preenche na NFC-e, e desde quando.
5. CFOP usados no varejo de cada segmento (supermercado, padaria, restaurante, oficina) e como tratar taxa de serviço e gorjeta no restaurante.
6. Oficina: peça em NFC-e ou NF-e, e o serviço em NFS-e. Hoje o Emissor só tem NFS-e de Caçapava em uso e o padrão Nacional nunca foi exercitado [C].
7. `cBenef`: em que UFs e CST se aplica aos clientes-alvo.
8. Tributos aproximados (Lei 12.741): fonte da alíquota (tabela IBPT) e texto no cupom.
9. Grupo de cartão: UFs que exigem credenciadora, bandeira e autorização mesmo sem TEF.
10. A dispensa de inutilização do Ajuste SINIEF 40/2026 vale para a NFC-e?
11. Nota offline rejeitada depois da venda: procedimento aceito.
12. Parecer sobre a opção (a), se o PO quiser mantê-la como modo degradado.

### 4.4 O que não foi verificado

- Nenhuma NFC-e foi montada, assinada ou transmitida; o suporte da Unimake foi inferido dos nomes de símbolos na DLL compilada.
- Não medi latência nem volume; os números de armazenamento e esforço são estimativas.
- Números de NT, prazos, listas de CST/CFOP e regras por UF marcados com [F] vêm de memória e podem estar desatualizados em 11/2026.
- Não li o adaptador `EmissorFiscalHttpEmitter.cs` por inteiro; do lado do BemAqui conferi a ADR 0027 e o `EmissorFiscalRateGate.cs`.
- **Nota de segurança:** existe em `D:\GIT\EmissorFiscal\docs\Certificado\` um arquivo `.pfx` e um arquivo de senha no diretório de trabalho. Não estão versionados (`git ls-files` vazio) e não foram abertos nesta análise, mas a regra do `CLAUDE.md` de lá pede que não fiquem no repositório. Ação registrada na [PP-34](06-pendencias-riscos.md).

### Arquivos centrais para a implementação

- `D:\GIT\EmissorFiscal\src\EmissorFiscal.Providers\Nfe\Unimake\NfeRequestMapper.cs` (`ide`, destinatário, pagamentos, PIS/COFINS, chave)
- `D:\GIT\EmissorFiscal\src\EmissorFiscal.Providers\Nfe\Unimake\UnimakeNfeProvider.cs` (modelo fixo em 55, CSC e QR Code, configuração e certificado por chamada)
- `D:\GIT\EmissorFiscal\src\EmissorFiscal.Application\Nfe\NfeIssuanceService.cs` (fluxo de emissão, contingência SVC, idempotência, numeração)
- `D:\GIT\EmissorFiscal\src\EmissorFiscal.Application\Nfe\NfeReconciliationService.cs` (reenvio do XML, que não vale para a 65)
- `D:\GIT\EmissorFiscal\src\EmissorFiscal.Application\Models\NfeRequest.cs` (base do novo `NfceRequest`; `PagamentoRequest` e `ItemNfeRequest` a estender)
- `D:\GIT\EmissorFiscal\src\EmissorFiscal.Domain\PrazoDeCancelamento.cs` e `D:\GIT\EmissorFiscal\src\EmissorFiscal.Domain\Entities\Issuer.cs` (prazo por modelo e UF; CSC por emitente)
