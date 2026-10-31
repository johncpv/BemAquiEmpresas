# FIS-02a — Configurações → Fiscal e Certificado digital: parâmetros pré-configurados "a confirmar", A1 direto ao Emissor, permissões do módulo `fiscal`

| Campo | Valor |
|---|---|
| ID | FIS-02a |
| Título | Telas **Configurações → Fiscal** e **Configurações → Fiscal → Certificado digital**: regime, CRT, CNAE, série exclusiva, ambiente, CFOP/CST padrão por cenário, alíquotas, tributos no custo do projeto, código de serviço, ISS e retenções da NFS-e, IBS/CBS, prazo de cancelamento e `transp`/volumes, tudo pré-configurado com os valores propostos e o chip "A confirmar com a contadora" com "Marcar como conferido" auditado; envio do A1 direto ao cofre do EmissorFiscal (o BemAqui guarda só validade, titular/CNPJ e thumbprint), alerta de vencimento; ações `fiscal.approve` e `fiscal.import_incoming` no catálogo e seed patch `fiscal-permissions` |
| Tamanho | G (re-estimada de M: passa a entregar as duas telas de Configurações com todos os parâmetros fiscais pré-configurados e a conferência auditada, e o envio do A1 ao Emissor) |
| Status | Done (validação real no `demo` e conferência da contadora: pré-condição externa) |
| Requisitos | R-FIS-2 (cadastro fiscal do tenant: certificado A1, regime, IE, série, ambiente) · R-FIS-4 · R-FIS-7 · R-FIS-8 · R-COM-2 · R-PRP-1 · R-PRP-5 · R-SEC-10 · R-SEC-5 |
| Dependências | FIS-01 (`IFiscalEmitter`, `GetAccountAsync`, `UploadCertificateAsync`, `fiscal_environment_allowed`), CAD-06 (`company_profile`: IE, IM, endereço, `city_ibge`), CAD-01b (consulta do cartão CNPJ), FND-06 (`ModuleCatalog`), CAD-01a (seed patch), FND-09e (`ConfirmDialog` com 2FA), BNK-01a (`EnvironmentBanner`), BNK-01b (`ToConfirmChip` "A confirmar com a contadora" com "Marcar como conferido", reutilizado aqui, sem criar outro); ADR 0042 aceita |
| Bloqueia | FIS-02b (regras nascem do mesmo seed), FIS-03a (emissão), FIS-04a (`fiscal.import_incoming`), FIS-06 (NFS-e) |
| Ordem de execução | 8 (Marco 5.4 "Nota de saída") |

## User story
Como **proprietário**, quero **encontrar os dados fiscais da empresa já preenchidos com o que foi proposto, conferir cada um com a contadora e enviar o certificado digital sem que ele fique guardado no sistema**, para **as notas saírem com os dados certos, a emissão em produção só ligar com tudo conferido e nunca parar por certificado vencido**.

## Contexto e referências
- PRD R-FIS-2; `05-backlog` FIS-02 ("Certificado A1, série, ambiente, regras por operação (conteúdo da contadora). CA: alerta 30 dias antes do vencimento do certificado").
- **Revisão pré-Fases 4 a 8** ([2026-10-21-revisao-pre-fases4-8.md](../revisoes/2026-10-21-revisao-pre-fases4-8.md)): D6 (2FA de novo ao enviar o A1), D12 (A1 só no cofre do Emissor; `fiscal.approve` reservado; `fiscal.import_incoming`; série exclusiva obrigatória), D4 (venda mista); tabela 3.2, pontos 35 a 39 (e 9, 11, 41, 73 nos valores pré-configurados); §4.3 baixos (PFX com `EphemeralKeySet`); §4.4 padrão 6.
- **Decisão do PO de 21/10/2026 (novo requisito):** tudo configurável em Configurações, pré-configurado com os valores propostos, cada valor não conferido com o chip "A confirmar com a contadora" e a ação "Marcar como conferido"; produção exige os campos fiscais críticos conferidos; em homologação não bloqueia. **ADR 0042** ([0042-configuracoes-fiscais-e-integracoes.md](../adr/0042-configuracoes-fiscais-e-integracoes.md)).
- **ADR 0027** ([0027-integracao-emissorfiscal.md](../adr/0027-integracao-emissorfiscal.md): A1 no Emissor, lacuna L-h: senha em `[FromForm]`, CNPJ conferido sem aceitar falha, rota de certificado sem `admin`). **ADR 0003, adendo de 21/10/2026** (`fiscal.approve`, `fiscal.import_incoming`). **ADR 0008, adendo de 21/10/2026** (`fiscal.approve` reservado ao Proprietário; D6). ADR 0016 adendo M6 (cada módulo é dono dos próprios parâmetros; Configurações monta a tela). **ADR 0034** ([0034-promocao-a-producao.md](../adr/0034-promocao-a-producao.md)).
- `03-modelo-dados` → `fiscal.fiscal_settings` (`regime`, `ie`, `cnae`, `certificate_secret`, `certificate_expiry`, `environment`, `nfe_series`, `next_number`; o `next_number` sai na FIS-01 e o `certificate_secret` sai aqui, D12).
- `06-pendencias-riscos` P-01 (regime: o EmissorFiscal registra "Lucro Presumido desde 01/01/2026"), P-02 (CNAE de fabricação em providência pela Konebras), P-03 (IBS/CBS).
- EmissorFiscal: série exclusiva acordada com o contador (L3 de lá); Caçapava IBGE 3508504 (ADR 005 de lá); alertas próprios do A1 em 60/30/15/7/1 dias.
- `ModuleCatalog` atual: `fiscal` com `view`, `create`, `edit` e nenhum grupo do seed com o módulo.

