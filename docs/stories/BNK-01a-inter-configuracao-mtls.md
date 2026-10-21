# BNK-01a — Banco Inter: credenciais por conta, certificado mTLS cifrado, token OAuth2 e rotação

| Campo | Valor |
|---|---|
| ID | BNK-01a |
| Título | Configuração da integração com o Banco Inter por conta bancária do tenant: client id e client secret (OAuth2 `client_credentials`), certificado e chave privada do mTLS cifrados por tenant, ambiente sandbox ou produção, teste de conexão, cache do token, alerta de vencimento do certificado e rotação sem parar a cobrança |
| Tamanho | M (quebra da BNK-01) |
| Status | Draft |
| Requisitos | R-FIN-7 (cobrança via Banco Inter) · R-FIN-8 (extrato via API do Inter) · R-FIN-2 (contas bancárias) · PRD §6 Segurança ("segredos (certificados, chaves Inter) criptografados por tenant") · R-SEC-5 · R-PRP-5 |
| Dependências | CAD-05b (`bank_accounts`; "`provider` e `credentials_secret` entram na BNK-01"), FND-11 (`ISecretProtector`, `Secret`), FND-12 (alertas), FND-07 (`TenantJob`); P-08 (conta PJ com API habilitada, pré-condição externa) |
| Bloqueia | BNK-01b (emissão), BNK-02 (reconsulta), BNK-03a (extrato pela API) |
| Ordem de execução | 3 (Marco 5.2 "Cobrança pelo Inter") |

## User story
Como **proprietário**, quero **ligar a conta do Inter ao sistema uma vez, com o certificado e as credenciais guardados de forma que ninguém os veja depois**, para **emitir boletos e ler o extrato sem digitar nada no banco e sem expor a chave da conta da empresa**.

## Contexto e referências
- CLAUDE.md, Integrações externas: "Banco Inter: API de Cobrança (boleto com Pix / 'bolepix'), webhooks e extrato. Certificado mTLS por tenant, guardado criptografado."
- `02-arquitetura` §6.2: "Autenticação OAuth2 client credentials + certificado mTLS do correntista, por tenant. Abstração `IBankBillingProvider` / `IBankStatementProvider` para outros bancos no futuro. Ambiente sandbox para desenvolvimento." §5: "Segredos por tenant (certificado A1, credenciais Inter, senha SMTP) criptografados com Data Protection + chave mestra fora do banco."
- ADR 0007: tipos `inter-credentials` e `inter-certificate` previstos para a Fase 5; segredo write-only, `Secret` com `***` no `ToString()`, máscara no Serilog, propósito por tenant.
- ADR 0008: `settings.edit` e `*.approve` reservadas ao Proprietário para conceder.
- `03-modelo-dados` → `bank_accounts` ("provider (Inter/OFX/None) e credentials_secret entram na BNK-01"). CAD-05b: troca dos dados bancários protegida e notificada.
- `06-pendencias-riscos` P-08 (conta PJ no Inter com API habilitada, "será providenciada") e o risco "Dependência da API do Banco Inter → abstração de provedor; baixa manual e OFX como alternativa".
- Documentação pública da API do Banco Inter (cobrança, extrato, webhook, OAuth2): **os caminhos, os escopos e o formato do certificado são conferidos no sandbox durante a implementação** e registrados no runbook; esta story não depende de um caminho específico.

