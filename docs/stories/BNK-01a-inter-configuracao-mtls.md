# BNK-01a — Banco Inter: Configurações → Integrações → Banco Inter, credenciais por conta, mTLS em memória, token OAuth2 e rotação

| Campo | Valor |
|---|---|
| ID | BNK-01a |
| Título | Tela **Configurações → Integrações → Banco Inter**, por conta bancária do tenant: client id e client secret (OAuth2 `client_credentials`, só com escopos de cobrança, extrato e webhook), certificado do mTLS (`.crt`/`.key` ou `.pfx`) cifrado e só de escrita, ambiente sandbox ou produção (produção só depois da liberação da GOL-01b), "Testar conexão", um `SocketsHttpHandler` por conta em cache pelo thumbprint, cache do token, alerta de vencimento do certificado, rotação testando o novo antes de trocar; filtro comum `RequireRecentTwoFactor` (2FA de novo, D6) e componente `EnvironmentBanner` |
| Tamanho | G (re-estimada de M: a tela passou para Configurações com seções e estados próprios, entra o filtro comum de 2FA de novo aplicado também à FIN-03a, o `EnvironmentBanner` único, o handler mTLS por conta com saída restrita e a recusa de credencial com escopo de pagamento) |
| Status | Done |
| Requisitos | R-FIN-7 (cobrança via Banco Inter) · R-FIN-8 (extrato via API do Inter) · R-FIN-2 (contas bancárias) · PRD §6 Segurança ("segredos (certificados, chaves Inter) criptografados por tenant") · R-SEC-5 · R-PRP-5 |
| Dependências | FND-16 (aba Integrações em Configurações), CAD-05b (`bank_accounts`), FND-11 (`ISecretProtector`, `Secret`), FND-12 (alertas), FND-07 (`TenantJob`), FIN-03a (pagar e estornar a OP, que ganham o 2FA de novo) |
| Bloqueia | BNK-01b (emissão), BNK-02 (reconsulta), BNK-03a (extrato pela API); o filtro `RequireRecentTwoFactor` é usado depois por FIS-02a, FIS-03b, CAD-04c2, RH-02c e LGPD-01b |
| Ordem de execução | 2 (Marco 5.2 "Cobrança pelo Inter") |

## User story
Como **proprietário**, quero **ligar a conta do Inter ao sistema uma vez, numa tela de Configurações, com o certificado e as credenciais guardados de forma que ninguém os veja depois**, para **emitir boletos e ler o extrato sem digitar nada no banco e sem expor a chave da conta da empresa**.

## Contexto e referências
- CLAUDE.md, Integrações externas: "Banco Inter: API de Cobrança (boleto com Pix / 'bolepix'), webhooks e extrato. Certificado mTLS por tenant, guardado criptografado."
- **ADR 0032** (Banco Inter: integração por conta, handler mTLS em memória, saída restrita, token na URL do webhook), **ADR 0042** (telas de Configurações, segredos só de escrita, valores "a confirmar"), **ADR 0007, adendo de 21/10/2026** (Inter por conta), **ADR 0008, adendo de 21/10/2026** (D6: 2FA de novo em pagar, estornar, salvar credenciais do Inter e demais ações listadas), **ADR 0034** (promoção a produção: Inter de produção só na GOL-01b, teste no D−1).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D1, D2, D6, D13, D18; tabela 3.2, pontos 13 a 16; §4.2 (BNK-01a), §4.3 (BNK-01a e "PFX com `EphemeralKeySet`"), §4.4 (padrão 6 e `EnvironmentBanner`).
- `02-arquitetura` §6.2 (OAuth2 client credentials + mTLS por tenant; `IBankBillingProvider`/`IBankStatementProvider`; sandbox) e §5 (segredos por tenant com Data Protection).
- ADR 0007: `inter-credentials` e `inter-certificate`; segredo write-only, `Secret` com `***` no `ToString()`, máscara no Serilog, propósito por tenant. ADR 0008: `settings.edit` reservada ao Proprietário.
- `06-pendencias-riscos` P-08 (conta PJ com API), P-19 (2FA a cada ação, decidida pela D6).
- Documentação pública da API do Inter: caminhos, escopos e formato do certificado conferidos no sandbox durante a implementação e registrados no runbook.