## Valores pré-configurados (seed da tela, todos "A confirmar com a contadora")
| Seção | Campo | Valor pré-configurado | Crítico para produção |
|---|---|---|---|
| Regime e atividades | Regime tributário | Lucro Presumido (P-01) | Sim |
| Regime e atividades | CRT | 3 – Regime Normal (derivado do regime) | Sim |
| Regime e atividades | CNAE principal e secundários | os do cartão CNPJ consultado (CAD-01b); hoje 4329-1/03 principal; fabricação em inclusão (P-02) | Sim |
| Série e ambiente | Série exclusiva da NF-e | 2, com a `<Dica>` "A série 1 costuma ser a do emissor atual. Confirme com a contadora a série reservada para o sistema." | Sim (obrigatória em produção) |
| Série e ambiente | Ambiente | Homologação (produção só na GOL-01b, pela flag `fiscal_environment_allowed`) | — |
| Operações padrão | Venda de produção própria a contribuinte em SP | CFOP 5101 | Sim |
| Operações padrão | Venda de produção própria a não contribuinte em SP | CFOP 5101 (consumidor final) | Sim |
| Operações padrão | Venda de produção própria a contribuinte de outro estado | CFOP 6101 | Sim |
| Operações padrão | Venda de produção própria a não contribuinte de outro estado | CFOP 6107, **DIFAL bloqueado** (a emissão recusa, FIS-02b) | Sim |
| Operações padrão | Revenda de peça (SP / outro estado) | CFOP 5102 / 6102 | Sim |
| Operações padrão | Venda mista (D4) | NF-e de venda do equipamento na expedição, sem nota de remessa para obra, com o endereço da obra nas informações complementares; instalação em NFS-e no aceite técnico | Sim |
| CST padrão | ICMS / PIS e COFINS / IPI | ICMS 00; PIS e COFINS 01 (cumulativo); IPI 50 | Sim |
| Alíquotas | ICMS interno SP | 18% | Sim |
| Alíquotas | ICMS interestadual | 12% (Sul e Sudeste, exceto ES), 7% (Norte, Nordeste, Centro-Oeste e ES), 4% (mercadoria de origem importada) | Sim |
| Alíquotas | PIS / COFINS | 0,65% / 3% (cumulativo) | Sim |
| Alíquotas | IPI | pela TIPI do NCM do item (CAD-02a) | Sim |
| Custo do projeto | Tributos que entram no custo do projeto | ICMS, PIS, COFINS, ISS, IBS/CBS; IPI fora (ponto 41) | Não (gerencial) |
| NFS-e | Item da LC 116 | 7.02, com a `<Dica>` "Alternativa: 14.06 (instalação e montagem de aparelhos). Com 7.02, o ISS é devido no município da obra; com 14.06, no da empresa." | Sim (só NFS-e) |
| NFS-e | Código municipal de Caçapava (`CodSrv`, `NN.NN.NN`), CTN e NBS | vazios até a contadora (a lista da prefeitura não foi consultada); vazio bloqueia só a emissão da NFS-e em produção | Sim (só NFS-e) |
| NFS-e | ISS | 5% (teto da LC 116, conservador para o projetado até a contadora); local de incidência: município da obra (item 7.02) | Sim (só NFS-e) |
| NFS-e | Retenções | ISS retido pelo tomador: não; retenções federais: nenhuma | Sim (só NFS-e) |
| Reforma Tributária (P-03) | IBS/CBS na NF-e e na NFS-e | CST 000, `cClassTrib` 000001; CBS 0,9% e IBS 0,1% (alíquotas de teste de 2026) | Sim |
| Prazos | Prazo de cancelamento da NF-e | 24 h da autorização | Sim |
| Transporte e cobrança | Modalidade do frete | 0 – por conta do remetente (CIF) | Sim |
| Transporte e cobrança | Volumes | espécie "VOLUME"; quantidade, peso bruto e transportadora do romaneio (PRD-06b) | Sim |
| Transporte e cobrança | Duplicatas na NF-e | não (a parcela do contrato é só cobrança, D4) | Sim |

