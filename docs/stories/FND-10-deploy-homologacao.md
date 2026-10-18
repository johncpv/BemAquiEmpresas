# FND-10 — Deploy em homologação, CI/CD e backup diário cifrado

| Campo | Valor |
|---|---|
| ID | FND-10 |
| Título | Compose endurecido na VPS compartilhada, vhost de homologação no `stack-nginx`, CI/CD por SSH, backup diário cifrado e restauração testada |
| Tamanho | G (re-estimada: o backlog dizia M; endurecimento, backup cifrado e restauração ampliaram o escopo) |
| Status | Em andamento (FND-10a pronta; FND-10b aguarda o PO) |
| Requisitos | PRD §6 Backup (dump diário por banco, 30 dias, cópia fora da VPS, restauração testada) · PRD §6 Disponibilidade · PRD §6 Segurança (HTTPS) |
| Dependências | FND-01, FND-03a (migrator), FND-11 (key ring), FND-12 (health e alertas), FND-13 (host da plataforma). Pré-requisito externo: DNS do host de homologação criado pelo PO |
| Bloqueia | Validação da Fase 0 pelo PO; GOL-01 (reaproveita o pipeline para produção) |
| Ordem de execução | 16 (ver Pontos em aberto sobre publicar homologação na 1ª semana) |

## User story
Como **PO**, quero **o sistema publicado automaticamente num host de homologação na VPS da Konebras, com backup diário cifrado fora da VPS**, para **validar cada entrega sem mexer no Dolibarr que está em `app.konebras.com.br`**.

## Contexto e referências
- **ADR 0005 D1:** `app.konebras.com.br` continua servindo o Dolibarr até o go-live; durante o desenvolvimento a validação usa um host de homologação na mesma VPS (sugestão: `homolog.konebras.com.br`, DNS pelo PO). A troca de host sai da Fase 0 e vai para a GOL-01.
- ADR 0005 D2 (host da plataforma), D6 (backup diário, RPO 24 h, dump cifrado fora da VPS, restauração testada), D7 (risco da VPS compartilhada aceito com mitigações).
- ADR 0004 §4 (compose em `/opt/projetos/bemaqui-empresas/`, rede externa `stack_stack`, vhost no `stack-nginx` em `/opt/infra/nginx/conf.d/` com `resolver 127.0.0.11` + `set $upstream`, `nginx -t` antes do reload, WebSocket para o SignalR; um certificado por host pelo certbot existente; sem Caddy).
- `docs/02-arquitetura.md` §7 (CI no GitHub Actions, deploy por SSH, `pg_dump` por banco para S3 compatível, retenção 30 dias, `/health`).
- `06-pendencias-riscos.md`: riscos da VPS compartilhada e RPO de 24 h aceitos.
- Revisão §2.3, CAs de segurança da FND-10.
- Acesso à VPS descrito em `D:\GIT\Konebras\CLAUDE.md`.

## Critérios de aceite
**Deploy**
1. **Dado** um push na `main` com o CI verde, **quando** o workflow de deploy roda, **então** as imagens são publicadas, o migrator roda (FND-03a) e a homologação em `https://homolog.konebras.com.br` passa a servir a versão nova, com `/health/ready` 200.
2. **Dado** o deploy, **quando** termina, **então** `https://app.konebras.com.br` continua servindo o Dolibarr, sem nenhuma alteração (ADR 0005 D1).
3. **Dado** um migrator que falha, **quando** o deploy roda, **então** a API nova não sobe, a versão anterior continua no ar e o alerta é enviado (FND-12).
4. **Dado** o vhost de homologação, **quando** inspeciono `/opt/infra/nginx/conf.d/`, **então** segue o padrão `resolver 127.0.0.11` + `set $upstream`, o reload só acontece depois de `nginx -t` com sucesso, e o certificado Let's Encrypt é do host de homologação (certbot existente, sem wildcard).
5. **[Segurança]** **Dado** o vhost, **quando** uma conexão SignalR fica aberta, **então** o WebSocket funciona e `proxy_read_timeout` é ≥ 120 s.
6. **[Segurança]** **Dado** os logs de acesso do nginx, **quando** uma URL ou cabeçalho contém token (convite, redefinição, `Authorization`), **então** o valor aparece mascarado no log.
7. **Dado** o host da plataforma, **quando** o deploy termina, **então** `plataforma.bemaquiapp.com.br` (ou o host de plataforma de homologação decidido) serve a tela do super admin atrás da allowlist (FND-13).

