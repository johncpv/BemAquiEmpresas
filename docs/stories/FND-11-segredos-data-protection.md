# FND-11 — Segredos e Data Protection

| Campo | Valor |
|---|---|
| ID | FND-11 |
| Título | Segredos e Data Protection (key ring, propósito por tenant, write-only) |
| Tamanho | M |
| Status | Done |
| Requisitos | R-PLT-1 (conexões criptografadas no catálogo) · PRD §6 Segurança (segredos criptografados por tenant) · base de R-SEC-2 (segredo TOTP cifrado) e R-AJD-8 (chave de IA cifrada) |
| Dependências | FND-01. **Pré-condição:** ADR 0007 redigida e aceita pelo PO |
| Bloqueia | FND-02 (connection string cifrada), FND-05 (segredo TOTP), FND-08 (senha SMTP), FND-13, FND-15 (chave de IA), FND-10 (backup do key ring) |
| Ordem de execução | 2 |

## User story
Como **proprietário da plataforma (PO)**, quero **que todo segredo (conexão do banco do tenant, senha SMTP, segredo TOTP, chave de IA e, depois, certificados e credenciais do Inter) seja cifrado com chave fora do banco, separado por tenant e nunca devolvido nem logado**, para **que um vazamento de banco, de backup ou de log não exponha credenciais dos clientes**.

## Contexto e referências
- `docs/02-arquitetura.md` §5: segredos por tenant criptografados com Data Protection e chave mestra fora do banco.
- `docs/03-modelo-dados.md`: `catalog.tenants.connection_secret` (cifrado), `identity.users.totp_secret` (cifrado); depois `finance.bank_accounts.credentials_secret` e `fiscal.fiscal_settings.certificate_secret`.
- Revisão §2.2, **ADR 0007 (crítico)**: key ring persistido e protegido por certificado, com cópia offline guardada pelo PO; propósito separado por tenant e por tipo de segredo; segredos write-only na API e nunca logados; backup da chave em trilha separada.
- ADR 0005 D6 (backup cifrado) e D7 (VPS compartilhada).

## Critérios de aceite
1. **Dado** um segredo cifrado para o tenant A com o tipo `SmtpPassword`, **quando** tento decifrá-lo com o propósito do tenant B ou com o tipo `AiApiKey`, **então** a operação falha com erro de criptografia e nenhum texto é devolvido.
2. **Dado** o key ring persistido, **quando** inspeciono o armazenamento das chaves, **então** cada chave está protegida pelo certificado configurado (`ProtectKeysWithCertificate`) e nenhuma chave aparece em texto claro.
3. **Dado** o Host sem acesso ao key ring ou ao certificado, **quando** ele sobe, **então** a inicialização falha com mensagem clara no log e o Host **não** gera um key ring novo em silêncio (fail fast).
4. **Dado** uma rotação de chave (nova chave ativa), **quando** leio um segredo cifrado com a chave anterior, **então** ele ainda é decifrado; **quando** gravo de novo, **então** passa a usar a chave ativa.
5. **Dado** um endpoint que grava um segredo, **quando** consulto o mesmo recurso pela API, **então** a resposta traz só `configured` (verdadeiro/falso) e `updatedAt`, nunca o valor nem parte dele (write-only).
6. **Dado** um segredo passando por log, exceção ou auditoria, **quando** inspeciono a saída do Serilog e o `audit_log` num teste com sink em memória, **então** o valor não aparece: a propriedade é omitida ou mostrada como `***`.
7. **Dado** um backup do catálogo e de um tenant restaurado em outra máquina **com** a cópia do key ring e do certificado, **quando** o Host sobe, **então** os segredos são decifrados; **sem** a cópia, **então** nenhum segredo é decifrado.
8. **Dado** o runbook de backup do key ring, **quando** o sigo, **então** a cópia vai para uma trilha separada do backup dos bancos (outro destino e outra credencial), e o runbook descreve a cópia offline guardada pelo PO.

## Regras de negócio e técnicas
- Serviço `ISecretProtector` em `BuildingBlocks`: `Protect(escopo, SecretKind, texto)` e `Unprotect(...)`. Propósito no formato `bemaqui:{escopo}:{tipo}:v1`, em que o escopo é `platform` ou o `tenantId`.
- `SecretKind` inicial: `TenantConnection`, `SmtpPassword`, `TotpSecret`, `AiApiKey`. `InterCredentials`, `InterCertificate` e `FiscalCertificate` entram na Fase 5 (BNK-01, FIS-02) sem mudar o mecanismo.
- Um tipo `Secret` (ou atributo `[Secret]`) marca a propriedade. A política de destructuring do Serilog e o interceptor de auditoria (FND-07) omitem o valor.
- DTOs de leitura nunca têm campo de segredo; DTOs de escrita aceitam o valor e não o ecoam.
- O certificado de proteção do key ring é montado como arquivo ou secret do Docker, fora do banco e fora da imagem.

## Permissões, eventos e tabelas
- Key ring: `catalog.data_protection_keys` (padrão `PersistKeysToDbContext`) ou volume de arquivo. Ver Pontos em aberto.
- Sem permissão de módulo: é infraestrutura. As telas que gravam segredo (SMTP, IA) declaram a permissão nas próprias stories.