## Critérios de aceite
**Catálogo e seed**
1. **Dado** o `ModuleCatalog`, **quando** esta story entra, **então** o módulo `fiscal` ganha a ação padrão `approve` ("cancelar ou inutilizar nota fiscal e ativar regra fiscal"), reservada ao Proprietário (ADR 0008 adendo: só um membro do Proprietário concede; fora da delegação; 2FA obrigatório para quem a tem) e a ação específica `import_incoming` ("importar nota fiscal de entrada", ADR 0003 adendo); o seed patch `fiscal-permissions` (ADR 0016) dá ao grupo Financeiro `fiscal.view`, `fiscal.create` e `fiscal.edit`, e ao Almoxarifado `fiscal.view` e `fiscal.import_incoming`, em todo tenant novo e nos existentes no próximo deploy, uma vez, incrementando o `perm_version` dos membros; nenhum grupo do seed além do Proprietário recebe `fiscal.approve`.
2. **Dado** a FND-16, **quando** o seed patch roda, **então** o "Ver conteúdo" dos eventos do provedor `emissor-fiscal` passa de "só Proprietário" para `fiscal.approve` (FND-16, com o JSON mascarado).

**Configurações → Fiscal: valores pré-configurados e conferência**
3. **Dado** um tenant com o módulo `fiscal` liberado, **quando** abro "Configurações → Fiscal" (`/app/configuracoes/fiscal`) pela primeira vez, **então** todos os campos da tabela "Valores pré-configurados" vêm preenchidos (o regime e os CNAEs pela consulta do cartão CNPJ da CAD-01b quando houver; os demais pelo seed `fiscal-defaults`), cada um com o chip **"A confirmar com a contadora"** (alerta), a faixa "Valores sugeridos pelo sistema. Confira cada um com a contadora antes de emitir em produção." e o contador "{n} de {m} campos conferidos"; IE, IM, razão social, endereço e código IBGE aparecem só leitura, de `company_profile` (CAD-06), com o link "Editar dados da empresa", e o CNPJ do catálogo.
4. **Dado** `settings.edit`, **quando** altero campos de uma seção e clico "Salvar configuração fiscal", **então** a configuração é gravada e auditada (antes e depois por campo); validações: CNAE com 7 dígitos, série de 1 a 999, CFOP de 4 dígitos começando por 5 (dentro do estado) ou 6 (fora), CST da tabela do CRT, alíquotas de 0 a 100 (`numeric(7,4)`), prazo de cancelamento de 1 a 168 h, `CodSrv` no formato `NN.NN.NN`; mudar um valor já conferido **volta o chip** para "A confirmar com a contadora" e limpa a conferência daquele campo.
5. **Dado** `settings.edit` e um campo com o chip "A confirmar com a contadora", **quando** clico "Marcar como conferido" (por campo ou "Marcar seção como conferida", com a lista dos campos no diálogo), **então** o chip vira "Conferido em dd/mm por {usuário}" (ok), a conferência grava quem, quando e o hash do valor conferido (auditoria), e "Desfazer conferência" volta o chip, também auditado; sem `settings.edit`, o botão some e a API responde 403.
6. **Dado** o ambiente **Produção** (permitido só com `fiscal_environment_allowed = Production`, ligada na GOL-01b, ADR 0034), **quando** a FIS-03a ou a FIS-06 vão emitir, **então** o serviço `IFiscalSettings.ReadinessAsync(documentType)` (novo, `Fiscal.Application`) recusa com 409 `fiscal_settings_not_confirmed` e a lista dos campos críticos ainda não conferidos (coluna "Crítico para produção"; os da NFS-e só para a NFS-e) e a tela da nota leva a "Configurações → Fiscal" no primeiro campo pendente; em **Homologação** os campos não conferidos não bloqueiam (só o aviso).
7. **Dado** a configuração, **quando** a IE, o código IBGE do município ou a série estão vazios, **então** a tela mostra as pré-condições ("Informe a inscrição estadual em Dados da empresa.", "Informe a série exclusiva da NF-e.") e a emissão fica bloqueada com o mesmo motivo (FIS-03a).
8. **[Segurança]** **Dado** a troca do ambiente para **Produção**, **quando** salvo, **então** o diálogo "Ligar a emissão com valor fiscal" explica "Notas em produção têm valor fiscal e só podem ser canceladas no prazo legal." e exige o mesmo verbo e o código do autenticador (2FA de novo, D6); o servidor recusa sem a flag `fiscal_environment_allowed = Production` ("A emissão desta empresa só liga na virada para a produção."); a troca de série com nota já emitida na série atual pede confirmação mostrando a última nota.
9. **Dado** a configuração salva, **quando** o regime ou o CRT mudam, **então** o sistema avisa que as regras fiscais (FIS-02b) do regime anterior ficam inativas e lista as que precisam de revisão; nenhuma nota já emitida muda.
10. **Dado** `settings.edit`, **quando** clico "Baixar para a contadora", **então** sai o PDF (`IPdfRenderer`) com todos os campos, o valor, a situação (conferido ou a confirmar) e espaço para a resposta, para a conversa com a contadora.