**Endurecimento dos containers**
8. **[Segurança]** **Dado** o `docker-compose.yml` de homologação, **quando** inspeciono o serviço `postgres`, **então** ele está só numa rede interna do projeto, sem `ports:` e fora da `stack_stack`.
9. **[Segurança]** **Dado** os serviços `api`, `web`, `migrator` e `backup`, **quando** inspeciono, **então** rodam como usuário não-root, com `read_only: true` (com `tmpfs` onde precisar), `cap_drop: [ALL]`, `security_opt: no-new-privileges` e limites de CPU e memória.
10. **Dado** a rede `stack_stack`, **quando** inspeciono, **então** só `web` e `api` estão ligados a ela.

**Backup (RPO 24 h)**
11. **[Segurança]** **Dado** o job de backup diário, **quando** roda, **então** gera um `pg_dump` por banco (catálogo e cada tenant), cifra cada dump antes de sair da VPS e envia para o storage externo S3 compatível.
12. **[Segurança]** **Dado** a credencial do backup no storage externo, **quando** tento apagar ou sobrescrever um objeto com ela, **então** o storage recusa (sem permissão de delete); a retenção de 30 dias é feita por regra de ciclo de vida do bucket.
13. **Dado** o backup do key ring (FND-11), **quando** é feito, **então** vai para a trilha separada definida no runbook, nunca junto com os dumps.
14. **Dado** uma falha no backup, **quando** o job termina com erro, **então** o alerta é enviado (FND-12).
15. **Dado** o runbook de restauração, **quando** restauro o banco de **um** tenant a partir do dump do dia anterior num ambiente separado, **então** o tenant sobe com os dados e segredos decifrados (com o key ring), sem afetar outros tenants, e o teste fica registrado em `docs/operacao/restauracao.md` com data e duração.

**Monitoramento**
16. **Dado** o monitor externo (FND-12), **quando** configurado, **então** checa `/health/live` e `/health/ready` da homologação e avisa o PO em caso de queda.

## Regras de negócio e técnicas
- Compose do projeto separado dos demais projetos da VPS; nada é alterado nos outros projetos nem no Dolibarr.
- Imagens construídas no CI e publicadas num registry; deploy por SSH com `docker compose pull && docker compose up -d`, com o migrator como serviço que precisa terminar com sucesso antes da `api` (`depends_on: condition: service_completed_successfully`).
- Segredos de produção e homologação (certificado do key ring, senhas de banco, SMTP) fora do repositório, como Docker secrets ou arquivos com permissão restrita.
- Backups cifrados com chave assimétrica cuja chave privada não fica na VPS (ferramenta a definir; ver Pontos em aberto).
- O pipeline de produção (tag → prod) fica preparado mas **desligado** até a GOL-01, porque o host de produção só muda no go-live.

## Permissões, eventos e tabelas
Não se aplica.

## Telas e estados
Sem tela. A homologação mostra a tela de login do tenant de homologação provisionado pela FND-03a/03b.

## Plano de testes
- **CI:** workflow de deploy em ambiente de homologação a cada push na `main` (CA 1); verificação estática do compose (script que falha se `postgres` tiver `ports:` ou se algum serviço rodar como root) (CA 8 a 10).
- **Fumaça pós-deploy:** `curl` em `/health/ready`, no login do tenant e no WebSocket (CA 1, 5); `curl` em `app.konebras.com.br` confirmando o Dolibarr (CA 2).
- **Backup:** execução manual do job e listagem do bucket; tentativa de delete com a credencial do backup (CA 11, 12).
- **Restauração:** exercício completo documentado (CA 15).

## Fora do escopo
- Troca de `app.konebras.com.br` para o BemAqui e mudança do Dolibarr para `adm.konebras.com.br` (GOL-01, ADR 0005 D1).
- Publicação em produção por tag (preparada, mas desligada até a GOL-01).
- VPS dedicada (só ao entrar o 2º tenant, conforme o 06).
- Teste de restauração mensal recorrente (processo operacional a partir do go-live).

