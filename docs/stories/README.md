# Stories — Fase 0 (Fundação) e Fase 1 (Cadastros e migração)

A Fase 1 está na [segunda parte deste arquivo](#fase-1--cadastros-e-migração).

Stories detalhadas da Fase 0 do BemAqui Empresas, uma por arquivo. Fonte: `docs/01` a `06`, ADRs 0001–0005 e a revisão `docs/revisoes/2026-10-16-revisao-pre-fase0.md` (correções, ADRs técnicas propostas 0006–0008 e CAs de segurança da §2.3).

## Definition of Ready
**Story sem CA testável não entra em execução.** Além disso, uma story só sai de `Draft` para `Ready` quando:
1. Todo critério de aceite está no formato Dado/Quando/Então e pode ser verificado por teste automatizado ou por um passo manual descrito.
2. As dependências estão concluídas ou a story explica como testa sem elas (implementação falsa).
3. Os "Pontos em aberto" que a própria story marca como pré-condição estão decididos e registrados (ADR ou `06-pendencias-riscos.md`).
4. O PO validou a story (checklist do @po).

## Legenda
| Item | Significado |
|---|---|
| Tamanho | P = até 1 sessão de trabalho · M = 2–3 sessões · G = 4+ sessões (mesma escala do backlog) |
| Status | `Draft` (criada, aguardando validação e decisões) → `Ready` → `InProgress` → `Review` → `Done` |
| `Requisitos:` | IDs do PRD com o prefixo `R-` (ADR 0005 D9). Requisitos não funcionais são citados pela seção (PRD §6) |
| **[Segurança]** | CA vindo da revisão de segurança (§2.3) ou exigido pelos docs de segurança |
| Pontos em aberto | Ambiguidade ou decisão que a story não toma; precisa de decisão do PO, do arquiteto ou da contadora |
| Re-estimada | Tamanho diferente do backlog original, com o motivo no cabeçalho da story |

## Índice
| Ordem | ID | Título | Tam. | Requisitos | Depende de | Arquivo |
|---|---|---|---|---|---|---|
| 1 | FND-01 | Estrutura da solução, CI e cadeia de suprimentos | M | PRD §6 Segurança | — | [FND-01](FND-01-estrutura-solucao.md) |
| 2 | FND-11 | Segredos e Data Protection | M | R-PLT-1, PRD §6 Segurança | 01 | [FND-11](FND-11-segredos-data-protection.md) |
| 3 | FND-07 | Building blocks | G | R-SEC-5, R-COM-2, R-COM-3, R-COM-4, R-PRP-5 | 01, 11 | [FND-07](FND-07-building-blocks.md) |
| 4 | FND-02 | Catálogo e resolução de tenant pelo host | G | R-PLT-1, R-PLT-2 | 01, 11, 07 | [FND-02](FND-02-catalogo-resolucao-tenant.md) |
| 5 | FND-03a | Migrator multi-tenant e serviço/CLI de provisionamento | G | R-PLT-1, R-PLT-3 | 02, 07, 11 | [FND-03a](FND-03a-migrator-provisionamento.md) |
| 6 | FND-08 | E-mail transacional por fila | M | R-COM-1 | 07, 11, 02 | [FND-08](FND-08-email.md) |
| 7 | FND-12 | Observabilidade | M | PRD §6 Observabilidade | 02, 07, 08, 11 | [FND-12](FND-12-observabilidade.md) |
| 8 | FND-04 | Autenticação do tenant | G | R-SEC-1, R-SEC-3, R-SEC-4, R-SEC-5, R-PLT-3 | 02, 03a, 07, 08, 11 | [FND-04](FND-04-autenticacao.md) |
| 9 | FND-05 | 2FA TOTP | M | R-SEC-2 | 04, 11 | [FND-05](FND-05-2fa-totp.md) |
| 10 | FND-13 | Autenticação do super admin | M | R-PLT-4 | 02, 04, 05, 11, 12 | [FND-13](FND-13-autenticacao-super-admin.md) |
| 11 | FND-03b | Tela do super admin: tenants | M | R-PLT-4, R-PLT-3 | 03a, 13, 09a, 09c | [FND-03b](FND-03b-tela-super-admin-tenants.md) |
| 12 | FND-06 | Catálogo de módulos e liberação por tenant | M | R-SEC-11, R-SEC-10, R-SEC-12 | 02, 03a, 13, 03b | [FND-06](FND-06-catalogo-modulos-liberacao.md) |
| 13 | FND-06b | Grupos de usuários e matriz de permissões | G | R-SEC-9, R-SEC-10, R-SEC-12, R-SEC-5 | 06, 04, 05, 09b, 09c | [FND-06b](FND-06b-grupos-permissoes.md) |
| 14 | FND-06c | Usuários, convite e permissão efetiva | M | R-SEC-4, R-SEC-12, R-SEC-2 | 06b, 04, 05, 08 | [FND-06c](FND-06c-usuarios-convite-permissao-efetiva.md) |
| 15 | FND-06d | Menu por permissão e módulo não liberado | P | R-SEC-11, R-SEC-12 | 06c, 09b | [FND-06d](FND-06d-menu-permissao-modulo-nao-liberado.md) |
| 16 | FND-10 | Deploy em homologação e backup cifrado | G | PRD §6 Backup, Disponibilidade, Segurança | 01, 03a, 11, 12, 13 | [FND-10](FND-10-deploy-homologacao.md) |
| 17 | FND-14 | Ajuda contextual e Dica | M | R-AJD-1, 2, 3, 4, 6, 7 | 06c, 06d, 09b | [FND-14](FND-14-ajuda-contextual-dica.md) |
| 18 | FND-15 | Pergunte ao BemAqui e provedor de IA | G | R-AJD-5, R-AJD-8, R-AJD-6 | 14, 11, 06c, 09c, 12 | [FND-15](FND-15-pergunte-ao-bemaqui.md) |
| ∥ | FND-09a | Tokens, tema e componentes visuais base | M | PRD §6 Usabilidade | 01 | [FND-09a](FND-09a-tokens-tema.md) |
| ∥ | FND-09b | Shell do backoffice e navegação | M | PRD §6 Usabilidade, R-COM-2 | 09a (sino: 07) | [FND-09b](FND-09b-shell-navegacao.md) |
| ∥ | FND-09c | DataTable, estados e formulário base | M | PRD §6 Desempenho, R-SEC-5, R-PRP-2 | 09a (histórico: 07) | [FND-09c](FND-09c-datatable-estados-formulario.md) |

Total: 21 stories (P: 1 · M: 13 · G: 7).

## Ordem de execução
Base: revisão §2.3 (`FND-01 → FND-07 → FND-02 → FND-03 → FND-08 → FND-04 → FND-05 → FND-06/06b/06c/06d → FND-10`, com a FND-09 em paralelo desde a FND-01), acrescida das stories novas:

```
FND-01 → FND-11 → FND-07 → FND-02 → FND-03a → FND-08 → FND-12 → FND-04 → FND-05
       → FND-13 → FND-03b → FND-06 → FND-06b → FND-06c → FND-06d → FND-10 → FND-14 → FND-15

Em paralelo, desde a FND-01:  FND-09a → FND-09b
                                     └→ FND-09c
```

Por que as novas entram onde entram:
- **FND-11** antes da FND-07 e da FND-02: o catálogo grava a connection string cifrada, e a auditoria precisa omitir segredos.
- **FND-12** depois da FND-08: os alertas saem por e-mail; os logs com tenant precisam da FND-02.
- **FND-13** antes da FND-03b e da FND-06: as telas da plataforma exigem o login do super admin.
- **FND-03b** e **FND-06** ficam depois da FND-13 (ambas são telas da plataforma).
- **FND-10** fecha a fundação de segurança; **FND-14/15** vêm depois porque dependem das permissões efetivas. A FND-14 pode começar em paralelo à FND-10.
- Telas que dependem da FND-09 (FND-03b, 04, 05, 06b, 06c, 06d, 14, 15) só fecham depois da 09a e da 09c (e da 09b quando usam o shell).

## Escolhas de quebra feitas nesta preparação
- **FND-03** → 03a (serviço, CLI e migrator) e 03b (tela do super admin), conforme a revisão.
- **FND-09** → 09a (tokens/tema), 09b (shell/navegação, ADR 0005 D8) e 09c (DataTable/estados/formulário), conforme a revisão. As telas de **login e 2FA**, que estavam na FND-09 original, foram para a FND-04 e a FND-05, junto das APIs que usam. O **sino** foi para a 09b e o **histórico por registro** (tela de auditoria da FND-07) para a 09c.
- **FND-07** ficou só com o backend (as telas foram para 09b/09c) e foi re-estimada para G.
- **FND-08**, **FND-10** e **FND-15** foram re-estimadas (motivo no cabeçalho de cada uma).

## Validação do PO (16/10/2026)
As 21 stories passaram pela checklist do @po (seção "Validação do PO" em cada arquivo). Correções aplicadas na validação:
- **Sequência FND-06b × FND-06c:** o serviço de permissão efetiva e a policy `perm:{modulo}.{acao}` passaram da FND-06c para a FND-06b, porque os endpoints de grupo e a regra de delegação já dependem deles.
- **FND-05 → FND-06c:** a tela Configurações → Segurança ("Exigir verificação em duas etapas para todos") passou para a FND-06c, porque depende da policy e do módulo `settings`, que ainda não existem na FND-05. A FND-05 mantém o mecanismo de exigência.
- **FND-03a, CA 1:** reescrito para ser testável sem a FND-04 (etapas de proprietário e convite com implementação falsa; ponta a ponta na FND-04, CA 19).
- **FND-06c, CA 2:** reescrito no formato Dado/Quando/Então.
- **FND-02:** dependência da FND-09a para a página "Acesso suspenso".

Resultado: nenhuma story está `Ready` ainda. Todas ficam em `Draft` com veredito **GO condicional**: entram em execução quando as pré-condições listadas na validação de cada uma forem decididas.

## Pré-condições de execução
- **ADR 0006 (persistência e mensageria)**, **ADR 0007 (chaves e segredos)** e **ADR 0008 (delegação de permissões)** estão só propostas na revisão §2.2; os arquivos `docs/adr/0006`–`0008` ainda não existem. A 0006 precisa estar aceita antes da FND-01; a 0007 antes da FND-11; a 0008 antes da FND-06b.
- DNS do host de homologação e do host da plataforma (PO) antes da FND-10.

## Rastreabilidade das correções da revisão
| Correção da revisão | Onde entra |
|---|---|
| §2.1 `tenant_domains` no 03; ADR 0001 marcada como alterada; remover `kiosk_token_index` | FND-02 (CA 13) |
| §2.1 tabela `module_actions` (ADR 0003) | FND-06 |
| §2.1 `webhook_events`, `devices`, `shipments`, `sales_targets`, junções de `payable_ids`/`receivable_ids`, `approver_group_id`, `UNIQUE NULLS NOT DISTINCT`, `project_cost_entries`, ajustes de máquinas de estado | Fora da Fase 0: entram nas stories das Fases 2 a 5 que usam essas tabelas |
| §2.2 ADR 0006 | FND-01 (arquitetura), FND-07 (outbox, inbox, `TenantJob`), FND-02 (interfaces por módulo), FND-03a (migrator) |
| §2.2 ADR 0007 | FND-11 (e consumo nas FND-02, 05, 08, 13, 15, 10) |
| §2.2 ADR 0008 | FND-04 (JWT só identidade), FND-06b e FND-06c (delegação e cache versionado) |
| §2.3 CAs de segurança | FND-01, 02, 03a, 03b, 04, 05, 06b, 06c, 07, 08, 10 (marcados com **[Segurança]**) |
| §2.3 stories novas FND-11, 12, 13; quebras de 03 e 09; DoR | Este diretório |
| §2.4 design (contraste, tokens, estados, 400px, acessibilidade, fonte, tema, `Money`, lint, mapeamento shadcn) | FND-09a, 09b, 09c, 06d |
| §3 FluentValidation na stack; licença QuestPDF | FND-01 |

## Pontos em aberto consolidados
Ver a seção "Pontos em aberto" de cada story para o detalhe.

**Decisões de produto (PO)**
1. Catálogo definitivo de módulos: códigos, nomes, áreas, ações aplicáveis e significado de "aprovar" (proposta na FND-06).
2. Módulo `settings` (Configurações) no catálogo e seu schema (FND-05, 06, 15).
3. Permissões padrão dos 8 grupos iniciais, inclusive RH com ou sem `hr.view_sensitive` e Financeiro com ou sem `finance.approve` (o PRD diz "padrão: proprietário aprova tudo") (FND-06b).
4. Plano inicial da Konebras e módulos liberados na Fase 0 (FND-03a, 06, 03b).
5. Política de senha, validade do convite, quantidade de códigos de recuperação e validade do token intermediário de 2FA (FND-04, 05, 06c).
6. Valores de rate limit e janela de tolerância do refresh concorrente (FND-04).
7. IPs da allowlist do super admin, acesso de fora deles e status da recusa (404 proposto) (FND-13).
8. Tela de "acesso negado" para módulo liberado sem permissão (texto proposto) (FND-06d).
9. Tela inicial do backoffice na Fase 0 e escopo da busca da topbar (só navegação) (FND-09b).
10. Se `identity.view` pode mostrar e-mails de todos os usuários (LGPD) (FND-06c).
11. Pergunta do usuário com dado de negócio enviada à IA: aviso, filtro ou aceitar (FND-15); tempo limite e cota de uso da IA; modelo como texto livre ou lista.
12. Homologação na 1ª semana (revisão) × FND-10 no fim da ordem: quebrar em FND-10a/10b? (FND-10).
13. CA original "tag publica em prod" fica para a GOL-01 (conflito com a ADR 0005 D1) (FND-10).
14. Canal de alerta (e-mail proposto) e serviço do monitor externo (FND-12).

**Decisões técnicas (arquiteto, registrar em ADR)**
15. Redigir e aceitar as ADRs 0006, 0007 e 0008.
16. Cookie de refresh: `__Host-` exige `Path=/`, o que conflita com "Path restrito" da revisão (FND-04).
17. Onde persistir o key ring, origem do certificado de proteção e destino da trilha separada do backup da chave (FND-11).
18. Migrator como container próprio (ADR 0006) × "migrations na subida do api" (`02-arquitetura` §7) (FND-03a).
19. Mecanismo de invalidação do cache de tenant para suspensão em < 10 s (FND-02).
20. Rotas anônimas fora da lista do CLAUDE.md: contexto público do login (nome e logo do tenant) e `/health/live` (FND-02, 12).
21. Allowlist de tipos e tamanho de anexos; política com ClamAV fora do ar (falha fechada proposta); campos `[PersonalData]` na Fase 0 (FND-07).
22. Sino em tempo real (SignalR) ou por consulta periódica (FND-07, 09b).
23. Motor dos templates padrão de e-mail (Fluid para todos, proposto), SMTP da plataforma ou por tenant, provedor e SPF/DKIM (FND-08).
24. Tabelas que não estão no `03-modelo-dados.md`: preferências do usuário, ajudas vistas, `platform_audit_log`, `ai_settings`, `ai_usage_logs`, `inbox_messages`, `platform_alerts` (FND-07, 09a, 12, 13, 14, 15).
25. Local da pasta `Ajuda/`, significado de `padrao` e método de busca do Pergunte ao BemAqui (conferir o NSHome) (FND-14, 15).
26. Se rotas públicas e da superfície Plataforma entram na cobertura obrigatória de ajuda (FND-02, 13, 14).
27. Hosts e diretório de homologação, host da plataforma em homologação, storage S3, ferramenta de cifragem dos dumps e registry das imagens (FND-10).
28. Runner do CI e licença QuestPDF (FND-01).

## Pendências herdadas de stories concluídas
- ~~**FND-11 → FND-12:**~~ (resolvido na FND-12: cabeçalhos e corpo nunca são logados; máscara por nome de propriedade) ao ligar o request logging (Serilog ou HttpLogging), mascarar `Authorization`, cookies e tokens, e as propriedades com nome `password`, `secret`, `token` ou `apikey`.
- **FND-11 → FND-10b:**
  - trilha separada do backup do key ring, com destino e credencial próprios;
  - `DataProtection:PreviousCertificates`, para continuar decifrando com o certificado antigo;
  - job de recifração antes de qualquer revogação de chave ou troca de certificado.
- **FND-11 → FND-03a:** a criação inicial do key ring passa a ser um passo explícito do provisionamento, recusado quando o catálogo já tiver tenants.
- **FND-02 → FND-09b:** página pública "Acesso suspenso", exibida quando `GET /api/v1/public/tenant-context` devolve `status: Suspended` ou quando a API responde 403 `tenant_suspended`. Texto definido na FND-02.
- **FND-02 → FND-10a:** em homologação e produção, configurar `ReverseProxy:KnownNetworks` com a sub-rede da `stack_stack`, `ReverseProxy:RequireTrustedProxy=true` e `Auth:SigningKey` como secret.

---

# Fase 1 — Cadastros e migração

Stories detalhadas da Fase 1, uma por arquivo, escritas pelo @sm (River) e validadas pelo @po (Pax) em 18/10/2026. Fonte: `docs/01` a `06`, ADRs 0001–0015, o código da Fase 0 (`ModuleCatalog`, building blocks, componentes do front) e a revisão pré-Fase 0 §3. A Definition of Ready, a legenda e a escala de tamanho são as mesmas da Fase 0.

## Índice
| Ordem | ID | Título | Tam. | Requisitos | Depende de | Arquivo |
|---|---|---|---|---|---|---|
| 1 | CAD-06 | Configurações da empresa | M | R-COM-4, R-COM-3, R-RH-2, R-PRJ-7, R-COML-1, R-SEC-5 | Fase 0 | [CAD-06](CAD-06-configuracoes-empresa.md) |
| 2 | CAD-05a | Plano de contas gerencial e centros de custo | M | R-FIN-1, R-PLT-3, R-FIN-13, R-SEC-5 | Fase 0 | [CAD-05a](CAD-05a-plano-contas-centros-custo.md) |
| 3 | CAD-05b | Contas bancárias, caixas e condições de pagamento | M | R-FIN-2, R-TER-2, R-PRP-1, R-SEC-5 | CAD-05a | [CAD-05b](CAD-05b-contas-bancarias-condicoes-pagamento.md) |
| 4 | CAD-01a | Terceiros: cadastro único com papéis, endereços e contatos | G | R-TER-1, R-TER-2, R-PRP-1, R-PRP-3, R-SEC-5 | CAD-06, CAD-05b | [CAD-01a](CAD-01a-terceiros.md) |
| 5 | CAD-01b | Consulta de CNPJ | M | R-TER-1, R-PRP-3, R-PRP-7 | CAD-01a | [CAD-01b](CAD-01b-consulta-cnpj.md) |
| 6 | CAD-03 | Locais de estoque, centros de trabalho e calendário | M | R-EST-2, R-PLN-1, R-SEC-5 | Fase 0 | [CAD-03](CAD-03-locais-centros-trabalho-calendario.md) |
| 7 | CAD-02a | Itens, unidades e categorias | M | R-EST-1, R-EST-8, R-PLT-3, R-PRJ-7, R-SEC-5 | CAD-06 | [CAD-02a](CAD-02a-itens-unidades-categorias.md) |
| 8 | CAD-02b | Conversões de unidade e códigos do fornecedor | M | R-EST-3, R-FIS-5, R-PRP-3 | CAD-02a, CAD-01a | [CAD-02b](CAD-02b-conversoes-codigos-fornecedor.md) |
| 9 | CAD-04a | Cargos, jornadas, sindicatos e custo-hora | M | R-RH-2, R-RH-1, R-PRD-4, PRD §6 LGPD | CAD-06, CAD-03 | [CAD-04a](CAD-04a-cargos-jornadas-custo-hora.md) |
| 10 | CAD-04b | Funcionários, dependentes, histórico e afastamentos | G | R-RH-1, R-RH-3, R-SEC-10, PRD §6 LGPD | CAD-04a, CAD-06, CAD-03 | [CAD-04b](CAD-04b-funcionarios-dependentes-historico.md) |
| 11 (∥ desde 2) | MIG-01a | Mapeamento e infraestrutura de importação do Dolibarr | G | R-TER-5, PRD §6 Segurança e LGPD | Fase 0 | [MIG-01a](MIG-01a-infra-importacao-dolibarr.md) |
| 12 | MIG-01b | Carga de terceiros, contatos, condições e contas bancárias | M | R-TER-5, R-TER-1, R-TER-2, R-FIN-2 | MIG-01a, CAD-01a, CAD-05b | [MIG-01b](MIG-01b-carga-terceiros-financeiro.md) |
| 13 | MIG-01c | Carga de unidades, categorias, itens, códigos do fornecedor e locais | M | R-TER-5, R-EST-1, R-EST-2, R-EST-3, R-PRJ-7 | MIG-01b, CAD-02a, CAD-02b, CAD-03 | [MIG-01c](MIG-01c-carga-itens-locais.md) |
| 14 | MIG-01d | Saldos iniciais de estoque ("Saldo inicial") | P | R-TER-5, R-EST-4 a R-EST-8, R-PRP-7 | MIG-01c, **EST-01** | [MIG-01d](MIG-01d-saldos-iniciais-estoque.md) |
| 15 | MIG-01e | Títulos em aberto e histórico de pagamentos | M | R-TER-5, R-FIN-3, R-FIN-4, R-FIN-5, R-TER-4, R-FIN-11 | MIG-01b, CAD-05a, **FIN-01, FIN-02** | [MIG-01e](MIG-01e-titulos-abertos-historico.md) |

Total: 15 stories (P: 1 · M: 11 · G: 3).

## Ordem de execução
```
CAD-06 → CAD-05a → CAD-05b → CAD-01a → CAD-01b → CAD-03 → CAD-02a → CAD-02b → CAD-04a → CAD-04b
         (a CAD-03 pode correr em paralelo com a CAD-01a/01b)
Em paralelo, desde a CAD-05a:  MIG-01a
Depois dos cadastros:          MIG-01b → MIG-01c
Dependem de tabelas de fases seguintes:
                               MIG-01d depois do EST-01 · MIG-01e depois da FIN-01 e da FIN-02
                               (as duas rodam de novo na carga final da GOL-01)
```

Por que esta ordem:
- **CAD-06** primeiro: traz o validador de CPF/CNPJ, a numeração editável e os parâmetros (% de encargos, garantia, modo de precificação) que as outras usam.
- **CAD-05a/05b** antes da CAD-01a: o terceiro tem condição de pagamento padrão.
- **CAD-03** não depende de cadastro nenhum; precisa vir antes da CAD-04a (centro de trabalho padrão do cargo) e da MIG-01c (locais).
- **CAD-02b** depois da CAD-01a: o código do fornecedor exige o papel Fornecedor.
- **CAD-04a** depois da CAD-06 (% de encargos e evento `PayrollChargesChanged`).
- **MIG-01a** não depende dos cadastros (é a infraestrutura de leitura, staging, chaves externas e relatório) e pode começar cedo; as cargas MIG-01b/01c esperam os cadastros em que gravam.
- **MIG-01d** precisa de `stock_movements` (EST-01, Fase 3) e **MIG-01e** de `receivables`/`payables` (FIN-01/FIN-02, Fase 2). Como o Dolibarr está com estoque zerado (P-07) e a carga final é na GOL-01, a proposta é executá-las depois dessas stories; o PO decide se a Fase 1 fecha sem elas.

## Escolhas de quebra feitas nesta preparação
- **CAD-01** → 01a (cadastro) e 01b (consulta de CNPJ), para o cadastro não esperar a escolha do provedor.
- **CAD-02** → 02a (itens, unidades, categorias, NCM, lote/série) e 02b (conversões e códigos do fornecedor).
- **CAD-04** → 04a (cargos, jornadas, sindicatos, custo-hora) e 04b (funcionários com LGPD, dependentes, histórico, afastamentos, vínculo com o usuário).
- **CAD-05** → 05a (plano de contas e centros de custo) e 05b (contas bancárias, caixas e condições de pagamento).
- **MIG-01** → 01a (mapeamento e infraestrutura), 01b (terceiros e cadastros financeiros), 01c (itens e locais), 01d (saldos iniciais) e 01e (títulos).
- **Re-estimadas:** CAD-03 (P → M) e CAD-06 (P → M), com o motivo no cabeçalho.

## Validação do PO (18/10/2026)
As 15 stories passaram pela checklist do @po (seção "Validação do PO" em cada arquivo). Correções aplicadas:
- **CAD-05b, CA 11:** percentuais da prévia "30/60/90" corrigidos para 33,3333% (com 33,33% a parcela não daria R$ 333,33).
- **CAD-06, CA 14:** o enum de tipos de item vai para `Inventory.Contracts`, criado na CAD-06, para o Identity não depender do domínio do Inventory.
- **CAD-01a:** retirado o botão "Importar do Dolibarr" do estado vazio (a migração é só CLI).
- **CAD-03:** Carnaval e Corpus Christi como pontos facultativos (sugeridos desmarcados).
- **CAD-04b, CA 16:** "Desativar o usuário" só com `identity.edit`, pelo endpoint da FND-06c.
- **MIG-01d e MIG-01e:** ponto em aberto novo sobre o fechamento da Fase 1 com dependência de stories das Fases 2 e 3.

Resultado: nenhuma story está `Ready`. Todas ficam em `Draft` com veredito **GO condicional** até as pré-condições de cada uma serem decididas.

## Pontos em aberto consolidados da Fase 1
Detalhe e sugestão na seção "Pontos em aberto" de cada story.

**Decisões de produto (PO)**
1. Fonte da verdade de razão social, nome fantasia e CNPJ da empresa: catálogo × banco do tenant (CAD-06).
2. Valor inicial sugerido e faixa do % de encargos (a P-05 fala em "padrão da contadora", sem número) (CAD-06, CAD-04a).
3. Modo de precificação padrão e onde fica o % padrão de markup/margem (P-14) (CAD-06).
4. Tipos de item com garantia e meses padrão (P-11) (CAD-06).
5. Tipos de documento novos na numeração (movimento de estoque, código de item, matrícula) e regras do prefixo (CAD-06, CAD-02a, CAD-04b, MIG-01d).
6. Conteúdo do plano de contas padrão, máscara do código, lista do grupo da DRE e seed de centros de custo, com a contadora (CAD-05a).
7. Seed de condições de pagamento, saldo inicial negativo, tipos de conta além de Banco/Caixa, vencimento em dia não útil (CAD-05b).
8. Permissões padrão de `parties` nos grupos (nenhum grupo do seed tem hoje) e como aplicar no tenant já provisionado (CAD-01a).
9. PF sem CPF, lista de regimes tributários, validação da IE por UF, fusão de duplicados na tela, máscara parcial do CPF (CAD-01a).
10. Provedor da consulta de CNPJ (sugestão: BrasilAPI, com ReceitaWS ou CNPJá de reserva), reserva automática, validade do cache, consulta de CEP, CNPJ com situação não ativa (CAD-01b).
11. Calendário único ou por centro, horas por dia derivadas dos turnos, pontos facultativos, turno que vira o dia, nome do local padrão (CAD-03).
12. Combinações de lote e série por tipo, NCM obrigatório para consumo, validação do NCM contra a tabela oficial, código do item, item genérico por modelo × por projeto (CAD-02a).
13. Salário que entra no custo-hora do cargo, horas mensais contratuais, se o custo-hora é sensível, seed de jornada (CAD-04a).
14. Lista de documentos do funcionário, campos sensíveis, vale-transporte, tipos de afastamento, retenção do registro de leitura sensível, exportação/anonimização LGPD, matrícula (CAD-04b).
15. O que não migra do Dolibarr; conflito na reexecução; ambiente da primeira carga; usuário técnico da auditoria (MIG-01a).
16. Instaladores e transportadoras no Dolibarr, terceiros encerrados, prospects, campo do CNPJ, saldo bancário de partida (MIG-01b).
17. Produto sem NCM, de-para de tipos, produto em várias categorias, sub-depósitos, campo do NCM (MIG-01c).
18. Quando executar MIG-01d e MIG-01e e se a Fase 1 fecha sem elas; histórico de pagos; notas de crédito; conta dos títulos importados; origem do título a pagar importado (MIG-01d, MIG-01e).

**Decisões técnicas (arquiteto, registrar em ADR)**
19. Rota anônima do logo do tenant (rota nova × data URI no `tenant-context`) e limite do arquivo (CAD-06).
20. Dono das condições de pagamento: `parties.payment_terms` (03) × `finance.payment_terms` (CAD-05b, CAD-01a).
21. Excluir × inativar em `inventory` e `production`, que não têm a ação `delete` no `ModuleCatalog` (CAD-02a, CAD-03).
22. Mecanismo de registro da leitura de dado sensível (`SensitiveRead` na auditoria) (CAD-04b).
23. Leitura do Dolibarr: conexão direta somente leitura × extração para arquivo; rede até o `dolibarr-db`; módulo `Migration` novo × Host/CLI; tela de relatório ou só CLI (MIG-01a).
24. Tipo de movimento `OpeningBalance` ("Saldo inicial") no 03 (MIG-01d).
25. Tabelas e campos propostos fora do 03, a incluir na story que os criar: `identity.company_profile`, `shared.number_formats`, `parties.cnpj_lookups`, `hr.unions`, schema `migration` e os campos `is_active`, `is_default`, `seed_key`, `warranty_months`, `reference_salary`, `monthly_hours`, `hourly_cost_manual`, `is_imported_history` e os desdobramentos de endereço e banco/Pix do funcionário.