## Critérios de aceite
**2FA de novo (filtro comum, D6)**
1. **[Segurança]** **Dado** o filtro `RequireRecentTwoFactor` (Host/BuildingBlocks, novo), **quando** um endpoint marcado com ele é chamado, **então** só passa se o usuário tem 2FA ativo e o token traz `amr` com `otp` confirmado há **até 15 minutos** (o diálogo pede o código TOTP de novo e renova a prova); senão responde 403 `two_factor_required`, e o front mostra no próprio diálogo o estado único "Para continuar, confirme o código da verificação em duas etapas." (sem 2FA ativo: "Para continuar, ative a verificação em duas etapas." com o link para ativar); teste de arquitetura: toda ação da lista da D6 (ADR 0008 adendo) tem o filtro.
2. **[Segurança]** **Dado** pagar e estornar a OP (FIN-03a, já no ar), **quando** esta story entra, **então** os dois endpoints passam a exigir o `RequireRecentTwoFactor` (melhoria do que está no ar, P-19 decidida), com teste de regressão do fluxo de pagamento e de estorno da FIN-03a (com código recente passa; sem código recente, 403 `two_factor_required` e nada é gravado).

**Configurar a integração**
3. **Dado** um membro do Proprietário (`settings.edit`), uma conta bancária ativa com código COMPE 077 e os módulos `settings` e `finance` liberados, **quando** abro **Configurações → Integrações → Banco Inter** (`/app/configuracoes/integracoes/inter`) e escolho a conta, **então** informo ambiente (Sandbox ou Produção), client id, client secret, o certificado (`.crt` + `.key`, ou `.pfx` + senha) e a conta corrente do Inter, e clico "Salvar integração bancária"; o diálogo pede o **2FA de novo** (CA 1) e a conta passa a ter `provider = Inter`. A integração é **por conta** (o tenant pode ter mais de uma conta Inter).
4. **[Segurança]** **Dado** o certificado enviado, **quando** salvo, **então** o servidor valida antes de gravar: o arquivo abre (`.pfx` carregado com `X509KeyStorageFlags.EphemeralKeySet`, nunca gravado em disco), a chave privada corresponde ao certificado, o certificado não está vencido, tem pelo menos 2048 bits e o CNPJ do titular (quando presente no assunto) é o do tenant; recusa com 422 em PT-BR ("A chave privada não corresponde a este certificado.", "O certificado venceu em dd/mm/aaaa."); só `.crt`, `.pem`, `.key` e `.pfx`, até 20 KB cada, sem passar pela pasta de anexos nem pelo `IFileStorage`; a senha do `.pfx` vem em `[FromForm]` e nunca em query.
5. **[Segurança]** **Dado** a integração salva, **quando** consulto pela API ou pela tela, **então** client secret, certificado, chave privada e senha **nunca voltam** (nem no detalhe, nem na auditoria, nem no log): a tela mostra "Certificado válido até dd/mm/aaaa · configurado em dd/mm/aaaa por {usuário}" e o client id mascarado (últimos 4); os segredos são cifrados com `SecretKind.InterCredentials` e `SecretKind.InterCertificate`, no propósito do tenant e da conta (ADR 0007 adendo), e decifrados só ao montar o handler; teste golden e teste de canário (valores conhecidos não aparecem no banco em claro, no log, no `audit_log` nem na resposta).
6. **[Segurança]** **Dado** um usuário sem `settings.edit` (inclusive do Financeiro, com `finance.edit`), **quando** tenta salvar, trocar ou desligar a integração, **então** 403 `forbidden` e os botões somem; com `settings.view` ou `finance.view`, vê a tela em modo leitura (situação, validade, ambiente e escopos, sem nenhum segredo).

