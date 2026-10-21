# Stories — Fase 0 (Fundação), Fase 1 (Cadastros e migração) e Fase 2 (Comercial, Projetos e Financeiro núcleo)

A Fase 1 está na [segunda parte deste arquivo](#fase-1--cadastros-e-migração) e a Fase 2 na [terceira](#fase-2--comercial-projetos-e-financeiro-núcleo).

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
| Fase 3 | CAD-04c | Funcionários: dependentes, banco/Pix, adicionais, VT, afastamentos e documentos | M | Draft | R-RH-1, R-RH-3, PRD §6 LGPD | CAD-04b | [CAD-04c](CAD-04c-funcionarios-complemento.md) |
| — | ~~MIG-01d~~ | ~~Saldos iniciais de estoque~~ | — | **Cancelada pelo PO** (18/10/2026) | — | — | arquivo apagado |
| — | ~~MIG-01e~~ | ~~Títulos em aberto e histórico de pagamentos~~ | — | **Cancelada pelo PO** (18/10/2026) | — | — | arquivo apagado |

Total da Fase 1: 13 stories (M: 11 · G: 2), todas `Ready`. Antes dela, a FND-09d (Fase 0, G, `Ready`). Depois, a CAD-04c (início da Fase 3, `Draft`).

## Ordem de execução (D1)
```
FND-09d (Fase 0)
Marco A — terceiros reais:  CAD-01a → MIG-01a → CAD-05b → MIG-01b → CAD-01b
Marco B — catálogo real:    CAD-06 → CAD-03 → CAD-02a → CAD-02b → MIG-01c
Marco C — base de custo:    CAD-05a → CAD-04a → CAD-04b
Início da Fase 3:           CAD-04c
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
- **`Draft` (GO condicional):** CAD-04c, que depende de duas decisões de produto antes da Fase 3 (formato do vale-transporte e tipos de afastamento).
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
1. **CAD-04c** (antes da Fase 3): formato do vale-transporte e lista de tipos de afastamento.
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


# Fase 6 — RH e Patrimônio (fim do MVP)

Stories detalhadas da Fase 6, uma por arquivo, escritas pelo @sm (River) e validadas pelo @po (Pax) em 21/10/2026. Fonte: `docs/01` a `06`, ADRs 0001–0022 (atenção à 0003, `hr.view_sensitive`; à 0016, dado sensível por rota; e à 0018, LGPD), o código das Fases 0 a 2 (`HR` com funcionários, cargos, jornadas, sindicatos e custo-hora; `IEmployees`, `IHourlyCost`, `IUserEmployeeLink`; auditoria com `[PersonalData]`/`[ProtectedSecret]` e `SensitiveRead`; anexos com ClamAV e regras por dono; `ModuleCatalog` e `GroupSeeder`; `IPdfRenderer`; e-mail por fila; `IPayables` com `DocumentOrigin.Payroll` e `Maintenance`) e as stories em rascunho da Fase 3 (CAD-04c, PRD-03a/03b, PRD-04). A Definition of Ready, a legenda e a escala de tamanho são as mesmas das fases anteriores. **Todas as stories estão `Draft`**: as que têm pontos em aberto marcados como pré-condição só passam a `Ready` depois da decisão do PO.

## Índice
| Ordem | ID | Título | Tam. | Status | Requisitos | Depende de | Arquivo |
|---|---|---|---|---|---|---|---|
| 1 (6.1) | RH-01a | Ponto: marcação pelo app, geolocalização opcional e consulta | M | Draft | R-RH-4, PRD §6 LGPD | CAD-04b, CAD-04c, PRD-03a (PRD-03b opcional) | [RH-01a](RH-01a-marcacao-ponto.md) |
| 2 (6.1) | RH-01b | Ponto: lançamento manual, pedido de ajuste, abono e aprovação | M | Draft | R-RH-4, R-COM-2, R-COM-3 | RH-01a | [RH-01b](RH-01b-ajustes-ponto-aprovacao.md) |
| 3 (6.1) | RH-01c | Ponto: espelho mensal, banco de horas e fechamento | G | Draft | R-RH-4, R-RH-5 | RH-01b, CAD-03, COM-03 | [RH-01c](RH-01c-espelho-banco-horas-fechamento.md) |
| 4 (6.2) | RH-02a | Folha lançada: competência, rubricas, lançamento e importação | G | Draft | R-RH-5, R-RH-1 | CAD-04c, RH-01c | [RH-02a](RH-02a-folha-lancada-rubricas.md) |
| 5 (6.2) | RH-02c | Folha lançada: fechamento, CP de salários e encargos, folha prevista | M | Draft | R-RH-5, R-FIN-4, R-FIN-10 | RH-02a, FIN-02, FIN-03a, FIN-05 | [RH-02c](RH-02c-fechamento-folha-cp-caixa.md) |
| 6 (6.2) | RH-02b | Folha lançada: holerite em PDF e envio | M | Draft | R-RH-5, R-COM-1 | RH-02a, RH-02c, COM-03 | [RH-02b](RH-02b-holerite-pdf-envio.md) |
| 7 (6.2) | RH-03 | Exportação do RH para a contadora (D) | M | Draft | R-RH-8, P-06 | RH-01c, RH-02c | [RH-03](RH-03-exportacao-contadora.md) |
| 8 (6.3) | LGPD-01a | Direitos do titular: pedido e exportação (JSON e PDF) | M | Draft | PRD §6 LGPD, ADR 0018 | RH-02c, CAD-01a, CAD-04c | [LGPD-01a](LGPD-01a-pedido-titular-exportacao.md) |
| 9 (6.3) | LGPD-01b | Direitos do titular: anonimização | M | Draft | PRD §6 LGPD, ADR 0018 | LGPD-01a | [LGPD-01b](LGPD-01b-anonimizacao.md) |
| 10 (6.4) | PAT-01a | Patrimônio: cadastro de bens | M | Draft | R-PAT-1, R-COM-4 | CAD-04b, CAD-03 | [PAT-01a](PAT-01a-cadastro-bens.md) |
| 11 (6.4) | PAT-01b | Patrimônio: etiquetas QR em lote e leitura pelo app | M | Draft | R-PAT-2 | PAT-01a, COM-03, PRD-03a | [PAT-01b](PAT-01b-etiqueta-qr-leitura.md) |
| 12 (6.4) | PAT-02 | Patrimônio: movimentação e baixa | P | Draft | R-PAT-3 | PAT-01a | [PAT-02](PAT-02-movimentacao-bens.md) |
| 13 (6.4) | PAT-03a | Patrimônio: planos preventivos e OS de manutenção | M | Draft | R-PAT-4 | PAT-01a | [PAT-03a](PAT-03a-planos-os-manutencao.md) |
| 14 (6.4) | PAT-03b | Patrimônio: custo, CP do prestador e capacidade | M | Draft | R-PAT-4, R-PAT-5 (D), R-PRD-9 (D) | PAT-03a, FIN-02, FIN-03a; PLN-01 (Fase 4) | [PAT-03b](PAT-03b-custo-cp-capacidade.md) |

Total da Fase 6: 14 stories (P: 1 · M: 11 · G: 2), todas `Draft`.

## Ordem de execução
```
6.1 Ponto:            RH-01a → RH-01b → RH-01c
6.2 Folha lançada:    RH-02a → RH-02c → RH-02b → RH-03 (D)
6.3 LGPD:             LGPD-01a → LGPD-01b
6.4 Patrimônio:       PAT-01a → PAT-01b → PAT-02 → PAT-03a → PAT-03b   (em paralelo aos marcos 6.1 a 6.3)
```

Por que esta ordem:
- **6.1 primeiro:** o ponto fechado é a base de horas da folha lançada (referência) e da calculada (Fase 7); marcação, ajuste e espelho dependem um do outro em sequência.
- **6.2 com a RH-02c antes da RH-02b:** o holerite só é enviado com a folha fechada, e o fechamento é o que leva a folha ao caixa (o objetivo do produto); a exportação para a contadora (D) fecha o marco.
- **6.3 depois do RH:** a exportação e a anonimização do titular precisam conhecer todo dado pessoal que o RH passou a guardar (ponto, geolocalização, folha, atestados), como pede a ADR 0018 ("junto com o RH-01/RH-02").
- **6.4 em paralelo:** o patrimônio só depende de cadastros já prontos (funcionários, centros, terceiros); a PAT-03b só mostra efeito na capacidade depois da PLN-01 (Fase 4).

## Quebras e re-estimativas
- **RH-01 (G)** → **01a** (marcação, M), **01b** (ajustes e aprovação, M) e **01c** (espelho, banco de horas e fechamento, G).
- **RH-02 (G)** → **02a** (lançamento e importação, G), **02b** (holerite e envio, M) e **02c** (fechamento, CP e previsão de caixa, M).
- **LGPD-01 (M)** → **01a** (pedido e exportação, M) e **01b** (anonimização, M): juntas passavam de G.
- **PAT-01 (M)** → **01a** (cadastro, M) e **01b** (etiqueta QR e leitura, M).
- **PAT-03 (M)** → **03a** (planos e OS, M) e **03b** (custo, CP do prestador e capacidade, M).
- **Depreciação** (R-PAT-6) não entrou: é P e Fase 9 no PRD; antecipá-la é ponto em aberto (PAT-01a).

## Padrões comuns da Fase 6
Valem além dos padrões das fases anteriores. Cada story só repete o que é específico dela.
1. **Dado do RH é sensível por padrão:** salário, valores de folha, localização do ponto, dados bancários, dependentes e atestados só por rota `/sensitive` ou comando próprio com `hr.view_sensitive`, com `SensitiveRead`, `no-store`, `[PersonalData]`, allowlist e canário (ADR 0016); DTOs de lista e resumo sem esses campos (teste de arquitetura).
2. **Nada é apagado:** marcação de ponto é desconsiderada por ajuste aprovado; folha e período fechados só reabrem com motivo; movimento de bem é imutável; o bem é baixado, não excluído.
3. **Autoatendimento do funcionário** ("Meu ponto", "Meu espelho"): o funcionário vem sempre da sessão (`users.employee_id`), nunca do corpo; a regra de permissão é a pré-condição da RH-01a.
4. **Segregação de função:** quem pede ajuste não aprova; quem cadastra tabela não confere (Fase 7); exceção só do Proprietário único, como a D6 do Financeiro, sempre como ponto em aberto.
5. **Folha no caixa:** o HR publica `PayrollClosed` sem valor; o Finance lê pelo contrato do HR e cria os títulos com origem `Payroll`; a previsão dos próximos meses entra como título `Planned` estimado e é substituída pela folha real; pagamento só por OP aprovada.
6. **Regra legal é parâmetro:** tolerâncias, banco de horas, rubricas, encargos e layouts vêm de parâmetro do módulo dono (`hr.parameters`, ADR 0016 M6) confirmado pela contadora; nenhum número legal fixo no código.
7. **PDF pelo `IPdfRenderer`** (espelho, holerite, etiqueta, exportação do titular), sem CPF completo, salário ou dado que o documento não precisa, conferido por extração de texto.
8. **Patrimônio:** primeiras permissões do módulo `assets` por seed patch (proposta); QR com só a URL da etiqueta e leitura com login; custo e CP do prestador pelo padrão da COM-06; capacidade pelo evento `MaintenanceScheduled` consumido pelo Production.
9. **Navegação:** área Pessoas (RH: Funcionários, Ponto, Folha, Rubricas, Tabelas da folha, Exportar para a contadora; "Meu ponto" para todos os ligados a funcionário); área Fábrica (Patrimônio: Bens, Manutenção, Custo, Locais); Configurações → Privacidade (só Proprietário). Cada story declara a entrada no `modules.ts` e a ajuda de cada rota e aba.

## Validação do PO (21/10/2026)
O @po (Pax) validou as 14 stories com a checklist de 10 itens das fases anteriores. **Todas tiveram 10/10.** Vereditos:
- **GO** (sem pré-condição de decisão): PAT-02.
- **GO condicional:** RH-01a, RH-01b, RH-01c, RH-02a, RH-02b, RH-02c, RH-03, LGPD-01a, LGPD-01b, PAT-01a, PAT-01b, PAT-03a, PAT-03b.
- **Correções da validação cruzada:** a RH-02b passou para depois da RH-02c (envio só com a folha fechada); a LGPD-01a passou a depender da RH-02c (a exportação cobre ponto e folha); o CA 6 da LGPD-01a deixou de ter uma pergunta no texto (virou o ponto em aberto 33); a PAT-03b ficou com a PLN-01 (Fase 4) como dependência do efeito na capacidade.
- **ADRs sugeridas antes dos Marcos 6.1 e 6.2:** autoatendimento do funcionário (ponto 1) e favorecido do título de salário (ponto 23).

## Dependências de outras fases ainda abertas
- **Fase 3 (rascunho em revisão):** CAD-04c `Draft` (vale-transporte e tipos de afastamento: alimentam RH-01b, RH-02a e a Fase 7); PRD-03a/03b `Draft` (superfície `/fab`, aparelho registrado e PIN, operador sem e-mail, funcionário do usuário por `Identity.Contracts`); PRD-04 (custo de mão de obra pelo apontamento, que a folha não substitui).
- **Fase 4 (ainda só no backlog):** PLN-01 (consome a redução de capacidade da PAT-03b e as férias da FLH-03); DSH-04 Minha pauta (detectores de ajuste de ponto, preventiva vencida, férias a vencer).
- **Fase 5 (ainda só no backlog):** FIS-04 (vínculo do bem com a NF de entrada, PAT-01a).
- **Pendência P-01** (regime tributário, aberta): encargos da empresa (RH-02a/02c e Fase 7).

## Pontos em aberto da Fase 6
Pendências do `06` aplicadas: P-05 (% de encargos, só para o custo-hora, já decidido), P-06 (layout padrão, RH-03), P-13 (impressora de etiquetas, pré-condição externa da PAT-01b), P-21 (QuestPDF Community). Pontos por story (a proposta está em cada arquivo; nenhum foi decidido; **(pré)** = pré-condição para `Ready`):

| # | Story | Ponto |
|---|---|---|
| 1 | RH-01a | Permissão do autoatendimento "Meu ponto" (pré, ADR) |
| 2 | RH-01a | Geolocalização: padrão, precisão, ciência e retenção |
| 3 | RH-01a | Ponto sem rede (fila offline ou recusa) |
| 4 | RH-01a | Intervalo mínimo entre marcações e retenção de "Minhas marcações" |
| 5 | RH-01a | Uso legal do ponto básico como registro oficial (contadora/jurídico) |
| 6 | RH-01b | Lançamento manual do RH também exige aprovação (pré) |
| 7 | RH-01b | Tipos de abono, alinhados aos afastamentos da CAD-04c (pré) |
| 8 | RH-01b | Proprietário único aprova ajuste das próprias marcações |
| 9 | RH-01b | Retenção do documento do abono (atestado) |
| 10 | RH-01c | Tolerância de marcação (pré, contadora) |
| 11 | RH-01c | Regras do banco de horas (pré, PO + contadora) |
| 12 | RH-01c | Período de apuração (pré, contadora) |
| 13 | RH-01c | Quem fecha o período de ponto |
| 14 | RH-01c | Assinatura do espelho |
| 15 | RH-02a | Lista padrão de rubricas (pré, contadora) |
| 16 | RH-02a | Tipos de folha lançada no MVP |
| 17 | RH-02a | Encargos da empresa: guias e vencimentos (pré, contadora) |
| 18 | RH-02a | Layout de importação dos valores da contadora (pré) |
| 19 | RH-02a | Limite do alerta de variação do líquido |
| 20 | RH-02b | Proteção do holerite no e-mail (pré, PO + jurídico) |
| 21 | RH-02b | Holerite no "Meu ponto" (autoatendimento) |
| 22 | RH-02b | Máscara do CPF no holerite |
| 23 | RH-02c | Favorecido do título de salário: o `IPayables` exige terceiro (pré, ADR) |
| 24 | RH-02c | Conta e centro de custo da folha (pré, contadora) |
| 25 | RH-02c | Quem fecha a folha (`hr.approve` muda o significado de "aprovar" no RH) (pré) |
| 26 | RH-02c | Vencimento dos salários e encargos |
| 27 | RH-02c | Meses da folha prevista; inclui 13º e férias |
| 28 | RH-03 | Colunas do layout padrão para a contadora (pré) |
| 29 | RH-03 | Guardar ou não o arquivo exportado |
| 30 | RH-03 | Biblioteca de XLSX |
| 31 | LGPD-01a | Tela em Configurações → Privacidade, só Proprietário |
| 32 | LGPD-01a | Prazo de resposta ao titular (pré, jurídico) |
| 33 | LGPD-01a | Dado de terceiros (dependentes) na exportação (pré, jurídico) |
| 34 | LGPD-01a | Usuários do Identity como titulares na tela |
| 35 | LGPD-01a | Prazo de guarda do arquivo exportado |
| 36 | LGPD-01b | Tabela de retenção por campo (pré, contadora + jurídico) |
| 37 | LGPD-01b | Eliminação atendida por anonimização |
| 38 | LGPD-01b | Anonimização automática no fim da retenção |
| 39 | LGPD-01b | Transação única ou por módulo |
| 40 | PAT-01a | Permissões padrão do `assets` (pré) |
| 41 | PAT-01a | Local do bem: cadastro próprio ou locais de estoque (pré) |
| 42 | PAT-01a | Quem vê o valor de aquisição |
| 43 | PAT-01a | Depreciação gerencial: Fase 9 no PRD, antecipar? (ADR) |
| 44 | PAT-01a | Inventário físico do patrimônio |
| 45 | PAT-01b | Modelo da impressora e tamanho da etiqueta (pré externa, P-13) |
| 46 | PAT-01b | Formato da URL curta e registro da leitura |
| 47 | PAT-01b | Bibliotecas de QR (pré, arquiteto) |
| 48 | PAT-02 | Venda do bem gera título a receber? |
| 49 | PAT-02 | Termo de responsabilidade |
| 50 | PAT-03a | Dono da OS de manutenção: Assets × `production_orders` tipo Maintenance (pré) |
| 51 | PAT-03a | Plano por horas: horímetro digitado ou uso do centro (pré) |
| 52 | PAT-03a | Base do próximo vencimento |
| 53 | PAT-03a | Baixa de peças do estoque |
| 54 | PAT-03a | Quem abre OS de manutenção (inclui operador) |
| 55 | PAT-03b | Conta e centro de custo da manutenção (pré, contadora) |
| 56 | PAT-03b | Quem vê o custo de manutenção (pré) |
| 57 | PAT-03b | Quanto a máquina parada reduz da capacidade (pré) |
| 58 | PAT-03b | Paradas que não são manutenção |

---


# Fase 7 — Folha calculada

Stories detalhadas da Fase 7, escritas pelo @sm (River) e validadas pelo @po (Pax) em 21/10/2026, com as mesmas fontes da Fase 6. **Regra da fase:** as regras legais brasileiras mudam todo ano; tabelas de INSS, IRRF, salário-família, salário mínimo, FGTS e encargos ficam **por vigência no banco**, conferidas pela contadora, nunca no código; o que é decisão de negócio ou jurídica está nos pontos em aberto, sem decidir. A folha calculada só substitui a lançada depois de 3 competências seguidas aceitas na conferência paralela (`06` Riscos). eSocial, DCTFWeb e FGTS Digital ficam fora do produto (PRD §3.3). Todas as stories estão `Draft`.

## Índice
| Ordem | ID | Título | Tam. | Status | Requisitos | Depende de | Arquivo |
|---|---|---|---|---|---|---|---|
| 1 (7.1) | FLH-01 | Tabelas legais por vigência | M | Draft | R-RH-6 | Fase 6 | [FLH-01](FLH-01-tabelas-legais-vigencia.md) |
| 2 (7.1) | FLH-02a | Cálculo mensal (salário, adicionais, faltas, INSS, IRRF, FGTS, salário-família, VT) | G | Draft | R-RH-6, R-RH-5 | FLH-01, RH-02a/02b/02c, RH-01c, CAD-04c | [FLH-02a](FLH-02a-calculo-mensal.md) |
| 3 (7.1) | FLH-02b | Horas extras, adicional noturno e DSR do ponto | M | Draft | R-RH-6, R-RH-4 | FLH-02a, RH-01c | [FLH-02b](FLH-02b-horas-extras-dsr.md) |
| 4 (7.2) | FLH-03 | Férias | G | Draft | R-RH-6 | FLH-02b | [FLH-03](FLH-03-ferias.md) |
| 5 (7.2) | FLH-04 | 13º salário | M | Draft | R-RH-6, R-FIN-10 | FLH-03 | [FLH-04](FLH-04-decimo-terceiro.md) |
| 6 (7.2) | FLH-05 | Rescisão | G | Draft | R-RH-6, R-RH-3 | FLH-03, FLH-04 | [FLH-05](FLH-05-rescisao.md) |
| 7 (7.3) | FLH-06 | Conferência paralela e virada | M | Draft | R-RH-6, `06` Riscos | FLH-02b | [FLH-06](FLH-06-conferencia-paralela.md) |

Total da Fase 7: 7 stories (M: 4 · G: 3), todas `Draft`.

## Ordem de execução
```
7.1 Tabelas e cálculo mensal:  FLH-01 → FLH-02a → FLH-02b
7.2 Férias, 13º e rescisão:    FLH-03 → FLH-04 → FLH-05
7.3 Virada:                    FLH-06 (começa depois da FLH-02b e corre em paralelo ao 7.2; a virada exige 3 competências seguidas aceitas)
```

Por que esta ordem: as tabelas vêm antes de qualquer cálculo; o mensal com as horas do ponto é o que a conferência paralela compara todo mês; férias alimentam o 13º (adiantamento) e a rescisão usa os dois.

## Quebras
- **FLH-02 (maior que G)** → **02a** (cálculo mensal, G) e **02b** (horas extras e DSR do ponto, M). As demais seguem o backlog (FLH-01, 03, 04, 05, 06).
- **Holerite e fechamento** não ganharam story própria: a folha calculada reaproveita a RH-02b e a RH-02c.

## Padrões comuns da Fase 7
1. **Motor puro** (`PayrollCalculator`, Domain, sem banco nem log de valores), cada regra legal uma função testada por **tabela de casos fornecida pela contadora**; os datasets de referência são pré-condição de `Ready` e rodam no CI, centavo a centavo.
2. **Memória de cálculo** em toda linha (tabela, vigência e entradas usadas).
3. **Simulação até a virada:** a calculada não fecha como oficial antes da FLH-06.
4. **Valores só com `hr.view_sensitive`**, canário em log e em argumentos de job, como na Fase 6.

## Validação do PO (21/10/2026)
As 7 stories tiveram 10/10 na checklist de 10 itens. Veredito: **GO condicional** para todas (todas dependem de regras e datasets da contadora). Correção da validação cruzada: a FLH-04 passou a depender da FLH-03 (adiantamento do 13º nas férias) e a FLH-05 das duas.

## Dependências de outras fases ainda abertas
- **Fase 6** inteira (esta fase reaproveita rubricas, folha, holerite, fechamento e ponto).
- **CAD-04c** (Fase 3, `Draft`): dependentes de IR e salário-família, adicionais, vale-transporte e afastamentos.
- **P-01** (regime tributário, aberta): encargos patronais.
- **PLN-01** (Fase 4): férias programadas fora da capacidade (FLH-03 CA 4).

## Pontos em aberto da Fase 7
| # | Story | Ponto |
|---|---|---|
| 1 | FLH-01 | Valores iniciais das tabelas conferidos e casos de teste da contadora (pré) |
| 2 | FLH-01 | Formato da redução adicional do IRRF vigente (pré) |
| 3 | FLH-01 | Encargos da empresa pelo regime (P-01) |
| 4 | FLH-01 | Segregação na conferência da tabela e exceção do Proprietário único |
| 5 | FLH-01 | Tabela sugerida pela plataforma × cadastro por tenant |
| 6 | FLH-01 | Regras de arredondamento de cada imposto |
| 7 | FLH-02a | Folha oficial durante a conferência: calculada só simulação (pré) |
| 8 | FLH-02a | Regras parametrizadas: proporcionalidade, base da insalubridade, acúmulo, valor da hora (pré) |
| 9 | FLH-02a | Afastamento por doença e acidente: quem paga cada período |
| 10 | FLH-02a | Elegibilidade do salário-família |
| 11 | FLH-02a | Pensão alimentícia e consignado |
| 12 | FLH-02a | Encargos patronais pelo regime (P-01) |
| 13 | FLH-02b | Percentuais de horas extras e adicional noturno (pré, contadora + convenção) |
| 14 | FLH-02b | Fórmula do DSR e perda do DSR por falta (pré) |
| 15 | FLH-02b | Pagamento do banco de horas vencido |
| 16 | FLH-03 | Regras de férias: faltas, afastamentos, fracionamento, abono, prazos (pré) |
| 17 | FLH-03 | INSS e IRRF das férias (pré) |
| 18 | FLH-03 | Férias coletivas |
| 19 | FLH-03 | Antecedência do alerta de vencimento |
| 20 | FLH-04 | Regras do 13º: avos, médias, prazos, FGTS e tributação (pré) |
| 21 | FLH-04 | Previsão do 13º no caixa desde janeiro |
| 22 | FLH-05 | Matriz de verbas por tipo de desligamento, aviso prévio e prazo (pré) |
| 23 | FLH-05 | Saldo do FGTS para a multa (pré) |
| 24 | FLH-05 | Termo de rescisão: só conferência ou modelo oficial |
| 25 | FLH-06 | Critério de aceite da competência (pré) |
| 26 | FLH-06 | Quem da contadora assina a virada |
| 27 | FLH-06 | O período de 3 meses precisa incluir férias, 13º ou rescisão? |
| 28 | Fase | eSocial, DCTFWeb e FGTS Digital: fora do produto (PRD §3.3), "avaliar após a folha calculada" |

---


# Fase 8 — Cliente e comunicação

Stories detalhadas da Fase 8, escritas pelo @sm (River) e validadas pelo @po (Pax) em 21/10/2026. O portal do cliente é a quarta superfície do front (`02-arquitetura` §4, `/portal`), com **público externo, autenticação separada do backoffice e isolamento estrito por cliente**: o cliente vê etapas, documentos compartilhados, parcelas e boletos, nunca custo, margem ou dado interno. Todas as stories estão `Draft`.

## Índice
| Ordem | ID | Título | Tam. | Status | Requisitos | Depende de | Arquivo |
|---|---|---|---|---|---|---|---|
| 1 (8.1) | POR-01a | Portal: acesso separado e isolamento por cliente | G | Draft | PRD §5.15, R-SEC-1/2/3/5/11 | Fases 0–6, CAD-01a, COM-04a | [POR-01a](POR-01a-acesso-portal-cliente.md) |
| 2 (8.2) | POR-01b | Portal: projetos, etapa, entrega e documentos | M | Draft | PRD §5.15, R-PRJ-4, R-COM-3 | POR-01a, COM-05, COM-06; PLN-01, FIS-03 | [POR-01b](POR-01b-portal-projetos-documentos.md) |
| 3 (8.2) | POR-01c | Portal: parcelas e boletos | M | Draft | PRD §5.15, R-FIN-3, R-FIN-7 | POR-01b, FIN-01; BNK-01, BNK-02 | [POR-01c](POR-01c-portal-parcelas-boletos.md) |
| 4 (8.3) | POR-02 | Portal: aceite online do orçamento | M | Draft | R-COML-10 | POR-01b, COM-02, COM-03, COM-04a | [POR-02](POR-02-aprovacao-online-orcamento.md) |
| 5 (8.4) | WPP-01 | Notificações por WhatsApp | G | Draft | Backlog WPP-01, R-FIN-9 | POR-01c, FIN-09, BNK-01 | [WPP-01](WPP-01-notificacoes-whatsapp.md) |

Total da Fase 8: 5 stories (M: 3 · G: 2), todas `Draft`.

## Ordem de execução
```
8.1 Acesso do cliente:   POR-01a
8.2 Acompanhamento:      POR-01b → POR-01c
8.3 Aceite online:       POR-02
8.4 Comunicação:         WPP-01
```

## Quebras
- **POR-01 (maior que G)** → **01a** (identidade e isolamento, G), **01b** (projetos e documentos, M) e **01c** (parcelas e boletos, M).

## Padrões comuns da Fase 8
1. **Identidade do portal separada:** usuários em `portal.portal_users` ligados a um contato de um terceiro Cliente; `aud`, claim e cookie próprios; token de um lado responde 401 no outro (teste sobre todos os endpoints).
2. **`party_id` sempre da sessão**, nunca de parâmetro; toda rota com id de outro cliente responde 404 uniforme (teste com dois clientes e dois tenants).
3. **DTOs do portal por allowlist** e contratos `ForPortal*` nos módulos donos; teste de arquitetura sem custo, margem, orçado, observação interna ou dado de funcionário.
4. **Nada compartilhado por padrão:** documento só aparece no portal marcado pelo backoffice (proposta: NF-e do próprio cliente por padrão).
5. **O sistema sugere, não decide:** o aceite do cliente pré-preenche a aprovação interna, que continua gerando o contrato (ADR 0005 D5).
6. **Mensagens externas** só por modelo aprovado, com consentimento registrado e webhook com assinatura.

## Validação do PO (21/10/2026)
As 5 stories tiveram 10/10. Vereditos: **GO** para a POR-01c (sem decisão pendente; depende da BNK-01/02 para os boletos); **GO condicional** para POR-01a, POR-01b, POR-02 e WPP-01. Correção da validação cruzada: a POR-02 deixou de gerar contrato sozinha (ponto 14), para não contrariar a ADR 0005 D5.

## Dependências de outras fases ainda abertas
- **Fase 4 (só no backlog):** PLN-01 (data prevista de entrega, POR-01b).
- **Fase 5 (só no backlog):** FIS-03 (NF-e e DANFE no portal), BNK-01 e BNK-02 (boleto e baixa), FIN-09 (régua de cobrança, D, base do WhatsApp de cobrança).
- **LGPD-01a** (Fase 6): o usuário do portal entra no `IPersonalDataProvider`.

## Pontos em aberto da Fase 8
| # | Story | Ponto |
|---|---|---|
| 1 | POR-01a | ADR do portal: identidade separada, rotas anônimas, `aud` e cookies (pré) |
| 2 | POR-01a | Módulo `portal` no `ModuleCatalog` (não existe hoje) e seed patch do Comercial (pré) |
| 3 | POR-01a | Host do portal: `/portal` no host do tenant ou host próprio (pré) |
| 4 | POR-01a | 2FA do portal opcional ou obrigatório |
| 5 | POR-01a | Um contato ver mais de um cliente (grupo econômico) |
| 6 | POR-01b | Qual data de entrega o cliente vê (prometida, prevista) (pré) |
| 7 | POR-01b | NF-e compartilhada por padrão |
| 8 | POR-01b | Onde guardar a marca "Compartilhar no portal" |
| 9 | POR-01b | Aviso por e-mail quando a etapa muda |
| 10 | POR-01b | Endereço da obra completo ou só cidade e bairro |
| 11 | POR-01c | Mostrar parcelas previstas sem data |
| 12 | POR-01c | Segunda via do boleto pelo próprio cliente |
| 13 | POR-01c | Período do histórico financeiro |
| 14 | POR-02 | Aceite do cliente só avisa ou gera o contrato (muda a ADR 0005 D5) (pré) |
| 15 | POR-02 | Assinatura eletrônica com validade jurídica |
| 16 | POR-02 | Link de aceite sem login (rota anônima, ADR) |
| 17 | WPP-01 | Provedor do WhatsApp e quem paga (pré) |
| 18 | WPP-01 | Base legal e forma do opt-in (pré, jurídico) |
| 19 | WPP-01 | Textos dos modelos aprovados no provedor |
| 20 | WPP-01 | Horário permitido para envio |
| 21 | WPP-01 | Sem a FIN-09, só a mensagem de etapa |
