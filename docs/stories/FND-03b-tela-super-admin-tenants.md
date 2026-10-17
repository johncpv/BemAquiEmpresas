# FND-03b — Tela do super admin: tenants

| Campo | Valor |
|---|---|
| ID | FND-03b |
| Título | Painel da plataforma: listar, provisionar, retomar, suspender e reativar tenants |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-PLT-4 · R-PLT-3 (provisionar pela tela) |
| Dependências | FND-03a (serviço de provisionamento), FND-13 (login da plataforma), FND-09a e FND-09c (componentes) |
| Bloqueia | FND-06 (a tela de liberação de módulos fica dentro do detalhe do tenant) |
| Ordem de execução | 11 |

## User story
Como **super admin da plataforma**, quero **uma tela para ver todos os tenants, provisionar um novo, acompanhar e retomar o provisionamento e suspender ou reativar um tenant**, para **operar a plataforma sem acessar o servidor por linha de comando**.

## Contexto e referências
- PRD R-PLT-4: painel de administração da plataforma (listar, suspender, provisionar tenants).
- FND-03a: serviço de provisionamento com etapas e retomada (a tela usa o mesmo serviço da CLI).
- ADR 0005 D2: host `plataforma.bemaquiapp.com.br`.
- Revisão §2.3, CA da FND-03: painel da plataforma e `/hangfire` respondem 404 em host de tenant.
- Identidade visual: `docs/prototipo/prototipo-erp-konebras.html` (tabela, chips de status, painéis).

## Critérios de aceite
1. **Dado** um super admin logado no host da plataforma, **quando** abre "Tenants", **então** vê a lista paginada com nome fantasia, CNPJ, host primário, plano, status (chip: Ativo = ok, Provisionando = info, Suspenso = crítico), versão de schema e data de criação, com busca por nome, CNPJ ou host.
2. **Dado** "Provisionar tenant", **quando** preencho chave, razão social, nome fantasia, CNPJ, host, e-mail do proprietário e plano e confirmo "Provisionar", **então** o provisionamento roda em segundo plano e a tela mostra o progresso etapa a etapa (atualização a cada poucos segundos ou em tempo real).
3. **Dado** o formulário, **quando** o CNPJ é inválido ou a chave/host já existe, **então** o erro aparece no campo em PT-BR antes de enviar (validação no cliente e no servidor).
4. **Dado** um provisionamento que falhou, **quando** abro o detalhe do tenant, **então** vejo o log por etapa (`provisioning_logs`) com a mensagem de erro e o botão "Retomar provisionamento", que continua da etapa que falhou.
5. **Dado** um tenant ativo, **quando** clico "Suspender", informo o motivo (obrigatório) e confirmo "Suspender", **então** o tenant fica `Suspended`, a confirmação diz "Tenant suspenso" e em menos de 10 s os usuários do tenant recebem a página "Acesso suspenso" (FND-02).
6. **Dado** um tenant suspenso, **quando** clico "Reativar" e confirmo "Reativar", **então** o tenant volta a `Active` e o acesso volta em menos de 10 s.
7. **[Segurança]** **Dado** um host de tenant, **quando** acesso a rota da tela ou a API da plataforma (`/platform/...`), **então** recebo 404.
8. **Dado** qualquer ação da tela (provisionar, retomar, suspender, reativar), **quando** é executada, **então** fica registrada na auditoria da plataforma com o super admin, o motivo (quando houver) e o antes/depois.
9. **Dado** a lista sem nenhum tenant, **quando** abro a tela, **então** vejo o estado vazio "Nenhum tenant ainda" com a ação "Provisionar tenant".

## Regras de negócio e técnicas
- API REST `/platform/api/v1/tenants` só no host da plataforma, esquema `Platform` (FND-13), OpenAPI separado ou tag própria para o Orval.
- O provisionamento pela tela chama o mesmo serviço da FND-03a e roda em job Hangfire (fila `maintenance`), sem `tenantId` de negócio porque o tenant ainda está nascendo; o job recebe o id do tenant do catálogo.
- CNPJ validado com dígito verificador; chave em minúsculas, `[a-z0-9-]`.
- Suspensão e reativação atualizam o status e disparam a invalidação de cache da FND-02.
- Datas exibidas em America/Sao_Paulo; números com `tabular-nums`.