**Conexão, escopos e token**
7. **Dado** a integração salva, **quando** clico "Testar conexão", **então** o sistema obtém o token OAuth2 com o mTLS e faz uma leitura sem efeito (saldo ou extrato do dia) e mostra "Conexão com o Inter funcionando" com os escopos concedidos; se faltar um escopo necessário (cobrança leitura e escrita, extrato leitura, webhook), mostra quais faltam e como liberar no Internet Banking; o teste segue o padrão 6 (Enviado / Aguardando / Demorando depois de 30 s / Confirmado / Recusado com motivo e ação), sem reenvio enquanto processa.
8. **[Segurança]** **Dado** uma credencial cujos escopos concedidos incluem **pagamento ou envio de Pix** (qualquer escopo fora de cobrança, extrato e webhook), **quando** salvo ou testo, **então** o servidor recusa com 422 "Esta credencial pode fazer pagamentos. Gere no Inter uma credencial só com cobrança, extrato e webhook." e nada é gravado (D13).
9. **[Segurança]** **Dado** o cliente do Inter (`InterApiClient`, Infrastructure do Finance), **quando** chama o banco, **então** usa **um `SocketsHttpHandler` por conta**, em cache pela chave `{tenant}:{conta}:{thumbprint}`, com o certificado só em memória (`EphemeralKeySet`) e descartado (`Dispose`) quando a integração muda ou o certificado gira; a **URL base é fixa por ambiente** no código (nenhuma URL vinda do usuário ou do banco, contra SSRF); a saída de rede da API fica liberada só para os hosts do Inter (ADR 0032); é proibido `ServerCertificateCustomValidationCallback` permissivo fora dos testes (teste de arquitetura).
10. **Dado** o token, **quando** o cliente precisa dele, **então** usa o token em cache por tenant e conta até 60 s antes de expirar (memória do processo, nunca no banco nem no log), renova sob lock (uma renovação por vez) e, num 401 de token, renova uma vez e repete.
11. **[Segurança]** **Dado** qualquer chamada ao Inter, **quando** é registrada no log, **então** traz só tenant, conta (id), operação, método, situação HTTP, id de correlação do Inter e duração; **nunca** token, client secret, cabeçalho `Authorization`, corpo com dados do pagador ou certificado.

**Ambiente e produção**
12. **[Segurança]** **Dado** o ambiente Produção, **quando** o tenant ainda não tem `catalog.tenants.bank_production_allowed` (ligada só pelo super admin da plataforma na GOL-01b, ADR 0034), **então** a integração de produção pode ser salva e **testada** ("Testar conexão", leitura sem efeito, usado no D−1 da GOL-01b), mas emitir boleto, registrar webhook e importar extrato em produção respondem 409 `bank_production_not_allowed` "A cobrança real será ligada na virada do sistema."; com a flag ligada, salvar ou trocar para Produção exige o diálogo "Ligar a cobrança real" com o texto "Boletos emitidos em produção são cobranças reais ao cliente." e o mesmo verbo para confirmar. Os testes de sandbox ficam no tenant `demo`; nada bancário real no `konebras` antes da GOL-01b.
13. **Dado** qualquer tela do sistema, **quando** a conta Inter em uso está em Sandbox, ou o tenant está em homologação, **então** o **`EnvironmentBanner`** (componente único, novo, D18) aparece no topo da tela de integração, do boleto e do extrato com "Ambiente de teste do banco: os boletos não são cobranças reais." (ou o texto do ambiente correspondente), com cor semântica informativa (nunca o acento), `role="status"`, e é o mesmo componente que a FIS-02a e a WPP-01 usam para a SEFAZ e o WhatsApp.

**Rotação, vencimento e desligamento**
14. **Dado** um certificado novo (renovação anual) ou um client secret novo, **quando** o Proprietário envia e clica "Trocar credenciais" (com 2FA de novo), **então** o servidor testa a conexão **com as credenciais novas** antes de trocar; se passar, troca na mesma transação, guarda a data, descarta o handler e o token em cache e notifica os membros do Proprietário ("Credenciais do Inter trocadas por {usuário}"); se falhar, mantém as antigas e mostra o motivo.
15. **Dado** o certificado com vencimento em 30 dias (e de novo em 15, 7 e 1 dia), **quando** o recorrente global `inter-certificate-expiry` roda (diário, 08:00 de São Paulo, por `tenantId`), **então** os membros do Proprietário recebem a notificação "O certificado do Inter da conta {nome} vence em {n} dias." com link para "Trocar credenciais", sem repetir no mesmo limiar (`dedup_key = inter-cert:{contaId}:{limiar}`); vencido, a emissão de boletos responde 409 `bank_integration_expired` e sai o alerta `bank_integration` (FND-12).
16. **Dado** a integração, **quando** o Proprietário clica "Desligar integração", confirma no diálogo `destructive` e passa pelo 2FA de novo, **então** os segredos são apagados (sobrescritos e removidos), o handler sai do cache, a conta volta a `provider = None`, os boletos já emitidos continuam no banco e são conferidos pelo extrato ou pela baixa manual, e a ação é auditada e notificada.

