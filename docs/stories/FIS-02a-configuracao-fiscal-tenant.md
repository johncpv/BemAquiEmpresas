# FIS-02a — Configuração fiscal do tenant: regime, inscrições, série, ambiente, certificado A1 e alerta de vencimento

| Campo | Valor |
|---|---|
| ID | FIS-02a |
| Título | Cadastro fiscal do tenant (regime tributário, IE, IM, CNAE principal e secundários, série da NF-e, ambiente homologação ou produção), situação do certificado A1 conforme a ADR 0023, alerta 30 dias antes do vencimento, permissões do módulo `fiscal` no catálogo e no seed |
| Tamanho | M (quebra da FIS-02) |
| Status | Draft |
| Requisitos | R-FIS-2 (cadastro fiscal do tenant: certificado A1 (criptografado), regime, IE, série, ambiente) · R-COM-2 · R-PRP-5 · R-SEC-10 · R-SEC-5 |
| Dependências | FIS-01 (ADR 0023 aprovada e `IFiscalEmitter`), CAD-06 (`company_profile`: IE, IM, endereço, `city_ibge`), FND-06 (`ModuleCatalog`), CAD-01a (seed patch); P-01 e P-02 (conteúdo, não estrutura) |
| Bloqueia | FIS-02b (regras), FIS-03a (emissão), FIS-06 (NFS-e) |
| Ordem de execução | 8 (Marco 5.4 "Nota de saída") |

## User story
Como **proprietário**, quero **informar uma vez os dados fiscais da empresa e saber com antecedência quando o certificado vai vencer**, para **as notas saírem com os dados certos e a emissão nunca parar por certificado vencido**.

## Contexto e referências
- PRD R-FIS-2; `05-backlog` FIS-02 ("Certificado A1, série, ambiente, regras por operação (conteúdo da contadora). CA: alerta 30 dias antes do vencimento do certificado").
- `03-modelo-dados` → `fiscal.fiscal_settings` (`regime`, `ie`, `cnae`, `certificate_secret`, `certificate_expiry`, `environment`, `nfe_series`, `next_number`; a FIS-01 tira o `next_number`, porque o número é do EmissorFiscal).
- `06-pendencias-riscos` P-01 (regime: "hoje consta fora do Simples (provável Lucro Presumido)"; o `CLAUDE.md` do EmissorFiscal registra "Lucro Presumido desde 01/01/2026", ainda sem decisão no `06`), P-02 (CNAE de fabricação em inclusão).
- ADR 0007 (`fiscal-certificate`), ADR 0008 (`*.approve` e `settings.edit` reservadas), ADR 0016 adendo M6 (cada módulo é dono dos próprios parâmetros; Configurações monta a tela), ADR 0023 (proposta na FIS-01: onde fica o A1).
- EmissorFiscal: alertas próprios em 60/30/15/7/1 dias; série exclusiva acordada com o contador (lacuna L3 de lá).
- `ModuleCatalog` atual: `fiscal` com `view`, `create`, `edit` e nenhum grupo do seed com o módulo.

## Critérios de aceite
**Catálogo e seed**
1. **Dado** o `ModuleCatalog`, **quando** esta story entra, **então** o módulo `fiscal` ganha a ação padrão `approve` com o significado "cancelar ou inutilizar nota fiscal" (proposta, Pontos em aberto 1), reservada ao Proprietário para conceder (ADR 0008, `*.approve`); o seed patch `fiscal-permissions` (ADR 0016) dá ao grupo Financeiro `fiscal.view`, `fiscal.create` e `fiscal.edit` e ao Almoxarifado `fiscal.view` (proposta, Pontos em aberto 2), em todo tenant novo e nos existentes no próximo deploy, incrementando o `perm_version` dos membros.