**Configurações → Fiscal → Certificado digital (A1 direto ao Emissor, D12)**
11. **[Segurança]** **Dado** `settings.edit` e a integração fiscal testada (FIS-01), **quando** abro "Configurações → Fiscal → Certificado digital" (`/app/configuracoes/fiscal/certificado`), escolho o `.pfx` (até 50 KB), digito a senha, clico "Enviar certificado" e confirmo com o código do autenticador (2FA de novo, D6), **então** o servidor recebe o arquivo e a senha **só no corpo do formulário** (`[FromForm]`, nunca na URL), abre o PFX em memória com `X509KeyStorageFlags.EphemeralKeySet`, confere que tem chave privada, que é A1, que não está vencido e que o **CNPJ do titular é o CNPJ da empresa do tenant** ("O certificado é de outro CNPJ: {CNPJ mascarado}."), repassa ao cofre do Emissor por `IFiscalEmitter.UploadCertificateAsync` (que confere de novo, sem aceitar falha, lacuna L-h) e descarta o arquivo e a senha; sem o código válido, 403 `two_factor_required` e nada é enviado.
12. **[Segurança]** **Dado** o certificado aceito pelo Emissor, **quando** a resposta volta, **então** o BemAqui grava **só** validade (`certificate_not_after`), titular, CNPJ, thumbprint, quem enviou e quando; o `.pfx` e a senha **nunca** vão para disco, log, auditoria (só "certificado trocado, válido até {data}"), `IFileStorage`, outbox ou resposta; teste de canário com um certificado gerado no próprio teste (nunca arquivo `.pfx` no repositório; a CI já recusa) e teste de que o corpo do request não é registrado pelo log de requisições.
13. **Dado** a tela do certificado, **quando** abre, **então** mostra titular, CNPJ, validade, thumbprint e a situação (Válido ok, Vence em {n} dias alerta, Vencido crítico), lidos do BemAqui e conferidos pelo `GET /me` do Emissor (cache de 1 h); diferença de thumbprint entre os dois (certificado trocado fora do BemAqui) mostra "O certificado no Emissor Fiscal mudou em {data}." e atualiza os dados.
14. **Dado** o certificado com vencimento em 30, 15 e 7 dias, **quando** o recorrente global `fiscal-certificate-expiry` roda (diário, 08:00 de São Paulo, um `TenantJob` por tenant com `tenantId` e o módulo `fiscal` liberado), **então** os membros do Proprietário recebem "O certificado digital da empresa vence em {n} dias. Sem ele, nenhuma nota é emitida." com link para a tela do certificado, sem repetir no mesmo limiar (`dedup_key = fiscal-cert:{limiar}:{thumbprint}`), e a pauta do Proprietário ganha o item "Certificado digital vencendo" (`IAgendaSource`, DSH-04); vencido, a emissão responde 409 `fiscal_certificate_expired` e sai o alerta `fiscal_integration` (FND-12); teste de execução dupla e de dois tenants.