## Permissões, eventos e tabelas
- **Tabelas:** `catalog.tenants`, `catalog.tenant_domains`, `catalog.provisioning_logs`, `catalog.platform_audit_log` (FND-13), `catalog.plans` (leitura).
- **Permissões:** acesso de super admin (sem `modulo.acao`).
- **Eventos:** `TenantSuspended`, `TenantReactivated`, `TenantProvisioned` (catálogo).

## Telas e estados
Superfície Plataforma (layout próprio, sem o dock do tenant).
- **Tenants (lista):** DataTable da FND-09c; carregando com skeleton; vazio com ação "Provisionar tenant"; erro com "Não foi possível carregar os tenants. Tente de novo." e botão "Tentar de novo".
- **Provisionar tenant (formulário):** campos acima; plano pré-selecionado com o único plano existente (propositivo); host sugerido a partir da chave quando houver domínio padrão da plataforma (ver Pontos em aberto); botão "Provisionar".
- **Detalhe do tenant:** dados, domínios, status, versão de schema, log de provisionamento por etapa (indicador de etapa no estilo display de andar, com a etapa atual), ações "Retomar provisionamento", "Suspender", "Reativar"; aba "Módulos" reservada para a FND-06.
- **Confirmações:** diálogo com o mesmo verbo da ação; toast "Tenant suspenso", "Tenant reativado", "Provisionamento concluído. Convite enviado para {e-mail}."
- **Próximo passo:** ao concluir o provisionamento, oferecer "Liberar módulos" (FND-06) e "Copiar link de acesso".
- **Ajuda:** conforme decisão sobre a superfície Plataforma (FND-13).

## Plano de testes
- **Unidade (front):** validação do formulário (CNPJ, chave, host) com Zod.
- **Integração (Testcontainers):** endpoints de lista, provisionar (com etapas falsas de e-mail), retomar, suspender e reativar; auditoria (CA 8); 404 em host de tenant (CA 7).
- **E2E (Playwright):** provisionar um tenant de teste, suspender, ver a página "Acesso suspenso" no host do tenant e reativar.

## Fora do escopo
- Liberação de módulos por tenant (FND-06, aba "Módulos").
- Edição de domínios, troca de plano, exclusão e exportação de tenant.
- Cobrança do SaaS.

## Pontos em aberto
- **Domínio padrão da plataforma para novos tenants** (subdomínio de `bemaquiapp.com.br`?): a ADR 0004 deixa para quando houver o segundo tenant. Até lá, o host é digitado.
- **Cadastro de planos:** não há tela de planos; a FND-03a propõe um plano "Piloto" por seed.
- **Progresso em tempo real:** consulta periódica ou SignalR no host da plataforma; sem definição (o CA 2 aceita os dois).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de integração e E2E passando.
- [ ] Migration: não se aplica além das da FND-03a e FND-13.
- [ ] Permissão registrada no seed: não se aplica.
- [ ] Telas com estados de carregando, vazio e erro.
- [ ] Ajuda das telas conforme decisão.
- [ ] Padrões e próximo passo propositivos avaliados (plano pré-selecionado, "Liberar módulos" ao concluir).
- [ ] Auditoria ligada.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Endpoints `/platform/api/v1/tenants` (lista, detalhe, provisionar, retomar, suspender, reativar).
- [ ] Job de provisionamento disparado pela tela.
- [ ] Cliente Orval da API da plataforma.
- [ ] Telas de lista, formulário e detalhe na superfície Plataforma.
- [ ] Testes E2E com dois hosts.

## Quality gates
- Agentes previstos: @dev, @ux-design-expert, @qa.
- Gate: E2E de suspensão e reativação obrigatório.

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
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** —
- **Pré-condições para `Ready`:** Nenhuma bloqueante.
- **Prontidão:** 9/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 16/10/2026 | 0.1 | Story criada pela quebra da FND-03 (revisão §2.3): parte da tela | River (SM) |
| 16/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |

## Dev Agent Record
_A preencher pelo @dev._

## QA Results
_A preencher pelo @qa._