**Três camadas e auditoria**
17. **Dado** o módulo `settings` ou `finance` não liberado, **quando** chamo a tela ou a API, **então** 403 `module_not_released` (a aba aparece com cadeado e a tela `ModuleNotReleased`); sem grupo que habilite, 403 `forbidden` e a aba some; `RequireModule("settings")` + `RequireModule("finance")`, `perm:settings.edit` + `RequireRecentTwoFactor` (escrita) ou `perm:settings.view`/`perm:finance.view` (leitura); IDOR em `/settings/integrations/inter/{bankAccountId}` (conta de outro tenant ou excluída: 404).
18. **Dado** qualquer alteração da integração, **quando** abro "Histórico de alterações", **então** vejo quem, quando e o que mudou (ambiente, conta corrente, escopos, "credenciais alteradas", "certificado trocado, válido até…"), nunca os valores (`[ProtectedSecret]`).

## Regras de negócio e técnicas
- **Onde fica:** `finance.bank_integrations`, uma por conta (`bank_account_id` único); a tela é de Configurações, os dados são do Finance (ADR 0042).
- **`finance.bank_integrations`:** `bank_account_id` (único), `provider` (`Inter`), `environment` (`Sandbox`/`Production`), `client_id_cipher`, `client_secret_cipher`, `certificate_cipher`, `private_key_cipher`, `certificate_password_cipher` (se `.pfx`), `certificate_not_after`, `certificate_thumbprint`, `account_number`, `granted_scopes`, `configured_at`, `configured_by`, `rotated_at`, `last_test_at`, `last_test_result`, `xmin`; auditada com `[ProtectedSecret]` nos campos cifrados.
- **Abstração:** `IBankBillingProvider` (BNK-01b) e `IBankStatementProvider` (BNK-03a) em `Finance.Application`; `InterBillingProvider` e `InterStatementProvider` em `Finance.Infrastructure`, sobre o `InterApiClient`.
- **Handler mTLS (ADR 0032):** `InterHandlerCache` com um `SocketsHttpHandler` por conta, chave pelo thumbprint, `PooledConnectionLifetime` limitado, descarte na troca; URL base fixa por ambiente.
- **Rede:** saída HTTPS só para os hosts do Inter (rede `egress`, ADR 0015 e 0032).
- **Relógio:** o health `ready` confere a deriva do relógio (alerta acima de 60 s), porque mTLS e token dependem da hora.
- **`RequireRecentTwoFactor`:** filtro de endpoint no BuildingBlocks (`amr` com `otp` e instante da última prova ≤ 15 min); a renovação da prova é um POST do TOTP que não troca a sessão; vale para toda ação da D6.

## Permissões, eventos e tabelas
- **Permissões:** `settings.edit` + 2FA de novo (configurar, trocar, desligar; reservada ao Proprietário, ADR 0008), `settings.view` e `finance.view` (ver). Nenhuma ação nova.
- **Eventos:** nenhum entre módulos; notificações síncronas (ADR 0019 §18).
- **Tabelas:** `finance.bank_integrations` (nova); `finance.bank_accounts.provider` (nova coluna); `catalog.tenants.bank_production_allowed` (nova coluna, padrão falso).
- **Segredos:** `SecretKind.InterCredentials`, `SecretKind.InterCertificate` (ADR 0007, por conta).
- **Jobs:** recorrente global `inter-certificate-expiry`.
- **Componentes novos:** `EnvironmentBanner` (único, D18); filtro `RequireRecentTwoFactor` e o passo "Confirme o código" reutilizável nos diálogos.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 6 (integração externa assíncrona no "Testar conexão"; `EnvironmentBanner` único).
- **Configurações → Integrações → Banco Inter** (`/app/configuracoes/integracoes/inter`, subaba da aba Integrações da FND-16): lista das contas com a situação (Não conectada neutro, Sandbox info, Produção ok, Certificado vence em breve alerta, Vencido crítico); detalhe da conta (`/app/configuracoes/integracoes/inter/$contaId`) com as seções Ambiente, Credenciais (só de escrita: campos vazios com "Configurado em …" e "Trocar"), Certificado (validade, titular, thumbprint), Situação (último teste, escopos) e Aviso de pagamento (a seção ganha as ações na BNK-02).
- **Verbos:** "Salvar integração bancária" / "Integração bancária salva"; "Testar conexão" / "Conexão testada"; "Trocar credenciais" / "Credenciais trocadas"; "Desligar integração" / "Integração desligada".
- **Estados:** carregando; vazio / primeira configuração ("Nenhuma conta do Inter cadastrada. Cadastre a conta com o banco 077 para conectar." + "Nova conta bancária"; com a conta, o formulário vem com o ambiente Sandbox e a conta corrente sugeridos); erro do teste dentro do formulário com o que fazer ("Libere o escopo de extrato no Internet Banking do Inter."); modo leitura sem `settings.edit`; módulo não liberado (`ModuleNotReleased`).
- **Propositivo:** a conta com banco 077 aparece primeiro; depois de salvar, o `NextSteps` oferece "Testar conexão" e, testada, "Ativar aviso de pagamento" (BNK-02), "Emitir boleto de um título em aberto" (BNK-01b) e "Importar extrato" (BNK-03a); certificado perto de vencer abre direto em "Trocar credenciais".
- **400 px:** seções empilhadas; envio de arquivo com botão de 44 px; diálogo de produção e passo do 2FA em tela cheia.
- **Ajuda:** `Ajuda/configuracoes-integracoes-inter.md` (prevista: lista e detalhe, `modulo: settings`); `<Dica>` em "Ambiente", "Certificado do Inter", "Escopos" ("Só cobrança, extrato e webhook. Credencial que paga é recusada.") e "Client secret" ("Gerado no Internet Banking do Inter, em Soluções para sua empresa → Nova integração.").