## Pontos em aberto
- **Host de homologação:** `homolog.konebras.com.br` é sugestão da ADR 0005 D1; o PO confirma e cria o DNS (Cloudflare, DNS-only).
- **Host da plataforma em homologação:** usar `plataforma.bemaquiapp.com.br` já na homologação ou um host separado (ex.: `plataforma-homolog.bemaquiapp.com.br`)? DNS e certificado são do PO.
- **Diretório na VPS:** a ADR 0004 cita `/opt/projetos/bemaqui-empresas/`; definir se a homologação usa esse diretório ou um próprio (ex.: `/opt/projetos/bemaqui-empresas-homolog/`), para não colidir com a produção futura.
- **Provedor do storage externo S3 compatível**, ferramenta de cifragem dos dumps (ex.: `age`, `gpg`) e guarda da chave privada.
- **Registry das imagens** (GitHub Container Registry?) e credenciais na VPS.
- **CA original "tag publica em prod":** conflita com a ADR 0005 D1 na Fase 0; esta story deixa o pipeline de produção desligado até a GOL-01. Confirmar.
- **Homologação na 1ª semana (revisão §2.3):** a ordem da revisão coloca a FND-10 no fim da Fase 0. Se o PO quiser homologação já na 1ª semana, sugere-se quebrar em FND-10a (pipeline e vhost, logo após a FND-01) e FND-10b (endurecimento, backup e restauração). Decisão do PO.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Deploy automático e fumaça pós-deploy verdes.
- [ ] Migration: aplicada pelo migrator no deploy.
- [ ] Permissão registrada no seed: não se aplica.
- [ ] Tela: não se aplica.
- [ ] Ajuda: runbooks `docs/operacao/deploy.md` e `docs/operacao/restauracao.md` escritos.
- [ ] Padrões e próximo passo propositivos: não se aplica.
- [ ] Auditoria: não se aplica.
- [ ] Teste de restauração de um tenant executado e registrado.
- [ ] Story marcada no backlog; `docs/02-arquitetura.md` §7 com o host de homologação.

## Tarefas técnicas sugeridas
- [ ] Dockerfiles não-root para `api`, `web` e `migrator`; imagem de `backup`.
- [ ] `docker-compose.homolog.yml` com redes, limites e endurecimento.
- [ ] Vhost de homologação e da plataforma no `stack-nginx`, com WebSocket, timeout e máscara de tokens no log.
- [ ] Certificados pelo certbot existente.
- [ ] Workflow de deploy por SSH (actions fixadas por SHA) e workflow de produção desligado.
- [ ] Job de backup cifrado, bucket com ciclo de vida e credencial sem delete.
- [ ] Runbooks de deploy e restauração; exercício de restauração.
- [ ] Monitor externo configurado.

## Quality gates
- Agentes previstos: @devops, cybersecurity (endurecimento e backup), @qa.
- Gate: o PO confirma que `app.konebras.com.br` continua com o Dolibarr após o primeiro deploy.

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
- **Pré-condições para `Ready`:** Hosts e DNS (homologação e plataforma), diretório na VPS, storage S3, ferramenta de cifragem, registry; decidir se quebra em 10a/10b.
- **Prontidão:** 7/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 16/10/2026 | 0.1 | Story detalhada conforme ADR 0005 (D1, D6, D7) e os CAs de segurança da revisão; troca de host movida para a GOL-01 | River (SM) |
| 16/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 18/10/2026 | 0.3 | Quebrada em FND-10a (repositório, pronta) e FND-10b (VPS); decisões na ADR 0015 | @dev |

## Dev Agent Record
_A preencher pelo @dev._

## QA Results
_A preencher pelo @qa.**FND-10b (em andamento desde 18/10/2026)**

Feito:
- DNS dos dois hosts, criado pelo PO e conferido.
- Diretório `/opt/projetos/bemaqui-empresas-homolog` e secrets gerados na VPS. O certificado do key ring e a chave `age` foram gerados na estação do PO; as cópias offline vão para o cofre.
- Key ring criado; api e web saudáveis.
- Vhost instalado com `nginx -t` antes de cada reload.
- Certificados Let's Encrypt dos dois hosts, que entram na renovação automática que já existe.
- Fumaça externa: `/health/ready` 200, front, HTTPS e redirecionamento; `app.konebras.com.br` continua no Dolibarr.
- Correção no primeiro deploy: a API resolveu `postgres` para o banco de outro projeto na `stack_stack`. A senha foi recusada e nada vazou. Desde então os nomes são únicos, a regra está no `check-compose.mjs` e na ADR 0015.
- Painel da plataforma **aberto para qualquer IP**, a pedido do PO, até a lista de IPs ser definida.

Pendente:
- SMTP, e depois disso o provisionamento do tenant Konebras (o convite do proprietário sai por e-mail).
- Super admin da plataforma: o PO roda `platform-admin create` pelo SSH, porque o link aparece uma única vez.
- Buckets, cron do backup e prova do CA 12.
- Pipeline automático: token de leitura do GHCR na VPS, chave de deploy, secrets do environment e `HOMOLOG_DEPLOY`.
- Monitor UptimeRobot (CA 16).
- Exercício de restauração com o backup real.
- Fechar a allowlist da plataforma.

## QA Results
_A preencher pelo @qa._