**Permissões e auditoria**
15. **Dado** o módulo `fiscal` não liberado, **quando** abro as telas ou chamo a API, **então** 403 `module_not_released` e a tela padrão `ModuleNotReleased`; sem grupo, 403 `forbidden`; ver com `settings.view` ou `fiscal.view` em modo leitura; editar, marcar como conferido e enviar certificado sem `settings.edit`: 403 e os botões somem; `RequireModule` e `perm:`; política de rate limit nomeada `fiscal-certificate` no envio (5 por hora por tenant).
16. **Dado** qualquer alteração, conferência ou troca de certificado, **quando** abro "Histórico de alterações", **então** vejo antes e depois de cada campo, quem conferiu e quando, e o certificado só como "trocado, válido até {data}".

## Regras de negócio e técnicas
- **`fiscal.fiscal_settings`** (03 ajustado, uma linha por tenant): `regime`, `crt`, `cnae_main`, `cnae_secondary` (array), `nfe_series`, `environment`, `default_operations` (jsonb: CFOP por cenário, venda mista), `default_csts` (jsonb), `default_rates` (jsonb: ICMS interno, interestaduais por grupo de UF e importado, PIS, COFINS, IPI pela TIPI), `cost_taxes` (lista), `nfse_service` (jsonb: item da LC 116, `CodSrv`, CTN, NBS, ISS, local de incidência, retenções), `ibs_cbs` (jsonb: CST, `cClassTrib`, alíquotas), `cancellation_window_hours`, `transport_defaults` (jsonb: modalidade do frete, espécie dos volumes), `include_duplicates`, `certificate_not_after`, `certificate_subject`, `certificate_document`, `certificate_thumbprint`, `certificate_uploaded_at`, `certificate_uploaded_by`, `updated_at`, `updated_by`, `xmin`; sem `certificate_secret`, sem `next_number`.
- **`fiscal.setting_confirmations`** (nova): `setting_key`, `value_hash`, `confirmed_by`, `confirmed_at`; único por `setting_key`; valor com hash diferente do conferido = "A confirmar". A lista de chaves e quais são críticas fica no código (`FiscalSettingKeys`), com teste.
- **Seed `fiscal-defaults`** (ADR 0016): grava os valores da tabela no tenant novo e nos existentes no próximo deploy, só se a linha não existir; nunca sobrescreve valor salvo.
- **Fonte única:** IE, IM, endereço e IBGE ficam em `settings.company_profile` (CAD-06); o Fiscal lê, não copia.
- **Parâmetros** são do Fiscal (ADR 0016 adendo M6); a tela fica em Configurações com `settings.edit`.
- **Certificado:** o BemAqui não guarda o A1 (D12); o arquivo só passa em memória do request ao Emissor.