## Telas e estados
Sem tela nesta story. Fica definido aqui o padrão do campo write-only, usado nas FND-08 e FND-15: campo vazio com o rótulo "Chave configurada em {data}" e o botão "Substituir". Nunca mostrar máscara com parte do valor.

## Plano de testes
- **Unidade:** propósito por tenant e por tipo (CA 1); rotação (CA 4); política do Serilog que omite `Secret` (CA 6).
- **Integração (Testcontainers):** key ring no PostgreSQL protegido por certificado de teste (CA 2); Host sem certificado falha ao subir (CA 3); restauração com e sem key ring (CA 7).
- **Arquitetura:** nenhum DTO de resposta em `*.Endpoints` tem propriedade do tipo `Secret` (CA 5).

## Fora do escopo
- Certificado A1 e credenciais do Inter (Fase 5).
- Rotação agendada própria (vale a padrão do Data Protection).
- HSM ou cofre externo (Vault, KMS).

## Pontos em aberto
- **ADR 0007:** proposta na revisão §2.2, ainda sem o arquivo `docs/adr/0007-*.md`. Redigir e aceitar antes da execução.
- **Onde persistir o key ring:** no banco de catálogo (`PersistKeysToDbContext`) ou em volume de arquivo. A ADR 0007 diz só "persistido e protegido por certificado".
- **Origem do certificado de proteção:** autoassinado gerado pelo PO ou emitido por uma AC; validade e procedimento de troca.
- **Destino da trilha separada** do backup do key ring (outro bucket? outro provedor?) e forma da cópia offline com o PO.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade e de integração (Testcontainers) passando.
- [ ] Migration criada, se o key ring ficar no catálogo.
- [ ] Permissão registrada no seed: não se aplica.
- [ ] Tela com estados: não se aplica.
- [ ] Ajuda da tela: não se aplica.
- [ ] Padrões e próximo passo propositivos: não se aplica.
- [ ] Auditoria ligada: o interceptor omite `Secret` (verificado também na FND-07).
- [ ] Runbook `docs/operacao/segredos-e-chaves.md` escrito (backup, restauração e troca do certificado).
- [ ] ADR 0007 aceita; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Redigir `docs/adr/0007-chaves-e-segredos.md` com o PO.
- [ ] Configurar o Data Protection no Host: `SetApplicationName("bemaqui-empresas")`, persistência do key ring e `ProtectKeysWithCertificate`.
- [ ] Implementar `ISecretProtector`, `SecretKind` e o tipo ou atributo `Secret`.
- [ ] Política de destructuring do Serilog que omite `Secret`.
- [ ] Verificação na inicialização (fail fast) quando faltam key ring ou certificado.
- [ ] Teste de arquitetura para DTOs de resposta.
- [ ] Runbook de backup e restauração do key ring.

## Quality gates
- Agentes previstos: @architect, cybersecurity (revisão de criptografia), @qa.
- Gate: revisão de segurança aprovada antes do merge.

## Validação do PO
Checklist do @po (Pax), 16/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança da revisão §2.3 que se aplicam | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | N/A |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** —
- **Pré-condições para `Ready`:** ADR 0007 aceita; decidir onde persistir o key ring e a origem do certificado.
- **Prontidão:** 8/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 16/10/2026 | 0.1 | Story nova, criada a partir da revisão pré-Fase 0 (§2.2, ADR 0007) | River (SM) |
| 16/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |

## Dev Agent Record
- **Data e branch:** 17/10/2026, `feat/fnd-11-segredos`.
- **Implementação:**
  - `Secret` (SharedKernel), com conversor JSON e igualdade em tempo constante.
  - `ISecretProtector` / `SecretProtector`, com propósitos fixos `bemaqui/{escopo}/{tipo}/v1` e teste golden.
  - `DataProtectionKeysDbContext` (`security.data_protection_keys`), com a migration `InitialDataProtectionKeys`.
  - `ProtectionCertificate`, que exige chave privada e RSA de pelo menos 3072 bits e lê a senha de arquivo.
  - `SecretsStartupCheck`, com fail fast em `StartingAsync`.
  - `SecretDestructuringPolicy` no Serilog.
  - Logger de bootstrap no Host.
  - Runbook em `docs/operacao/segredos-e-chaves.md`.
- **Critérios de aceite:**
  - **CA 1 a 7:** atendidos, com testes de unidade, de integração (Testcontainers) e de arquitetura.
  - **CA 4:** a rotação foi testada expirando a chave no key ring persistido. Não dava para simular a passagem do tempo, porque o Data Protection não usa `TimeProvider`. E uma chave recém-criada não vira a chave padrão enquanto está na janela de propagação.
  - **CA 8: parcial.** O runbook está escrito, mas o destino da trilha separada fica para a FND-10b.
- **Revisão de segurança** (cybersecurity, 17/10/2026): aprovada com ajustes.
  - **Ajustes obrigatórios (os 4 foram aplicados):**
    - procedimento de revogação e de troca do certificado;
    - recusa da máscara `***`;
    - propósitos fixos;
    - opt-in para o certificado de desenvolvimento e aviso quando a flag de criação estiver ligada.
  - **Também entraram:** senha do certificado lida de arquivo, validação do certificado, contagem só de chaves ativas, logger de bootstrap, igualdade em tempo constante e limpeza dos certificados de teste.
- **Pendências herdadas:** estão em `docs/stories/README.md` (FND-12, FND-10b e FND-03a).

## QA Results
_A preencher pelo @qa._