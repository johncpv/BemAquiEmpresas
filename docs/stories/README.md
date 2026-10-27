# Stories — Fases 0 a 8

A Fase 1 está na [segunda parte deste arquivo](#fase-1--cadastros-e-migração), a Fase 2 na [terceira](#fase-2--comercial-projetos-e-financeiro-núcleo), a Fase 3 na [quarta](#fase-3--engenharia-estoque-compras-e-produção) e as Fases 4 a 8 nas seguintes (revistas em 21/10/2026 depois das decisões do PO sobre a revisão pré-Fases 4 a 8), seguidas do [marco final "Go-live"](#marco-final--go-live).

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
| 19 | FND-09d | Componentes de formulário e moldes de tela (nova, D7 da revisão pré-Fase 1) | G | PRD §6 Usabilidade, R-PRP-1, R-PRP-2, R-PRP-3, R-AJD-4 | 09a, 09b, 09c | [FND-09d](FND-09d-componentes-formulario.md) |
| 20 | FND-09e | Componentes e convenções compartilhados da Fase 2 (nova, revisão pré-Fase 2 §3) | M | PRD §6 Usabilidade, R-PRP-2, R-PRP-7, R-COM-2, R-SEC-5 | 09c, 09d, 07; ADR 0019, 0021 | [FND-09e](FND-09e-componentes-convencoes-fase2.md) |

Total: 23 stories (P: 1 · M: 14 · G: 8).

## Ordem de execução
Base: revisão §2.3 (`FND-01 → FND-07 → FND-02 → FND-03 → FND-08 → FND-04 → FND-05 → FND-06/06b/06c/06d → FND-10`, com a FND-09 em paralelo desde a FND-01), acrescida das stories novas:

```
FND-01 → FND-11 → FND-07 → FND-02 → FND-03a → FND-08 → FND-12 → FND-04 → FND-05
       → FND-13 → FND-03b → FND-06 → FND-06b → FND-06c → FND-06d → FND-10 → FND-14 → FND-15

Em paralelo, desde a FND-01:  FND-09a → FND-09b
                                     └→ FND-09c
Antes da Fase 1 (D7):          FND-09d (depois da 09a, 09b e 09c)
Antes da Fase 2:               FND-09e (depois da 09c e da 09d; antes da FIN-01)
```

Por que as novas entram onde entram:
- **FND-11** antes da FND-07 e da FND-02: o catálogo grava a connection string cifrada, e a auditoria precisa omitir segredos.
- **FND-12** depois da FND-08: os alertas saem por e-mail; os logs com tenant precisam da FND-02.
- **FND-13** antes da FND-03b e da FND-06: as telas da plataforma exigem o login do super admin.
- **FND-03b** e **FND-06** ficam depois da FND-13 (ambas são telas da plataforma).
- **FND-10** fecha a fundação de segurança; **FND-14/15** vêm depois porque dependem das permissões efetivas. A FND-14 pode começar em paralelo à FND-10.
- Telas que dependem da FND-09 (FND-03b, 04, 05, 06b, 06c, 06d, 14, 15) só fecham depois da 09a e da 09c (e da 09b quando usam o shell).
- **FND-09d** (nova em 18/10/2026, decisão D7): moldes e componentes de formulário que todos os cadastros da Fase 1 usam. Fecha a Fase 0 e vem antes do Marco A. O PO valida a FND-09a/09b no início dela.
- **FND-09e** (nova em 20/10/2026, revisão pré-Fase 2 §3): componentes e convenções que as telas e os documentos da Fase 2 usam (mensagens da ADR 0021, diálogo financeiro, editor de linhas, tabela com totais, gráficos, concorrência, idempotência). Vem antes da FIN-01; o detalhe está na [terceira parte](#fase-2--comercial-projetos-e-financeiro-núcleo).

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

Stories detalhadas da Fase 1, uma por arquivo, escritas pelo @sm (River) e validadas pelo @po (Pax) em 18/10/2026. Fonte: `docs/01` a `06`, ADRs 0001–0018, o código da Fase 0 e a revisão `docs/revisoes/2026-10-18-revisao-pre-fase1.md`. A Definition of Ready, a legenda e a escala de tamanho são as mesmas da Fase 0.

## Decisões do PO (18/10/2026, John)
- **Aprovadas** as recomendações da revisão pré-Fase 1: D1, D3, D4, D6, D7, D8, D9, D10 e D11 (a tabela 1.1 inteira), mais as seções 2.1 a 2.5.
- **Escopo da migração:** a migração traz **todos os cadastros**, inclusive condições de pagamento e contas bancárias e caixas **sem saldo**. **Não traz lançamentos nem financeiro transacional:** títulos a receber e a pagar, pagamentos, recebimentos, movimentos bancários, saldos bancários e saldos de estoque, que serão feitos no BemAqui. Por isso **D2 e D5 ficam sem efeito** (ADR 0017).
- **Canceladas pelo PO:** MIG-01d (saldos iniciais de estoque) e MIG-01e (títulos em aberto e histórico de pagamentos). Os arquivos foram apagados; elas não vão para as Fases 2 e 3. O `OpeningBalance` continua como tipo de movimento da EST-01, para o saldo inicial digitado no sistema.
- ADRs novas: **0016** (decisões técnicas da Fase 1), **0017** (importação do Dolibarr) e **0018** (LGPD, retenção e titular).

## Índice
| Ordem | ID | Título | Tam. | Status | Requisitos | Depende de | Arquivo |
|---|---|---|---|---|---|---|---|
| 0 (Fase 0) | FND-09d | Componentes de formulário e moldes de tela | G | Ready | PRD §6 Usabilidade, R-PRP-1, R-PRP-2, R-PRP-3, R-AJD-4 | FND-09a, 09b, 09c | [FND-09d](FND-09d-componentes-formulario.md) |
| 1 (A) | CAD-01a | Terceiros: cadastro único com papéis, endereços e contatos | G | Ready | R-TER-1, R-TER-2, R-PRP-1, R-PRP-3, R-SEC-5 | FND-09d | [CAD-01a](CAD-01a-terceiros.md) |
| 2 (A) | MIG-01a | Mapeamento e infraestrutura de importação do Dolibarr | G | Done | R-TER-5, PRD §6 Segurança e LGPD | FND-09d, Fase 0 | [MIG-01a](MIG-01a-infra-importacao-dolibarr.md) |
| 3 (A) | CAD-05b | Contas bancárias, caixas e condições de pagamento | M | Done | R-FIN-2, R-TER-2, R-PRP-1, R-SEC-5 | CAD-01a, FND-09d | [CAD-05b](CAD-05b-contas-bancarias-condicoes-pagamento.md) |
| 4 (A) | MIG-01b | Carga de terceiros, contatos, condições de pagamento e contas bancárias | M | Done | R-TER-5, R-TER-1, R-TER-2, R-FIN-2 | MIG-01a, CAD-01a, CAD-05b | [MIG-01b](MIG-01b-carga-terceiros-cadastros-financeiros.md) |
| 5 (A) | CAD-01b | Consulta de CNPJ e de CEP | M | Done | R-TER-1, R-PRP-3, R-PRP-7 | CAD-01a | [CAD-01b](CAD-01b-consulta-cnpj.md) |
| 6 (B) | CAD-06 | Configurações da empresa | M | Ready | R-COM-4, R-COM-3, R-RH-2, R-COML-1, R-SEC-5 | CAD-01a, CAD-01b, FND-09d | [CAD-06](CAD-06-configuracoes-empresa.md) |
| 7 (B) | CAD-03 | Locais de estoque, centros de trabalho e calendário | M | Ready | R-EST-2, R-PLN-1, R-SEC-5, R-PRP-1 | FND-09d | [CAD-03](CAD-03-locais-centros-trabalho-calendario.md) |
| 8 (B) | CAD-02a | Itens, unidades, categorias e garantia padrão | M | Ready | R-EST-1, R-EST-8, R-PLT-3, R-PRJ-7, R-SEC-5 | CAD-03, FND-09d | [CAD-02a](CAD-02a-itens-unidades-categorias.md) |
| 9 (B) | CAD-02b | Conversões de unidade e códigos do fornecedor | M | Ready | R-EST-3, R-FIS-5, R-PRP-3 | CAD-02a, CAD-01a | [CAD-02b](CAD-02b-conversoes-codigos-fornecedor.md) |
| 10 (B) | MIG-01c | Carga de unidades, categorias, itens, códigos do fornecedor e locais | M | Ready | R-TER-5, R-EST-1, R-EST-2, R-EST-3, R-PRJ-7 | MIG-01a, MIG-01b, CAD-03, CAD-02a, CAD-02b | [MIG-01c](MIG-01c-carga-itens-locais.md) |
| 11 (C) | CAD-05a | Plano de contas gerencial e centros de custo | M | Ready | R-FIN-1, R-PLT-3, R-FIN-13, R-SEC-5 | CAD-05b, FND-09d | [CAD-05a](CAD-05a-plano-contas-centros-custo.md) |
| 12 (C) | CAD-04a | Cargos, jornadas, sindicatos e custo-hora | M | Ready | R-RH-2, R-RH-1, R-PRD-4, R-SEC-10, PRD §6 LGPD | CAD-06, CAD-03, CAD-01a | [CAD-04a](CAD-04a-cargos-jornadas-custo-hora.md) |
| 13 (C) | CAD-04b | Funcionários: cadastro base, salário, cargo, desligamento e LGPD | M | Ready | R-RH-1, R-RH-3, R-SEC-10, PRD §6 LGPD | CAD-04a, CAD-01a, CAD-01b | [CAD-04b](CAD-04b-funcionarios-base.md) |
| Fase 3 / Fase 6 | CAD-04c1 e CAD-04c2 | Quebra da CAD-04c (D9 de 21/10/2026): afastamentos e situação na Fase 3 (P); dependentes, banco/Pix, adicionais, VT e documentos na Fase 6 (M) | P + M | Ready | R-RH-3; R-RH-1, PRD §6 LGPD | CAD-04b | [CAD-04c1](CAD-04c1-afastamentos-situacao.md) · [CAD-04c2](CAD-04c2-funcionarios-complemento.md) |
| — | ~~MIG-01d~~ | ~~Saldos iniciais de estoque~~ | — | **Cancelada pelo PO** (18/10/2026) | — | — | arquivo apagado |
| — | ~~MIG-01e~~ | ~~Títulos em aberto e histórico de pagamentos~~ | — | **Cancelada pelo PO** (18/10/2026) | — | — | arquivo apagado |

Total da Fase 1: 13 stories (M: 11 · G: 2), todas `Ready`. Antes dela, a FND-09d (Fase 0, G, `Ready`). Depois, a CAD-04c, quebrada em 21/10/2026 em CAD-04c1 (Marco 3.1) e CAD-04c2 (Fase 6), ambas `Ready`.

## Ordem de execução (D1)
```
FND-09d (Fase 0)
Marco A — terceiros reais:  CAD-01a → MIG-01a → CAD-05b → MIG-01b → CAD-01b
Marco B — catálogo real:    CAD-06 → CAD-03 → CAD-02a → CAD-02b → MIG-01c
Marco C — base de custo:    CAD-05a → CAD-04a → CAD-04b
Fase 3:                     CAD-04c1 (Marco 3.1); CAD-04c2 na Fase 6 (D9 de 21/10/2026)
```

Por que esta ordem:
- **FND-09d** primeiro: os 10 cadastros usam os mesmos moldes e componentes; o PO aprova os moldes e valida a FND-09a/09b antes da primeira tela.
- **Marco A:** o PO vê os terceiros reais da Konebras na homologação depois de 4 stories. A CAD-01a traz o `BrazilianDocuments` (antes na CAD-06); a CAD-05b vem antes da MIG-01b porque a carga grava condições e contas; a CAD-01b (consulta) não é pré-requisito da carga.
- **Marco B:** a CAD-06 traz numeração e parâmetros; a CAD-03 cria o schema `inventory` e a ação `delete`; itens e conversões vêm antes da carga do catálogo.
- **Marco C:** a CAD-05a não bloqueia nenhuma story da fase; a CAD-04a precisa do % de encargos (CAD-06) e dos centros (CAD-03); a CAD-04b fecha a fase com a menor superfície de dado pessoal possível (D6).

## Quebras, cancelamentos e renomeações
- **CAD-01** → 01a (cadastro) e 01b (consulta de CNPJ e de CEP).
- **CAD-02** → 02a (itens, unidades, categorias, garantia padrão) e 02b (conversões e códigos do fornecedor).
- **CAD-04** → 04a (cargos, jornadas, sindicatos, custo-hora), **04b** (funcionário base, arquivo renomeado para `CAD-04b-funcionarios-base.md`, re-estimada de G para M) e **04c** (complemento, Fase 3), pela D6.
- **CAD-05** → 05a (plano de contas e centros de custo) e 05b (contas, caixas e condições).
- **MIG-01** → 01a (infraestrutura), **01b** (terceiros, contatos, condições e contas, arquivo renomeado para `MIG-01b-carga-terceiros-cadastros-financeiros.md`) e 01c (itens e locais). **MIG-01d e MIG-01e canceladas.**
- **Nova:** FND-09d (D7), na Fase 0.
- **Re-estimadas:** CAD-03 e CAD-06 (P → M); CAD-04b (G → M, pela quebra).

## Padrões comuns a todos os cadastros
Vindos da revisão pré-Fase 1 §2.4 e entregues pela FND-09d. Toda story de cadastro os referencia; o CA da story só repete o que é específico dela.
1. **Lista:** h1 com linha descritiva; "Novo {entidade}" dentro de `<Can>`; `DataTable` com busca e filtros na URL; a primeira coluna leva ao detalhe; vazio com ação para começar e vazio filtrado com "Limpar filtros".
2. **Onde editar:** cadastro simples (até cerca de 6 campos) em painel lateral; cadastro composto em rota de detalhe com abas; criação em página única em seções, só com o obrigatório, e as abas aparecem a partir da edição.
3. **Abas:** ligadas à URL e a `useHelpTab`, com uma ajuda por aba; "Histórico de alterações" é sempre a última; no funcionário, a linha do tempo de RH se chama "Linha do tempo".
4. **Campos:** "obrigatório" no rótulo, com `aria-required`; máscara na digitação e valor gravado normalizado; números no padrão brasileiro, com `tabular-nums`; aviso âmbar que não bloqueia; foco no primeiro erro, inclusive em aba escondida, e contador de erros por aba.
5. **Padrões preenchidos:** o campo que comanda vem primeiro (tipo do item, PF/PJ inferido pelos dígitos); o derivado só muda enquanto o usuário não mexeu nele; o primeiro registro vira o padrão.
6. **Nada digitado duas vezes:** a consulta de CNPJ preenche dados e endereço, e o usuário confirma com "Usar estes dados"; criação rápida dentro do combobox; "Duplicar"; documento já existente oferece "Adicionar papel" / "Abrir cadastro".
7. **Verbos:** específicos ("Salvar conta contábil", "Salvar conta bancária", "Salvar centro de trabalho"); o diálogo repete o verbo e o botão de sair diz "Voltar"; quando o servidor recusa, o diálogo oferece a ação alternativa ("Inativar", "Restaurar").
8. **Excluir × inativar (D10):** "Excluir" (lógico) quando o registro não tem uso e "Inativar" quando tem; inativo com chip neutro (o vermelho é só para o crítico), filtro "Inativos", some dos seletores e continua visível em quem já o usa; `Tag` para atributos, separada do chip de situação.
9. **Próximos passos:** faixa fixa no detalhe depois de criar, com até 3 ações; o toast só confirma o verbo; o que muda dado já existente (numeração, % de encargos que recalcula N cargos) pede confirmação mostrando o impacto.
10. **Estados:** carregando, vazio e erro; modo leitura sem `edit`; "Dados protegidos" sem `view_sensitive`.
11. **Acessibilidade:** grupos com `fieldset`/`legend`; árvore como `treegrid`; editor de linhas com "Remover {linha n}"; totais ao vivo em `aria-live`; `Dica` com alvo de 24 px e `aria-label="Dica: {campo}"`.
12. **400 px:** painel em tela cheia e editores empilhados; abas com rolagem horizontal; ações principais visíveis sem rolar; um CA de 400 px e de teclado por tela com editor.
13. **Importado:** `Tag` "Importado do Dolibarr" e a referência de origem no detalhe.
14. **Segurança (todas):** IDOR pai/filho conferido em toda rota `/{paiId}/filhos/{id}`; id de outro módulo validado pelo Contracts do dono; todo endpoint com `RequireModule` e `perm:` (teste de arquitetura); `Cache-Control: no-store` nas respostas com dado pessoal; seeds também nos tenants existentes no próximo deploy (`IgnoreQueryFilters()`); uma migration por story, regerada no rebase.

## Validação do PO (18/10/2026)
Primeira validação: as 15 stories originais passaram pela checklist, todas `Draft` com GO condicional. Depois das decisões do PO e da revisão pré-Fase 1, o @po (Pax) revalidou cada story alterada com a mesma checklist de 10 itens:
- **10/10 e `Ready`:** FND-09d, CAD-01a, MIG-01a, CAD-05b, MIG-01b, CAD-01b, CAD-06, CAD-03, CAD-02a, CAD-02b, MIG-01c, CAD-05a, CAD-04a e CAD-04b. Os NO-GOs da revisão (MIG-01a e CAD-04b) foram resolvidos pela ADR 0017, pela D6 e pelos CAs de segurança da §2.5.
- **`Draft` (GO condicional):** CAD-04c, que dependia de duas decisões de produto antes da Fase 3 (formato do vale-transporte e tipos de afastamento). Decididas em 21/10/2026 (revisão pré-Fase 3, pontos 1 e 2) e quebrada em CAD-04c1 e CAD-04c2, ambas `Ready`.
- **Pré-condições externas com padrão definido** (não bloqueiam o `Ready`):
  - MIG-01a: aceite do mapeamento pelo PO (CA 1 da story);
  - MIG-01b: lista de ids de instaladores e transportadoras (padrão: Fornecedor), COMPE das contas sem código (padrão: erro e cadastro manual), painel da plataforma fora do modo aberto antes da carga real (ADR 0018, CA 1);
  - MIG-01c: de-para de tipo produto a produto (padrão: Componente com aviso);
  - FND-09d: validação visual da FND-09a/09b e aprovação dos moldes (CA 1 e 2).

## Rastreabilidade da revisão pré-Fase 1
| Item da revisão | Onde entra |
|---|---|
| D1 (marcos) | Ordem acima; campo "Ordem de execução" e dependências de cada story |
| D2, D5 | Sem efeito (decisão do PO sobre o escopo da migração) |
| D3, D4 | ADR 0017; MIG-01a |
| D6 | CAD-04b (base) e CAD-04c (Fase 3) |
| D7 | FND-09d |
| D8 | ADR 0018; pré-condição da MIG-01b (CA 1) |
| D9 | CAD-01a (CA 17), CAD-04b (CA 8 e 11) |
| D10 | ADR 0016; CAD-03 (CA 1) e todos os cadastros |
| D11 (tabela 1.1) | CAs e "Pontos em aberto" de cada story |
| §2.1 | ADRs 0016 e 0017 |
| §2.2 | CAs com valores; `BrazilianDocuments` na CAD-01a; CA da condição padrão na CAD-05b; seeds em tenants existentes; contagens pelo total lido; NCM só para item ativo; R-TER-3 para a COM-05/"Terceiro 360"; Ctrl K na CAD-01a e na CAD-02a |
| §2.3 | ADR 0016; garantia na CAD-02a; perfil em `settings`; `IWorkCenterCapacity` por id; `AuditableEntityRegistry` e testes de posse de tabela |
| §2.4 | Padrões comuns acima; FND-09d; correções da CAD-01a, CAD-04b e CAD-06 |
| §2.5 | CAs **[Segurança]** nas stories; CAD-04c para afastamentos, banco/Pix e dependentes |
| §3 | MIG-01d/01e canceladas; CAD-04c na Fase 3; "Primeiros passos" segue como sugestão sem story |

## Pontos em aberto da Fase 1
Nenhum ponto bloqueante nas stories `Ready`. Ficam com o PO:
1. **CAD-04c** (antes da Fase 3): formato do vale-transporte e lista de tipos de afastamento. Decidido em 21/10/2026 (CAD-04c1 e CAD-04c2).
2. **Insumos da migração** (com padrão definido): lista de instaladores e transportadoras, de-para de tipo de produto, COMPE de contas sem código, aceite do mapeamento.
3. **Primeiros passos** (sugestão da revisão §3, sem story): lista no Início com o progresso da implantação.

---


# Fase 2 — Comercial, Projetos e Financeiro núcleo

Stories detalhadas da Fase 2, uma por arquivo, escritas pelo @sm (River) e validadas pelo @po (Pax) em 19/10/2026, e revistas em 20/10/2026 depois das decisões do PO sobre a revisão `docs/revisoes/2026-10-20-revisao-pre-fase2.md`. Fonte: `docs/01` a `06`, ADRs 0001–0022, o código das Fases 0 e 1 (contratos `IParties`, `IItems`, `IHourlyCost`, `IWorkCenters`, `IPaymentTerms`, `IChartOfAccounts`, `ITenantParameters`, anexos, e-mail, outbox, notificações, `RecurringJobsRegistrar`, `ModuleCatalog`, `GroupSeeder` e `SeedPatchSeeder`) e o protótipo. A Definition of Ready, a legenda e a escala de tamanho são as mesmas das fases anteriores.

## Decisões do PO (20/10/2026, John)
- **Aprovadas como recomendado na revisão:** D2, D3, D4, D5, D6, D7, D8, D9, D11, D12, D13, D14, D15, D16, D17 e a seção 1.1 inteira (os 29 pontos em aberto, com os ajustes da revisão).
- **D7 (custo e margem):** veem Proprietário, Comercial e Financeiro, pelas ações `quotes.view_cost` e `projects.view_cost` (ADR 0003, ADR 0019 §20); DTO sem custo para os demais; o PDF nunca mostra custo.
- **D1, alternativa (b), "promover a homologação":** os dados reais entram na homologação (`homolog.konebras.com.br`) a partir da FIN-00, e na GOL-01 (Fase 5) o banco do tenant da homologação vira o de produção; `app.konebras.com.br` continua no Dolibarr até a GOL-01 (ADR 0020).
- **Segurança aplicada como CA:** §2.2-C1 (pago = aprovado), A2 (fracionamento), A3 (regra por conta com teto), A4/D9 (forma de pagamento congelada na OP e avisos), A5/D8 (limites, relatório e notificação), M2 (e-mail do orçamento), M4, M8 e os baixos; anexos que suportam o que as stories pedem (§2.1-M1); `Idempotency-Key` em todos os comandos sem documento-pai (§2.1-A2).
- **Adiadas para depois do término do planejamento atual (não aplicadas):** §2.2-A1, 2FA a cada ação (continua só no login, FND-06c), e D10, estorno de pagamento com `finance.approve` (continua com `finance.pay`). Registradas como **P-19** e **P-20** em `06-pendencias-riscos.md`.
- **Arquitetura e design:** §2.1 e §2.3 aplicadas como correção, exceto o que conflitava com os adiamentos (o filtro `RequireTwoFactor` saiu da FND-09e; o estado de 2FA no diálogo ficou só para a regra que já existe).
- **ADRs novas:** **0019** (decisões técnicas da Fase 2, com D7), **0020** (homologação promovida a produção, D1), **0021** (mensagens e próximos passos, D11, resolve a P-18), **0022** (geração de PDF, D17). **Adendos:** ADR 0008 (D5) e ADR 0016 (M6 dono dos parâmetros, M7 número com revisão).

## Índice
| Ordem | ID | Título | Tam. | Status | Requisitos | Depende de | Arquivo |
|---|---|---|---|---|---|---|---|
| 0 (Fase 0) | FND-09e | Componentes e convenções compartilhados da Fase 2 | M | Ready | PRD §6, R-PRP-2, R-PRP-7, R-COM-2, R-SEC-5 | FND-09c, FND-09d, FND-07; ADR 0019, 0021 | [FND-09e](FND-09e-componentes-convencoes-fase2.md) |
| 1 (2.1a) | FIN-01 | Contas a receber: títulos, parcelas, baixa, quitação com desconto, estorno, limites | G | Ready | R-FIN-3, R-FIN-1, R-FIN-2, R-COM-4, R-COM-2 | FND-09e, CAD-05a, CAD-05b, CAD-01a, CAD-06 | [FIN-01](FIN-01-contas-a-receber.md) |
| 2 (2.1a) | FIN-02 | Contas a pagar, forma de pagamento e despesas recorrentes | M | Ready | R-FIN-4, R-FIN-6, R-FIN-5, R-FIN-1, R-COM-4 | FIN-01 | [FIN-02](FIN-02-contas-a-pagar-recorrentes.md) |
| 3 (2.1a) | FIN-00 | Implantação financeira (data de corte, saldos, títulos em aberto) | M | Ready | R-FIN-2, R-FIN-3, R-FIN-4, R-FIN-1 | FIN-01, FIN-02, CAD-05b; ADR 0020 | [FIN-00](FIN-00-implantacao-financeira.md) |
| 4 (2.1a) | FIN-05 | Fluxo de caixa base: realizado + projetado | M | Ready | R-FIN-10, R-FIN-2, R-PRP-5 | FIN-01, FIN-02, FIN-00 | [FIN-05](FIN-05-fluxo-de-caixa-base.md) |
| 5 (2.1a) | FIN-06a | Inadimplência por cliente (aging) e aviso diário | P | Ready | R-FIN-12 (parte), R-COM-2, R-PRP-5 | FIN-01, FIN-00 | [FIN-06a](FIN-06a-aging-aviso-diario.md) |
| 6 (2.1b) | FIN-03a | Ordem de pagamento: criar, aprovar/reprovar, pagar | G | Ready | R-FIN-5, R-COM-2, R-COM-3, R-COM-4, R-SEC-2 | FIN-02, FIN-01, FIN-05, FND-06c | [FIN-03a](FIN-03a-ordem-de-pagamento.md) |
| 7 (2.1b) | FIN-04 | Movimentação bancária, transferências e lançamento sem OP | M | Ready | R-FIN-2, R-FIN-1, R-FIN-10 | FIN-01, FIN-03a, FIN-05 | [FIN-04](FIN-04-movimentacao-bancaria.md) |
| 8 (2.2) | COM-01a | Orçamento: itens, composição de custo, preço e margem | G | Ready | R-COML-1, R-COM-4, R-PRP-1, R-PRP-3 | FND-09e, CAD-01a, CAD-02a, CAD-03, CAD-04a, CAD-06 | [COM-01a](COM-01a-orcamento-itens-custo-preco.md) |
| 9 (2.2) | COM-01b | Orçamento: marcos, validade, perda e cópia | M | Ready | R-COML-6, R-COML-4, R-COML-2, R-COML-9 | COM-01a, CAD-05b | [COM-01b](COM-01b-orcamento-marcos-validade-copia.md) |
| 10 (2.2) | COM-02 | Revisões, expiração, prorrogação e comparação | M | Ready | R-COML-3, R-COML-4, R-COM-2 | COM-01a, COM-01b | [COM-02](COM-02-revisoes-validade.md) |
| 11 (2.2) | COM-03 | PDF e envio do orçamento | M | Ready (pré-condições externas) | R-COML-5, R-COM-1, R-COM-3 | COM-02, CAD-06, FND-08, FIN-03a; ADR 0022 | [COM-03](COM-03-pdf-envio-orcamento.md) |
| 12 (2.3) | COM-04a | Aprovação: contrato, projeto, itens e orçamento do projeto | G | Ready | R-COML-7, R-PRJ-1, R-PRJ-2, R-FIN-1, R-COM-3, R-COM-4 | COM-02, COM-01b, FIN-02 | [COM-04a](COM-04a-aprovacao-contrato-projeto.md) |
| 13 (2.3) | COM-04b | Recebíveis do contrato, liberação por marco, entrega manual, cancelamento | M | Ready | R-COML-7, R-PRJ-6, R-FIN-3, R-COM-2 | COM-04a, FIN-01, FND-09e | [COM-04b](COM-04b-recebiveis-marcos-contrato.md) |
| 14 (2.3) | COM-04c | Contrato de implantação (contrato já em andamento) | P | Ready | R-COML-7, R-PRJ-1, R-PRJ-6, R-FIN-3 | COM-04a, COM-04b, FIN-00 | [COM-04c](COM-04c-contrato-de-implantacao.md) |
| 15 (2.3) | COM-05 | Projeto 360 | M | Ready | R-PRJ-1, R-PRJ-4, R-PRJ-2, R-PRJ-5 (parte) | COM-04a, COM-04b, FIN-02 | [COM-05](COM-05-projeto-360.md) |
| 16 (2.3) | COM-06 | OS de instalação | G | Ready | R-PRJ-3, R-FIN-4, R-PRJ-6, R-COM-4 | COM-04a, COM-04b, COM-05, FIN-02, FIN-03a | [COM-06](COM-06-os-instalacao.md) |
| 17 (2.4) | FIN-06b | Inadimplência por projeto, pontualidade e histórico do terceiro | M | Ready | R-FIN-12 (parte), R-TER-4, R-TER-3 | FIN-06a, COM-04b, COM-01a, COM-04a | [FIN-06b](FIN-06b-inadimplencia-projeto-pontualidade.md) |
| 18 (2.4) | COM-07 | Aditivo de contrato (D) | M | Ready | R-COML-8, R-PRJ-5 | COM-04b, COM-05, COM-01b | [COM-07](COM-07-aditivo-contrato.md) |
| 19 (2.4) | FIN-03b | Alçadas configuráveis, multinível e link no e-mail | M | Ready | R-FIN-5, R-COM-1, R-COM-2 | FIN-03a, FND-08 | [FIN-03b](FIN-03b-alcadas-multinivel.md) |
| 20 (2.4) | COM-08 | Funil comercial (D) | P | Ready | R-COML-9 | COM-01b, COM-02, COM-04a | [COM-08](COM-08-funil-comercial.md) |

Total da Fase 2: 20 stories (P: 3 · M: 12 · G: 5), todas `Ready`; três com pré-condição externa explícita (FIN-00, COM-03, COM-06). Antes delas, a FND-09e (Fase 0, M, `Ready`).

## Ordem de execução (D13, seção 4 da revisão)
```
Pré:  ADR 0019 + ADR 0021 (P-18) + adendos às ADRs 0008 e 0016 + ADR 0020 (D1) + FND-09e
2.1a "Ver o caixa":     FIN-01 → FIN-02 → FIN-00 (implantação) → FIN-05 → FIN-06a (aging + aviso)
2.1b "Pagar com OP":    FIN-03a → FIN-04 (+ lançamento sem OP)
2.2  Orçamento:         COM-01a → COM-01b → COM-02 → COM-03
2.3  Contrato ao caixa: COM-04a (+ cost_type) → COM-04b (+ entrega manual) → COM-04c (implantação de contrato) → COM-05 → COM-06
2.4  Controles e D:     FIN-06b → COM-07 (D) → FIN-03b → COM-08 (D)
```

Por que esta ordem:
- **Financeiro primeiro:** não depende da P-01, usa os cadastros reais da Fase 1 e entrega o resultado da fase no PRD §7 ("OP aprovada e caixa projetado"). **Os dados reais entram na homologação a partir da FIN-00** (ADR 0020): saldos das contas na data de corte e títulos em aberto; os contratos em andamento entram com a COM-04c.
- **FND-09e antes da FIN-01:** mensagens (P-18), diálogo financeiro, editor de linhas, tabela com totais, gráficos e as convenções de concorrência e idempotência, para nenhuma tela improvisar a sua versão.
- **FIN-00 entre a FIN-02 e a FIN-05:** a implantação precisa dos dois tipos de título e o fluxo de caixa só faz sentido com o saldo real.
- **FIN-05 antes da FIN-03a e da FIN-04:** o fluxo de caixa passa a ser dono da consulta de saldo atual; pagamentos e transferências entram no realizado sem código novo, porque são transações.
- **FIN-06a no 2.1a e FIN-06b no 2.4:** o aging por cliente e o aviso diário só dependem dos títulos; a visão por projeto, a pontualidade e a aba do terceiro dependem do Comercial.
- **COM-04c logo depois da COM-04b:** o modo de implantação mexe no consumidor do `ContractSigned`, que nasce na COM-04b.
- **FIN-03b no fim (D5):** o padrão "Proprietário aprova tudo" já vale desde a FIN-03a, e a delegação a outro grupo fica para quando houver mais de um aprovador.
- **COM-07 e COM-08** são **D** no PRD.

## Quebras, stories novas e re-estimativas
- **COM-01** → 01a e 01b; **COM-04** → 04a, 04b e **04c** (nova); **FIN-03** → 03a e 03b; **FIN-06** → **06a** (aging por cliente e aviso diário, P) e **06b** (por projeto, pontualidade e aba do terceiro, M). O arquivo `FIN-06-inadimplencia-aging.md` foi substituído pelos dois.
- **Novas:** **FND-09e** (componentes e convenções da revisão §3, M, Fase 0), **FIN-00** (implantação financeira, D2, M) e **COM-04c** (contrato de implantação, D3, P).
- **Decisão do SM sobre o "contrato de implantação" (D3):** virou a **COM-04c (P)**, e não um acréscimo à COM-04a, porque a COM-04a já é G (o comando de aprovação, o snapshot, o projeto e o campo de projeto no título a pagar) e o modo de implantação mexe também no consumidor do Finance, que só existe na COM-04b. A data de assinatura editável em toda aprovação ficou na COM-04a.
- **Re-estimadas:** FIN-01 (M → G), COM-06 (M → G), FIN-04 (P → M: recebeu o lançamento bancário sem OP, D4).

## Padrões comuns a todos os documentos (Fase 2)
Valem para orçamento, contrato, projeto, OS, títulos e OP, além dos "Padrões comuns a todos os cadastros" da Fase 1. Cada story só repete o que é específico dela.
1. **Numeração:** `INumberSequence` na mesma transação do documento (CR e CP com 5 dígitos, OP com 4, OS com 5, ORC, CTR e PRJ com 4), com índice único; o número nunca é reaproveitado. As revisões do orçamento mantêm o número (ADR 0016 adendo M7).
2. **Nunca apagar o que tem efeito:** título, baixa, transação, OP, contrato e OS são cancelados (motivo obrigatório) ou estornados (lançamento inverso). Só o rascunho sem efeito (orçamento rev. 0, despesa recorrente sem título) tem exclusão lógica.
3. **Tipos e arredondamento:** dinheiro `numeric(18,2)` com `AwayFromZero`; quantidade e horas `numeric(18,4)`; custo unitário `numeric(18,6)`; percentuais `numeric(7,4)` (margem `numeric(9,4)`); arredonda só no total da linha; parcelas e marcos pelo `PaymentTermSplitter`; orçamento do projeto pelo maior resto; nunca `double` (ADR 0019 §9).
4. **Datas:** de negócio em `date`; instantes em `timestamptz`; "hoje" é `BusinessTime.Today` (America/Sao_Paulo); "Vencido" é calculado, nunca gravado.
5. **Concorrência e transições:** Guid v7; `xmin` em todo documento, com `version` no DTO e 409 `concurrency_conflict` "… mudou desde que você abriu. Recarregue." com "Recarregar"; transição de estado pela entidade rastreada, nunca por `UPDATE` cru (ADR 0019 §2).
6. **Idempotência:** todo comando sem documento-pai (título manual, parcelamento, implantação, OP, transferência, lançamento sem OP, envio do orçamento, aditivo) exige `Idempotency-Key` e devolve o resultado original no reenvio (ADR 0019 §3).
7. **Permissões:** as 3 camadas em todo endpoint (`RequireModule` + `perm:`); `*.approve` e `finance.pay` são reservadas ao Proprietário para conceder (ADR 0008; o Financeiro recebe `finance.pay` por seed patch, adendo D5); quem tem essas ações tem 2FA obrigatório **no login** (FND-06c CA 12; 2FA a cada ação adiado, P-19); custo e margem com `quotes.view_cost`/`projects.view_cost`, DTO sem custo para os demais; "Aguardando você" só mostra o que o usuário pode decidir.
8. **Fronteiras:** referência entre módulos só por id; leitura pelos `*.Contracts` do dono (`IProjects`, `IContracts`, `IReceivables`, `IPayables`, `IInstallationOrders`, `ICustomerPaymentStats`, `IFinanceParameters`, `ICommercialParameters`); `*.Contracts` não referencia outro `*.Contracts` (vocabulário comum no SharedKernel: `CostType`, `MilestoneTrigger`, `TriggerEvent`, `DocumentOrigin`); reação entre módulos pelo outbox, com consumidor idempotente pela inbox e registro de gatilhos para os eventos fora de ordem; payload de evento sem dado pessoal e sem texto livre.
9. **IDOR:** toda rota `/{paiId}/filhos/{id}` confere o vínculo; ids de outro módulo são validados pelo Contracts do dono.
10. **Validação:** 422 com mensagens PT-BR no campo, inclusive em linhas de editor (`lines[n].campo`); valores vindos do cliente são revalidados no servidor e no consumidor (§2.2-M8).
11. **Provedores de uso:** quem guarda o id de terceiro, conta contábil, centro de custo, conta bancária, item, centro de trabalho ou condição implementa o `I*UsageProvider` correspondente, para o cadastro só inativar.
12. **Parâmetros:** cada módulo é dono dos seus (`finance.parameters`, `commercial.parameters`); Configurações → Parâmetros só monta a tela, com `settings.edit` (ADR 0016 adendo M6).
13. **Jobs e notificações:** recorrentes no `RecurringJobsRegistrar` com o fuso de São Paulo; todo job idempotente, com teste de execução dupla e de dois tenants; argumentos só com ids; notificação com `dedup_key` quando pode repetir; notificação dentro do próprio módulo é síncrona (ADR 0019 §12, §18).
14. **Anexos e e-mail:** regras por dono (ação de leitura e de escrita, `OwnerExists` obrigatório, tipos e tamanho da ADR 0019 §7); arquivo gerado pelo sistema por `StoreGeneratedAsync`; e-mail com anexo só depois do `Clean`.
15. **Migrations:** uma por story, não destrutiva, regerada no rebase, testada sobre cópia do banco do tenant `konebras` antes do deploy (ADR 0020); o `docs/03-modelo-dados.md` é atualizado na story que cria a tabela ou coluna.

## Padrões comuns da Fase 2 (telas, revisão §2.3)
Entregues pela FND-09e (componentes) e aplicados em cada story.
1. **Ação com efeito financeiro:** diálogo grande (`size="lg"`, cerca de 640 px, tela cheia a 400 px) com o impacto (valores, conta, títulos afetados, saldo antes e depois); botão com o mesmo verbo; próximo passo no `NextSteps`; `recovery` quando o servidor recusa; estilo `destructive` só para estornar e cancelar.
2. **Pré-condições antes de confirmar:** o diálogo lista o que falta, com link para corrigir e o botão desabilitado com o motivo; o 422 é só defesa.
3. **Erros:** 422 sempre no campo, inclusive em linhas de editor e dentro de diálogos (`applyServerErrors` com caminhos aninhados); contador por aba; 409 de concorrência com "Recarregar".
4. **Valores:** todo % com o rótulo do que é ("Markup 40,00% · Margem 28,57%"); valor digitado à mão com `Tag` e "Voltar ao sugerido"; margem negativa no chip crítico "Preço abaixo do custo"; totais ao vivo num rodapé fixo, anunciados com espera de cerca de 500 ms.
5. **Somas que precisam fechar:** mostrar o que falta ("Faltam 10,00%"), com "Dividir igualmente" e "Ajustar o último".
6. **Situação:** o mesmo mapa de chips em todas as listas (Previsto e Cancelado neutros, Em aberto info, Vence hoje alerta, Vencido crítico, Pago e Aprovado ok); faixa lateral vermelha para atrasado e âmbar para risco.
7. **Etapa:** `StageIndicator` com letra explícita por etapa e rótulo visível; a lista de etapas do projeto está na COM-04a (P Planejado, E Em produção, F Fabricado, I Instalando, T Entregue, C Encerrado).
8. **Detalhe de documento:** cabeçalho com número, revisão, chip e contraparte; resumo em "facts" (4 → 2 colunas a 400 px); abas na URL; faixa de modo leitura que explica por quê e oferece a saída ("Nova revisão").
9. **Estados:** skeleton por aba; vazio com verbo e ação; processamento assíncrono com tempo limite (cerca de 30 s, depois o estado de demora); sem permissão, a aba some.
10. **Gráficos:** Recharts pelo `Chart`, com tokens `--chart-*` claros e escuros; realizado e projetado distinguidos pelo traço (contínuo × tracejado); tabela equivalente acessível por teclado.
11. **400 px:** cada story declara as colunas secundárias (número, contraparte, vencimento, valor e situação ficam sempre); editores de linha viram cartões; checklist de obra com alvos de 56 px; E2E de 400 px e teclado em toda tela com editor ou diálogo financeiro.
12. **2FA no momento da decisão:** só para a regra que já existe (FND-06c CA 12): estado único dentro do diálogo ("Para aprovar, ative a verificação em duas etapas." com "Ativar agora"), sem perder o preenchido. O pedido do código a cada ação está adiado (P-19).
13. **Mensagens (ADR 0021):** sucesso numa região `role="status"` no topo do conteúdo, sem bloquear, por no mínimo 5 s; erro sempre dentro do diálogo ou formulário, com foco; próximo passo sempre no `NextSteps`, nunca na mensagem.
14. **Navegação (D12):** o Financeiro tem dois grupos, "Caixa e pagamentos" (A receber, A pagar, Ordens de pagamento, Contas e extrato, Fluxo de caixa, Inadimplência) e "Configuração financeira" (Plano de contas, Centros de custo, Condições, Recorrentes, Alçadas, Implantação financeira); Contratos e Projetos entram na área Comercial e Instalações na área Fábrica, cada um na sua story; toda story declara a sua entrada no `modules.ts`.

## Validação do PO (20/10/2026)
Primeira validação (19/10/2026): as 17 stories originais com 10/10 e GO condicional, todas `Draft`. Depois das decisões do PO e da revisão pré-Fase 2, o @po (Pax) revalidou cada story alterada e as novas com a mesma checklist de 10 itens:
- **10/10 e `Ready`:** FND-09e, FIN-01, FIN-02, FIN-00, FIN-05, FIN-06a, FIN-03a, FIN-04, COM-01a, COM-01b, COM-02, COM-03, COM-04a, COM-04b, COM-04c, COM-05, COM-06, FIN-06b, COM-07, FIN-03b e COM-08. Os NO-GOs da revisão (COM-03 e COM-04b) foram resolvidos pelas ADRs 0022 e 0019 §1.
- **Pré-condições externas explícitas** (não são decisão de story; bloqueiam o passo indicado, não o desenvolvimento):
  - FIN-00: backup diário no ar; o teste de restauração é recomendado e não bloqueia (PO, 20/10/2026, adendo da ADR 0020);
  - COM-03: SPF, DKIM e DMARC no domínio de envio, antes do deploy em homologação (P-21 decidida: licença Community);
  - COM-06: nenhuma (P-22 decidida: checklist configurável com modelo padrão sugerido).
- **Correções da revalidação cruzada:**
  - FIN-05 deixou de depender da FIN-03a e da FIN-04 (nova ordem) e passou a ser dona da consulta de saldo atual;
  - FIN-03b: o exemplo deixou de dar `finance.approve` ao Financeiro do seed (D5);
  - COM-04a ganhou o campo de projeto e a natureza do custo no título a pagar (item 26), que saíram da COM-05, e passou a depender da FIN-02;
  - COM-06: o seed patch só acrescenta `production.create` à Engenharia; a Supervisão de produção já tem `production.approve` e, portanto, também registra o aceite técnico (o aceite não move dinheiro: o CP segue para OP aprovada);
  - COM-03 passou a depender da FIN-03a (regras de anexo por dono).

## Pré-condições da fase
- ADRs 0019, 0020, 0021 e 0022 aceitas; adendos às ADRs 0008 e 0016 registrados (20/10/2026).
- **P-01** (regime tributário): aberta; não bloqueia (a COM-01a usa imposto digitado, decisão do PO na seção 1.1, item 16).
- Pré-condições externas: COM-03 (SPF/DKIM/DMARC). P-21 e P-22 decididas pelo PO em 20/10/2026.

## Rastreabilidade da revisão pré-Fase 2
| Item da revisão | Onde entra |
|---|---|
| D1 (b) | ADR 0020; FIN-00 (CA 12, 13); GOL-01 no backlog |
| D2 | FIN-00 |
| D3 | COM-04a (CA 2, data de assinatura) e COM-04c |
| D4 | FIN-04 (CA 11–13) |
| D5 | ADR 0008 adendo; FIN-03a (CA 1); FIN-03b no fim da fase |
| D6 | FIN-03a (CA 9, 10); COM-07 (CA 9) |
| D7 | ADR 0019 §20; COM-01a (CA 1–3); COM-02, COM-04a, COM-05 (CA 10), COM-07 |
| D8 | FIN-01 (CA 13, 14, 17, 18, 22); FIN-02 (CA 12); FIN-03a (CA 18); FIN-04 (CA 7, 14) |
| D9 | FIN-02 (CA 6–8); FIN-03a (CA 4, 6, 15) |
| D10 | Adiada (P-20); FIN-03a CA 21 com `finance.pay` |
| D11 | ADR 0021; FND-09e (CA 1–3) |
| D12 | FND-09e (CA 12); entrada no menu em cada story |
| D13 | Ordem acima |
| D14 | FIN-01 (CA 19); COM-04b |
| D15 | COM-04b (CA 10) |
| D16 | COM-02 (CA 6, 7); COM-08 (CA 6) |
| D17 | ADR 0022; COM-03 (CA 1, 5, 6) |
| Seção 1.1 (29 pontos) | "Pontos em aberto" de cada story, como decididos |
| §2.1 (C1 a M14, baixos) | ADR 0019; FND-09e; FIN-01, FIN-02, FIN-03a, FIN-03b, COM-01a, COM-01b, COM-03, COM-04a, COM-04b, COM-06, COM-07 |
| §2.2 (C1, A2 a A5, M2, M4, M8, baixos) | CAs **[Segurança]** em FIN-01, FIN-02, FIN-03a, FIN-03b, FIN-04, COM-01b, COM-03, COM-04a, COM-06, COM-07, FIN-05, FIN-06a, FIN-06b, COM-08 |
| §2.2-A1 | Adiada (P-19) |
| §2.3 | Padrões comuns da Fase 2 acima; ajustes de tela em cada story |
| §3 | FND-09e (sem o filtro `RequireTwoFactor`) |
| §4 | Ordem acima |

## Pontos em aberto da Fase 2
Nenhum ponto bloqueante nas stories. Os 29 pontos do README anterior foram decididos pelo PO em 20/10/2026 (seção 1.1 da revisão) e estão registrados em cada story como "decididos". Ficam com o PO:
1. **P-19** (2FA a cada ação) e **P-20** (estorno de pagamento com `finance.approve`): revisar depois do término do planejamento atual.
2. SPF, DKIM e DMARC antes do deploy da COM-03 (P-21 e P-22 decididas em 20/10/2026).
3. Backup diário do tenant `konebras` no ar antes da primeira carga real da FIN-00; teste de restauração recomendado, sem bloquear.
4. **P-01** (regime tributário) continua aberta, sem bloquear a fase.

---

# Fase 3 — Engenharia, Estoque, Compras e Produção

Stories detalhadas da Fase 3, uma por arquivo, escritas pelo @sm (River) e validadas pelo @po (Pax) em 21/10/2026, e revistas no mesmo dia depois das decisões do PO sobre a revisão `docs/revisoes/2026-10-21-revisao-pre-fase3.md`. Fonte: `docs/01` a `06`, ADRs 0001–0026, o código das Fases 0 a 2 (`ModuleCatalog`, `GroupSeeder`, `SeedPatchSeeder`, `ProductionOrder`/`InstallationOrderService` da COM-06, `IItems`, `ILocations`, `IUnitConversion`, `IItemSupplierCodes`, `IWorkCenters`, `IProjects`, `ProjectItemCreated`, `IPayables`, motor de alçadas da FIN-03b, `IHourlyCost`, `IEmployees`, `IPdfRenderer`, anexos, e-mail, outbox, `PingHub`) e o protótipo (Kanban, App do operador, Estoque). A Definition of Ready, a legenda e a escala de tamanho são as mesmas das fases anteriores.

## Decisões do PO (21/10/2026, John)
- **Aprovadas todas as recomendações da revisão:** D1 a D17, a tabela 1.1 inteira (pontos 1 a 95) e as seções 2.1 a 2.3, 3 e 4, com os valores propostos: R$ 1.000,00 e 20% na D7 (entrada e saída manual), R$ 5.000,00 na D8 (anexo da proposta na cotação).
- **D17 (c)** (idempotência com o usuário no escopo) **não entra nas stories**: vai num PR de código separado (ver "Pendências herdadas").
- **Sem rede da PRD-03b:** adiado para depois do piloto, se o Wi-Fi falhar (ADR 0026 §6, P-23). **Operador sem e-mail:** um e-mail da empresa por operador, apelido em `konebras.com.br` (P-24).
- **CAD-04c2:** Fase 6, antes da RH-02 (D9).
- **ADRs novas:** **0023** (motor de estoque), **0024** (compras e financeiro), **0025** (custo real do projeto), **0026** (chão de fábrica: PIN e aparelho). **Adendos de 21/10/2026:** ADR 0003 (`engineering.delete`, `purchasing.receive`, `production.approve_quality`), ADR 0008 (seed com `engineering.approve`, `amr=pin`), ADR 0009 (rota anônima `POST /auth/pin`) e ADR 0019 §7 (donos de anexo `Rfq`, `PurchaseOrder`, `NonConformity`, `Shipment`, `EmployeeDocument`; dwg/dxf). Fluxos (`04-fluxos` §1, §2 e §4), modelo (`03`) e pendências (`06`: P-04, P-12, P-23, P-24) atualizados.

## Índice
| Ordem | ID | Título | Tam. | Status | Requisitos | Depende de | Arquivo |
|---|---|---|---|---|---|---|---|
| 1 (3.1) | ENG-01 | Templates de roteiro | M | Done | R-ENG-1, R-ENG-2, R-PRP-1, R-PLN-1 | CAD-03, CAD-02a | [ENG-01](ENG-01-templates-roteiro.md) |
| 2 (3.1) | PRD-01 | Geração da OS de fabricação | G | Ready | R-PRD-1, R-PRJ-2, R-ENG-1, R-PRP-1, R-COM-4 | ENG-01, COM-04a, COM-06 | [PRD-01](PRD-01-geracao-os-fabricacao.md) |
| 3 (3.1) | PRD-02 | Kanban da produção | M | Done | R-PRD-2, R-COM-2, PRD §5.14, PRD §6 | PRD-01, FND-02 | [PRD-02](PRD-02-kanban.md) |
| 4 (3.1) | CAD-04c1 | Funcionários: afastamentos e situação | P | Ready | R-RH-3, PRD §6 LGPD, R-SEC-5, R-SEC-10 | CAD-04b | [CAD-04c1](CAD-04c1-afastamentos-situacao.md) |
| 4b (3.1) | CAD-04c1b | Funcionários: editar e encerrar antes um afastamento; `hr.view_health` (PO 22/10/2026) | P | Done | R-RH-3, PRD §6 LGPD, R-SEC-5, R-SEC-10, R-PRP-1 | CAD-04c1 | [CAD-04c1b](CAD-04c1b-editar-encerrar-afastamento.md) |
| 5 (3.1) | PRD-03a | App do operador: fila, apontamento e checklist | G | Done | R-PRD-3, R-PRD-6, PRD §6 Usabilidade | PRD-01, PRD-02, CAD-04c1 | [PRD-03a](PRD-03a-pwa-fila-apontamento.md) |
| 6 (3.1) | PRD-04 | Custo de mão de obra e custo real do projeto | M (re-estimada de P) | Ready | R-PRD-4, R-RH-2, R-PRJ-5 | PRD-03a, CAD-04a, COM-05; ADR 0025 | [PRD-04](PRD-04-custo-mao-de-obra.md) |
| 7 (3.1) | PRD-05 | Qualidade e não conformidade | M | Ready | R-PRD-6, R-COM-2, R-COM-3, R-COM-4 | PRD-03a | [PRD-05](PRD-05-qualidade-nao-conformidade.md) |
| 8 (3.1) | PRD-06a1 | Conclusão da OS e fabricação concluída do projeto | P | Ready | R-PRD-7, R-PRJ-6, R-COM-4 | PRD-05, PRD-04, COM-04b | [PRD-06a1](PRD-06a1-conclusao-os.md) |
| 9 (3.1) | PRD-03b | App do operador: aparelho registrado e PIN | M | Done | R-PRD-3, R-SEC-1, R-SEC-3, R-SEC-7 | PRD-03a, PRD-05; ADR 0026 | [PRD-03b](PRD-03b-pwa-pin-aparelho.md) |
| 10 (3.2) | EST-01a | Saldos, movimentos e custo médio | G | Ready (pré-condição externa) | R-EST-4, R-EST-5, R-EST-6, R-EST-7, R-EST-8 | CAD-02a, CAD-02b, CAD-03, CAD-06, FND-09e; ADR 0023 | [EST-01a](EST-01a-saldos-movimentos-custo-medio.md) |
| 11 (3.2) | ENG-02 | BOM da OS | M | Ready | R-ENG-3, R-ENG-4, R-ENG-5, R-PRP-2 | PRD-01, PRD-03a, EST-01a | [ENG-02](ENG-02-bom-da-os.md) |
| 12 (3.2) | EST-02 | Reservas e disponível | M | Done | R-EST-4, R-ENG-4, R-ENG-5, R-CMP-1 | ENG-02, EST-01a, PRD-01 | [EST-02](EST-02-reservas-disponivel.md) |
| 13 (3.2) | EST-03 | Requisição de material para a OS e devolução | M | Done | R-PRD-5, R-EST-4, R-EST-6, R-PRJ-5 | EST-02, PRD-03a, PRD-04 | [EST-03](EST-03-requisicao-material-os.md) |
| 14 (3.3) | CMP-01 | Solicitação de compra | M | Done | R-CMP-1, R-CMP-6, R-COM-2, R-COM-4 | EST-02 | [CMP-01](CMP-01-solicitacao-compra.md) |
| 15 (3.3) | CMP-03a | Pedido de compra: criação, aprovação por alçada e CP previsto | G | Done | R-CMP-3, R-CMP-4, R-CMP-6, R-FIN-4, R-FIN-5, R-FIN-10 | CMP-01, FIN-03b, FIN-02, FIN-05; ADR 0024 | [CMP-03a](CMP-03a-pedido-compra-alcada-cp.md) |
| 16 (3.3) | CMP-04 | Recebimento manual contra o pedido | G | Done | R-CMP-5, R-EST-6, R-EST-7, R-EST-8, R-FIN-4 | CMP-03a, EST-01a, EST-02 | [CMP-04](CMP-04-recebimento-manual.md) |
| 17 (3.3) | CMP-02 | Cotação | M | Done | R-CMP-2, R-CMP-6, R-COM-4, R-PRP-1 | CMP-01, CMP-03a | [CMP-02](CMP-02-cotacao.md) |
| 19 (3.4) | PRD-06a2 | Produto acabado: entrada no estoque, séries e lotes consumidos | M | Done | R-PRD-7, R-EST-8, R-EST-7 | PRD-06a1, EST-03, EST-02, EST-01a | [PRD-06a2](PRD-06a2-produto-acabado.md) |
| 20 (3.4) | PRD-06b | Expedição: romaneio e saída do produto acabado | M | Done | R-PRD-8, R-EST-6, R-EST-8 | PRD-06a2, COM-03 | [PRD-06b](PRD-06b-expedicao-romaneio.md) |
| 21 (3.4) | EST-04 | Lote, série e rastreabilidade | M | Done | R-EST-8, R-PRD-7, R-PRJ-4 | CMP-04, EST-03, PRD-06a2, PRD-06b | [EST-04](EST-04-lote-serie-rastreabilidade.md) |
| 22 (3.4) | EST-01b | Ajuste de estoque com motivo e aprovação | M | Done | R-EST-6, R-EST-5, R-EST-7, R-SEC-10 | EST-01a, EST-02 | [EST-01b](EST-01b-ajuste-estoque-aprovacao.md) |
| 23 (3.4) | EST-05 | Inventário (contagem por local) | M | Done | R-EST-9, R-EST-6 | EST-01b, EST-02 | [EST-05](EST-05-inventario.md) |
| 24 (3.4) | EST-06 | Capacidade de produzir (D, desejável; pode ir à Fase 4) | P | Done | R-EST-10 | ENG-02, EST-02, CMP-01 | [EST-06](EST-06-capacidade-de-produzir.md) |


| 18 (3.3) | CMP-03b | PDF e envio do pedido de compra ao fornecedor | P | Done | R-CMP-3, R-COM-1, R-COM-3 | CMP-03a, COM-03 | [CMP-03b](CMP-03b-pdf-envio-pedido.md) |
| Fase 6 | CAD-04c2 | Funcionários: dependentes, banco/Pix, adicionais, vale-transporte e documentos | M | Done | R-RH-1, PRD §6 LGPD | CAD-04c1 | [CAD-04c2](CAD-04c2-funcionarios-complemento.md) |

Total da Fase 3: 24 stories (P: 4 · M: 15 · G: 5), todas `Ready`; cinco com pré-condição externa explícita que não bloqueia (PRD-03b, EST-01a, CMP-03a, CMP-04, PRD-06a2). A CAD-04c2 (M, `Ready`) foi movida para a Fase 6.

| Marco | Stories | P | M | G |
|---|---|---|---|---|
| 3.1 "Fábrica visível" | ENG-01, PRD-01, PRD-02, CAD-04c1, PRD-03a, PRD-04, PRD-05, PRD-06a1, PRD-03b | 2 | 5 | 2 |
| 3.2 "Estoque e BOM" | EST-01a, ENG-02, EST-02, EST-03 | 0 | 3 | 1 |
| 3.3 "Comprar o que falta" | CMP-01, CMP-03a, CMP-04, CMP-02, CMP-03b | 1 | 2 | 2 |
| 3.4 "Fechar e rastrear" | PRD-06a2, PRD-06b, EST-04, EST-01b, EST-05, EST-06 | 1 | 5 | 0 |

## Ordem de execução (D1, seção 4 da revisão)
```
3.1 Fábrica visível:      ENG-01 → PRD-01 → PRD-02 → CAD-04c1 → PRD-03a → (ADR 0025) PRD-04 → PRD-05 → PRD-06a1 → (ADR 0026) PRD-03b
3.2 Estoque e BOM:        (ADR 0023) EST-01a → ENG-02 (liga o bloqueio por BOM) → EST-02 (reconcilia as BOMs já liberadas) → EST-03 (realizado de material, com recomposição)
3.3 Comprar o que falta:  (ADR 0024) CMP-01 → CMP-03a → CMP-04 → CMP-02 → CMP-03b
3.4 Fechar e rastrear:    PRD-06a2 → PRD-06b → EST-04 → EST-01b → EST-05 → EST-06 (D, desejável; pode ir para a Fase 4)
Adiados:                  sem rede da PRD-03b (depois do piloto, P-23) · CAD-04c2 (Fase 6, antes da RH-02)
```

Por que esta ordem:
- **"Fábrica visível" primeiro (D1):** pela ordem anterior o PO só via a fábrica no sistema na 14ª story, e o estoque da Konebras veio zerado do Dolibarr (depende de contagem física). O Marco 3.1 roda sem estoque e sem BOM: OS, Kanban, apontamento, custo de mão de obra, qualidade e conclusão da OS que libera a parcela "fabricação concluída". **O PO valida no fim do 3.1:** OS no Kanban, horas no Projeto 360 e a parcela "fabricação concluída" liberada sozinha.
- **ADR 0025 antes da PRD-04:** a PRD-04 cria a `commercial.project_costs` e muda o cálculo do Projeto 360 (COM-05) já no ar.
- **ADR 0026 antes da PRD-03b**, que fica no fim do 3.1 porque depende da `production.approve_quality` da PRD-05 (D6).
- **3.2:** o motor de estoque (ADR 0023) vem antes de tudo que mexe em saldo; a ENG-02 liga o bloqueio "etapas depois da primeira exigem BOM liberada" (D12); a EST-02 reconcilia as BOMs já liberadas; a EST-03 leva o material ao realizado do projeto e recompõe as saídas anteriores (D3).
- **3.3:** ADR 0024 antes da CMP-03a; o pedido nasce manual ou da SC e o recebimento fecha o ciclo do caixa; a cotação vem depois (CMP-02 gera os pedidos pela API da CMP-03a) e o PDF/envio por último.
- **3.4:** produto acabado com série e custo (as OS concluídas antes da PRD-06a2 ficam sem entrada no estoque, documentado), romaneio que baixa (D4), rastreabilidade, ajuste e inventário perto um do outro (a EST-01b entrega o ponto de bloqueio do local que a EST-05 liga), e a capacidade de produzir como desejável.


## Quebras, movidas, adiadas e re-estimativas (21/10/2026)
- **CAD-04c → CAD-04c1 + CAD-04c2 (D9):** a 04c1 (P, Fase 3, Marco 3.1) entrega afastamentos, `Status` calculado, `IEmployees.Status` e o job do afastamento com data futura; a 04c2 (M) leva dependentes, banco/Pix, adicionais, vale-transporte "usa / não usa" e documentos para a **Fase 6, antes da RH-02**. O arquivo `CAD-04c-funcionarios-complemento.md` foi substituído pelos dois. Revê a decisão D6 de 18/10/2026.
- **PRD-06a → PRD-06a1 + PRD-06a2 (D1):** a 06a1 (P, Marco 3.1) conclui a OS, passa o projeto a "Fabricado" e libera a parcela, sem estoque; a 06a2 (M, Marco 3.4) dá entrada no produto acabado com série e custo. O arquivo `PRD-06a-conclusao-os-produto-acabado.md` foi substituído pelos dois.
- **PRD-03b replanejada (D5):** fica aparelho registrado e PIN (ADR 0026); o funcionamento sem rede saiu para "Fora do escopo", para depois do piloto, se o Wi-Fi falhar (P-23). Arquivo renomeado para `PRD-03b-pwa-pin-aparelho.md`.
- **Movidas:** EST-01b para o Marco 3.4, perto da EST-05; CMP-02 para depois da CMP-04; CAD-04c2 para a Fase 6.
- **Re-estimada:** PRD-04 (P → M: cria a `project_costs`, muda a COM-05 com teste de regressão e recompõe os apontamentos).
- **Contagem:** 24 stories na Fase 3 depois das quebras (a revisão dizia 25 contando a CAD-04c2, que agora é da Fase 6), mais a CAD-04c2.

## Padrões comuns da Fase 3 (domínio)
Valem além dos padrões das fases anteriores. Cada story só repete o que é específico dela.
1. **Efeito síncrono, projeção por evento (ADR 0023 §1):** o efeito de estoque e de título de um documento é chamada síncrona ao contrato do dono (`IStockMovements`, `IPayables`), na transação do documento; o evento fica para custo do projeto, notificação, Kanban e painéis.
2. **Um motor de estoque (ADR 0023 §2 a §4):** só o `IStockMovements` escreve posição, lote e custo médio; a reserva trava na posição agregada `stock_positions` (item, local), com `CHECK`s; trava item → local → lote; `PostAsync` exige transação aberta; movimento imutável (trigger), estorno por movimento inverso pelo documento de origem, idempotência por `(origem, perna, tipo)`; 409 `insufficient_stock`; saldos e custo médio fora do diff da auditoria.
3. **Reconciliar a partir da fonte (ADR 0023 §6):** consumidores de reserva e falta leem a BOM liberada vigente e a situação da OS, nunca o delta do evento; `StockShortageChanged` é só aviso.
4. **Unidades:** toda quantidade gravada está na unidade de estoque (`numeric(18,4)`); conversão na borda pelo `IUnitConversion`, nunca fator 1.
5. **Custo:** custo médio ponderado móvel global por item (`numeric(18,6)`); total da linha em 2 casas `AwayFromZero`; frete e IPI rateados pelo valor no recebimento (P-16); IPI não recuperável até a P-01; custo nunca no PWA.
6. **Custo real do projeto (ADR 0025, D2):** realizado = título confirmado de serviço + baixa de material para a OS + horas apontadas; "Comprometido" = títulos previstos e compra de item estocável ainda não baixada; projetado = realizado + max(orçado − realizado, comprometido em aberto). A `commercial.project_costs` nasce na PRD-04 como razão de diferenças; `Production` e `Shipment` nunca entram.
7. **Recompor o passado (D3):** a story que entrega um consumidor recompõe, a partir da fonte e uma vez, o que foi publicado antes dela (PRD-04, EST-02, EST-03, CMP-01), com a marca "Calculado na implantação".
8. **Caixa das compras (ADR 0024):** pedido aprovado → títulos `Planned` por parcela, na mesma transação; cada recebimento → títulos próprios em aberto (origem `GoodsReceipt`) e os previstos replanejados para o saldo; Σ recebido ≤ aprovado + tolerância (padrão 0%).
9. **Segregação de função:** entrada e saída manual acima de R$ 1.000,00 ou com desvio de custo acima de 20% aprovadas por outra pessoa (D7); todo ajuste com `inventory.approve` de outra pessoa (EST-01b); recontador diferente do primeiro contador (EST-05); solicitante do pedido não aprova e quem pediu ou aprovou não recebe (CMP-03a, CMP-04, D8); quem fez etapa da tentativa não aprova a qualidade (PRD-05, "Autoinspeção" sem outro aprovador); quem corrige ou aprova o apontamento não é o dono (PRD-03a, com a mesma exceção do ponto 76); exceção do Proprietário único pelo helper comum `SoleOwnerRule`, com marca e notificação.
10. **Idempotência e concorrência:** todo comando que cria documento ou movimento exige `Idempotency-Key`; `xmin` em todo documento; consumidores idempotentes pela inbox e pela origem.
11. **Chão de fábrica:** `/fab` sem dock, com o kit da PRD-03a; fila com todas as etapas prontas, as do centro do operador primeiro (D11); PIN só em aparelho registrado, sessão de 12 h restrita à lista permitida (ADR 0026); tempo real pelo `ProductionHub` com `RequireModule` e permissão no `MapHub`, mensagem só com o id, push depois do commit, conexão fechada quando a permissão ou o security stamp mudam. O token de tela (quiosque) é do painel da TV (PNL-01, Fase 4).
12. **Permissões novas (ADR 0003 adendo):** `engineering.delete`, `purchasing.receive`, `production.approve_quality`; seed patches `engineering-permissions` (ENG-01), `engineering-release-bom` (ENG-02, ADR 0008 adendo), `purchasing-permissions` (CMP-01), `production-quality` (PRD-05), `inventory-adjust-warehouse` (EST-01b); `*.approve` continua reservada ao Proprietário para conceder.
13. **Numerações (D15, `04-fluxos` §4):** `MOV-AAAA-000001`, `AJ-AAAA-0001`, `REQ-AAAA-0001`, `ROM-AAAA-0001`, `INV-AAAA-0001`, série `{código}-{AAAA}-{seq}`; prefixos únicos no tenant.
14. **Anexos (ADR 0019 §7, adendo de 21/10/2026):** donos `ProductionOrder` (fabricação, com dwg/dxf de 25 MB), `Rfq`, `PurchaseOrder`, `NonConformity`, `Shipment` e `EmployeeDocument`; antivírus configurado para 30 MB; limite de envios por usuário.

## Padrões comuns da Fase 3 (telas, revisão §2.3)
As stories citam estes números na seção "Telas e estados" ("Padrões de tela da Fase 3: 2, 4, 6") e só detalham o específico.
1. **Tela dedicada** (`/fab`, `/fab/entrar`, contagem pelo celular): alvos de 56 × 56 px com 8 px entre eles (16 px entre ações opostas), texto de 18 px, nenhum gesto fino, região de mensagens própria, confirmação em folha de tela cheia, botão "Ajuda" no cabeçalho (abre sozinha só na `/fab/entrar`). Kit "chão de fábrica" criado na PRD-03a.
2. **Faixa lateral sempre com texto** ("Atrasada {n} d", "Em risco, previsto {data}", "+3 h acima do padrão"); nada só por cor.
3. **Indicador de etapa:** atual em âmbar, concluídas apagadas, formato compacto "M · 3/12" acima de 8 etapas, letra vinda do roteiro, `key` pelo índice (corrigido na ENG-01, D17 b).
4. **Efeito em estoque, financeiro ou custo** sempre com diálogo de impacto (antes → depois) e o mesmo verbo, inclusive no PWA ("Finalizar etapa · {h} h apontadas", com "Pausar" separado).
5. **"Aguardando você"** como filtro padrão de toda lista com aprovação.
6. **Valores sugeridos com a tag "Sugerido"** (lote, fornecedor, preço, roteiro, local, vencedor).
7. **Processamento assíncrono:** "Fazendo X…", aviso de demora depois de 30 s e notificação ao terminar.
8. **Lista com saldo** mostra físico, reservado e disponível; a 400 px fica só o disponível, o resto na linha expansível.
9. **Uma navegação só por módulo** (topbar), com abas agrupadas em "Operação" e "Cadastros" (Estoque e Produção).
10. **OS de fabricação e de instalação** compartilham `OrderHeader` e `FactsBar` (PRD-01); ordem das abas da OS: Etapas, BOM, Material, Apontamentos, Qualidade, Custos, Séries, Documentos, Histórico.
11. **Recomposição do passado (D3)** com a marca "Calculado na implantação".

## Validação do PO (21/10/2026)
Primeira validação (21/10/2026, antes da revisão): as 23 stories originais com 10/10; 4 GO e 19 GO condicional, todas `Draft`. Depois das decisões do PO e da revisão pré-Fase 3, o @po (Pax) revalidou cada story alterada e as novas (CAD-04c1, CAD-04c2, PRD-06a1, PRD-06a2) com a mesma checklist de 10 itens, lendo o item 10 como "pontos em aberto decididos e registrados":
- **10/10 e `Ready`:** as 24 stories da Fase 3 e a CAD-04c2 (Fase 6). Os NO-GOs da revisão foram resolvidos pelas ADRs 0023 (EST-01a, EST-02), 0024 (CMP-03a, CMP-04) e 0026 (PRD-03b), e pela D8 (CMP-04).
- **Pré-condições externas explícitas** (não são decisão de story; não bloqueiam o desenvolvimento):
  - PRD-03b: um e-mail da empresa por operador antes de convidar os operadores (P-24);
  - EST-01a e CMP-04: P-01 (crédito de imposto) continua aberta; até lá o custo é o digitado e o IPI entra no custo;
  - CMP-03a: conferir com a contadora a conta e a natureza do título de compra (P-17); o padrão é parâmetro;
  - PRD-06a2: conferir a numeração `elev_*` do Dolibarr (só leitura) antes de implementar a série; o D15 vale se nada mudar.
- **Correções do fechamento da revisão (21/10/2026):**
  - EST-01b: ponto de bloqueio do local por inventário (`ILocationCountLock`, CA 18): com contagem aberta, lançar, aprovar e estornar ajuste e estornar movimento manual recebem 409; a EST-05 liga a implementação real; modo Delta na linha do ajuste para a EST-05 (CA 13);
  - CMP-03a: contrato `IPurchaseOrders.OpenQuantityByItemAsync` (saldo a receber por item, CA 24), usado pelo job do mínimo da CMP-01 e pela EST-06;
  - PRD-03a: o CA 21 (quem apontou não corrige nem aprova) ganhou a exceção do ponto 76 pelo `SoleOwnerRule` (sem outro aprovador elegível, permite com a marca "Autoaprovado" e notifica o Proprietário); o `ProductionStepStarted` é reaproveitado da PRD-02; o bloqueio de BOM vale também na "Concluir etapa" (ENG-02 CA 16);
  - PRD-05: aprovar e reprovar a qualidade só com `production.approve_quality` (D6); a Supervisão de produção ganha a ação pelo seed patch `production-quality`; a conclusão da OS segue com `production.approve` (PRD-06a1);
  - EST-03: o botão "Pedir material" no `/fab` é da EST-03 (API e botão), não da PRD-03a;
  - PRD-06a2: a liberação das reservas na conclusão usa o `IStockReservations.ReleaseForOrderAsync` da EST-02; cria `inventory.serial_components` e a série `{código}-{AAAA}-{seq}` (D15), como a EST-04 supõe;
  - ADR 0024 §2: `CreateOpenInstallmentsAsync` para os títulos do recebimento e índice único parcial `(origin, origin_id, installment) WHERE status <> 'Cancelled'`.

## Pré-condições da fase
- ADRs 0023, 0024, 0025 e 0026 aceitas e adendos às ADRs 0003, 0008, 0009 e 0019 registrados (21/10/2026). Cada uma antes da sua story: ADR 0025 antes da PRD-04, ADR 0026 antes da PRD-03b, ADR 0023 antes da EST-01a, ADR 0024 antes da CMP-03a.
- Componentes compartilhados nascem dentro das stories: indicador de etapa corrigido (ENG-01), `OrderHeader`/`FactsBar` (PRD-01), kit chão de fábrica (PRD-03a), `SoleOwnerRule` (EST-01a).
- Migrations: nada destrutivo; cuidado com a versão anterior em `production_orders` (colunas anuláveis com `CHECK`, deploy em duas etapas na PRD-01), na troca do índice de `payables` (CMP-03a) e no trigger e `CHECK`s de saldo (EST-01a). Toda migration testada sobre cópia do banco do tenant `konebras` (ADR 0020).

## Rastreabilidade da revisão pré-Fase 3
| Item da revisão | Onde entra |
|---|---|
| D1 (ordem "fábrica visível") | Ordem acima; quebra da PRD-06a em PRD-06a1 e PRD-06a2 |
| D2 (custo realizado e "Comprometido") | ADR 0025 §1 e §4; PRD-04 (troca do cálculo da COM-05, teste de regressão sobre cópia do `konebras`); CMP-03a |
| D3 (recompor o passado) | Padrão de tela 11 e padrão de domínio 7; PRD-04, EST-02, EST-03, CMP-01 |
| D4 (romaneio baixa) | PRD-06b; ENG-01 (etapa Expedição "Preparar e embalar"); `04-fluxos` §2; P-04 |
| D5 (PIN e aparelho) | ADR 0026; PRD-03b; P-12, P-23, P-24 |
| D6 (qualidade × PIN) | ADR 0003 adendo; PRD-05 (CA 1, 2, 13); PRD-03a (CA 16); PRD-03b |
| D7 (segregação no estoque) | EST-01a; EST-03 (consumo acima da BOM acima de R$ 1.000,00) |
| D8 (segregação em compras) | ADR 0003 adendo (`purchasing.receive`); CMP-01 (seed), CMP-02 (anexo acima de R$ 5.000,00), CMP-03a, CMP-04 |
| D9 (CAD-04c) | CAD-04c1 (Fase 3) e CAD-04c2 (Fase 6, antes da RH-02) |
| D10 (Kanban sem arraste) | PRD-02; ENG-01 (campo "Letra") |
| D11 (fila do operador) | PRD-03a |
| D12 (etapas sem BOM e conclusão explícita) | PRD-03a (CA 9, 17); ENG-02 (CA 16); PRD-05; PRD-06a1; `04-fluxos` §1 |
| D13 (permissões da Engenharia) | ADR 0003 e 0008 adendos; ENG-01 (`engineering-permissions`), ENG-02 (`engineering-release-bom`) |
| D14 (quais itens geram OS) | PRD-01 |
| D15 (numerações) | EST-01a (MOV), EST-01b (AJ), EST-03 (REQ), PRD-06b (ROM), EST-05 (INV), PRD-06a2 (série); `04-fluxos` §4 |
| D16 (desenhos na OS) | ADR 0019 §7 adendo; PRD-01 |
| D17 (a) | PRD-04 (D2) |
| D17 (b) | ENG-01 (`StageIndicator`) |
| D17 (c) | Fora das stories: PR de código separado (ver "Pendências herdadas") |
| D17 (d) | ADR 0026; PRD-03b (prazo absoluto de 30 dias também na sessão completa) |
| Tabela 1.1 (pontos 1 a 95) | "Pontos em aberto" de cada story, como decididos |
| §2.1 Arquitetura | ADRs 0023 a 0026; EST-01a, EST-02, EST-03, CMP-03a, CMP-04, PRD-01, PRD-02, PRD-04, PRD-06a1, PRD-06a2, PRD-06b, PRD-03b, EST-05 |
| §2.2 Segurança | CAs **[Segurança]** em CAD-04c1, CAD-04c2, EST-01a, EST-01b, EST-03, EST-05, CMP-01, CMP-02, CMP-03a, CMP-03b, CMP-04, PRD-01, PRD-02, PRD-03a, PRD-03b, PRD-05, PRD-06a1, PRD-06b, EST-04 |
| §2.3 Design | Padrões comuns da Fase 3 (telas) acima; achados pontuais nas stories |
| §3 Pré-requisitos | Pré-condições da fase acima |
| §4 Ordem | Ordem de execução acima |

## Pendências herdadas
1. **D17 (c), idempotência com o usuário no escopo:** fora das stories; entra num PR de código pequeno e separado, a qualquer momento (risco baixo: a chave é aleatória e o banco é do tenant).
2. **Seed de Compras:** o seed patch `purchasing-permissions` (CMP-01) dá ao Almoxarifado só `purchasing.view` + `purchasing.receive` e ao Financeiro `purchasing.view` (D8). Com isso, **só o Proprietário cria solicitação de compra e cotação** no seed. A implantação da Konebras precisa dar `purchasing.create` (e `purchasing.edit`, se for o caso) ao grupo de quem compra; a ajuda de Compras e o roteiro de implantação dizem isso.
3. **Seed da qualidade:** o seed patch `production-quality` (PRD-05) dá `production.approve_quality` à Supervisão de produção do seed; o tenant concede a ação ao inspetor que usa o tablet.
4. **P-01** (crédito de imposto no custo médio e IPI recuperável) continua aberta, sem bloquear (EST-01a, CMP-04).
5. **P-17** (conta e natureza do título de compra) conferida com a contadora, sem bloquear (CMP-03a).
6. **P-23** (funcionamento sem rede do app do operador): depois do piloto, se o Wi-Fi falhar.
7. **P-24** (operador sem e-mail): um e-mail da empresa por operador antes do piloto da PRD-03b.
8. **P-19** e **P-20** (Fase 2) continuam adiadas.

## Pontos em aberto da Fase 3
Nenhum ponto bloqueante nas stories. Os 95 pontos da tabela 1.1 da revisão foram decididos pelo PO em 21/10/2026 e estão registrados em cada story como "decididos". Ficam com o PO só as pré-condições externas e as pendências acima.
3. Backup diário do tenant `konebras` no ar antes da primeira carga real da FIN-00; teste de restauração recomendado, sem bloquear.
4. **P-01** (regime tributário) continua aberta, sem bloquear a fase.

---

# Fase 4 — Planejamento, painéis e visão do dono

Stories detalhadas das Fases 4 a 8, uma por arquivo, escritas pelo @sm (River) e validadas pelo @po (Pax) em 21/10/2026, e revistas no mesmo dia depois das decisões do PO sobre a revisão consolidada `docs/revisoes/2026-10-21-revisao-pre-fases4-8.md` (architect, po + pm, design-squad e cybersecurity). Fonte: `docs/01` a `06`, ADRs 0001–0042, as decisões da Fase 3 (`docs/revisoes/2026-10-21-revisao-pre-fase3.md`, ADRs 0023–0026), o código das Fases 0 a 2 e o projeto `D:\GIT\EmissorFiscal` (somente leitura). A Definition of Ready, a legenda e a escala de tamanho são as mesmas das fases anteriores. **As 60 stories das Fases 4 a 8 e as 2 do marco final "Go-live" estão `Ready`** (35 com pré-condição externa explícita, que não bloqueia o desenvolvimento).

## Decisões do PO (21/10/2026, John)
Valem para as Fases 4 a 8 e o marco final "Go-live".
- **Aprovadas como recomendado:** D2 a D13, D15, D17, D18 e D19; as tabelas 3.1 a 3.5 inteiras (pontos em aberto de cada fase); as seções 4 (correções sem decisão) e 5 (ADRs). Valores aprovados: R$ 1.000,00 e 20% no estoque; R$ 5.000,00 no anexo da cotação; multa de 2% e juros de 1% a.m.; régua −3, 0, +3, +10 dias; demais valores das tabelas.
- **D1 mudada: tudo antes do go-live.** As Fases 4 a 8 inteiras vêm antes do go-live; GOL-01a e GOL-01b saem da Fase 5 e formam o **marco final "Go-live"**, depois da Fase 8. A GOL-01a exige "Fases 0 a 8 concluídas". O ponto 78 da Fase 5 fica decidido assim. SEFAZ e Inter em produção continuam só na GOL-01b; até lá, homologação SEFAZ e sandbox no tenant `demo` (ADR 0034).
- **D14 (folha no financeiro): opção do arquiteto.** O título a pagar ganha `payee_type` (`Party` ou `Employee`) e `payee_id`, com `CHECK` (`party_id` aceitando nulo é expansão); os dados bancários do funcionário saem de um contrato sensível do HR (`IEmployeeBankAccounts`), lidos só no pagamento da OP e registrados como `SensitiveRead`; encargos com os órgãos como terceiros; previsão com a origem `PayrollForecast`; fechamento síncrono. Mantêm-se as proteções da conta de salário (2FA de novo, segunda confirmação, aviso ao funcionário e ao Proprietário, quarentena de 30 dias, quem fecha não paga). ADR 0036. O ponto 23 da Fase 6 fica decidido assim.
- **D16 (portal): opção da segurança.** Host próprio para o portal; 2FA opcional para ver; código por e-mail obrigatório no aceite. ADR 0040.
- **Ponto (D15, ponto 5 da Fase 6):** o ponto do app entra **sem ser registro oficial** (controle interno, com aviso na tela e na ajuda de que não substitui o registro legal) e deixa de ser pré-condição do Marco 6.1. Story futura pós-MVP "Ponto como registro oficial (REP-P / Portaria 671)" no backlog (Fase 9) e P-26 no `06`.
- **P-02:** o CNAE de fabricação está em providência pela Konebras; continua pré-condição externa da emissão de NF-e de produção.
- **P-19 e P-20:** decididas pela D6: 2FA de novo (TOTP recente) em pagar, estornar, cancelar ou inutilizar NF-e, trocar conta de salário, fechar a folha, anonimizar titular, salvar credenciais do Inter e enviar o certificado A1 (filtro comum `RequireRecentTwoFactor`, criado na BNK-01a e aplicado também à FIN-03a); estorno de pagamento e de baixa bancária com `finance.approve` (BNK-02).
- **Novo requisito: tudo configurável em Configurações** (ADR 0042). Parâmetros fiscais e de integração pré-configurados com os valores propostos e editáveis; o que ainda não foi conferido leva o chip **"A confirmar com a contadora"** (`ToConfirmChip`, BNK-01b) com "Marcar como conferido", auditado; a produção exige os campos críticos conferidos. Telas:

  | Tela | Rota | Story | Quem edita | Ajuda prevista |
  |---|---|---|---|---|
  | Configurações → Cobrança (multa, juros, desconto, envio automático, limite de e-mails, vencido e dia não útil) | `/app/configuracoes/cobranca` | BNK-01b; a FIN-09 acrescenta "Régua de cobrança" e a POR-01c "Segunda via pelo portal" | `settings.edit` | `Ajuda/configuracoes-cobranca.md` |
  | Configurações → Integrações → Banco Inter (credenciais, mTLS, conta, ambiente, webhook, teste de conexão; segredos só de escrita) | `/app/configuracoes/integracoes/inter` | BNK-01a | `settings.edit` + 2FA de novo | `Ajuda/configuracoes-integracoes-inter.md` |
  | Configurações → Fiscal (regime, CRT, série, ambiente, CFOP/CST e alíquotas por cenário, tributos no custo do projeto, NFS-e, IBS/CBS, prazos) | `/app/configuracoes/fiscal` | FIS-02a (as regras detalhadas nascem do mesmo seed na FIS-02b) | `settings.edit` | `Ajuda/configuracoes-fiscal.md` |
  | Configurações → Fiscal → Certificado digital (A1 direto ao cofre do EmissorFiscal; o BemAqui guarda validade, titular e thumbprint; alerta de vencimento) | `/app/configuracoes/fiscal/certificado` | FIS-02a | `settings.edit` + 2FA de novo | `Ajuda/configuracoes-fiscal-certificado.md` |
  | Configurações → Integrações → WhatsApp (parceiro, credenciais, número, modelos, teste de envio) | `/app/configuracoes/integracoes/whatsapp` | WPP-01 | `settings.edit` + 2FA de novo | `Ajuda/configuracoes-integracoes-whatsapp.md` |
  | Configurações → Parâmetros → RH, Produção e Patrimônio; Configurações → Privacidade (retenção) | `/app/configuracoes/parametros`, `/app/configuracoes/privacidade` | RH-02a, RH-01c, PLN-01b, PAT-01b, PAT-03b, LGPD-01a, LGPD-01b | `settings.edit` (Privacidade: só o Proprietário) | `Ajuda/configuracoes-parametros.md`, `Ajuda/configuracoes-privacidade.md` |

  `settings.edit` é reservada ao Proprietário para conceder (ADR 0008). Nenhuma story `CFG-*` foi necessária: cada tela entrou na story que já tratava do assunto.
- **ADRs novas:** 0027 (EmissorFiscal), 0028 (composição do painel e dos relatórios; revoga o `Reporting` da 0006), 0029 (planejamento finito), 0030 (quiosque e TV), 0031 (recepção de webhooks), 0032 (Banco Inter), 0033 (Gantt próprio), 0034 (promoção a produção e go-live depois da Fase 8), 0035 (autoatendimento do funcionário), 0036 (folha no financeiro), 0037 (execução da LGPD), 0038 (patrimônio, manutenção e capacidade), 0039 (motor da folha calculada), 0040 (identidade do portal), 0041 (WhatsApp), 0042 (Configurações pré-configuradas). **Adendos de 21/10/2026:** 0025 (comprometido por saldo; fontes `InvoiceTax`/`InvoiceTaxReversal`), 0003 (`fiscal.approve`, `fiscal.import_incoming`, `kiosk.delete`, `hr.view_health`, módulo `portal`, ações do `assets`), 0007 (`WebhookSecret`, `FiscalApiKey`, `WhatsAppCredentials`; Inter por conta; A1 fora do BemAqui), 0008 (`fiscal.approve` reservado; D6), 0009 (rotas anônimas da TV, dos webhooks e do portal) e 0026 (PIN no "Meu ponto"); nota de revogação na 0006.
- **Docs atualizados:** `02-arquitetura` (rota `/tv#token`, sem índice de quiosque no catálogo, webhook no host da plataforma com `tenantKey` opaco, Dashboard, plano finito, EmissorFiscal, Inter por conta, portal em host próprio, ambiente de produção novo), `03-modelo-dados`, `04-fluxos-estados` e `06-pendencias-riscos` (P-01, P-02, P-03, P-08, P-13, P-17, P-19, P-20 atualizadas; P-26 a P-29 novas).

## Ordem de execução até o go-live
```
Agora (fora das stories): trilha externa da D2 · lacunas do EmissorFiscal (D3, P-27) · PR de reforço do código no ar (D19)
Fase 4:  4.1 FIN-07 → DSH-02 → FIN-08 · 4.2 DSH-04 → DSH-01a · 4.3 PLN-01a → PLN-01b → PLN-02 → PLN-04 → PLN-03
         · 4.4 PNL-01a → PNL-01b → DSH-03 · 4.5 DSH-01b → PLN-05
Fase 5:  5.1 FND-16 → trilha do banco: 5.2 BNK-01a → BNK-01b → BNK-02 · 5.3 BNK-03a → BNK-03b
                  → trilha fiscal (FIS-01 pode correr em paralelo à do banco): 5.1 FIS-01 · 5.4 FIS-02a → FIS-02b → FIS-03a → FIS-03b
         · 5.5 FIS-04a → FIS-04b → FIS-05 · 5.6 FIS-06 → FIN-09
Fase 6:  6.2 Folha lançada: CAD-04c2 → RH-02a → RH-02c → RH-02b → RH-03 · 6.1 Ponto: RH-01a → RH-01b → RH-01c
         · 6.3 LGPD-01a → LGPD-01b · 6.4 Patrimônio em paralelo: PAT-01a → PAT-01b → PAT-02 → PAT-03a → PAT-03b
Fase 7:  7.1 FLH-01 → FLH-02a → FLH-02b · 7.2 FLH-03 → FLH-04 → FLH-05 · 7.3 FLH-06 (em paralelo ao 7.2)
Fase 8:  8.1 POR-01a · 8.2 POR-01b → POR-01c · 8.3 POR-02 · 8.4 WPP-01
Go-live: GOL-01a → GOL-01b (início de um mês, fora do horário comercial)
```
Por que esta ordem: a Fase 4 entrega primeiro a visibilidade financeira (FIN-07 e DSH-02 não dependem de ninguém de fora); a Fase 5 começa pela recepção de webhooks e corre em duas trilhas, porque o banco não depende da contadora nem do A1; a Fase 6 começa pela folha, que leva o maior custo da empresa ao caixa, e o ponto (controle interno) vem depois; a Fase 7 pode rodar em simulação, e a virada da folha calculada (FLH-06) é operação, que não segura o go-live; a Fase 8 fecha o relacionamento com o cliente; o go-live só vem com tudo pronto, com ensaio e rollback numa cópia.

## Padrões comuns das Fases 4 a 8 (design, revisão §4.4)
As stories citam estes números na seção "Telas e estados" ("Padrões comuns das Fases 4 a 8: 1, 2") e só detalham o específico.
1. **Família de gráficos sobre `ChartFrame`** (`Chart`, `ColumnChart`, `Gantt`): série nunca só por cor (traço e rótulo direto), referências em x e em y (zero, meta, capacidade), resumo textual acima e tabela equivalente. `ChartFrame` e a troca do `--chart-3` (hoje âmbar) na FIN-07; `ColumnChart`, `Kpi` e `DashboardCard` na DSH-02; `Gantt` próprio em SVG na PLN-03 (ADR 0033).
2. **`Kpi` e `DashboardCard`:** todo número clicável até a origem, "Como calculamos", "Atualizado às", estados por card; faixa "Atenção" com até 3 alertas.
3. **Tabelas analíticas** (DRE, ocupação, espelho, conciliação): coluna fixa, navegação por célula, drill-down em painel lateral com total igual ao da célula (`TreeTable`, FIN-08). No DRE, sinal no rótulo ("(−) Custos"), crítico só em resultado negativo e coluna "Δ mês anterior".
4. **Kit TV** (D11, ADR 0030): tema escuro, 32/28/48 px, até 4 linhas por cartão e 3 cartões por coluna com "+{n} OS", "Ao vivo / Reconectando… / Atualizado às", prévia no backoffice (`/app/painel-tv/$id/previa`); `StageIndicator` com `size="tv"` e `size="portal"` (PNL-01a/01b).
5. **Kit Portal** (POR-01a): celular primeiro, 16 px, alvos de 44 px, vocabulário leigo com teste de texto (sem OS, marco, romaneio, letras de etapa), etapa com nome e frase do que acontece e do que vem depois, bloco "Fale com a {empresa}" em todo erro e boleto vencido, "Preferências de aviso", mensagem própria de indisponibilidade.
6. **Integração externa assíncrona** (SEFAZ, Inter, WhatsApp): "Enviado / Aguardando / Demorando (30 s) / Confirmado / Recusado com motivo e ação", sem reenvio enquanto processa; `EnvironmentBanner` único (BNK-01a).
7. **Ações irreversíveis:** `ConfirmDialog` com confirmação digitada quando apaga dado (LGPD), contagem do prazo legal (cancelamento de NF-e) e impacto em lista nas ações em lote; criado na BNK-01b e reutilizado (FIS-03a, FIS-03b, LGPD-01b, CAD-04c2, RH-02c).
8. **Dado sensível do RH:** bloco "Dados protegidos" padrão; autoatendimento ("Meu ponto", "Meu espelho", "Meus holerites") com o mesmo layout no `/fab` e no celular.

Achados pontuais da §4.4 aplicados nas stories: conciliação com uma linha por par e as seções "Sem par no banco" e "No sistema, não no banco" (BNK-03b); fila "A emitir" em Notas de saída e na pauta (FIS-03a, DSH-04); "Pagamentos a conferir" e "Números a inutilizar" como filtros (BNK-02, FIS-03b); campos incertos com texto e foco no primeiro (FIS-05); prévia da mensagem e falhas reenviáveis (WPP-01); botões de `ErrorState`/`EmptyState` com 44 px a 400 px; "Ver conteúdo" com JSON mascarado (FND-16); "Simular" em painel lateral (FIS-02b); diferença em R$ e % por rubrica (FLH-06).

## Índice
| Ordem | ID | Título | Tam. | Status | Requisitos | Depende de | Arquivo |
|---|---|---|---|---|---|---|---|
| 1 (4.1) | FIN-07 | Projeção de caixa ajustada pelo histórico do cliente | M | Done | R-FIN-11, R-FIN-10, R-TER-4, R-PRP-5, PRD §5.14, PRD §6 | FIN-05, FIN-06b, FIN-00, FND-09e | [FIN-07](FIN-07-projecao-ajustada.md) |
| 2 (4.1) | DSH-02 | Orçado × realizado e margem, por projeto e consolidado | G (re-estimada de M) | Ready | R-PRJ-5, PRD §5.14, PRD §1.1, PRD §1.2, R-PRP-5, PRD §6 | FIN-07, COM-05, COM-07, PRD-04, ADR 0025, EST-03, COM-06 | [DSH-02](DSH-02-orcado-realizado-margem.md) |

| 1 (4.1) | FIN-07 | Projeção de caixa ajustada pelo histórico do cliente | M | Ready | R-FIN-11, R-FIN-10, R-TER-4, R-PRP-5, PRD §5.14, PRD §6 | FIN-05, FIN-06b, FIN-00, FND-09e | [FIN-07](FIN-07-projecao-ajustada.md) |
| 2 (4.1) | DSH-02 | Orçado × realizado e margem, por projeto e consolidado | G (re-estimada de M) | Done | R-PRJ-5, PRD §5.14, PRD §1.1, PRD §1.2, R-PRP-5, PRD §6 | FIN-07, COM-05, COM-07, PRD-04, ADR 0025, EST-03, COM-06 | [DSH-02](DSH-02-orcado-realizado-margem.md) |
| 3 (4.1) | FIN-08 | DRE gerencial mensal | G (re-estimada de M) | Ready (pré-condição externa) | R-FIN-13, R-FIN-1, PRD §5.14, R-PRP-5, PRD §6 | CAD-05a, FIN-01, FIN-02, FIN-04, FIN-00, COM-01b, PRD-06b | [FIN-08](FIN-08-dre-gerencial.md) |

| 3 (4.1) | FIN-08 | DRE gerencial mensal | G (re-estimada de M) | Done | R-FIN-13, R-FIN-1, PRD §5.14, R-PRP-5, PRD §6 | CAD-05a, FIN-01, FIN-02, FIN-04, FIN-00, COM-01b, PRD-06b | [FIN-08](FIN-08-dre-gerencial.md) |
| 4 (4.2) | DSH-04 | Minha pauta | M | Ready | R-PRP-4, R-PRP-5, R-COM-2, PRD §5.14, PRD §6 | FIN-03a, FIN-03b, FIN-01, FIN-02, FIN-05, FIN-07, COM-02 | [DSH-04](DSH-04-minha-pauta.md) |
| 5 (4.2) | DSH-01a | Painel do proprietário: módulo Dashboard e cards de caixa, faturamento, inadimplência, comercial e decisões | G | Done | PRD §5.14, R-FIN-10, R-FIN-11, R-FIN-12, R-FIN-13, R-COML-9 | FIN-05, FIN-07, FIN-08, FIN-06a, COM-08, DSH-04, DSH-02 | [DSH-01a](DSH-01a-painel-dono-financeiro-comercial.md) |
| 6 (4.3) | PLN-01a | Motor de agendamento finito | G | Ready | R-PLN-3, R-PLN-1, PRD §6, R-SEC-5 | PRD-01, PRD-03a, PRD-05, PRD-06a1, CAD-03 | [PLN-01a](PLN-01a-motor-agendamento.md) |

| 5 (4.2) | DSH-01a | Painel do proprietário: módulo Dashboard e cards de caixa, faturamento, inadimplência, comercial e decisões | G | Ready | PRD §5.14, R-FIN-10, R-FIN-11, R-FIN-12, R-FIN-13, R-COML-9 | FIN-05, FIN-07, FIN-08, FIN-06a, COM-08, DSH-04, DSH-02 | [DSH-01a](DSH-01a-painel-dono-financeiro-comercial.md) |
| 6 (4.3) | PLN-01a | Motor de agendamento finito | G | Done | R-PLN-3, R-PLN-1, PRD §6, R-SEC-5 | PRD-01, PRD-03a, PRD-05, PRD-06a1, CAD-03 | [PLN-01a](PLN-01a-motor-agendamento.md) |
| 7 (4.3) | PLN-01b | Recálculo por evento, data prevista do projeto e risco de atraso | M | Done | R-PLN-3, R-PLN-4, R-COM-2, R-PRP-5, PRD §1.2, PRD §6 | PLN-01a, COM-05, COM-07, PRD-02, PRD-03a, PRD-05, PRD-06a1 | [PLN-01b](PLN-01b-recalculo-previsao-risco.md) |
| 8 (4.3) | PLN-02 | Fila de prioridade dos projetos | M | Done | R-PLN-2, R-PLN-3, R-PLN-4, R-PRP-2, R-SEC-5 | PLN-01a, PLN-01b, COM-05, FND-09e | [PLN-02](PLN-02-fila-prioridade.md) |
| 9 (4.3) | PLN-04 | Ocupação da fábrica por semana e centro | M | Done | R-PLN-6, R-PLN-1, PRD §5.14, PRD §6 | PLN-01a, CAD-03, DSH-02, FIN-08 | [PLN-04](PLN-04-ocupacao-fabrica.md) |
| 10 (4.3) | PLN-03 | Cronograma (Gantt) por projeto e por centro de trabalho | G | Done | R-PLN-4, R-PLN-3, PRD §5.14, R-PRP-5, PRD §6 | PLN-01a, PLN-01b, PLN-02, FIN-07, COM-05, COM-06 | [PLN-03](PLN-03-gantt.md) |
| 11 (4.4) | PNL-01a | Tokens de tela, sessão de quiosque e tempo real da TV | G | Done | R-SEC-6, PRD §5.14, PRD §2, PRD §6, R-SEC-5 | PRD-02, PLN-01b, FND-02, FND-04, FND-09b, FND-12, ADR 0015 | [PNL-01a](PNL-01a-tokens-tela-sessao-quiosque.md) |
| 12 (4.4) | PNL-01b | Visões do painel da TV | M | Done | PRD §5.14, R-PRD-2, R-PLN-4, PRD §6 | PNL-01a, PRD-02, PRD-03a, PLN-01a, PLN-01b, PLN-02 | [PNL-01b](PNL-01b-visoes-painel-tv.md) |
| 13 (4.4) | DSH-03 | Painel do supervisor | M | Done | PRD §5.14, R-PRD-2, R-PRD-6, R-PLN-6, R-EST-4, R-CMP-1 | PRD-02, PLN-04, PLN-01b, PRD-05, EST-02, CMP-01, PRD-03a | [DSH-03](DSH-03-painel-supervisor.md) |
| 14 (4.5) | DSH-01b | Painel do proprietário: margem, carteira, produção, entregas e resultado gerencial por projeto | G (re-estimada de M) | Done | PRD §5.14, R-FIN-13, R-PRJ-5, R-PLN-4, R-PLN-6, R-PRP-5 | DSH-01a, DSH-02, FIN-08, PLN-01b, PLN-04, PRD-02, COM-05 | [DSH-01b](DSH-01b-painel-dono-margem-producao.md) |
| 15 (4.5) | PLN-05 | Simulação de prazo do orçamento | M | Done | R-PLN-5, R-COML-1, R-PRP-1, R-PRP-2 | PLN-01a, PLN-01b, PLN-02, COM-01a, PRD-01, ENG-01 | [PLN-05](PLN-05-simulacao-prazo-orcamento.md) |

Total da Fase 4: 15 stories (M: 8 · G: 7), todas `Ready`; 2 com pré-condição externa explícita, que não bloqueia.

| Marco | Stories |
|---|---|
| 4.1 "O dinheiro do jeito que ele vai acontecer" | FIN-07, DSH-02, FIN-08 |
| 4.2 "O painel do dono, parte do dinheiro" | DSH-04, DSH-01a |
| 4.3 "Datas que a fábrica cumpre" | PLN-01a, PLN-01b, PLN-02, PLN-04, PLN-03 |
| 4.4 "A fábrica na parede" | PNL-01a, PNL-01b, DSH-03 |
| 4.5 "A visão completa do dono" | DSH-01b, PLN-05 |

## Quebras e re-estimativas
- **Quebras (primeira validação):** PLN-01 → 01a/01b; PNL-01 → 01a/01b; DSH-01 → 01a/01b.
- **"Resultado gerencial por projeto"** saiu da FIN-08 (agora "DRE gerencial mensal") e foi para a DSH-01b, no módulo `Dashboard` (D9, ADR 0028), com a nota de que não fecha com o DRE mensal.
- **Re-estimadas (M → G):** FIN-08 (já na primeira validação), DSH-02 (componentes da D18, comprometido por saldo e imposto pela nota da D8) e DSH-01b (recebe o resultado por projeto).

## Padrões comuns da Fase 4 (domínio)
1. **Cada número tem regra e origem:** todo indicador mostra a regra em `<Dica>` ("Como calculamos") e leva ao documento de origem; o painel nunca recalcula: chama o mesmo contrato do relatório, com teste que compara os dois.
2. **Um cálculo, um lugar:** `ICashForecast` (FIN-07), `IRevenueSummary` (FIN-08), `IProjectMargins` (DSH-02), `IProductionSchedule` (PLN-01a), `IFactoryLoad` (PLN-04), `IPlanningQueue` (PLN-02), `IAgendaService` (DSH-04), `IBusinessCalendar` (DSH-01a, no `Production.Contracts`).
3. **Composição no módulo `Dashboard`** (ADR 0028): só Application e Endpoints, referenciando `*.Contracts`, sem schema; cada card com o `RequireModule` e a permissão do módulo dono e DTO por forma de permissão; cache de 60 s com chave `{tenant}:{usuário}:{versão de permissão}`, contador de geração por tenant e botão "Atualizar". Início com a Minha pauta é a página inicial de todos; o Proprietário com `dashboard.view` cai em `/app/painel`.
4. **Sigilo de custo:** custo, orçado e margem só com `projects.view_cost`; drill-down de salário e custo-hora com nome só com `hr.view_sensitive`; a TV e a simulação do orçamento nunca levam valores.
5. **Custo do projeto (ADR 0025, adendo D8):** comprometido de material = max(0, Σ títulos de compra de item estocável do projeto − Σ baixas líquidas para a OS), fonte única nos títulos do Finance; imposto realizado só pela nota autorizada (`InvoiceTax`/`InvoiceTaxReversal`); até lá, o projetado usa o orçado.
6. **Receita (D4):** pela data da nota; antes de existirem notas reais (go-live), a `issue_date` do título com o rótulo "provisório"; sinal como adiantamento até a nota.
7. **Plano finito, um escritor (ADR 0029):** datas previstas só em `production.schedule_*` e `commercial.project_forecasts`, fora dos documentos e da auditoria; fila de projetos no Production (`production.planning_queue`), reordenada com `production.edit`, auditada; `commercial.projects.priority` sem uso, sem apagar; ordem pela data prometida e depois a assinatura; horas restantes com mínimo de 10% do padrão na etapa em andamento.
8. **Quiosque (ADR 0030):** `/tv#token` trocado por cookie `__Host-bq_kiosk`, sessão de 30 dias sem uso e 180 absolutos, `kiosk.delete`, criar exige `production.view`, cliente nunca aparece, operador pelo primeiro nome e inicial.
9. **Tempo real:** grupos do SignalR derivados das claims, conexão fechada quando a permissão ou o security stamp mudam, mensagem só com id ou sinal, polling de 60 s como reserva.
10. **Parâmetros novos** no schema do módulo dono, editados em Configurações com `settings.edit` (ADR 0016 M6 e ADR 0042): `finance.adjusted_default_threshold_pct` (20%), contas do DRE, `finance.revenue_targets`, `planning.installation_days` (5 dias úteis).

## Validação do PO (21/10/2026)
Primeira validação (21/10/2026, antes da revisão): as 15 stories com 10/10, 2 GO e 13 GO condicional, todas `Draft`. Depois das decisões do PO e da revisão pré-Fases 4 a 8, o @po (Pax) revalidou cada story com a mesma checklist de 10 itens, lendo o item 10 como "pontos em aberto decididos e registrados":
- **10/10 e `Ready`:** as 15 stories. Os ajustes da revisão foram resolvidos pelas ADRs 0025 (adendo, DSH-02), 0028 (DSH-01a/01b, DSH-03, DSH-04), 0029 (PLN-01a, PLN-01b, PLN-02), 0030 (PNL-01a/01b) e 0033 (PLN-03).
- **Pré-condições externas explícitas** (não bloqueiam): FIN-08 (contas de juros, multa e desconto e a conta 2.1 conferidas pela contadora; valores pré-configurados); PLN-01b (prazo padrão de instalação de 5 dias úteis confirmado pela Konebras).
- **Revalidação por amostragem do PO no fechamento (21/10/2026):** DSH-02 conferida de novo contra a ADR 0025 (adendo) e o padrão 1; FIN-08 contra o padrão 3.

## Pontos em aberto da Fase 4
Nenhum. Os 56 pontos da tabela 3.1 da revisão foram decididos pelo PO em 21/10/2026 e estão em cada story como "decididos", com o CA onde entraram. Ficam só as pré-condições externas acima.

---

# Fase 5 — Integrações fiscais e bancárias

Stories da Fase 5 sem o go-live (as GOL foram para o [marco final](#marco-final--go-live)). Ver as [decisões do PO](#decisões-do-po-21102026-john-1) na Fase 4. O EmissorFiscal foi analisado na [FIS-01](FIS-01-emissorfiscal-adr-adaptador.md) e a forma de integração está na ADR 0027: serviço HTTP na rede interna `bqe-fiscal`, um tenant do Emissor por tenant do BemAqui, A1 só no cofre do Emissor, lacunas com dono (P-27).

## Índice
| Ordem | ID | Título | Tam. | Status | Requisitos | Depende de | Arquivo |
|---|---|---|---|---|---|---|---|
| 1 (5.1) | FND-16 | Recepção de webhooks: rota pública, validação, idempotência e processamento por fila | M | Done | R-FIN-7, R-FIS-3, PRD §6, R-SEC-5 | FND-02, FND-07, FND-11, FND-12, FND-10b | [FND-16](FND-16-webhooks-de-entrada.md) |
| 2 (5.2) | BNK-01a | Banco Inter: Configurações → Integrações → Banco Inter, credenciais por conta, mTLS em memória, token OAuth2 e rotação | G (re-estimada de M) | Done | R-FIN-7, R-FIN-8, R-FIN-2, PRD §6, R-SEC-5, R-PRP-5 | FND-16, CAD-05b, FND-11, FND-12, FND-07, FIN-03a | [BNK-01a](BNK-01a-inter-configuracao-mtls.md) |
| 3 (5.2) | BNK-01b | Bolepix a partir do título a receber: Configurações → Cobrança, emissão, PDF, linha digitável, Pix copia-e-cola, e-mail e cancelamento | G | Done | R-FIN-7, R-FIN-3, R-COM-1, R-COM-3, R-PRP-1, R-PRP-2 | BNK-01a, FIN-01, COM-03, CAD-01a, FND-09e | [BNK-01b](BNK-01b-bolepix-emissao-cancelamento.md) |
| 4 (5.2) | BNK-02 | Webhook de pagamento do Inter: reconsulta, baixa automática do título, transação e conferência diária | G (re-estimada de M) | Ready (pré-condição externa) | R-FIN-7, R-FIN-3, R-FIN-2, R-FIN-5, R-TER-4, R-COM-2 | FND-16, BNK-01a, BNK-01b, FIN-01, FIN-03a, FIN-06b | [BNK-02](BNK-02-webhook-pagamento-baixa.md) |
| 5 (5.3) | BNK-03a | Extrato do banco: importação pela API do Inter e por arquivo OFX | M | Done | R-FIN-8, R-FIN-2, R-FIN-10, R-PRP-3, R-PRP-5, R-SEC-5 | BNK-01a, FIN-04, FND-07 | [BNK-03a](BNK-03a-extrato-api-ofx.md) |
| 6 (5.3) | BNK-03b | Conciliação bancária: sugestão de par, conciliação manual, lançamento a partir do extrato e fechamento do período | G | Ready (pré-condição externa) | R-FIN-8, R-FIN-2, R-FIN-3, R-FIN-5, R-FIN-10, R-PRP-1 | BNK-03a, FIN-04, FIN-01, FIN-03a, BNK-02, BNK-01b, BNK-01a | [BNK-03b](BNK-03b-conciliacao-bancaria.md) |
| 7 (5.1) | FIS-01 | Integração com o EmissorFiscal (ADR 0027) e adaptador `IFiscalEmitter` | G (re-estimada de M) | Done (suíte `emissor-homolog`: pré-condição externa) | R-FIS-1, R-FIS-8, R-FIS-6, R-FIS-7, PRD §6, R-SEC-5 | FND-16, FND-11, FND-07, FND-12, BNK-01a, ADR 0027 | [FIS-01](FIS-01-emissorfiscal-adr-adaptador.md) |
| 8 (5.4) | FIS-02a | Configurações → Fiscal e Certificado digital: parâmetros pré-configurados "a confirmar", A1 direto ao Emissor, permissões do módulo `fiscal` | G (re-estimada de M) | Ready (pré-condição externa) | R-FIS-2, R-FIS-4, R-FIS-7, R-FIS-8, R-COM-2, R-PRP-1 | FIS-01, CAD-06, CAD-01b, FND-06, CAD-01a, FND-09e, BNK-01a | [FIS-02a](FIS-02a-configuracao-fiscal-tenant.md) |
| 9 (5.4) | FIS-02b | Regras fiscais por operação e cálculo dos tributos da nota | G | Ready (pré-condição externa) | R-FIS-4, R-FIS-3, R-FIS-7, R-FIS-8, R-PRJ-5, R-PRP-1 | FIS-02a, CAD-02a, CAD-01a, FIS-01 | [FIS-02b](FIS-02b-regras-fiscais-calculo.md) |
| 10 (5.4) | FIS-03a | NF-e de saída: rascunho a partir do romaneio, emissão, autorização, rejeição, DANFE e XML ao cliente, impostos e receita pela nota | G | Ready (pré-condição externa) | R-FIS-3, R-PRD-8, R-PRJ-5, R-FIS-4, R-COM-1, R-COM-3 | FIS-01, FIS-02a, FIS-02b, PRD-06b, PRD-04, COM-04a, COM-05 | [FIS-03a](FIS-03a-nfe-saida-emissao.md) |
| 11 (5.4) | FIS-03b | NF-e de saída: cancelamento em uma etapa com `fiscal.approve`, carta de correção e inutilização de numeração | M | Ready (pré-condição externa) | R-FIS-3, R-SEC-10, R-PRJ-5, R-COM-2, R-PRP-2, R-PRP-7 | FIS-03a, FIS-01, FIS-02a, PRD-06b, DSH-04 | [FIS-03b](FIS-03b-nfe-cancelamento-cce-inutilizacao.md) |
| 12 (5.5) | FIS-04a | NF-e de entrada por XML: importação segura, de-para dos itens e conferência contra o pedido | M | Ready | R-FIS-5, R-CMP-5, R-PRP-3, R-EST-3, R-COM-3, R-SEC-5 | FIS-02a, CAD-02b, CAD-01a, CMP-03a, FND-07 | [FIS-04a](FIS-04a-nfe-entrada-xml-importacao.md) |
| 13 (5.5) | FIS-04b | Recebimento pela NF-e de entrada: estoque pelo custo da nota e títulos a pagar pelas duplicatas, na mesma transação | M | Ready (pré-condição externa) | R-CMP-5, R-EST-7, R-FIN-4, R-FIN-10, R-PRP-2, R-PRP-3 | FIS-04a, CMP-04, CMP-03a, EST-01a, FIS-02a | [FIS-04b](FIS-04b-recebimento-pela-nota.md) |
| 14 (5.5) | FIS-05 | NF-e de entrada por PDF (DANFE): chave, leitura assistida em worker isolado, "Digitar a nota" e conferência obrigatória | M | Ready | R-FIS-6, R-PRP-3, R-SEC-5 | FIS-04a, FIS-04b, FIS-01, FND-07 | [FIS-05](FIS-05-nfe-entrada-pdf.md) |
| 15 (5.6) | FIS-06 | NFS-e da instalação no aceite técnico, com o processo provisório pelo portal da prefeitura | M | Ready (pré-condição externa) | R-FIS-7, R-FIS-8, R-PRJ-3, R-PRJ-5, R-COM-1, R-PRP-2 | FIS-01, FIS-02a, FIS-02b, FIS-03a, COM-06, PRD-04 | [FIS-06](FIS-06-nfse-instalacao.md) |
| 16 (5.6) | FIN-09 | Régua de cobrança por e-mail (D) | M | Ready | R-FIN-9, R-COM-1, R-FIN-12, R-PRP-5, R-SEC-5 | BNK-01b, BNK-02, FIN-01, FND-08, COM-03, FIN-06a | [FIN-09](FIN-09-regua-de-cobranca.md) |

Total da Fase 5: 16 stories (M: 8 · G: 8), todas `Ready`; 11 com pré-condição externa explícita, que não bloqueia.

| Marco | Stories |
|---|---|
| 5.1 Base das integrações | FND-16, FIS-01 |
| 5.2 Cobrança pelo Inter | BNK-01a, BNK-01b, BNK-02 |
| 5.3 Conciliação | BNK-03a, BNK-03b |
| 5.4 Nota de saída | FIS-02a, FIS-02b, FIS-03a, FIS-03b |
| 5.5 Nota de entrada | FIS-04a, FIS-04b, FIS-05 |
| 5.6 NFS-e e cobrança | FIS-06, FIN-09 |

## Quebras, stories novas e re-estimativas
- **Quebras (primeira validação):** FIS-02, FIS-03, FIS-04, BNK-01, BNK-03 em a/b; **nova:** FND-16. A GOL-01 (01a/01b) foi para o marco final.
- **Re-estimadas (M → G):** FIS-01 (adaptador, fake, webhook, conferência de pendentes e testes de contrato), BNK-02 (primeiro consumidor de webhook com efeito financeiro), **BNK-01a** (tela Configurações → Integrações → Banco Inter, filtro comum de 2FA de novo, `EnvironmentBanner`, handler mTLS por conta) e **FIS-02a** (telas Configurações → Fiscal e Certificado digital com os parâmetros pré-configurados e o envio do A1 ao Emissor). A BNK-01b cresceu com a tela de Cobrança, o `ToConfirmChip` e o `ConfirmDialog`, sem passar de G.

## Padrões comuns da Fase 5
1. **Integração atrás de contrato:** o Fiscal só conhece `IFiscalEmitter`; o Finance, `IBankBillingProvider`/`IBankStatementProvider`; nenhum código do EmissorFiscal ou do banco é copiado.
2. **Segredos por tenant, só de escrita** (ADR 0007, adendo): `InterCredentials` e `InterCertificate` por conta, `FiscalApiKey`, `WebhookSecret`, `WhatsAppCredentials`; o A1 não é segredo do BemAqui (vai ao cofre do Emissor). Nunca em log, auditoria, resposta ou `IFileStorage`; canário em cada story; rotação testa o novo antes de trocar.
3. **Webhook nunca é verdade (ADR 0031):** rota única `/webhooks/{provedor}/{tenantKey}` no host da plataforma, fora da allowlist de IP e do middleware de tenant por host; evento e outbox na mesma transação; deduplicação pelo id e pelo SHA-256 do corpo; corpo guardado 90 dias; o efeito vem da reconsulta à fonte (o Inter por mTLS, ADR 0032); GET de verificação só para o provedor que o declara (Meta).
4. **Efeito fiscal e financeiro com confirmação:** automático só o que o banco ou a SEFAZ confirmaram; pagamento com diferença e divergência vão para conferência humana.
5. **Segregação:** configurar integrações com `settings.edit` e 2FA de novo; cancelar e inutilizar nota com `fiscal.approve` (reservada ao Proprietário, uma etapa, "Pedir cancelamento" para os demais); importar nota de entrada com `fiscal.import_incoming`; regra fiscal ativada por outra pessoa; quem importou o extrato não confirma a conciliação; fechamento da conciliação com `finance.approve`; estorno de baixa e de pagamento com `finance.approve`.
6. **Documento fiscal nunca é apagado:** só cancelado ou inutilizado; XML e eventos guardados 5 anos; a NF-e só referencia o romaneio (D4 da Fase 3) e cancelar não mexe no estoque.
7. **Venda mista (D4):** o equipamento sai em NF-e na expedição; a instalação, em NFS-e no aceite técnico; a parcela do contrato é só cobrança; a divisão vem do tipo do item do orçamento.
8. **Produção só no go-live:** SEFAZ e Inter em produção só na GOL-01b no tenant `konebras`; homologação SEFAZ e sandbox no `demo`; a produção exige Configurações → Fiscal conferida.

## Validação do PO (21/10/2026)
Primeira validação (21/10/2026): as 18 stories (com as GOL) com 10/10, GO condicional, todas `Draft`. Na revalidação depois das decisões do PO, as 16 stories da fase tiveram **10/10 e `Ready`**; os NO-GOs da revisão foram resolvidos: **FIS-01** pela ADR 0027 e pelas lacunas do Emissor como pré-condição externa (P-27), com o fake do `IFiscalEmitter` para o desenvolvimento; **FIS-03a** pela reconciliação no Emissor, pela D4 e pela remoção dos dois caminhos de baixa; **FIS-04b** pelas duplicatas no `CreateOpenInstallmentsAsync` na transação do recebimento (ADR 0024) com `purchasing.receive`; **FIS-06** pelo processo provisório da prefeitura até o provider de Caçapava (D5).
- **Pré-condições externas explícitas** (não bloqueiam): BNK-01a, BNK-01b, BNK-02 e BNK-03b (conta Inter com API, P-08, e conferências no sandbox; extratos reais para as regras); FIS-01, FIS-03a, FIS-03b e FIS-06 (lacunas do EmissorFiscal, P-27; e-CNPJ A1; P-02 para a NF-e de produção em produção); FIS-02a, FIS-02b e FIS-04b (conteúdo da contadora pré-configurado em Configurações → Fiscal, P-28).
- **Revalidação por amostragem do PO no fechamento (21/10/2026):** FIS-02a (valores pré-configurados, `ToConfirmChip` reutilizado da BNK-01b), FIS-03a (`ConfirmDialog` reutilizado, D4) e BNK-01a (filtro de 2FA de novo aplicado à FIN-03a) conferidas de novo; FND-16 ganhou o GET de verificação da Meta para a WPP-01.

## Pontos em aberto da Fase 5
Nenhum. Os pontos 1 a 73 da tabela 3.2 foram decididos em 21/10/2026 e estão nas stories; os pontos 74 a 81 estão nas GOL-01a/01b (marco final). Ficam as pré-condições externas acima.

---

# Fase 6 — RH e Patrimônio (fim do MVP)

Ver as [decisões do PO](#decisões-do-po-21102026-john-1) na Fase 4. A fase **começa pela folha** (Marco 6.2), que leva o maior custo da empresa ao caixa, e o ponto (Marco 6.1) vem depois, como **controle interno, sem ser registro oficial** (D15 e decisão do PO; ADR 0035). Os marcos mantêm a numeração da primeira validação; a execução é 6.2 → 6.1 → 6.3, com o 6.4 em paralelo.

## Índice
| Ordem | ID | Título | Tam. | Status | Requisitos | Depende de | Arquivo |
|---|---|---|---|---|---|---|---|
| 1 (6.2) | CAD-04c2 | Funcionários: dependentes, banco/Pix, adicionais, vale-transporte e documentos | M | Ready | R-RH-1, PRD §6, R-SEC-5, R-SEC-10 | CAD-04c1 | [CAD-04c2](CAD-04c2-funcionarios-complemento.md) |
| 2 (6.2) | RH-02a | Folha lançada: competência, rubricas, lançamento e importação dos valores da contadora | G | Done (pré-condição externa: contadora) | R-RH-5, R-RH-1, PRD §6, R-SEC-5, R-SEC-10, R-PRP-1 | CAD-04b, CAD-04c1, CAD-04c2, FND-09e, FND-07 | [RH-02a](RH-02a-folha-lancada-rubricas.md) |
| 3 (6.2) | RH-02c | Folha lançada: fechamento, títulos a pagar de salários e encargos e folha prevista no fluxo de caixa | G (re-estimada de M) | Done (pré-condição externa: contadora) | R-RH-5, R-FIN-4, R-FIN-10, R-FIN-13, R-PRP-2, R-PRP-7 | RH-02a, CAD-04c2, FIN-02, FIN-03a, FIN-05, CAD-05a, CMP-03a | [RH-02c](RH-02c-fechamento-folha-cp-caixa.md) |
| 4 (6.2) | RH-02b | Folha lançada: holerite em PDF, "Meus holerites" e aviso ao funcionário | M | Done (pré-condição externa: usuários dos funcionários) | R-RH-5, R-COM-1, R-COM-3, PRD §6, R-SEC-5, R-PRP-2 | RH-02a, RH-02c, CAD-04b, COM-03, ADR 0019, CAD-06, FND-08 | [RH-02b](RH-02b-holerite-pdf-envio.md) |
| 5 (6.2) | RH-03 | Exportação do RH para a contadora (layout padrão) | M | Done (pré-condição externa) | R-RH-8, PRD §6, R-SEC-5, R-PRP-3 | RH-02a, RH-02c, CAD-04b, CAD-04c1 | [RH-03](RH-03-exportacao-contadora.md) |
| 6 (6.1) | RH-01a | Ponto: marcação pelo app (Meu ponto), geolocalização opcional e consulta das marcações | M | Ready (pré-condição externa) | R-RH-4, PRD §6, R-SEC-5, R-PRP-1, R-PRP-2 | CAD-04a, CAD-04b, CAD-04c1, PRD-03a, PRD-03b, RH-02b, ADR 0035 | [RH-01a](RH-01a-marcacao-ponto.md) |
| 7 (6.1) | RH-01b | Ponto: lançamento manual, pedido de ajuste, abono de ausência e aprovação | M | Ready | R-RH-4, PRD §6, R-SEC-5, R-COM-2, R-COM-3, R-PRP-2 | RH-01a, CAD-04c1, DSH-04, FND-09e, FND-06c, FND-07 | [RH-01b](RH-01b-ajustes-ponto-aprovacao.md) |
| 8 (6.1) | RH-01c | Ponto: espelho mensal, banco de horas simples e fechamento do período | G | Ready (pré-condição externa) | R-RH-4, R-RH-5, R-RH-8, R-COM-3, PRD §6, R-SEC-5 | RH-01a, RH-01b, CAD-04a, CAD-04c1, CAD-03, COM-03, ADR 0022 | [RH-01c](RH-01c-espelho-banco-horas-fechamento.md) |
| 9 (6.3) | LGPD-01a | Direitos do titular: registro do pedido, inventário do dado pessoal e exportação dos dados (JSON e PDF) | M | Ready (pré-condição externa) | PRD §6, R-SEC-5, R-COM-3 | CAD-01a, CAD-04b, CAD-04c1, CAD-04c2, RH-01c, RH-02b, RH-02c | [LGPD-01a](LGPD-01a-pedido-titular-exportacao.md) |
| 10 (6.3) | LGPD-01b | Direitos do titular: anonimização e eliminação que preservam os documentos com movimento | M | Ready (pré-condição externa) | PRD §6, R-SEC-5, R-PRP-7 | LGPD-01a | [LGPD-01b](LGPD-01b-anonimizacao.md) |
| 11 (6.4) | PAT-01a | Patrimônio: cadastro de bens | M | Ready | R-PAT-1, R-COM-3, R-COM-4, R-SEC-5, R-PRP-1, R-PRP-3 | CAD-04b, CAD-03, CAD-01a, CAD-06, FIS-04a, FND-09d | [PAT-01a](PAT-01a-cadastro-bens.md) |
| 12 (6.4) | PAT-01b | Patrimônio: etiquetas com QR code em lote e leitura pelo app | M | Ready (pré-condição externa) | R-PAT-2, R-PRP-6, R-COM-3, R-SEC-5 | PAT-01a, COM-03, ADR 0022, PRD-03a | [PAT-01b](PAT-01b-etiqueta-qr-leitura.md) |
| 13 (6.4) | PAT-02 | Patrimônio: movimentação de bens e baixa | P | Ready | R-PAT-3, R-PRP-1, R-PRP-2, R-PRP-6, R-SEC-5 | PAT-01a, PAT-01b, FIN-01 | [PAT-02](PAT-02-movimentacao-bens.md) |
| 14 (6.4) | PAT-03a | Patrimônio: planos de manutenção preventiva e OS de manutenção | M | Ready (pré-condição externa) | R-PAT-4, R-COM-2, R-COM-3, R-COM-4, R-PRP-2, R-PRP-5 | PAT-01a, PAT-01b, PAT-02, DSH-04, FND-07, FND-09e | [PAT-03a](PAT-03a-planos-os-manutencao.md) |
| 15 (6.4) | PAT-03b | Patrimônio: custo da manutenção, CP do prestador e redução da capacidade | M | Ready (pré-condição externa) | R-PAT-4, R-PAT-5, R-FIN-4, R-FIN-10, R-PRD-9, R-PRP-7 | PAT-03a, FIN-02, FIN-03a, FIN-05, CAD-03, CAD-04a, PLN-01a | [PAT-03b](PAT-03b-custo-cp-capacidade.md) |

Total da Fase 6: 15 stories (P: 1 · M: 11 · G: 3), todas `Ready`; 11 com pré-condição externa explícita, que não bloqueia (inclui a CAD-04c2, vinda da Fase 3).

| Marco (ordem de execução) | Stories |
|---|---|
| 6.2 "Folha lançada" (primeiro) | CAD-04c2, RH-02a, RH-02c, RH-02b, RH-03 |
| 6.1 "Ponto" (controle interno) | RH-01a, RH-01b, RH-01c |
| 6.3 "LGPD" | LGPD-01a, LGPD-01b |
| 6.4 "Patrimônio" (em paralelo) | PAT-01a, PAT-01b, PAT-02, PAT-03a, PAT-03b |

## Quebras e re-estimativas
- **Quebras (primeira validação):** RH-01 → 01a/01b/01c; RH-02 → 02a/02b/02c; LGPD-01 → 01a/01b; PAT-01 → 01a/01b; PAT-03 → 03a/03b. Depreciação (R-PAT-6) continua na Fase 9.
- **Re-estimada (M → G):** RH-02c (favorecido funcionário no `finance.payables` e na OP da FIN-03a, leitura sensível da conta no pagamento, 2FA de novo e previsão por funcionário, ADR 0036).
- **Dependência retirada:** a RH-02a não depende mais da RH-01c (horas lançadas ou importadas; quando o ponto existir, "Usar horas do espelho", RH-01c CA 16).

## Padrões comuns da Fase 6
1. **Dado do RH é sensível por padrão:** salário, valores de folha, localização do ponto, dados bancários, dependentes e atestados só por rota `/sensitive` ou comando próprio com `hr.view_sensitive` (atestado com `hr.view_health`), com `SensitiveRead`, `no-store`, `[PersonalData]`, allowlist e canário (ADR 0016).
2. **Nada é apagado:** marcação de ponto é desconsiderada por ajuste aprovado; folha e período fechados só reabrem com motivo; movimento de bem é imutável; o bem é baixado, não excluído.
3. **Autoatendimento do funcionário (ADR 0035):** "Meu ponto", "Meu espelho" e "Meus holerites" em `/api/v1/me/hr/*`, liberadas pela camada 1 e pelo vínculo com funcionário ativo, sem ação no grupo; o funcionário vem sempre da sessão; no tablet, só os endpoints da lista `[PinSessionAllowed]` (ADR 0026, adendo) e marcar encerra a sessão por PIN; quem ganha `hr.view_sensitive` perde o PIN.
4. **Ponto como controle interno:** faixa "Controle interno de jornada…" na tela, no comprovante, no espelho e na ajuda; o registro oficial (REP-P / Portaria 671) é story futura (P-26).
5. **Folha no caixa (ADR 0036):** o fechamento chama o `IPayables` na mesma transação; um título por funcionário (`payee_type = Employee`) e um por guia (órgão como `Party`); conta de salário lida só no "Pagar" da OP; previsão `PayrollForecast` de 3 meses com 13º e férias; quem fecha (`hr.approve`) não paga (`finance.pay`); conta em quarentena fora do pagamento automático.
6. **Segregação:** quem pede ajuste não aprova; quem troca a conta de salário não a revisa; exceção do Proprietário único pelo `SoleOwnerRule`, com marca e notificação.
7. **Regra legal é parâmetro:** tolerâncias, banco de horas, rubricas, guias, vencimentos, layouts e retenção pré-configurados em Configurações (Parâmetros → RH, Privacidade) com o chip "A confirmar com a contadora".
8. **PDF pelo `IPdfRenderer`** sem CPF completo (máscara 3+2), salário ou dado que o documento não precisa; holerite só em "Meus holerites", e o e-mail só avisa, sem anexo.
9. **LGPD (ADR 0037):** inventário de onde há dado pessoal, cada lugar com `IPersonalDataProvider` ou expurgo; `PersonalDataTests` cobre as entidades novas; anonimização com confirmação digitada e 2FA de novo, uma transação por módulo, retomável.
10. **Patrimônio (ADR 0038):** `assets.sites`, OS de manutenção no Assets, custo com `assets.edit`, parada na capacidade pelo `IWorkCenterCapacity`; QR só com a URL da etiqueta (`/b/{etiqueta}`), leitura com login, foto sem EXIF/GPS.

## Validação do PO (21/10/2026)
Primeira validação: as 14 stories com 10/10 (PAT-02 com GO; as demais com GO condicional), todas `Draft`; a CAD-04c2 já era `Ready`. Na revalidação, as 15 stories tiveram **10/10 e `Ready`**; os NO-GOs foram resolvidos pela ADR 0036 (RH-02c, favorecido) e pela D15 (RH-02b, "Meus holerites" como canal).
- **Pré-condições externas explícitas** (não bloqueiam): RH-02a, RH-02c, RH-03, RH-01c, LGPD-01b e PAT-03b (conferência da contadora e do jurídico sobre os valores pré-configurados); RH-01a e RH-02b (usuário para cada funcionário, com e-mail de apelido quando preciso); LGPD-01a (prazo de 15 dias e dado de terceiros, jurídico); PAT-01b (impressora e etiqueta, P-13); PAT-03a (máquinas com plano e horímetro).
- **Revalidação por amostragem do PO no fechamento (21/10/2026):** RH-02c conferida de novo contra a ADR 0036 e a D6; `IBusinessCalendar` passou a ser criado na DSH-01a (Fase 4), com os métodos da folha acrescentados na RH-02a.

## Pontos em aberto da Fase 6
Nenhum. Os 58 pontos da tabela 3.3 foram decididos em 21/10/2026; o ponto 5 (uso legal do ponto) foi decidido pelo PO como controle interno, e o 23 (favorecido) pela opção do arquiteto.

---

# Fase 7 — Folha calculada

Ver as [decisões do PO](#decisões-do-po-21102026-john-1) na Fase 4. A Fase 7 entra no planejamento antes do go-live (D1 mudada). **Regra da fase:** as regras legais mudam todo ano; tabelas de INSS, IRRF, salário-família, salário mínimo, FGTS e encargos ficam **por vigência no banco**, pré-cadastradas com a legislação vigente e marcadas "A confirmar com a contadora" até a conferência. A calculada roda como simulação e só substitui a lançada depois de 3 competências seguidas aceitas na conferência paralela (FLH-06); essa virada é operação e **não segura o go-live**. eSocial, DCTFWeb e FGTS Digital ficam fora do produto (PRD §3.3).

## Índice
| Ordem | ID | Título | Tam. | Status | Requisitos | Depende de | Arquivo |
|---|---|---|---|---|---|---|---|
| 1 (7.1) | FLH-01 | Folha calculada: tabelas legais por vigência (INSS, IRRF, salário-família e parâmetros) | M | Ready (pré-condição externa) | R-RH-6, R-SEC-5, PRD §6 | RH-02a, RH-02c, CAD-06, ADR 0016, FND-09e | [FLH-01](FLH-01-tabelas-legais-vigencia.md) |
| 2 (7.1) | FLH-02a | Folha calculada: cálculo mensal (salário, adicionais, faltas, INSS, IRRF, FGTS, salário-família, VT) | G | Ready (pré-condição externa) | R-RH-6, R-RH-5, R-FIN-10, PRD §6, R-SEC-5, R-PRP-7 | FLH-01, RH-02a, RH-02b, RH-02c, ADR 0036, RH-01c, CAD-04c2 | [FLH-02a](FLH-02a-calculo-mensal.md) |
| 3 (7.1) | FLH-02b | Folha calculada: horas extras, adicional noturno e DSR a partir do ponto | M | Ready (pré-condição externa) | R-RH-6, R-RH-4, R-SEC-5 | FLH-02a, RH-01c, FLH-01, CAD-03 | [FLH-02b](FLH-02b-horas-extras-dsr.md) |
| 4 (7.2) | FLH-03 | Folha calculada: férias (períodos aquisitivos, programação, cálculo e recibo) | G | Ready (pré-condição externa) | R-RH-6, R-RH-4, R-RH-5, R-FIN-10, R-PRP-5, R-SEC-5 | FLH-02a, FLH-02b, FLH-01, RH-01c, RH-02b, ADR 0036, CAD-04b | [FLH-03](FLH-03-ferias.md) |
| 5 (7.2) | FLH-04 | Folha calculada: 13º salário (1ª e 2ª parcelas) | M | Ready (pré-condição externa) | R-RH-6, R-RH-5, R-FIN-10, R-SEC-5 | FLH-02a, FLH-01, FLH-03, RH-02b, ADR 0036 | [FLH-04](FLH-04-decimo-terceiro.md) |
| 6 (7.2) | FLH-05 | Folha calculada: rescisão | G | Ready (pré-condição externa) | R-RH-6, R-RH-3, R-RH-5, R-FIN-10, R-PRP-2, R-PRP-7 | FLH-02a, FLH-03, FLH-04, FLH-01, CAD-04b, RH-01c, RH-02b | [FLH-05](FLH-05-rescisao.md) |
| 7 (7.3) | FLH-06 | Folha calculada: conferência paralela com a contadora e virada | M | Ready (pré-condição externa) | R-RH-6, R-SEC-5, R-PRP-2 | FLH-02a, FLH-02b, RH-02a, RH-03, FLH-03, FLH-05 | [FLH-06](FLH-06-conferencia-paralela.md) |

Total da Fase 7: 7 stories (M: 4 · G: 3), todas `Ready`; 7 com pré-condição externa explícita, que não bloqueia.

## Ordem de execução
```
7.1 Tabelas e cálculo mensal:  FLH-01 → FLH-02a → FLH-02b
7.2 Férias, 13º e rescisão:    FLH-03 → FLH-04 → FLH-05
7.3 Virada:                    FLH-06 (começa depois da FLH-02b e corre em paralelo ao 7.2; a virada exige 3 competências seguidas aceitas)
```

## Quebras
- **FLH-02** → **02a** (cálculo mensal, G) e **02b** (horas extras e DSR do ponto, M). Holerite e fechamento reaproveitam a RH-02b e a RH-02c.

## Padrões comuns da Fase 7
1. **Motor puro** (`PayrollCalculator`, Domain, sem banco nem log de valores), cada regra legal uma função testada por tabela de casos. **O CI roda com dataset sintético** tirado da legislação vigente (nunca holerite real no Git, ADR 0039); **a conferência da contadora com o dataset de referência dela é pré-condição da virada (FLH-06)**, não do `Ready`.
2. **Determinismo (ADR 0039):** `calculator_version`, ids das tabelas e dos parâmetros e retrato das entradas por holerite; folha fechada nunca é recalculada; retificação nasce `Draft` e passa por nova conferência.
3. **Memória de cálculo** em toda linha (tabela, vigência e entradas usadas).
4. **Simulação até a virada:** a calculada não fecha como oficial antes da FLH-06; parâmetro mudado com `settings.edit` aparece como aviso no fechamento.
5. **Valores só com `hr.view_sensitive`**, canário em log e em argumentos de job; `parallel_differences` com `[PersonalData]`.

## Validação do PO (21/10/2026)
Primeira validação: as 7 stories com 10/10, GO condicional, `Draft` (dependiam dos datasets da contadora). Na revalidação, com o dataset sintético no CI e a conferência da contadora como pré-condição da virada, as 7 tiveram **10/10 e `Ready (pré-condição externa)`**.
- **Pré-condição externa** de todas: a contadora confere as tabelas e os parâmetros pré-cadastrados e entrega o dataset de referência antes da virada (FLH-06), que ela assina.

## Pontos em aberto da Fase 7
Nenhum. Os 28 pontos da tabela 3.4 e a linha "Determinismo" foram decididos em 21/10/2026.

---

# Fase 8 — Cliente e comunicação

Ver as [decisões do PO](#decisões-do-po-21102026-john-1) na Fase 4. O portal do cliente é a quarta superfície do front, em **host próprio** (ADR 0040; no `konebras`, `portal.konebras.com.br`), com identidade separada do backoffice e isolamento estrito por cliente: o cliente vê etapas, documentos compartilhados, parcelas e boletos, nunca custo, margem ou dado interno.

## Índice
| Ordem | ID | Título | Tam. | Status | Requisitos | Depende de | Arquivo |
|---|---|---|---|---|---|---|---|
| 1 (8.1) | POR-01a | Portal do cliente: acesso separado do backoffice e isolamento por cliente | G | Ready (pré-condição externa) | PRD §5.15, PRD §2, R-SEC-1, R-SEC-2, R-SEC-3, R-SEC-5 | FND-02, FND-03b, FND-04, FND-05, FND-06, FND-08, FND-11 | [POR-01a](POR-01a-acesso-portal-cliente.md) |
| 2 (8.2) | POR-01b | Portal do cliente: projetos, etapa, previsão de entrega e documentos | M | Ready | PRD §5.15, R-PRJ-4, R-COM-3, R-SEC-5, PRD §6 | POR-01a, COM-04a, COM-05, COM-06, FND-07, FND-08, PLN-01a | [POR-01b](POR-01b-portal-projetos-documentos.md) |
| 3 (8.2) | POR-01c | Portal do cliente: parcelas e boletos | M | Ready | PRD §5.15, R-FIN-3, R-FIN-7, R-SEC-5, PRD §6 | POR-01a, POR-01b, FIN-01, COM-04b, BNK-01b, BNK-02 | [POR-01c](POR-01c-portal-parcelas-boletos.md) |
| 4 (8.3) | POR-02 | Portal do cliente: aceite online do orçamento | M | Ready | R-COML-10, R-COML-4, R-COML-3, R-COM-2, R-PRP-2, R-PRP-7 | POR-01a, POR-01b, COM-02, COM-03, COM-04a, FND-08 | [POR-02](POR-02-aprovacao-online-orcamento.md) |
| 5 (8.4) | WPP-01 | Notificações por WhatsApp (cobrança e status do projeto) | G | Ready (pré-condição externa) | R-FIN-9, PRD §3.2, PRD §6, R-SEC-5 | FIN-09, POR-01a, POR-01b, POR-01c, CAD-01a, CAD-03, FND-16 | [WPP-01](WPP-01-notificacoes-whatsapp.md) |

Total da Fase 8: 5 stories (M: 3 · G: 2), todas `Ready`; 2 com pré-condição externa explícita, que não bloqueia.

## Ordem de execução
```
8.1 Acesso do cliente:   POR-01a
8.2 Acompanhamento:      POR-01b → POR-01c
8.3 Aceite online:       POR-02
8.4 Comunicação:         WPP-01 (bloqueia a GOL-01a)
```

## Quebras
- **POR-01** → **01a** (identidade e isolamento, G), **01b** (projetos e documentos, M) e **01c** (parcelas e boletos, M).

## Padrões comuns da Fase 8
1. **Identidade do portal separada (ADR 0040):** chave de assinatura própria, claim de tipo de token, validação estrita de `aud` nos dois esquemas; token de um lado dá 401 no outro (teste sobre todos os endpoints); refresh com detecção de reuso; trocar o e-mail do contato invalida o acesso.
2. **`party_id` sempre da sessão;** um contato vê um cliente só; rota com id de outro cliente responde 404 uniforme (teste com dois clientes e dois tenants).
3. **DTOs do portal por allowlist** e contratos `ForPortal*` nos módulos donos; teste de arquitetura sem custo, margem, orçado, observação interna ou dado de funcionário.
4. **Compartilhar é explícito:** documento só aparece marcado em `portal.shared_documents` (NF-e do próprio cliente compartilhada por padrão).
5. **O sistema sugere, não decide:** o aceite do cliente só avisa o Comercial e pré-preenche a aprovação interna (ADR 0005 D5); código por e-mail obrigatório no aceite, com versão, hash do PDF congelado no envio, IP, user-agent e recibo.
6. **Mensagens externas (ADR 0041):** WhatsApp pela API oficial da Meta por um parceiro, opt-in pelo próprio contato (trocar o número derruba o opt-in), mensagem só com o aviso e o link do portal, dias úteis das 8 h às 18 h.
7. **Kit Portal** (padrão 5 das Fases 4 a 8).

## Validação do PO (21/10/2026)
Primeira validação: as 5 stories com 10/10 (POR-01c GO; as demais GO condicional), `Draft`. Na revalidação, as 5 tiveram **10/10 e `Ready`**; os NO-GOs foram resolvidos pela ADR 0040 (POR-01a) e pela ADR 0041 (WPP-01).
- **Pré-condições externas explícitas:** POR-01a (DNS do host do portal pela Konebras); WPP-01 (contrato com o parceiro, número verificado e modelos aprovados na Meta, P-29).
- **Ajustes em stories já entregues, feitos dentro das stories da fase:** POR-02 grava `pdf_sha256` no envio da COM-03; POR-01c acrescenta "Segunda via pelo portal" em Configurações → Cobrança (BNK-01b).
- **Revalidação por amostragem do PO no fechamento (21/10/2026):** POR-01a conferida de novo contra a ADR 0040 (host próprio).

## Pontos em aberto da Fase 8
Nenhum. Os 21 pontos da tabela 3.5 foram decididos em 21/10/2026 (D16 pela opção da segurança).

---

# Marco final — Go-live

As GOL-01a e GOL-01b são as últimas stories do planejamento (D1 mudada pelo PO em 21/10/2026; ADR 0034). A GOL-01a exige **Fases 0 a 8 concluídas** e o PR de reforço da D19.

## Índice
| Ordem | ID | Título | Tam. | Status | Requisitos | Depende de | Arquivo |
|---|---|---|---|---|---|---|---|
| 1 (Go-live) | GOL-01a | Go-live Konebras: critérios de entrada, ambiente de produção novo, runbook e ensaio completo da virada | G (re-estimada de M) | Ready (pré-condição externa) | PRD §7, PRD §1.2, PRD §6, R-PLT-2, R-TER-5, R-SEC-3 | Fases 0 a 8 concluídas | [GOL-01a](GOL-01a-preparacao-ensaio-virada.md) |
| 2 (Go-live) | GOL-01b | Go-live Konebras: a virada, o corte do Dolibarr e a primeira semana | M | Ready (pré-condição externa) | PRD §7, PRD §1.2, R-PLT-2, R-TER-5, R-FIS-2, R-FIN-7 | GOL-01a | [GOL-01b](GOL-01b-virada-primeira-semana.md) |

Total: 2 stories (M: 1 · G: 1), todas `Ready`; 2 com pré-condição externa explícita, que não bloqueia.

## Checklist de go-live (resumo; detalhe na GOL-01a e na GOL-01b)
Critérios de entrada com evidência e "Go" do PO (Fases 0 a 8 concluídas; P-02; P-19 e P-20 aplicadas; contagem física do estoque com saldo inicial; usuários do chão de fábrica, P-24; folha do mês anterior pela RH-02c; Configurações → Fiscal sem campo crítico "A confirmar com a contadora"; certificado A1 válido no cofre do EmissorFiscal; lacunas do Emissor e provider de Caçapava entregues; Inter de produção testado no D−1; linha de base da métrica "desvio da data prevista ≤ 15%") → **ambiente de produção novo** (catálogo novo só com o `konebras`, key ring, certificado do Data Protection e segredos novos) → **restauração e rollback ensaiados numa cópia, obrigatórios** → D−1 backups extras → congelamento do Dolibarr, carga final só dos cadastros alterados → `DROP USER` com revogação na rede, `DOLAPIKEY` regenerada, Dolibarr em `adm.` só leitura com allowlist → **todas as famílias de refresh revogadas e security stamp de todos subido**; convites, redefinições, PINs, aparelhos e quiosques expirados → webhooks e Inter de sandbox para produção → primeira nota e primeiro boleto reais com o PO; envio de WhatsApp aos clientes ligado → rollback possível até o fim do primeiro dia útil → primeira semana acompanhada → Dolibarr desligado depois do primeiro fechamento ou em até 3 meses, backup por 5 anos. A virada da folha calculada (FLH-06) é operação e não segura o go-live.

## Validação do PO (21/10/2026)
As 2 stories tiveram **10/10 e `Ready (pré-condição externa)`**: a trilha externa da D2 com dono e data (contadora, Konebras, Inter, EmissorFiscal) e a janela da virada no início de um mês. Os pontos 74 a 81 da tabela 3.2 foram decididos (D1 mudada, D7). **Revalidação por amostragem do PO no fechamento:** GOL-01a conferida de novo (FLH-06 fora do go-live; WhatsApp ligado na GOL-01b).

## Pré-condições do planejamento (fora das stories)
1. **Trilha externa da D2**, aberta já, com dono e data para cada item: P-01, P-02 e P-03 com a contadora; certificado A1 e conta Inter com API (P-08) com a Konebras; EmissorFiscal implantado em homologação (`demo`).
2. **Lacunas do EmissorFiscal** (D3, P-27), no repositório dele: reconciliação com a SEFAZ, DANFE em PDF, `transp`, IBS/CBS, escopo `nfe:eventos`, timestamp no HMAC, rede `bqe-fiscal` e as demais da ADR 0027; provider da NFS-e de Caçapava antes da FIS-06.
3. **PR de reforço do código no ar (D19):** `KnownProxies` só com o IP do `stack-nginx`; apagar o modelo do e-mail de convite e de redefinição depois do envio; teste de arquitetura da lista versionada de rotas anônimas; idempotência com o usuário no escopo (D17 c da Fase 3) e expurgo de 30 dias da `idempotency_records`.
4. **Protótipos antes dos marcos (D18):** nota de saída (rascunho, emissão, rejeitada) e conciliação antes do Marco 5.4; kit Portal antes da Fase 8.
5. **Riscos de migração (§4.2):** `payables.party_id` anulável é expansão (código N-1 tolerante); conferir linhas em `kiosk_tokens` antes de trocar a rota da TV; `commercial.projects.priority` e `planned_*` das OS de fabricação só são apagados com ADR, na contração; expurgos e anonimização apagam dado de propósito (ADRs 0031 e 0037).

## Rastreabilidade da revisão pré-Fases 4 a 8
| Item da revisão | Onde entra |
|---|---|
| D1 (mudada: tudo antes do go-live) | Ordem acima; marco final "Go-live"; ADR 0034; GOL-01a (Fases 0 a 8 concluídas) |
| D2 (trilha externa) | Pré-condições do planejamento; GOL-01a |
| D3 (lacunas do EmissorFiscal) | ADR 0027; P-27; FIS-01, FIS-03a, FIS-03b, FIS-06 |
| D4 (venda mista e receita) | FIS-03a, FIS-06, FIN-08, DSH-01a, DSH-01b; P-17 |
| D5 (NFS-e) | FIS-06 (processo provisório até o provider de Caçapava) |
| D6 (P-19 e P-20) | ADR 0008 adendo; BNK-01a (filtro e FIN-03a), BNK-02, FIS-02a, FIS-03b, CAD-04c2, RH-02c, LGPD-01b, WPP-01 |
| D7 (critérios da GOL) | GOL-01a, GOL-01b; ADR 0034 |
| D8 (custo e imposto no projeto) | ADR 0025 adendo; DSH-02, FIS-03a, FIS-03b |
| D9 (painel do dono) | ADR 0028; DSH-01a, DSH-01b, DSH-03, DSH-04, FIN-08 |
| D10 (planejamento) | ADR 0029; PLN-01a, PLN-01b, PLN-02, PLN-04, PAT-03b |
| D11 (TV) | ADR 0030; PNL-01a, PNL-01b; `02-arquitetura` §2.1 e §4 |
| D12 (fiscal) | ADRs 0027, 0003 e 0008 adendos; FIS-02a, FIS-03a, FIS-03b, FIS-04a, FIS-04b, FIS-05 |
| D13 (Inter) | ADRs 0031 e 0032; FND-16, BNK-01a, BNK-01b, BNK-02, BNK-03a, BNK-03b |
| D14 (folha, opção do arquiteto) | ADR 0036; CAD-04c2, RH-02a, RH-02c |
| D15 (ponto como controle interno) | ADR 0035, ADR 0026 adendo; RH-01a, RH-01b, RH-01c, RH-02b; P-26 |
| D16 (portal, opção da segurança) | ADR 0040, ADR 0009 adendo; POR-01a, POR-01b, POR-01c, POR-02 |
| D17 (WhatsApp) | ADR 0041; WPP-01; FND-16 (GET de verificação) |
| D18 (componentes e protótipos) | Padrões comuns das Fases 4 a 8 (1 a 7); FIN-07, DSH-02, FIN-08, PLN-03, PNL-01a, BNK-01a, BNK-01b |
| D19 (reforço do código) | Pré-condições do planejamento, item 3 |
| Novo requisito (Configurações) | ADR 0042; BNK-01a, BNK-01b, FIN-09, FIS-02a, FIS-02b, WPP-01, POR-01c; P-28 |
| Tabelas 3.1 a 3.5 | "Pontos em aberto" de cada story, como decididos |
| §4.1 (decisões da Fase 3) | CAD-04c1/CAD-04c2 e PRD-06a1/PRD-06a2 nas dependências; FIS-03a e FIS-03b sem os dois caminhos de baixa; FIS-04b; DSH-02; DSH-03 e DSH-04 com `production.approve_quality`; PNL-01b, PLN-05, PLN-02; FND-16 |
| §4.2 (arquitetura) | ADRs 0029, 0031, 0032, 0035, 0037, 0038; FND-16, BNK-01a, RH-01a, RH-01c, LGPD-01a, PAT-03b |
| §4.3 (segurança) | CAs **[Segurança]** em todas as stories das Fases 4 a 8 |
| §4.4 (design) | Padrões comuns das Fases 4 a 8 e achados pontuais |
| §5 (ADRs) | ADRs 0027 a 0042 e adendos às 0003, 0006, 0007, 0008, 0009, 0025 e 0026 |
| §6 (ordem) | Substituída pela ordem da D1 mudada |