## Permissões, eventos e tabelas
- **Permissões:** `settings.edit` (editar, conferir, enviar certificado; 2FA de novo no envio e na troca para produção), `settings.view`/`fiscal.view` (ver); ações novas `fiscal.approve` e `fiscal.import_incoming`; seed patch `fiscal-permissions`.
- **Eventos:** `FiscalSettingsChanged` (`{ changedKeys }`, só aviso, para a FIS-02b listar regras a revisar).
- **Contratos:** `IFiscalSettings` (Fiscal, novo: valores padrão, `ReadinessAsync`); `IAgendaSource` (pauta, DSH-04).
- **Tabelas:** `fiscal.fiscal_settings` (03 ajustado), `fiscal.setting_confirmations` (nova); seeds `fiscal-defaults` e `fiscal-permissions`.
- **Jobs:** recorrente global `fiscal-certificate-expiry`.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 6 (envio do certificado ao Emissor: "Enviando / Confirmado / Recusado com motivo"; `EnvironmentBanner` da BNK-01a).
- **Configurações → Fiscal** (`/app/configuracoes/fiscal`): seções Empresa (só leitura, do CAD-06), Regime e atividades, Série e ambiente, Operações padrão, CST e alíquotas, Custo do projeto, NFS-e, Reforma Tributária (IBS/CBS), Transporte e cobrança, Prazos; cada campo com o chip de conferência (o `ToConfirmChip` criado na BNK-01b, reutilizado); índice das seções com o contador de pendentes; link "Certificado digital".
- **Configurações → Fiscal → Certificado digital** (`/app/configuracoes/fiscal/certificado`): situação do certificado, "Enviar certificado" (arquivo e senha), histórico de trocas.
- **Verbos:** "Salvar configuração fiscal" / "Configuração fiscal salva"; "Marcar como conferido" / "Conferido"; "Desfazer conferência" / "Conferência desfeita"; "Enviar certificado" / "Certificado enviado"; "Baixar para a contadora".
- **Estados:** carregando; primeira configuração (valores sugeridos com a faixa do CA 3 e "Baixar para a contadora" em destaque); integração fiscal não configurada ("Conecte o Emissor Fiscal antes de enviar o certificado." + "Configurar integração", na tela do certificado); sem certificado ("Nenhum certificado enviado. Sem ele, nenhuma nota é emitida." + "Enviar certificado"); erro de validação no campo, com foco no primeiro; erro do envio dentro do formulário com o motivo; certificado vencendo (alerta) ou vencido (crítico); `EnvironmentBanner` "Ambiente de homologação: as notas não têm valor fiscal."
- **Propositivo:** valores pré-configurados; regime e CNAE pela consulta do cartão CNPJ; o índice leva ao primeiro campo pendente; depois de tudo conferido, o `NextSteps` oferece "Conferir as regras fiscais" (FIS-02b) e "Enviar certificado" quando faltar.
- **400 px:** seções empilhadas com o índice recolhido; chips e botões de 44 px; diálogos de produção e de certificado em tela cheia.
- **Ajuda:** `Ajuda/configuracoes-fiscal.md` e `Ajuda/configuracoes-fiscal-certificado.md` (previstas); `<Dica>` em "CRT", "Série exclusiva", "Ambiente", "Venda mista", "Item da LC 116" (14.06 como alternativa), "IBS/CBS", "A confirmar com a contadora", "Entra no custo do projeto" e "Certificado A1".

## Plano de testes
- **Unidade:** CRT pelo regime; validações do CA 4; hash do valor e volta do chip ao mudar (CA 4, 5); campos críticos por tipo de documento (CA 6); conferência do CNPJ do titular e do tipo A1 (CA 11).
- **Integração (Testcontainers + `FakeFiscalEmitter`):** seed patch e seed de valores uma vez só, sem sobrescrever (CA 1, 3); "Ver conteúdo" da FND-16 com `fiscal.approve` (CA 2); salvar e auditar (CA 4); conferir e desfazer (CA 5); `ReadinessAsync` em produção e em homologação (CA 6); pré-condições (CA 7); produção sem a flag e sem 2FA (CA 8); troca de regime (CA 9); PDF para a contadora (CA 10); envio do certificado com 2FA, CNPJ errado, vencido, sem chave privada, senha só no formulário (CA 11); canário do `.pfx` e da senha, inclusive no log de requisições (CA 12); thumbprint diferente no `GET /me` (CA 13); vencimento por limiar, pauta, execução dupla e dois tenants (CA 14); três camadas e rate limit (CA 15); auditoria (CA 16).
- **E2E (Playwright):** abrir a configuração pré-preenchida, marcar uma seção como conferida, enviar um certificado de teste ao fake e ver a validade; tela a 400 px.

## Fora do escopo
- Regras de tributação por operação e o cálculo: FIS-02b (nascem do mesmo seed). Credenciais da prefeitura: ficam no Emissor (FIS-06).
- Configurar a integração com o Emissor: FIS-01.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.2, pontos 35–39):
1. `fiscal.approve` novo no catálogo, reservado ao Proprietário (D12; CA 1).
2. Seed: Financeiro com `fiscal.view`, `fiscal.create` e `fiscal.edit`; Almoxarifado com `fiscal.view` e `fiscal.import_incoming` (CA 1).
3. Série exclusiva, obrigatória em produção (pré-configurada, a confirmar; CA 6, 7).
4. Sem produção antes da GOL-01b (flag `fiscal_environment_allowed`, CA 8).
5. Lucro Presumido pré-configurado e a confirmar (P-01; CA 3).