## Critérios de aceite
**Configurar**
1. **Dado** um membro do Proprietário (`settings.edit`) e uma conta bancária ativa do tipo Banco com código COMPE 077, **quando** abro "Configuração financeira → Integração bancária" e escolho a conta, **então** informo ambiente (Sandbox ou Produção), client id, client secret, o arquivo do certificado e o da chave privada (`.crt` e `.key`, ou `.pfx` com senha, Pontos em aberto 1) e a conta corrente do Inter, e clico "Salvar integração bancária"; a conta passa a ter `provider = Inter`.
2. **[Segurança]** **Dado** o certificado enviado, **quando** salvo, **então** o servidor valida antes de gravar: o arquivo abre, a chave privada corresponde ao certificado, o certificado não está vencido e tem pelo menos 2048 bits, e o CNPJ do titular do certificado (quando presente no assunto) é o do tenant; recusa com 422 em PT-BR ("A chave privada não corresponde a este certificado.", "O certificado venceu em dd/mm/aaaa."); só `.crt`, `.pem`, `.key` e `.pfx`, até 20 KB cada, sem passar pela pasta de anexos nem pelo `IFileStorage`.
3. **[Segurança]** **Dado** a integração salva, **quando** consulto pela API ou pela tela, **então** client secret, certificado, chave privada e senha **nunca voltam** (nem no detalhe, nem na auditoria, nem no log): a tela mostra "Certificado válido até dd/mm/aaaa · configurado em dd/mm/aaaa por {usuário}" e o client id mascarado (últimos 4); os segredos são cifrados com `SecretKind.InterCredentials` (`inter-credentials`) e `SecretKind.InterCertificate` (`inter-certificate`), no propósito do tenant (ADR 0007), e decifrados só no momento de abrir a conexão; teste golden e teste de canário (valores conhecidos não aparecem no banco em claro, no log nem no `audit_log`).
4. **[Segurança]** **Dado** um usuário sem `settings.edit` (inclusive do Financeiro, com `finance.edit`), **quando** tenta salvar, trocar ou remover a integração, **então** 403 `forbidden`; com `finance.view` ou `settings.view`, vê a tela em modo leitura (situação, validade e ambiente, sem nenhum segredo).

**Conexão e token**
5. **Dado** a integração salva, **quando** clico "Testar conexão", **então** o sistema obtém o token OAuth2 com o mTLS e faz uma leitura sem efeito (proposta: saldo ou extrato do dia), e mostra "Conexão com o Inter funcionando" com os escopos concedidos; se faltar um escopo necessário (cobrança leitura e escrita, extrato leitura, webhook), mostra quais faltam e como liberar no Internet Banking.
6. **Dado** o cliente do Inter (`InterApiClient`, Infrastructure do Finance), **quando** precisa do token, **então** usa o token em cache por tenant e conta até 60 s antes de expirar (memória do processo, nunca no banco nem no log), renova sob lock (uma renovação por vez) e, num 401 de token, renova uma vez e repete; o `HttpClient` é criado por tenant e conta com o certificado do cliente carregado do segredo e **descartado** da memória quando a integração muda (CA 9).
7. **[Segurança]** **Dado** o ambiente Sandbox, **quando** o tenant é `konebras` e o host não é o de produção, **então** o sistema permite; **dado** o ambiente Produção, **quando** é salvo, **então** exige o diálogo "Ligar a cobrança real" com o texto "Boletos emitidos em produção são cobranças reais ao cliente." e o mesmo verbo para confirmar (Pontos em aberto 3: Produção antes da GOL-01).
8. **[Segurança]** **Dado** qualquer chamada ao Inter, **quando** é registrada no log, **então** traz só tenant, conta (id), operação, método, situação HTTP, id de correlação do Inter e duração; **nunca** token, client secret, cabeçalho `Authorization`, corpo com dados do pagador ou certificado.

**Rotação e vencimento**
9. **Dado** um certificado novo (renovação anual) ou um client secret novo, **quando** o Proprietário envia e clica "Trocar credenciais", **então** o servidor testa a conexão **com as credenciais novas** antes de trocar; se passar, troca na mesma transação, guarda a data, invalida o token e o `HttpClient` em cache e notifica os membros do Proprietário ("Credenciais do Inter trocadas por {usuário}"); se falhar, mantém as antigas e mostra o motivo.
10. **Dado** o certificado com vencimento em 30 dias (e de novo em 15, 7 e 1 dia), **quando** o recorrente global `inter-certificate-expiry` roda (diário, 08:00 de São Paulo), **então** os membros do Proprietário e quem tem `finance.edit` recebem a notificação "O certificado do Inter da conta {nome} vence em {n} dias." com link para "Trocar credenciais", sem repetir no mesmo limiar (`dedup_key = inter-cert:{contaId}:{limiar}`); vencido, a emissão de boletos responde 409 `bank_integration_expired` e sai o alerta `bank_integration` (FND-12).
11. **Dado** a integração, **quando** o Proprietário clica "Desligar integração" e confirma no diálogo `destructive`, **então** os segredos são apagados (sobrescritos e removidos), a conta volta a `provider = None`, os boletos já emitidos continuam no banco e são conferidos pelo extrato ou pela baixa manual, e a ação é auditada e notificada.