**Cadastro fiscal**
2. **Dado** `settings.edit` (Proprietário), **quando** abro "Configurações → Fiscal" e informo regime (Simples Nacional, Lucro Presumido, Lucro Real), CRT derivado, CNAE principal e secundários (7 dígitos, validados pelo formato), série da NF-e (1 a 999; padrão vazio, Pontos em aberto 3), ambiente (Homologação ou Produção) e clico "Salvar configuração fiscal", **então** a configuração é gravada e auditada; IE, IM, razão social, endereço e código IBGE vêm de `company_profile` (CAD-06) e o CNPJ do catálogo, só leitura aqui, com link "Editar dados da empresa".
3. **Dado** a configuração, **quando** a IE ou o código IBGE do município estão vazios em `company_profile`, **então** a tela mostra as pré-condições ("Informe a inscrição estadual em Dados da empresa.") e a emissão fica bloqueada com o mesmo motivo (FIS-03a).
4. **[Segurança]** **Dado** a troca do ambiente para **Produção**, **quando** salvo, **então** o diálogo "Ligar a emissão com valor fiscal" explica "Notas em produção têm valor fiscal e só podem ser canceladas no prazo legal." e exige o mesmo verbo; no tenant `konebras`, Produção só é aceita depois da GOL-01 (flag do catálogo `fiscal_production_allowed`, ligada no checklist do go-live; Pontos em aberto 4); a troca de série com nota já emitida na série atual pede confirmação mostrando a última nota.
5. **Dado** a configuração salva, **quando** o regime ou o CRT mudam, **então** o sistema avisa que as regras fiscais (FIS-02b) do regime anterior ficam inativas e lista as que precisam de revisão; nenhuma nota já emitida muda.

**Certificado A1 (conforme a ADR 0023)**
6. **Dado** a decisão (a) da ADR 0023 (A1 só no EmissorFiscal), **quando** abro a seção "Certificado", **então** vejo titular, CNPJ, validade e situação lidos pelo `IFiscalEmitter.GetCertificateStatusAsync` (cache de 1 h), e o texto "O certificado é carregado no Emissor Fiscal pelo administrador."; **dado** a decisão (b), **quando** o Proprietário envia o `.pfx` e a senha, **então** o servidor valida (abre, tem chave privada, CNPJ do titular = CNPJ do tenant, não vencido, tipo A1), cifra com `SecretKind.FiscalCertificate` (`fiscal-certificate`, ADR 0007), envia ao EmissorFiscal e nunca o devolve (a tela mostra só titular e validade).
7. **[Segurança]** **Dado** qualquer um dos caminhos, **quando** o certificado ou a senha passam pelo BemAqui, **então** não aparecem no log, na auditoria (só "certificado trocado, válido até…"), no `IFileStorage` nem na resposta; teste de canário com um certificado de teste gerado no próprio teste (nunca arquivo `.pfx` no repositório; a CI já recusa).
8. **Dado** o certificado com vencimento em 30 dias (e de novo em 15, 7 e 1), **quando** o recorrente global `fiscal-certificate-expiry` roda (diário, 08:00 de São Paulo), **então** os membros do Proprietário e quem tem `fiscal.edit` recebem "O certificado digital da empresa vence em {n} dias. Sem ele, nenhuma nota é emitida." com link para a seção Certificado, sem repetir no mesmo limiar (`dedup_key = fiscal-cert:{limiar}:{validade}`); vencido, a emissão responde 409 `fiscal_certificate_expired` e sai o alerta `fiscal_integration` (FND-12); teste de execução dupla e de dois tenants.

**Permissões e auditoria**
9. **Dado** o módulo `fiscal` não liberado, **quando** chamo, **então** 403 `module_not_released`; editar sem `settings.edit`: 403 `forbidden`; ver com `fiscal.view` ou `settings.view` em modo leitura; `RequireModule` e `perm:`.
10. **Dado** qualquer alteração, **quando** abro o histórico, **então** vejo antes e depois (regime, CNAE, série, ambiente), quem e quando; certificado só como "trocado".

## Regras de negócio e técnicas
- **`fiscal.fiscal_settings`** (03 ajustado): `regime`, `crt`, `cnae_main`, `cnae_secondary` (array), `nfe_series`, `environment`, `certificate_mode` (`EmissorFiscal`/`Uploaded`, pela ADR 0023), `certificate_cipher` e `certificate_password_cipher` (só no modo (b)), `certificate_not_after`, `certificate_subject`, `updated_at`, `updated_by`, `xmin`; uma linha por tenant; auditada com `[ProtectedSecret]`.
- **Fonte única:** IE, IM, endereço e IBGE ficam em `settings.company_profile` (CAD-06); o Fiscal lê, não copia.
- **Parâmetros** do Fiscal são dele (ADR 0016 adendo M6); a tela fica em Configurações com `settings.edit`.
- **Sem decidir o regime:** a story guarda o que o Proprietário informa; o conteúdo vem da contadora (P-01).