Decisões da revisão aplicadas: novo requisito do PO (telas de Configurações pré-configuradas com "A confirmar com a contadora" e "Marcar como conferido", ADR 0042; CA 3 a 6, 10); D12 (A1 só no Emissor, o BemAqui guarda validade, titular/CNPJ e thumbprint; CA 11 a 13); D6 (2FA de novo no envio do A1 e na troca para produção; CA 8, 11); D4 (venda mista nos valores pré-configurados); pontos 9, 11, 41 e 73 nos valores (IBS/CBS, `transp`, tributos no custo, item 7.02 com 14.06 na `<Dica>`); §4.3 (PFX com `EphemeralKeySet`, CA 11; job com `tenantId`, CA 14; rate limit nomeado, CA 15).

**Pré-condição externa:** conteúdo da contadora (P-01, P-02, P-03, série, códigos da NFS-e de Caçapava), que **não bloqueia** o `Ready` porque os valores vêm pré-configurados e a conferência é feita na tela; e-CNPJ A1 da Konebras (compra) e a rota de certificado sem `admin` no Emissor (lacuna L-h, dono PO) para o envio real; em homologação, o `demo` usa o A1 da Konebras no próprio tenant do Emissor.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers) e E2E passando.
- [x] Migration criada (não destrutiva; `certificate_secret` deixa de ser usado sem apagar coluna), testada sobre cópia do banco do tenant `konebras`; seeds `fiscal-defaults` e `fiscal-permissions` idempotentes.
- [x] Permissões só do `ModuleCatalog` (`fiscal.approve`, `fiscal.import_incoming`).
- [x] Telas com estados, inclusive a 400 px.
- [x] Ajudas escritas; teste de cobertura passando.
- [x] Padrões pré-configurados, conferência e próximo passo avaliados; alertas acionáveis (vencimento com link).
- [x] Auditoria ligada (sem segredo; certificado só como "trocado").
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` (`fiscal_settings`, `setting_confirmations`) atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `FiscalSettings`, `FiscalSettingKeys` (críticos por documento) e `SettingConfirmation` (Domain); `fiscal.approve` e `fiscal.import_incoming` no `ModuleCatalog`; seeds `fiscal-defaults` e `fiscal-permissions`.
- [x] `IFiscalSettings` com `ReadinessAsync`; endpoints `/api/v1/fiscal/settings` (+ `/confirmations`, `/certificate` multipart, `/export-pdf`).
- [x] Telas, chip de conferência, PDF para a contadora, recorrente `fiscal-certificate-expiry`, fonte da pauta.
- [x] Ajudas e Dicas.

## Quality gates
- Agentes previstos: @dev, cybersecurity (certificado, produção, 2FA), @qa, contadora (conferência dos valores, fora do código).
- Gate: CA 1, 4, 5, 6, 8, 11, 12 e 14 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK (ajustado) |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK (valores pré-configurados marcados "A confirmar com a contadora") |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** IE, IM e endereço lidos do CAD-06, sem cópia; o certificado seguia a decisão da ADR 0027, com os dois caminhos testáveis; o regime registrado no EmissorFiscal não foi tomado como decisão.
- **Correções da revalidação (21/10/2026):** pontos 35 a 39 decididos; a story passa a entregar as telas Configurações → Fiscal e Certificado digital com todos os parâmetros pré-configurados, o chip "A confirmar com a contadora", "Marcar como conferido" auditado e o bloqueio de produção por campo crítico (novo requisito do PO, ADR 0042; CA 3 a 6, 10); o caminho (b) do certificado (A1 cifrado no BemAqui) saiu: o A1 vai direto ao cofre do Emissor e o BemAqui guarda só validade, titular/CNPJ e thumbprint (D12; CA 11 a 13); 2FA de novo (D6); `fiscal.import_incoming` no catálogo e no seed do Almoxarifado (CA 1); "Ver conteúdo" da FND-16 com `fiscal.approve` (CA 2); alerta em 30/15/7 dias ao Proprietário e na pauta (CA 14); re-estimada para G; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externas: conteúdo da contadora (pré-configurado), e-CNPJ A1 e lacuna L-h do Emissor.
- **Conferência do fechamento (21/10/2026, amostragem do PO):** chip "A confirmar com a contadora" reutilizado da BNK-01b, sem componente novo; checklist refeita, sem mudança de resultado.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da FIS-02) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D4, D6, D12, novo requisito de Configurações, tabela 3.2 pontos 35–39, §4.3, §4.4); telas Configurações → Fiscal e Certificado digital pré-configuradas; re-estimada para G; ordem 8 (Marco 5.4); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