**Três camadas e auditoria**
12. **Dado** o módulo `finance` não liberado, **quando** chamo a tela ou a API, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; `RequireModule("finance")` e `perm:settings.edit` (escrita) ou `perm:finance.view` (leitura); IDOR em `/bank-accounts/{id}/integration` (conta de outro tenant ou excluída: 404).
13. **Dado** qualquer alteração da integração, **quando** abro o histórico da conta, **então** vejo quem, quando e o que mudou (ambiente, conta corrente, "credenciais alteradas", "certificado trocado, válido até…"), nunca os valores.

## Regras de negócio e técnicas
- **Onde fica:** na conta bancária (`finance.bank_accounts`: `provider`, mais a tabela `finance.bank_integrations`, proposta), uma integração por conta; o tenant pode ter mais de uma conta Inter.
- **`finance.bank_integrations`** (proposta): `bank_account_id` (único), `provider` (`Inter`), `environment` (`Sandbox`/`Production`), `client_id_cipher`, `client_secret_cipher`, `certificate_cipher`, `private_key_cipher`, `certificate_password_cipher` (se `.pfx`), `certificate_not_after`, `certificate_thumbprint`, `account_number` (conta corrente do Inter), `granted_scopes`, `configured_at`, `configured_by`, `rotated_at`, `last_test_at`, `last_test_result`, `xmin`; auditada com `[ProtectedSecret]` nos campos cifrados.
- **Abstração:** `IBankBillingProvider` (BNK-01b) e `IBankStatementProvider` (BNK-03a) em `Finance.Application`; `InterBillingProvider` e `InterStatementProvider` em `Finance.Infrastructure`, sobre o `InterApiClient` desta story.
- **Rede:** saída HTTPS para o Inter pela rede `egress` do projeto (ADR 0015); a API ganha acesso de saída só para os hosts do Inter (proposta).
- **Relógio:** o mTLS e o token dependem da hora certa; o health `ready` passa a conferir a deriva do relógio (proposta: alerta acima de 60 s).

## Permissões, eventos e tabelas
- **Permissões:** `settings.edit` (configurar, trocar, desligar; reservada ao Proprietário, ADR 0008), `finance.view` e `settings.view` (ver). Nenhuma ação nova.
- **Eventos:** nenhum entre módulos; notificações síncronas (ADR 0019 §18).
- **Tabelas:** `finance.bank_integrations` (nova, proposta); `finance.bank_accounts.provider` (nova coluna).
- **Segredos:** `SecretKind.InterCredentials`, `SecretKind.InterCertificate` (previstos na ADR 0007).
- **Jobs:** recorrente global `inter-certificate-expiry`.

## Telas e estados
- **Financeiro → Configuração financeira → Integração bancária** (`/app/financeiro/integracao-bancaria`, entrada nova no `modules.ts`): lista das contas com a situação da integração (Não conectada neutro, Sandbox info, Produção ok, Certificado vence em breve alerta, Vencido crítico); detalhe da conta com as seções Ambiente, Credenciais (write-only), Certificado (validade) e Situação (último teste, escopos).
- **Verbos:** "Salvar integração bancária" / "Integração bancária salva"; "Testar conexão" / "Conexão testada"; "Trocar credenciais" / "Credenciais trocadas"; "Desligar integração" / "Integração desligada".
- **Estados:** carregando; vazio ("Nenhuma conta do Inter cadastrada. Cadastre a conta com o banco 077 para conectar." + "Nova conta bancária"); erro do teste dentro do formulário com o que fazer ("Libere o escopo de extrato no Internet Banking do Inter."); modo leitura sem `settings.edit`.
- **Propositivo:** a conta com banco 077 aparece primeiro; depois de salvar, o `NextSteps` oferece "Testar conexão" e, testada, "Emitir boleto de um título em aberto" (BNK-01b) e "Importar extrato" (BNK-03a).
- **400 px:** seções empilhadas; envio de arquivo com botão de 44 px; diálogo de produção em tela cheia.
- **Ajuda:** `Ajuda/financeiro-integracao-bancaria.md` (lista) e `Ajuda/financeiro-integracao-bancaria-conta.md` (detalhe); `<Dica>` em "Ambiente", "Certificado do Inter" e "Client secret" ("Gerado no Internet Banking do Inter, em Soluções para sua empresa → Nova integração.").

