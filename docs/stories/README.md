# Stories — Fases 0 a 8

A Fase 1 está na [segunda parte deste arquivo](#fase-1--cadastros-e-migração), a Fase 2 na [terceira](#fase-2--comercial-projetos-e-financeiro-núcleo), a Fase 3 na [quarta](#fase-3--engenharia-estoque-compras-e-produção) e as Fases 4 a 8 nas seguintes (rascunho, ainda sem a revisão pré-fase).

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
| 1 (3.1) | ENG-01 | Templates de roteiro | M | Ready | R-ENG-1, R-ENG-2, R-PRP-1, R-PLN-1 | CAD-03, CAD-02a | [ENG-01](ENG-01-templates-roteiro.md) |
| 2 (3.1) | PRD-01 | Geração da OS de fabricação | G | Ready | R-PRD-1, R-PRJ-2, R-ENG-1, R-PRP-1, R-COM-4 | ENG-01, COM-04a, COM-06 | [PRD-01](PRD-01-geracao-os-fabricacao.md) |
| 3 (3.1) | PRD-02 | Kanban da produção | M | Ready | R-PRD-2, R-COM-2, PRD §5.14, PRD §6 | PRD-01, FND-02 | [PRD-02](PRD-02-kanban.md) |
| 4 (3.1) | CAD-04c1 | Funcionários: afastamentos e situação | P | Ready | R-RH-3, PRD §6 LGPD, R-SEC-5, R-SEC-10 | CAD-04b | [CAD-04c1](CAD-04c1-afastamentos-situacao.md) |
| 5 (3.1) | PRD-03a | App do operador: fila, apontamento e checklist | G | Ready | R-PRD-3, R-PRD-6, PRD §6 Usabilidade | PRD-01, PRD-02, CAD-04c1 | [PRD-03a](PRD-03a-pwa-fila-apontamento.md) |
| 6 (3.1) | PRD-04 | Custo de mão de obra e custo real do projeto | M (re-estimada de P) | Ready | R-PRD-4, R-RH-2, R-PRJ-5 | PRD-03a, CAD-04a, COM-05; ADR 0025 | [PRD-04](PRD-04-custo-mao-de-obra.md) |
| 7 (3.1) | PRD-05 | Qualidade e não conformidade | M | Ready | R-PRD-6, R-COM-2, R-COM-3, R-COM-4 | PRD-03a | [PRD-05](PRD-05-qualidade-nao-conformidade.md) |
| 8 (3.1) | PRD-06a1 | Conclusão da OS e fabricação concluída do projeto | P | Ready | R-PRD-7, R-PRJ-6, R-COM-4 | PRD-05, PRD-04, COM-04b | [PRD-06a1](PRD-06a1-conclusao-os.md) |
| 9 (3.1) | PRD-03b | App do operador: aparelho registrado e PIN | M | Ready (pré-condição externa) | R-PRD-3, R-SEC-1, R-SEC-3, R-SEC-7 | PRD-03a, PRD-05; ADR 0026 | [PRD-03b](PRD-03b-pwa-pin-aparelho.md) |
| 10 (3.2) | EST-01a | Saldos, movimentos e custo médio | G | Ready (pré-condição externa) | R-EST-4, R-EST-5, R-EST-6, R-EST-7, R-EST-8 | CAD-02a, CAD-02b, CAD-03, CAD-06, FND-09e; ADR 0023 | [EST-01a](EST-01a-saldos-movimentos-custo-medio.md) |
| 11 (3.2) | ENG-02 | BOM da OS | M | Ready | R-ENG-3, R-ENG-4, R-ENG-5, R-PRP-2 | PRD-01, PRD-03a, EST-01a | [ENG-02](ENG-02-bom-da-os.md) |
| 12 (3.2) | EST-02 | Reservas e disponível | M | Ready | R-EST-4, R-ENG-4, R-ENG-5, R-CMP-1 | ENG-02, EST-01a, PRD-01 | [EST-02](EST-02-reservas-disponivel.md) |
| 13 (3.2) | EST-03 | Requisição de material para a OS e devolução | M | Ready | R-PRD-5, R-EST-4, R-EST-6, R-PRJ-5 | EST-02, PRD-03a, PRD-04 | [EST-03](EST-03-requisicao-material-os.md) |
| 14 (3.3) | CMP-01 | Solicitação de compra | M | Ready | R-CMP-1, R-CMP-6, R-COM-2, R-COM-4 | EST-02 | [CMP-01](CMP-01-solicitacao-compra.md) |
| 15 (3.3) | CMP-03a | Pedido de compra: criação, aprovação por alçada e CP previsto | G | Ready (pré-condição externa) | R-CMP-3, R-CMP-4, R-CMP-6, R-FIN-4, R-FIN-5, R-FIN-10 | CMP-01, FIN-03b, FIN-02, FIN-05; ADR 0024 | [CMP-03a](CMP-03a-pedido-compra-alcada-cp.md) |
| 16 (3.3) | CMP-04 | Recebimento manual contra o pedido | G | Ready (pré-condição externa) | R-CMP-5, R-EST-6, R-EST-7, R-EST-8, R-FIN-4 | CMP-03a, EST-01a, EST-02 | [CMP-04](CMP-04-recebimento-manual.md) |
| 17 (3.3) | CMP-02 | Cotação | M | Ready | R-CMP-2, R-CMP-6, R-COM-4, R-PRP-1 | CMP-01, CMP-03a | [CMP-02](CMP-02-cotacao.md) |
| 18 (3.3) | CMP-03b | PDF e envio do pedido de compra ao fornecedor | P | Ready | R-CMP-3, R-COM-1, R-COM-3 | CMP-03a, COM-03 | [CMP-03b](CMP-03b-pdf-envio-pedido.md) |
| 19 (3.4) | PRD-06a2 | Produto acabado: entrada no estoque, séries e lotes consumidos | M | Ready (pré-condição externa) | R-PRD-7, R-EST-8, R-EST-7 | PRD-06a1, EST-03, EST-02, EST-01a | [PRD-06a2](PRD-06a2-produto-acabado.md) |
| 20 (3.4) | PRD-06b | Expedição: romaneio e saída do produto acabado | M | Ready | R-PRD-8, R-EST-6, R-EST-8 | PRD-06a2, COM-03 | [PRD-06b](PRD-06b-expedicao-romaneio.md) |
| 21 (3.4) | EST-04 | Lote, série e rastreabilidade | M | Ready | R-EST-8, R-PRD-7, R-PRJ-4 | CMP-04, EST-03, PRD-06a2, PRD-06b | [EST-04](EST-04-lote-serie-rastreabilidade.md) |
| 22 (3.4) | EST-01b | Ajuste de estoque com motivo e aprovação | M | Ready | R-EST-6, R-EST-5, R-EST-7, R-SEC-10 | EST-01a, EST-02 | [EST-01b](EST-01b-ajuste-estoque-aprovacao.md) |
| 23 (3.4) | EST-05 | Inventário (contagem por local) | M | Ready | R-EST-9, R-EST-6 | EST-01b, EST-02 | [EST-05](EST-05-inventario.md) |
| 24 (3.4) | EST-06 | Capacidade de produzir (D, desejável; pode ir à Fase 4) | P | Ready | R-EST-10 | ENG-02, EST-02, CMP-01 | [EST-06](EST-06-capacidade-de-produzir.md) |
| Fase 6 | CAD-04c2 | Funcionários: dependentes, banco/Pix, adicionais, vale-transporte e documentos | M | Ready | R-RH-1, PRD §6 LGPD | CAD-04c1 | [CAD-04c2](CAD-04c2-funcionarios-complemento.md) |

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

Stories detalhadas da Fase 4, uma por arquivo, escritas pelo @sm (River) e validadas pelo @po (Pax) em 21/10/2026. Fonte: `docs/01` a `06`, ADRs 0001–0022, a revisão pré-Fase 0 §3 (quiosque), as stories da Fase 3 em rascunho (branch `docs/fase3-stories`: OS de fabricação, etapas, apontamentos, `project_costs`, `ProductionHub`, faltas, compras), o código das Fases 0 a 2 (`ModuleCatalog` com `dashboard` e `kiosk`, `GroupSeeder`, `IProjects`, `IContracts`, `IReceivables`, `IPayables`, `ICustomerPaymentStats`, `CashFlowService`, `IWorkCenterCapacity`, `IInstallationOrders`, `PingHub`, `INotificationService`, `Chart` só de linhas, `StageIndicator`, `TvLayout` em `/tv/$token`) e o protótipo (Painel, TV). A Definition of Ready, a legenda e a escala de tamanho são as mesmas das fases anteriores. **Todas as stories estão `Draft`**: a Fase 4 só começa com a Fase 3 concluída (regra do backlog), e as que têm pontos em aberto marcados como pré-condição só passam a `Ready` depois da decisão do PO.

## Índice
| Ordem | ID | Título | Tam. | Status | Requisitos | Depende de | Arquivo |
|---|---|---|---|---|---|---|---|
| 1 (4.1) | FIN-07 | Projeção de caixa ajustada pelo histórico do cliente | M | Draft | R-FIN-11, R-FIN-10, R-TER-4, PRD §5.14 | FIN-05, FIN-06b | [FIN-07](FIN-07-projecao-ajustada.md) |
| 2 (4.1) | DSH-02 | Orçado × realizado e margem, por projeto e consolidado | M | Draft | R-PRJ-5, PRD §5.14, PRD §1.2 | COM-05, COM-07, EST-03, PRD-04, CMP-03a | [DSH-02](DSH-02-orcado-realizado-margem.md) |
| 3 (4.1) | FIN-08 | DRE gerencial mensal e por projeto | G (M → G) | Draft | R-FIN-13, R-FIN-1, PRD §5.14 | CAD-05a, FIN-01, FIN-02, FIN-04, DSH-02 | [FIN-08](FIN-08-dre-gerencial.md) |
| 4 (4.2) | DSH-04 | Minha pauta | M | Draft | R-PRP-4, R-PRP-5, R-COM-2 | FIN-03b, COM-07, stories da Fase 3 (por fonte) | [DSH-04](DSH-04-minha-pauta.md) |
| 5 (4.2) | DSH-01a | Painel do dono: estrutura, caixa, faturamento, inadimplência, comercial e pendências | G | Draft | PRD §5.14, R-FIN-10 a 13, R-COML-9, R-PRP-4 | FIN-07, FIN-08, FIN-06a, COM-08, DSH-04 | [DSH-01a](DSH-01a-painel-dono-financeiro-comercial.md) |
| 6 (4.3) | PLN-01a | Motor de agendamento finito | G | Draft | R-PLN-3, R-PLN-1, PRD §6 | PRD-01, PRD-03a, PRD-05, CAD-03 | [PLN-01a](PLN-01a-motor-agendamento.md) |
| 7 (4.3) | PLN-01b | Recálculo por evento, data prevista do projeto e risco | M | Draft | R-PLN-3, R-PLN-4, R-COM-2 | PLN-01a, COM-05, COM-07, PRD-02 | [PLN-01b](PLN-01b-recalculo-previsao-risco.md) |
| 8 (4.3) | PLN-02 | Fila de prioridade dos projetos | M | Draft | R-PLN-2, R-PLN-3, R-PLN-4 | PLN-01a, PLN-01b | [PLN-02](PLN-02-fila-prioridade.md) |
| 9 (4.3) | PLN-04 | Ocupação da fábrica por semana e centro | M | Draft | R-PLN-6, R-PLN-1, PRD §5.14 | PLN-01a | [PLN-04](PLN-04-ocupacao-fabrica.md) |
| 10 (4.3) | PLN-03 | Cronograma (Gantt) por projeto e por centro | G | Draft | R-PLN-4, R-PLN-3 | PLN-01a, PLN-01b, PLN-02 | [PLN-03](PLN-03-gantt.md) |
| 11 (4.4) | PNL-01a | Tokens de tela, sessão de quiosque e tempo real da TV | G | Draft | R-SEC-6, PRD §5.14, PRD §6 | PRD-02, FND-02, FND-04, FND-09b | [PNL-01a](PNL-01a-tokens-tela-sessao-quiosque.md) |
| 12 (4.4) | PNL-01b | Visões do painel da TV | M | Draft | PRD §5.14, R-PRD-2, R-PLN-4, PRD §6 | PNL-01a, PRD-03a, PLN-01b, PLN-02 | [PNL-01b](PNL-01b-visoes-painel-tv.md) |
| 13 (4.4) | DSH-03 | Painel do supervisor | M | Draft | PRD §5.14, R-PRD-2, R-PRD-6, R-PLN-6, R-EST-4, R-CMP-1 | PRD-02, PLN-04, PRD-05, EST-02, CMP-01 | [DSH-03](DSH-03-painel-supervisor.md) |
| 14 (4.5) | DSH-01b | Painel do dono: margem, carteira, produção e entregas | M | Draft | PRD §5.14, R-PRJ-5, R-PLN-4, R-PLN-6 | DSH-01a, DSH-02, PLN-01b, PLN-04 | [DSH-01b](DSH-01b-painel-dono-margem-producao.md) |
| 15 (4.5) | PLN-05 | Simulação de prazo do orçamento (D) | M | Draft | R-PLN-5, R-COML-1 | PLN-01a, PLN-01b, PLN-02, COM-01a | [PLN-05](PLN-05-simulacao-prazo-orcamento.md) |

Total da Fase 4: 15 stories (M: 10 · G: 5), todas `Draft`.

## Ordem de execução
```
4.1 O dinheiro do jeito que ele vai acontecer:  FIN-07 → DSH-02 → FIN-08
4.2 O painel do dono, parte do dinheiro:         DSH-04 → DSH-01a
4.3 Datas que a fábrica cumpre:                  PLN-01a → PLN-01b → PLN-02 → PLN-04 → PLN-03
4.4 A fábrica na parede:                         PNL-01a → PNL-01b → DSH-03
4.5 A visão completa do dono:                    DSH-01b → PLN-05 (D)
```

Por que esta ordem:
- **Visibilidade financeira primeiro (4.1 e 4.2):** é o objetivo central do produto. A FIN-07 só depende da Fase 2 (fluxo base e pontualidade); o DSH-02 fecha o orçado × realizado com o material e as horas da Fase 3; a FIN-08 usa o realizado do DSH-02 na visão por projeto. A DSH-04 vem antes da DSH-01a porque as pendências do dono são categorias da pauta.
- **4.3:** o motor (PLN-01a) é a base de tudo o que é data; a PLN-01b leva a data prevista ao projeto e troca a regra provisória de risco da PRD-02; a fila (PLN-02) precisa da simulação do motor; a ocupação e o cronograma só leem o plano.
- **4.4:** a sessão de quiosque e o hub (PNL-01a) vêm antes das visões (PNL-01b), que usam a previsão e a fila; o painel do supervisor compõe Kanban, ocupação, qualidade e faltas.
- **4.5:** a DSH-01b precisa da margem (DSH-02), da previsão (PLN-01b) e da ocupação (PLN-04); a PLN-05 é **D** no PRD.

## Quebras e re-estimativas
- **PLN-01** → **01a** (motor, gravação, recálculo manual e por hora, G) e **01b** (gatilhos por evento, data prevista do projeto, risco e aviso, M): o motor sozinho já é G pelos testes de cenário e de desempenho.
- **PNL-01** → **01a** (tokens, sessão de quiosque, esquema de autenticação, hub, revogação e layout base, G) e **01b** (visões, rotação e < 2 s, M): a parte de segurança da rota anônima pesava sobre as visões.
- **DSH-01** → **01a** (estrutura, cache e cards do Financeiro e do Comercial, G) e **01b** (margem, carteira, produção e entregas, M): a 01a entrega o painel do dono no Marco 4.2, sem esperar o planejamento.
- **Re-estimada:** FIN-08 (M → G: duas visões, mapeamento de juros e descontos das baixas, drill-down por célula e o resumo para o painel).

## Padrões comuns da Fase 4
Valem além dos padrões das fases anteriores. Cada story só repete o que é específico dela.
1. **Cada número tem regra e origem:** todo indicador mostra a regra em `<Dica>` ("Como calculamos") e leva ao relatório ou documento de origem; o painel nunca recalcula: chama o mesmo contrato que o relatório usa, com teste que compara os dois.
2. **Um cálculo, um lugar:** `ICashForecast` (FIN-07), `IRevenueSummary` (FIN-08), `IProjectMargins` (DSH-02), `IProductionSchedule` (PLN-01a), `IFactoryLoad` (PLN-04), `IPlanningQueue` (PLN-02), `IAgendaService` (DSH-04).
3. **Sigilo de custo (D7):** custo, orçado e margem só com `projects.view_cost`; DTO separado e teste de arquitetura em toda lista, card e painel novo; a TV e a simulação do orçamento nunca levam valores.
4. **Card pela permissão do dono:** abrir o painel exige `dashboard.view`; cada card ou bloco exige a permissão do módulo dono do dado e não é chamado sem ela.
5. **Consultas agregadas:** no módulo dono (ou composição no Host, ponto 18), sempre com parâmetros e ordenação por allowlist; metas: painel completo < 2 s, card < 1,5 s, relatórios < 1–2 s com 50 mil títulos, 5 mil projetos, 200 mil custos e 300 OS; materialized view só se a medição pedir (ADR 0006).
6. **Cache:** só de agregados por tenant, nunca de resposta por usuário; TTL de 60 s com invalidação pelos eventos que mudam o número; "Atualizado às" e "Atualizar"; teste com dois tenants.
7. **Plano finito, um escritor:** só o `ScheduleService` grava datas previstas e alocações, uma execução por vez por tenant, pedidos agrupados; o motor é puro e determinístico; o plano não tem efeito financeiro, fiscal nem de estoque.
8. **Tempo real:** grupos SignalR derivados das claims (`{tenantId}:production`, `{tenantId}:kiosk:{id}`), hub sem método de entrada em grupo, mensagem só com id ou sinal, push depois do commit, polling de 60 s como reserva.
9. **Quiosque:** token de 256 bits só como hash, mostrado uma vez, nunca no caminho nem na query (iria para o log do nginx), trocado por cookie `__Host-bq_kiosk`; principal de quiosque só em GET do painel; revogação em menos de 1 minuto; sem dado pessoal além do primeiro nome do operador.
10. **Eventos fora de ordem:** quem publica previsão leva `computedAt` e o consumidor só aplica o mais novo (PLN-01b).
11. **Gráficos:** linhas pelo `Chart`; barras pelo `ColumnChart` (PLN-04) e cronograma pelo `Gantt` (PLN-03), componentes próprios com tokens `--chart-*`, sem cor literal e com tabela equivalente acessível.
12. **Parâmetros novos** no schema do módulo dono, editados em Configurações → Parâmetros com `settings.edit` (ADR 0016 M6): `finance.adjusted_default_threshold_pct`, contas do DRE, `finance.revenue_targets`, `planning.installation_days`.
13. **Navegação:** Visão geral → Painel do proprietário (`/app/painel`); Financeiro → DRE; Comercial → Projetos → Orçado × realizado; Fábrica → Produção → Planejamento (Fila, Cronograma, Ocupação) e Painel do supervisor; Telas dedicadas → Painel da TV (`/app/painel-tv`) e a superfície `/tv`; Minha pauta no Início.

## Validação do PO (21/10/2026)
O @po (Pax) validou as 15 stories com a checklist de 10 itens das fases anteriores. **Todas tiveram 10/10.** Vereditos:
- **GO** (sem pré-condição de decisão): PLN-03, DSH-03.
- **GO condicional** (pontos em aberto marcados como pré-condição): FIN-07, DSH-02, FIN-08, DSH-04, DSH-01a, DSH-01b, PLN-01a, PLN-01b, PLN-02, PLN-04, PNL-01a, PNL-01b, PLN-05.
- **Correções da validação cruzada:** a DSH-03 deixou de depender da DSH-04 (nenhum CA a usava); a PLN-01b passou a dizer como o Commercial resolve os destinatários do aviso de risco (síncrono no consumidor, ADR 0019 §18); os números de ocupação da PLN-04 foram conferidos com o cenário da PLN-01a (na semana atual, só os dias a partir de hoje).
- **ADRs sugeridas:** antes do Marco 4.2, onde ficam as consultas do painel (Host × módulo `Reporting` da ADR 0006); antes do Marco 4.3, dono da fila de prioridade (`production.planning_queue` × `commercial.projects.priority`) e datas previstas fora da auditoria; antes do Marco 4.4, caminho e sessão da TV (`/tv#token` com cookie, atualizando o `02-arquitetura` §4).

## Dependências da Fase 3 ainda em aberto
Pontos da Fase 3 (numeração do README da branch `docs/fase3-stories`) que a Fase 4 recebe sem decisão:

| Ponto da Fase 3 | Story da Fase 3 | Afeta | Efeito |
|---|---|---|---|
| 41 e 59 (contar a compra para o projeto duas vezes) | CMP-03a, EST-03 | DSH-02, FIN-08, DSH-01b | fonte do realizado de material (pré-condição da DSH-02) |
| 39 (conta e natureza do título do pedido) | CMP-03a | FIN-08 | compra como custo no DRE ou estoque fora do DRE |
| 38 e 40 (alçada do pedido por `Finance.Contracts`; um título por parcela) | CMP-03a | DSH-04, FIN-07 | categoria "pedido aguardando você"; parcelas previstas no fluxo |
| 62 (regra provisória de "em risco") | PRD-02 | PLN-01b | substituída pela prevista × prometida |
| 60 (arrastar no Kanban) | PRD-02 | PLN-02 | a fila tem arraste próprio; o Kanban continua sem |
| 18, 19 e 20 (itens que geram OS, uma OS por item, projetos da Fase 2 sem OS) | PRD-01 | PLN-01a, PLN-02, PLN-05 | o que entra no plano, na fila e na simulação |
| 64 e 65 (etapa Projeto sem BOM; vários operadores na etapa) | PRD-03a | PLN-01a, PNL-01b | capacidade por centro, não por operador; "operador +n" na TV |
| 73 e 75 (momento do snapshot do custo-hora; pausa paga) | PRD-04 | DSH-02, DSH-03 | valor da mão de obra; indicador de horas paradas |
| 78 e 79 (custo da NC; aprovação da última etapa) | PRD-05 | DSH-02, PLN-01a | NC fora do realizado; horas restantes em `Quality` |
| 82 (conclusão parcial) | PRD-06a | PLN-01a | OS parcialmente concluída no plano |
| 28 (quem recebe o material que chega) | EST-02 | PLN-02 | ordem das faltas pela fila (só com decisão) |
| 6 (roteiro no item do orçamento) | ENG-01 | PLN-05 | ordem das etapas da simulação |

## Pontos em aberto da Fase 4
Pendências do `06` que afetam a fase: **P-17** (decidida: competência pela data da venda; a FIN-08 aplica e deixa a data antes da NF-e como ponto 10), **P-01** (aberta: impostos realizados antes da Fase 5, ponto 7, sem bloquear) e **P-12** (decidida: a TV é providenciada pela Konebras). Pontos por story (a proposta está em cada arquivo; nenhum foi decidido):

| # | Story | Ponto |
|---|---|---|
| 1 | FIN-07 | Qual "% de inadimplência" pondera o ajustado (pré-condição) |
| 2 | FIN-07 | Limite padrão de 20% |
| 3 | FIN-07 | Deslocamento em dias corridos ou úteis |
| 4 | FIN-07 | Vencido em aberto no ajustado (pré-condição) |
| 5 | DSH-02 | Contar a compra para o projeto duas vezes (pré-condição; herdado da CMP-03a) |
| 6 | DSH-02 | Título `Planned` vai para "Comprometido" (muda a COM-05; pré-condição) |
| 7 | DSH-02 | Impostos realizados antes da Fase 5 (P-01) |
| 8 | DSH-02 | Margem do consolidado ponderada pela receita |
| 9 | DSH-02 | Custo da NC no realizado (herdado da PRD-05) |
| 10 | FIN-08 | Data da receita antes da NF-e (pré-condição) |
| 11 | FIN-08 | Juros, multas e descontos das baixas no DRE |
| 12 | FIN-08 | Definição do DRE por projeto (pré-condição) |
| 13 | FIN-08 | Cancelamento depois do mês (sem fechamento) |
| 14 | FIN-08 | Compra para estoque no DRE (pré-condição; herdado da CMP-03a) |
| 15 | DSH-04 | Ordem "do que mais destrava" (pré-condição) |
| 16 | DSH-04 | Categorias e limites do MVP |
| 17 | DSH-04 | Pauta no Início, acima dos "Próximos passos" da ADR 0010 item 9 (pré-condição) |
| 18 | DSH-01a | Consultas do painel no Host ou no módulo `Reporting` (arquiteto; pré-condição) |
| 19 | DSH-01a | Meta de faturamento: formato e permissão (pré-condição) |
| 20 | DSH-01a | O que é "faturamento" antes da Fase 5 (pré-condição) |
| 21 | DSH-01a | Cache de 60 s e frescor |
| 22 | DSH-01a | Primeira tela do dono depois do login |
| 23 | DSH-01b | Definição da carteira a produzir e a faturar (pré-condição) |
| 24 | DSH-01b | Margem "realizada" do PRD × "projetada" do protótipo (pré-condição) |
| 25 | PLN-01a | Ordem da fila antes da PLN-02 |
| 26 | PLN-01a | Etapas em andamento antes da fila (pré-condição) |
| 27 | PLN-01a | Início da etapa seguinte: dia seguinte ou mesmo dia (pré-condição) |
| 28 | PLN-01a | Horas do retrabalho depois da NC |
| 29 | PLN-01a | Datas previstas fora da auditoria (arquiteto; pré-condição) |
| 30 | PLN-01a | Itens que geram OS e uma OS por item (herdado da PRD-01) |
| 31 | PLN-01b | Prazo de instalação na data prevista (pré-condição) |
| 32 | PLN-01b | Valor padrão do prazo de instalação (pré-condição) |
| 33 | PLN-01b | Até 10 s para a data nova aparecer |
| 34 | PLN-01b | Quem é avisado do risco e com qual tolerância |
| 35 | PLN-01b | Regra provisória de risco da Fase 3 (herdado da PRD-02) |
| 36 | PLN-02 | Dono da prioridade: Production × `commercial.projects.priority` (pré-condição; pede ADR) |
| 37 | PLN-02 | Permissão para reordenar: `production.edit` ou `production.approve` (pré-condição) |
| 38 | PLN-02 | Onde entra o projeto novo na fila |
| 39 | PLN-02 | Ordem das faltas pela fila (herdado da EST-02) |
| 40 | PLN-04 | Ocupação acima de 100% (protótipo × plano finito; pré-condição) |
| 41 | PLN-04 | Faixas de cor da ocupação |
| 42 | PLN-03 | Componente próprio de Gantt ou biblioteca (com ADR) |
| 43 | PLN-03 | OS de instalação no cronograma |
| 44 | PNL-01a | Caminho da TV: `/tv#token` com cookie × `/tv/{token}` (pré-condição; muda o `02` §4) |
| 45 | PNL-01a | Quem edita e revoga a tela: `kiosk.create` ou ações novas no catálogo (pré-condição) |
| 46 | PNL-01a | Duração da sessão da TV |
| 47 | PNL-01a | `02-arquitetura` §2.1 ainda cita o índice de quiosque no catálogo |
| 48 | PNL-01b | Nome do operador na TV (LGPD; pré-condição) |
| 49 | PNL-01b | Cliente na TV (LGPD; pré-condição) |
| 50 | PNL-01b | Visões do MVP |
| 51 | DSH-03 | Indicadores do supervisor |
| 52 | DSH-03 | Faltas que bloqueiam a próxima etapa |
| 53 | DSH-03 | Supervisor sem `purchasing` no seed |
| 54 | PLN-05 | Ordem das etapas sem roteiro |
| 55 | PLN-05 | Quem pode simular (pré-condição) |
| 56 | PLN-05 | Roteiro no item do orçamento (herdado da ENG-01) |

---


# Fase 5 — Integrações fiscais e bancárias (go-live)

Stories detalhadas da Fase 5, uma por arquivo, escritas pelo @sm (River) e validadas pelo @po (Pax) em 21/10/2026. Fonte: `docs/01` a `06`, ADRs 0001–0022, o código das Fases 0 a 2 (`SecretKind`/`ISecretProtector`, `TenantJob`, `RecurringJobsRegistrar`, `IEmailSender` com anexo, `IPdfRenderer`, `ReceivableService`, `IReceivables`, `IPayables`, `BankAccount`, `ModuleCatalog`, `GroupSeeder`, `SeedPatchSeeder`), as stories da Fase 3 em revisão (branch `docs/fase3-stories`: CMP-04, CMP-03a, PRD-06a/06b, EST-01a) e a **análise do projeto `D:\GIT\EmissorFiscal`** (somente leitura, commit `e294444`), registrada na [FIS-01](FIS-01-emissorfiscal-adr-adaptador.md). A Definition of Ready, a legenda e a escala de tamanho são as mesmas das fases anteriores. **Todas as stories estão `Draft`** (GO condicional): dependem de decisões do PO e da contadora, do certificado A1, da conta do Inter com API e das stories da Fase 3.

## Resumo da análise do EmissorFiscal
- **O que é:** serviço HTTP .NET 10 independente (Postgres próprio com RLS, outbox, cofre AES-GCM), sobre a Unimake.DFe; o BemAqui entra como mais um sistema cliente, por `X-Api-Key` com escopos. Não calcula imposto (o BemAqui calcula, FIS-02b). Sem porta pública (rede `stack_stack`, HTTP).
- **Cobre:** NF-e 55 (saída e entrada, devolução, notas referenciadas), prévia sem consumir número, idempotência obrigatória, numeração transacional, retorno 201/202/422 com contingência SVC automática, webhook HMAC (`nfe.autorizada/rejeitada/cancelada/denegada`), cancelamento (24 h), carta de correção (até 20), inutilização, XML autorizado guardado, DANFE em HTML, status da SEFAZ, alertas do A1, rate limit; contrato de NFS-e com o padrão Nacional e a decisão CONAM/e-Transparência para Caçapava.
- **Falta:** nenhuma nota autorizada de verdade (sem A1; F7 não começou; não implantado); IBS/CBS (NF-e e NFS-e); DANFE em PDF; transportadora, volumes, duplicatas e DIFAL no payload; Distribuição DF-e, manifestação e consulta de NF-e de terceiros; NFS-e de Caçapava configurada, cancelamento, PDF e webhook de NFS-e; status do certificado sem escopo `admin`; TLS ou rede dedicada; `tipoOperacao` inconsistente entre o guia e o modelo.

## Índice
| Ordem | ID | Título | Tam. | Status | Requisitos | Depende de | Arquivo |
|---|---|---|---|---|---|---|---|
| 1 (5.1) | FND-16 | Recepção de webhooks: rota, validação, idempotência e fila (nova) | M | Draft | R-FIN-7, R-FIS-3, PRD §6 Segurança | FND-02, FND-07, FND-11, FND-12 | [FND-16](FND-16-webhooks-de-entrada.md) |
| 2 (5.1) | FIS-01 | Análise do EmissorFiscal, ADR 0023 e adaptador `IFiscalEmitter` | G | Draft | R-FIS-1, R-FIS-8, R-FIS-6, R-FIS-7 | FND-16, FND-11 | [FIS-01](FIS-01-emissorfiscal-adr-adaptador.md) |
| 3 (5.2) | BNK-01a | Inter: credenciais, mTLS cifrado, token e rotação | M | Draft | R-FIN-7, R-FIN-8, R-FIN-2 | CAD-05b, FND-11; P-08 | [BNK-01a](BNK-01a-inter-configuracao-mtls.md) |
| 4 (5.2) | BNK-01b | Bolepix a partir do título: emissão, PDF, e-mail, cancelamento | G | Draft | R-FIN-7, R-FIN-3, R-COM-1 | BNK-01a, FIN-01, COM-03 | [BNK-01b](BNK-01b-bolepix-emissao-cancelamento.md) |
| 5 (5.2) | BNK-02 | Webhook de pagamento: reconsulta, baixa e conferência diária | G | Draft | R-FIN-7, R-FIN-3, R-TER-4 | FND-16, BNK-01b, FIN-01 | [BNK-02](BNK-02-webhook-pagamento-baixa.md) |
| 6 (5.3) | BNK-03a | Extrato pela API do Inter e por OFX | M | Draft | R-FIN-8, R-FIN-2 | BNK-01a, FIN-04 | [BNK-03a](BNK-03a-extrato-api-ofx.md) |
| 7 (5.3) | BNK-03b | Conciliação bancária | G | Draft | R-FIN-8, R-FIN-5, R-FIN-3 | BNK-03a, FIN-04, BNK-02 | [BNK-03b](BNK-03b-conciliacao-bancaria.md) |
| 8 (5.4) | FIS-02a | Configuração fiscal do tenant e certificado A1 | M | Draft | R-FIS-2 | FIS-01, CAD-06 | [FIS-02a](FIS-02a-configuracao-fiscal-tenant.md) |
| 9 (5.4) | FIS-02b | Regras fiscais e cálculo dos tributos | G | Draft | R-FIS-4, R-FIS-3, R-FIS-8, R-PRJ-5 | FIS-02a; P-01, P-02, P-03 | [FIS-02b](FIS-02b-regras-fiscais-calculo.md) |
| 10 (5.4) | FIS-03a | NF-e de saída: emissão, autorização, DANFE/XML, impostos no projeto | G | Draft | R-FIS-3, R-PRD-8, R-PRJ-5 | FIS-01, FIS-02a, FIS-02b, PRD-06b | [FIS-03a](FIS-03a-nfe-saida-emissao.md) |
| 11 (5.4) | FIS-03b | Cancelamento, carta de correção e inutilização | M | Draft | R-FIS-3, R-SEC-10 | FIS-03a | [FIS-03b](FIS-03b-nfe-cancelamento-cce-inutilizacao.md) |
| 12 (5.5) | FIS-04a | NF-e de entrada por XML: importação, de-para, conferência | M | Draft | R-FIS-5, R-CMP-5 | CAD-02b, CMP-03a | [FIS-04a](FIS-04a-nfe-entrada-xml-importacao.md) |
| 13 (5.5) | FIS-04b | Recebimento pela nota | M | Draft | R-CMP-5, R-EST-7, R-FIN-4 | FIS-04a, CMP-04 | [FIS-04b](FIS-04b-recebimento-pela-nota.md) |
| 14 (5.5) | FIS-05 | NF-e de entrada por PDF (leitura assistida) | M | Draft | R-FIS-6 | FIS-04a | [FIS-05](FIS-05-nfe-entrada-pdf.md) |
| 15 (5.6) | FIN-09 | Régua de cobrança (D) | M | Draft | R-FIN-9 | BNK-01b, BNK-02 | [FIN-09](FIN-09-regua-de-cobranca.md) |
| 16 (5.6) | FIS-06 | NFS-e da instalação (D, P-02) | M | Draft | R-FIS-7, R-FIS-8 | FIS-01, FIS-02b, COM-06 | [FIS-06](FIS-06-nfse-instalacao.md) |
| 17 (5.7) | GOL-01a | Go-live: preparação, runbook e ensaio da virada | M | Draft | PRD §7, §1.2, §6 | as stories acima, exceto as D (ponto 74) | [GOL-01a](GOL-01a-preparacao-ensaio-virada.md) |
| 18 (5.7) | GOL-01b | Go-live: virada, corte do Dolibarr e primeira semana | M | Draft | PRD §7, §1.2 | GOL-01a | [GOL-01b](GOL-01b-virada-primeira-semana.md) |

Total da Fase 5: 18 stories (M: 11 · G: 7), todas `Draft`.

## Ordem de execução
```
5.1 Base das integrações:  FND-16 → FIS-01 (a ADR 0023 pode correr em paralelo ao 5.2)
5.2 Cobrança pelo Inter:   BNK-01a → BNK-01b → BNK-02
5.3 Conciliação:           BNK-03a → BNK-03b
5.4 Nota de saída:         FIS-02a → FIS-02b → FIS-03a → FIS-03b
5.5 Nota de entrada:       FIS-04a → FIS-04b → FIS-05
5.6 Desejáveis (D):        FIN-09 → FIS-06
5.7 Go-live:               GOL-01a → GOL-01b (última)
```

Por que esta ordem:
- **FND-16 primeiro:** o EmissorFiscal e o Inter avisam por webhook; a rota única, a validação e a deduplicação vêm antes dos dois consumidores.
- **Banco antes do fiscal:** o Inter não depende da P-01/P-02/P-03 nem do A1 e entrega cedo a visibilidade financeira (boleto do título, baixa automática, conciliação). O fiscal depende da ADR 0023, da contadora e do certificado.
- **FIS-02a/02b antes da FIS-03a:** o EmissorFiscal não calcula imposto; regra e cálculo são do BemAqui.
- **Entrada depois da saída:** depende da CMP-03a e da CMP-04 (Fase 3) e não bloqueia o faturamento.
- **GOL-01a/01b por último:** ensaio completo e rollback testado antes do dia da virada.

## Quebras, stories novas e re-estimativas
- **FIS-02** → 02a (configuração, M) e 02b (regras e cálculo, G); **FIS-03** → 03a (emissão, G) e 03b (eventos, M); **FIS-04** → 04a (importação, M) e 04b (recebimento, M); **BNK-01** → 01a (configuração, M) e 01b (emissão, G); **BNK-03** → 03a (extrato, M) e 03b (conciliação, G); **GOL-01** → 01a (preparação e ensaio, M) e 01b (virada e primeira semana, M).
- **Nova:** FND-16 (recepção de webhooks, M), pedida pela revisão pré-Fase 0 (`webhook_events`).
- **Re-estimadas:** FIS-01 (M → G: análise + ADR + adaptador + webhook + contrato) e BNK-02 (M → G: primeiro consumidor de webhook com efeito financeiro, cadastro do webhook e conferência diária).

## Padrões comuns da Fase 5
Valem além dos padrões das fases anteriores.
1. **Integração atrás de contrato:** o Fiscal só conhece `IFiscalEmitter`; o Finance só conhece `IBankBillingProvider`/`IBankStatementProvider`; nenhum código do EmissorFiscal ou de banco é copiado.
2. **Segredos por tenant, write-only:** `inter-credentials`, `inter-certificate`, `fiscal-certificate` (ADR 0007) e os propostos `fiscal-api-key` e `webhook-secret` (adendo à ADR 0007 e ao teste golden); decifrados só no uso; nunca em log, auditoria, resposta ou `IFileStorage`; teste de canário em cada story; rotação testa o novo antes de trocar.
3. **Webhook nunca é verdade:** a rota valida a origem, grava o evento uma vez e responde rápido; o efeito vem da **reconsulta** à fonte (EmissorFiscal, ou o Inter por mTLS); job de conferência como reserva.
4. **Efeito fiscal e financeiro com confirmação:** diálogo grande com o impacto; automático só o que o banco ou a SEFAZ confirmaram; divergência vai para conferência humana (R-PRP-7).
5. **Segregação:** configurar integrações com `settings.edit` (Proprietário); cancelar e inutilizar nota com `fiscal.approve` (proposta); regra fiscal ativada por outra pessoa; lançamento sem OP a partir do extrato com `finance.pay`; nenhuma saída de caixa sem OP aprovada, inclusive na conciliação.
6. **Documento fiscal nunca é apagado:** nota com número só é cancelada ou inutilizada; XML e eventos guardados 5 anos nos dois lados; sem rota DELETE (teste de arquitetura).
7. **Visibilidade financeira:** boleto a partir do título; baixa automática alimenta caixa, aging, pontualidade e conclusão do contrato; impostos da nota autorizada entram como `CostType.Tax` no realizado do projeto; entrada pela nota leva o custo real ao custo médio e as duplicatas ao título a pagar.
8. **Homologação com dado real (ADR 0020):** SEFAZ e Inter em produção só depois da GOL-01 no tenant `konebras` (proposta); sandbox e homologação SEFAZ no tenant `demo`.

## Validação do PO (21/10/2026)
O @po (Pax) validou as 18 stories com a checklist de 10 itens das fases anteriores. **Todas tiveram 10/10, veredito GO condicional, status `Draft`.**
- **Correções da validação cruzada:** a FND-16 abre o corpo dos eventos do Fiscal só para o Proprietário até a FIS-02a criar `fiscal.approve`; o número da NF-e passou a ser do EmissorFiscal (o `next_number` do 03 sai na FIS-01); a FIS-03a testa os dois caminhos da baixa do estoque até a decisão da PRD-06b; a FIS-04b usa o recebimento da CMP-04, sem caminho paralelo; a GOL-01 foi quebrada para o ensaio e o rollback virem antes da virada.
- **ADR antes do Marco 5.4:** ADR 0023 (integração com o EmissorFiscal), aprovada pelo PO, gate da FIS-01 e da FIS-02a; adendo à ADR 0007 (novos `SecretKind`).

## Checklist de go-live (resumo; detalhe na GOL-01a e na GOL-01b)
Critérios de entrada com evidência e "Go" do PO → ensaio completo e rollback numa cópia restaurada → D−1 backups extras → congelamento do Dolibarr (só leitura) e Konebras UI fora → carga final só dos cadastros alterados (dry-run, aceite, real) → `DROP USER`, secrets da migração e staging apagados → promoção do banco do tenant `konebras` com segredos novos (o key ring acompanha) → `app.konebras.com.br` no BemAqui, Dolibarr em `adm.konebras.com.br`, `tenant_domains` atualizado → EmissorFiscal e Inter em produção, webhook no host novo, `deploy-prod.yml` ligado → smoke, primeira nota e primeiro boleto reais com o PO (ponto sem retorno) → rollback possível até o fim do primeiro dia útil → primeira semana acompanhada → fechamento do go-live com a linha de base das métricas.

## Dependências da Fase 3 ainda abertas
- **PRD-06b ponto 1:** quem baixa o estoque, romaneio ou NF-e autorizada (FIS-03a CA 11, FIS-03b CA 4).
- **PRD-06b:** `IShipments`/`ShipmentDispatched`, CA 6 ("Emitir NF-e" na Fase 5) e CA 8 (estorno exige cancelar a nota).
- **CMP-04 pontos 1 a 6:** IPI recuperável, tolerância, preço diferente, confirmação do CP no parcial, quem estorna, quem registra (FIS-04b).
- **CMP-03a pontos 39 a 41:** conta e natureza do título do pedido, um título por parcela, compra para o projeto contada duas vezes (FIS-04b).
- **EST-01a ponto 2 / P-01:** crédito de imposto no custo médio (FIS-04b CA 2).
- **Fase 4 (ainda sem stories detalhadas):** DSH-02 e FIN-08 usam os impostos da nota e a data de emissão (P-17).

## Pontos em aberto da Fase 5
Pendências do `06` que afetam a fase: **P-01** (regime e crédito de impostos; o EmissorFiscal registra "Lucro Presumido desde 01/01/2026", sem decisão no `06`), **P-02** (CNAE e NFS-e), **P-03** (IBS/CBS), **P-08** (conta Inter com API), **P-17** (DRE pela data da emissão), **P-20** (estorno com `finance.approve`). Pontos por story (a proposta está em cada arquivo; nenhum foi decidido):

| # | Story | Ponto |
|---|---|---|
| 1 | FND-16 | mTLS do webhook do Inter no `stack-nginx` |
| 2 | FND-16 | Host do webhook e exceção à allowlist da plataforma |
| 3 | FND-16 | Janela de sobreposição na rotação do segredo (24 h) |
| 4 | FND-16 | Retenção do corpo do webhook (90 dias, LGPD) |
| 5 | FND-16 | `SecretKind.WebhookSecret` (adendo à ADR 0007) |
| 6 | FIS-01 | Quem implanta o EmissorFiscal, quando e em que ambientes |
| 7 | FIS-01 | Onde fica o certificado A1 (só no EmissorFiscal × carregado no BemAqui) |
| 8 | FIS-01 | Homologação SEFAZ sem escrever no tenant `konebras` |
| 9 | FIS-01 | IBS/CBS exigidos pela NT vigente (P-03) e prazo no EmissorFiscal |
| 10 | FIS-01 | DANFE em PDF (EmissorFiscal, `IPdfRenderer` ou HTML) |
| 11 | FIS-01 | Transportadora, volumes e duplicatas na NF-e |
| 12 | FIS-01 | Formato do `Idempotency-Key` da emissão |
| 13 | BNK-01a | Formato do certificado do Inter (.crt/.key ou .pfx) |
| 14 | BNK-01a | Integração por conta ou por tenant |
| 15 | BNK-01a | Inter em produção antes da GOL-01 |
| 16 | BNK-01a | Quem recebe o alerta de vencimento do certificado |
| 17 | BNK-01b | Multa e juros padrão (sem decidir valores) |
| 18 | BNK-01b | Formato do "seu número" |
| 19 | BNK-01b | Título vencido e vencimento em dia não útil (FIN-01 ponto 3) |
| 20 | BNK-01b | Envio automático do boleto na emissão |
| 21 | BNK-01b | Limite diário de e-mails |
| 22 | BNK-01b | Desconto do boleto × limite de desconto da D8 |
| 23 | BNK-02 | URL do webhook com query string no Inter |
| 24 | BNK-02 | Pagamento com diferença (conferência × baixa automática) |
| 25 | BNK-02 | Estorno de baixa confirmada pelo banco com `finance.approve` (P-20) |
| 26 | BNK-02 | Id de entrega do aviso do Inter |
| 27 | BNK-02 | Resumo das notificações de recebimento |
| 28 | BNK-03a | Horário e frequência da importação do extrato |
| 29 | BNK-03a | `ofx` na allowlist de anexos |
| 30 | BNK-03a | OFX numa conta Inter |
| 31 | BNK-03a | Contraparte mascarada para `finance.view` |
| 32 | BNK-03b | Janela de datas da sugestão (3 dias) |
| 33 | BNK-03b | Fechamento da conciliação por período |
| 34 | BNK-03b | Regras de tipo padrão do extrato |
| 35 | FIS-02a | `fiscal.approve` × ação específica `fiscal.cancel` |
| 36 | FIS-02a | Fiscal no seed (Financeiro, Almoxarifado) |
| 37 | FIS-02a | Série exclusiva da NF-e (contadora) |
| 38 | FIS-02a | Produção fiscal antes da GOL-01 |
| 39 | FIS-02a | Regime tributário (P-01) |
| 40 | FIS-02b | Conteúdo das regras e casos de referência (contadora) |
| 41 | FIS-02b | Quais tributos entram no custo do projeto (P-01) |
| 42 | FIS-02b | IBS/CBS já obrigatórios (P-03) |
| 43 | FIS-02b | DIFAL: bloquear ou estender o EmissorFiscal |
| 44 | FIS-02b | Segregação na ativação da regra |
| 45 | FIS-03a | Quem baixa o estoque (herdado da PRD-06b) |
| 46 | FIS-03a | Remessa para obra e venda |
| 47 | FIS-03a | Imposto digitado à mão |
| 48 | FIS-03a | Nota × parcelas do contrato e data da receita (P-17) |
| 49 | FIS-03a | DANFE em PDF (herdado da FIS-01) |
| 50 | FIS-03a | Os 3 cenários da contadora |
| 51 | FIS-03a | Certificado A1 (lacuna L1 do EmissorFiscal) |
| 52 | FIS-03b | Prazo de cancelamento aplicável |
| 53 | FIS-03b | Cancelamento em uma ou duas etapas |
| 54 | FIS-03b | Nota de devolução fora do prazo |
| 55 | FIS-03b | Carta de correção com `edit` ou `approve` |
| 56 | FIS-04a | Permissão para importar NF de entrada (`fiscal.import_incoming`?) |
| 57 | FIS-04a | XML sem protocolo de autorização |
| 58 | FIS-04a | Consulta da chave na SEFAZ (lacuna L-f) |
| 59 | FIS-04a | Item sem estoque na nota |
| 60 | FIS-04a | Nota sem pedido |
| 61 | FIS-04b | Nota sem duplicatas |
| 62 | FIS-04b | Crédito de impostos na entrada (P-01) |
| 63 | FIS-04b | Dependências da CMP-04 e da CMP-03a (Fase 3) |
| 64 | FIS-05 | Distribuição DF-e antes do go-live |
| 65 | FIS-05 | OCR na imagem da API |
| 66 | FIS-05 | Digitar a nota sem PDF legível |
| 67 | FIN-09 | Passos padrão da régua |
| 68 | FIN-09 | Títulos de implantação na régua |
| 69 | FIN-09 | Conferência do título no envio do e-mail |
| 70 | FIS-06 | NFS-e no go-live (instalação é o CNAE principal) |
| 71 | FIS-06 | P-02 e a venda mista |
| 72 | FIS-06 | PDF, cancelamento, webhook e IBS/CBS da NFS-e no EmissorFiscal |
| 73 | FIS-06 | Códigos do serviço (contadora) |
| 74 | GOL-01a | Stories obrigatórias para o go-live |
| 75 | GOL-01a | Teste de restauração obrigatório antes da virada |
| 76 | GOL-01a | Forma da promoção (ambiente novo × homologação vira produção) |
| 77 | GOL-01a | Data e janela da virada |
| 78 | GOL-01a | Itens da Fase 4 depois do go-live |
| 79 | GOL-01b | Desligamento definitivo do Dolibarr e backup final |
| 80 | GOL-01b | Modo só leitura do Dolibarr |
| 81 | GOL-01b | Comunicação ao cliente final |

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