## Permissões, eventos e tabelas
- **Permissões:** `settings.edit` (editar), `fiscal.view`/`settings.view` (ver); ação nova `fiscal.approve` (proposta); seed patch `fiscal-permissions`.
- **Eventos:** nenhum entre módulos.
- **Tabelas:** `fiscal.fiscal_settings` (03 ajustado); `catalog.tenants.fiscal_production_allowed` (proposta).
- **Jobs:** recorrente global `fiscal-certificate-expiry`.

## Telas e estados
- **Configurações → Fiscal** (`/app/configuracoes/fiscal`): seções Empresa (só leitura, do CAD-06), Regime e atividades, Série e ambiente, Certificado.
- **Verbos:** "Salvar configuração fiscal" / "Configuração fiscal salva"; "Trocar certificado" / "Certificado trocado".
- **Estados:** carregando; não configurado ("Configure o fiscal para emitir notas." com a lista do que falta: integração, IE, série, certificado); erro de validação no campo; faixa âmbar "Ambiente de homologação: as notas não têm valor fiscal."; certificado vencendo (alerta) ou vencido (crítico).
- **Propositivo:** regime sugerido pelo cartão CNPJ consultado (CAD-01b), com "Confirme com a contadora"; CNAE principal e secundários preenchidos da mesma consulta.
- **400 px:** seções empilhadas; diálogo de produção em tela cheia.
- **Ajuda:** `Ajuda/configuracoes-fiscal.md`; `<Dica>` em "CRT", "Série", "Ambiente" e "Certificado A1".

## Plano de testes
- **Unidade:** CRT pelo regime; validação de CNAE e série; validação do certificado (modo b).
- **Integração (Testcontainers + fake do EmissorFiscal):** seed patch idempotente e `perm_version` (CA 1); salvar e pré-condições (CA 2, 3); produção bloqueada sem a flag (CA 4); troca de regime (CA 5); certificado pelos dois modos e canário (CA 6, 7); vencimento por limiar, execução dupla e dois tenants (CA 8); três camadas (CA 9); auditoria (CA 10).
- **E2E (Playwright):** preencher a configuração fiscal com a integração fake e ver o certificado vencendo; tela a 400 px.

## Fora do escopo
- Regras de tributação por operação: FIS-02b. Configuração da NFS-e (código de serviço, credenciais da prefeitura): FIS-06.

## Pontos em aberto
1. **`fiscal.approve`** (PO/segurança): cancelar e inutilizar nota com `fiscal.approve` (reservada ao Proprietário) ou ação específica `fiscal.cancel` (ADR 0003)? Proposta: `approve`, sem ação específica.
2. **Fiscal no seed** (PO): Financeiro com `view/create/edit`; Almoxarifado com `view` (e `create` para importar NF de entrada? ver FIS-04a ponto 1).
3. **Série** (contadora, lacuna L3 do EmissorFiscal): qual série exclusiva do sistema, para não colidir com o emissor atual do contador.
4. **Produção antes da GOL-01** (PO, ADR 0020): proposta: não; a flag é ligada no checklist da GOL-01b.
5. **Regime tributário** (contadora, P-01): confirmar Lucro Presumido desde 01/01/2026 (registro do EmissorFiscal) e registrar a decisão no `06`.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`; seed patch idempotente.
- [ ] Permissões só do `ModuleCatalog` (`fiscal.approve`).
- [ ] Tela com estados, inclusive a 400 px.
- [ ] Ajuda escrita; teste de cobertura passando.
- [ ] Alertas acionáveis (vencimento com link).
- [ ] Auditoria ligada (sem segredo).
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `FiscalSettings` (Domain), `fiscal.approve` no `ModuleCatalog`, seed patch `fiscal-permissions`.
- [ ] Endpoints `/api/v1/fiscal/settings` (+ `/certificate`); seção em Configurações; recorrente `fiscal-certificate-expiry`.
- [ ] Ajuda e Dicas.

## Quality gates
- Agentes previstos: @dev, cybersecurity (certificado, produção), @qa.
- Gate: CA 1, 4, 7 e 8 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** IE, IM e endereço lidos do CAD-06, sem cópia (CA 2); o certificado segue a decisão da ADR 0023, com os dois caminhos testáveis (CA 6); o regime registrado no EmissorFiscal não foi tomado como decisão (ponto 5).
- **Pré-condições para `Ready`:** ADR 0023 (FIS-01); pontos em aberto 1, 2 e 3.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da FIS-02) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