## Plano de testes
- **Unidade:** validação do certificado e da chave, `.crt`/`.key` e `.pfx` (CA 4), com certificados gerados no próprio teste (relógio fixo, nenhum arquivo no repositório); escopos permitidos e recusados (CA 8); cache e renovação do token sob concorrência (CA 10); janela de 15 min do `RequireRecentTwoFactor` (CA 1).
- **Integração (Testcontainers + fake do Inter com mTLS de teste):** filtro de 2FA de novo, sem 2FA e com prova vencida (CA 1); regressão de pagar e estornar a OP da FIN-03a com o filtro (CA 2); salvar com 2FA, ler sem segredo, canário (CA 3–5); 403 sem `settings.edit` (CA 6, 17); testar conexão e escopos faltando (CA 7); credencial com escopo de pagamento recusada (CA 8); handler por conta, descarte na troca e URL fixa (CA 9); log sem segredo (CA 11); produção sem a flag só testa, com a flag pede confirmação (CA 12); rotação com teste antes de trocar e falha mantendo as antigas (CA 14); vencimento por limiar sem repetir, execução dupla e dois tenants (CA 15); desligar (CA 16); auditoria (CA 18).
- **Arquitetura:** proibido `ServerCertificateCustomValidationCallback` permissivo fora dos testes; ações da D6 com `RequireRecentTwoFactor`.
- **Contrato (sandbox do Inter, manual, fora do CI, tenant `demo`):** token, escopos, formato do certificado e leitura sem efeito, registrados no runbook.
- **E2E (Playwright):** configurar a conta com o fake, passar pelo 2FA de novo, testar e trocar o certificado; ver o `EnvironmentBanner` em sandbox; tela a 400 px.

## Fora do escopo
- Emitir, consultar e cancelar boleto: BNK-01b. Webhook e "Aviso de pagamento": BNK-02. Extrato: BNK-03a.
- Outros bancos por API: depois do MVP (o OFX da BNK-03a cobre os demais).
- Pix cobrança imediata e pagamentos pela API do Inter: fora do MVP (a credencial com escopo de pagamento é recusada, CA 8).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.2, pontos 13–16):
1. Aceitar `.crt`/`.key` e `.pfx` (CA 4).
2. Integração por conta (CA 3).
3. Sem produção antes da GOL-01b; teste de conexão de produção no D−1 (CA 12, ADR 0034).
4. Alerta de vencimento ao Proprietário (CA 15).

Decisões da revisão aplicadas: D6 e P-19 (filtro `RequireRecentTwoFactor` criado aqui e aplicado também a pagar e estornar a OP da FIN-03a, CA 1, 2; 2FA de novo para salvar, trocar e desligar, CA 3, 14, 16); D13 (escopos só de cobrança, extrato e webhook, CA 8); D18 (`EnvironmentBanner` único, CA 13); D1 (Inter real só na GOL-01b, sandbox no `demo`, CA 12); novo requisito de Configurações e ADR 0042 (tela em Configurações → Integrações → Banco Inter, segredos só de escrita, CA 3, 5); §4.2 (um `SocketsHttpHandler` por conta em cache pelo thumbprint, saída restrita, teste de arquitetura do callback, CA 9); §4.3 (só de escrita com canário, URL base fixa, testar a credencial nova antes de trocar, `EphemeralKeySet`, CA 4, 5, 9, 14).