## Plano de testes
- **Unidade:** validação do certificado e da chave (CA 2) com certificados de teste gerados no próprio teste (âncora no relógio fixo, nunca arquivo no repositório); cache e renovação do token sob concorrência (CA 6).
- **Integração (Testcontainers + fake do Inter com mTLS de teste):** salvar, ler sem segredo, canário (CA 1–3); 403 sem `settings.edit` (CA 4, 12); testar conexão e escopos faltando (CA 5); produção com confirmação (CA 7); log sem segredo (CA 8); rotação com teste antes de trocar e falha mantendo as antigas (CA 9); vencimento por limiar sem repetir, execução dupla e dois tenants (CA 10); desligar (CA 11); auditoria (CA 13).
- **Contrato (sandbox do Inter, manual, fora do CI):** token, escopos e leitura sem efeito com a credencial de sandbox do PO, registrada no runbook.
- **E2E (Playwright):** configurar a conta com o fake, testar e trocar o certificado; tela a 400 px.

## Fora do escopo
- Emitir, consultar e cancelar boleto: BNK-01b. Webhook: BNK-02. Extrato: BNK-03a.
- Outros bancos por API: depois do MVP (o OFX da BNK-03a cobre os demais).
- Pix cobrança imediata (sem boleto): só se necessário (`02-arquitetura` §6.2), fora do MVP.

## Pontos em aberto
1. **Formato do certificado** (arquiteto, no sandbox): o Inter entrega `.crt` e `.key`; aceitar também `.pfx` com senha? Proposta: aceitar os dois.
2. **Uma integração por conta ou por tenant** (PO): proposta: por conta, porque o certificado e a conta corrente são da conta.
3. **Produção antes da GOL-01** (PO, ADR 0005 D3 e ADR 0020): a homologação já tem os dados reais; ligar a cobrança real em `homolog.konebras.com.br` antes do go-live? Proposta: não; no tenant `konebras` só Sandbox até a GOL-01, e os testes de sandbox ficam no tenant `demo`.
4. **Quem recebe o alerta de vencimento** (PO): proposta: Proprietário e quem tem `finance.edit`.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers + fake com mTLS) e E2E passando; teste manual no sandbox registrado.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`; `SecretKind` novos no teste golden.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda da lista e do detalhe; teste de cobertura passando.
- [ ] Alertas acionáveis (vencimento com link para trocar).
- [ ] Auditoria ligada (sem segredo).
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/operacao/segredos-e-chaves.md` (rotação do certificado do Inter) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `BankIntegration` (Domain) e validação do certificado (Application); `SecretKind.InterCredentials` e `InterCertificate`.
- [ ] `InterApiClient` (token em cache por tenant e conta, `HttpClient` com certificado de cliente, invalidação na troca).
- [ ] Endpoints `/api/v1/finance/bank-accounts/{id}/integration` (+ `/test`, `/rotate`, `DELETE` lógico dos segredos).
- [ ] Recorrente `inter-certificate-expiry`; tela, diálogos e ajudas; fake do Inter com mTLS para os testes.

## Quality gates
- Agentes previstos: @dev, cybersecurity (segredos, mTLS, log, rotação), @qa.
- Gate: CA 2, 3, 4, 8 e 9 obrigatórios.

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

- **Correções feitas na validação:** a configuração exige `settings.edit` (reservada) e não `finance.edit` (CA 4); a rotação testa as credenciais novas antes de trocar (CA 9); caminhos e escopos da API do Inter deixam de ser fixados na story e passam a ser conferidos no sandbox.
- **Pré-condições para `Ready`:** P-08 (conta com API habilitada e credencial de sandbox); pontos em aberto 1 e 3.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da BNK-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