**Pré-condição externa:** P-08 (conta PJ do Inter com API habilitada e credencial de sandbox, a cargo da Konebras); confirmação no sandbox do formato do certificado e dos escopos; não bloqueia o `Ready` (o desenvolvimento usa o fake com mTLS de teste).

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers + fake com mTLS), arquitetura e E2E passando; regressão da FIN-03a com o filtro; teste manual no sandbox **pendente da P-08** (credencial de sandbox da Konebras): fica para o tenant `demo` assim que a credencial chegar, antes da BNK-01b.
- [x] Migration criada (não destrutiva), só aditiva (tabela nova `finance.bank_integrations` e coluna nova no catálogo), aplicada nos testes de integração; a aplicação sobre a cópia do `konebras` acontece no deploy da homologação; `bank_production_allowed` no catálogo.
- [x] Permissões só do `ModuleCatalog`; `SecretKind` no teste golden; lista da D6 no teste de arquitetura.
- [x] Telas com estados (inclusive primeira configuração e módulo não liberado), a 400 px.
- [x] Ajuda `configuracoes-integracoes-inter` escrita; teste de cobertura passando.
- [x] Alertas acionáveis (vencimento com link para trocar).
- [x] Auditoria ligada (sem segredo).
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` e `docs/operacao/segredos-e-chaves.md` (rotação do certificado do Inter) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `RequireRecentTwoFactor` (filtro, endpoint de prova do TOTP, passo reutilizável no diálogo) e aplicação na FIN-03a.
- [x] `BankIntegration` (Domain) e validação do certificado e dos escopos (Application); `SecretKind.InterCredentials` e `InterCertificate` por conta.
- [x] `InterApiClient` com `InterHandlerCache` (um `SocketsHttpHandler` por conta, thumbprint, descarte), token em cache, URL base fixa.
- [x] Endpoints `/api/v1/settings/integrations/inter/{bankAccountId}` (+ `/test`, `/rotate`, `DELETE` lógico dos segredos); `bank_production_allowed`.
- [x] Recorrente `inter-certificate-expiry`; subaba Banco Inter, `EnvironmentBanner`, diálogos e ajuda; fake do Inter com mTLS para os testes.

## Quality gates
- Agentes previstos: @dev, @architect (handler por conta, saída restrita), cybersecurity (segredos, mTLS, escopos, log, rotação, 2FA de novo), @ux-design-expert (`EnvironmentBanner`, tela de Configurações), @qa.
- Gate: CA 1, 2, 4, 5, 6, 8, 9, 11, 12 e 14 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: dois módulos e 2FA de novo, CA 17) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: ordem 2, depois da FND-16) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs | OK (ADRs 0032, 0042, 0034, adendos 0007 e 0008) |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK (ajustado: tela em Configurações, primeira configuração) |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** a configuração exige `settings.edit` (reservada) e não `finance.edit`; a rotação testa as credenciais novas antes de trocar; caminhos e escopos da API do Inter conferidos no sandbox.
- **Correções da revalidação (21/10/2026):** pontos 13 a 16 decididos (CA 4, 3, 12, 15); tela movida para Configurações → Integrações → Banco Inter (ADR 0042); filtro `RequireRecentTwoFactor` criado aqui e aplicado também a pagar e estornar a OP da FIN-03a, com teste de regressão (CA 1, 2); 2FA de novo para salvar, trocar e desligar; credencial com escopo de pagamento recusada (CA 8); `SocketsHttpHandler` por conta em cache pelo thumbprint, `EphemeralKeySet`, URL base fixa e callback proibido (CA 4, 9); produção só testável antes da flag da GOL-01b (CA 12); `EnvironmentBanner` único (CA 13); alerta só ao Proprietário; re-estimada para G; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Pré-condição externa explícita: P-08 e a conferência no sandbox (formato do certificado e escopos), sem bloquear.
- **Conferência do fechamento (21/10/2026, amostragem do PO):** filtro RequireRecentTwoFactor também em pagar e estornar a OP da FIN-03a; checklist refeita, sem mudança de resultado.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da BNK-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D1, D6, D13, D18, tabela 3.2 pontos 13–16, §4.2 a §4.4); tela em Configurações; filtro `RequireRecentTwoFactor`; re-estimada para G; nova ordem (Marco 5.2, ordem 2); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
