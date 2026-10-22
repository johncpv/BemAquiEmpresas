# RH-01a — Ponto: marcação pelo app (Meu ponto), geolocalização opcional e consulta das marcações

| Campo | Valor |
|---|---|
| ID | RH-01a |
| Título | "Meu ponto" como **controle interno de jornada** (não substitui o registro legal de ponto): o funcionário registra entrada, intervalo e saída pelo app (tablet da fábrica com PIN, celular ou computador), com a hora do servidor e comprovante na tela; geolocalização desligada por padrão, com ciência registrada; o RH consulta as marcações por funcionário e por dia, com a localização só com `hr.view_sensitive` |
| Tamanho | M (quebra da RH-01 em 01a marcação, 01b lançamento manual e ajustes, 01c espelho, banco de horas e fechamento) |
| Status | Ready (pré-condição externa) |
| Requisitos | R-RH-4 (parte: marcação pelo PWA com geolocalização opcional) · PRD §6 (LGPD, Usabilidade) · R-SEC-5 · R-PRP-1 · R-PRP-2 |
| Dependências | CAD-04a (jornada do funcionário), CAD-04b (funcionário e vínculo com o usuário, `IUserEmployeeLink`), CAD-04c1 (situação `OnLeave` no `IEmployees`), PRD-03a (superfície `/fab`), PRD-03b (aparelho registrado e sessão por PIN), RH-02b (autoatendimento `/api/v1/me/hr/*`, ADR 0035), RH-02a (`hr.parameters` e a seção RH de Configurações → Parâmetros), FND-09e; Fase 0 (FND-06c, FND-07, FND-14) |
| Bloqueia | RH-01b, RH-01c, LGPD-01a (exportação inclui marcações), FLH-02b |
| Ordem de execução | 6 (Marco 6.1 "Ponto") |

## User story
Como **funcionário**, quero **registrar a minha entrada, o intervalo e a saída com um toque, no tablet da fábrica ou no celular**, e como **RH**, quero **ver as marcações de cada funcionário por dia**, para **ter o controle interno de jornada que vai alimentar o espelho, o banco de horas e a folha**.

## Contexto e referências
- PRD R-RH-4 ("Ponto básico: marcação pelo PWA (com geolocalização opcional) ou lançamento manual pelo RH, espelho mensal, banco de horas simples, ajustes com justificativa e aprovação"); R-RH-7 (ponto legal da Portaria 671 é P, Fase 9); PRD §6 LGPD ("dados pessoais de funcionários com acesso restrito ao RH").
- `03-modelo-dados` → `hr.time_entries` (`employee_id`, `at`, `type` In/Out/BreakStart/BreakEnd, `source` PWA/Manual, `lat`, `lng`, `justification`, `approved_by`); `hr.work_schedules` (CAD-04a, dias e horários da jornada).
- `ModuleCatalog`: `hr` com `view`, `create`, `edit`, `approve` (significado: "ajustes de ponto e fechamento da folha"), `delete` e `view_sensitive`. Seed: o grupo RH tem `view`, `create`, `edit` e `view_sensitive`; `approve` fica com o Proprietário (ADR 0008).
- `02-arquitetura` §4 (PWA `/fab`, leitura por câmera, PIN em aparelho registrado e "Meu ponto", ADR 0026 e 0035; sem funcionamento sem rede, P-23).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D15 com a decisão do PO de 21/10/2026 (o ponto do app entra **sem ser registro oficial**, como controle interno, com aviso claro na tela e na ajuda; deixa de ser pré-condição do Marco 6.1; o registro oficial vira story pós-MVP na Fase 9 e a pendência P-26), tabela 3.3 pontos 1 a 5, §4.2 (RH-01a: "Meu ponto" no tablet na lista `[PinSessionAllowed]`; quem ganha `hr.view_sensitive` perde o PIN), §4.4 padrão 8.
- **ADR 0035** (`docs/adr/0035-autoatendimento-funcionario.md`: autoatendimento do funcionário em `/api/v1/me/hr/*`, camada 1 e vínculo com funcionário ativo, sem ação no grupo, ponto não oficial) e o **adendo de 21/10/2026 à ADR 0026** (marcação do "Meu ponto" na lista `[PinSessionAllowed]`; marcar no tablet encerra a sessão por PIN no servidor).
- ADR 0016 (rota `/sensitive`, `SensitiveRead`, `[PersonalData]`, `no-store`; adendo M6, parâmetros do módulo dono), ADR 0018 (retenção do funcionário), ADR 0019 §2 e §3 (`xmin`, `Idempotency-Key`), ADR 0021 (mensagens).
- CLT art. 74 §2 e Portaria 671 (REP-P): o ponto do app **não** é o registro oficial; é controle interno até a story da Fase 9.
- Protótipo: o app do operador usa o layout `/fab` (botões ≥ 56 px, uso com luva).

## Critérios de aceite
**Controle interno, não registro oficial**
1. **Dado** "Meu ponto" (tablet, celular ou computador) e RH → Ponto → Marcações, **quando** abrem, **então** mostram a faixa informativa fixa "Controle interno de jornada. Este registro não substitui o registro legal de ponto (REP-P, Portaria 671)." e a ajuda das telas repete o aviso com a explicação; o comprovante da marcação diz "Comprovante de controle interno" (nunca "comprovante de registro de ponto"); teste de texto confere a faixa nas três rotas.

**Meu ponto**
2. **Dado** um usuário ligado a um funcionário ativo (CAD-04b) e o módulo `hr` liberado no tenant, **quando** abre "Meu ponto" (`/fab/ponto` no tablet ou `/app/meu-ponto` no backoffice e no celular, o mesmo layout), **então** vê o nome ("Olá, {primeiro nome}"), a data e a hora do servidor em America/Sao_Paulo, a jornada do dia (ex.: "08:00–17:48, intervalo 1 h"), as marcações de hoje e um botão grande (≥ 56 px) com a **próxima marcação sugerida** pela sequência Entrada → Saída para o intervalo → Volta do intervalo → Saída.
3. **Dado** o botão sugerido, **quando** toco "Registrar entrada" (ou o verbo da marcação sugerida), **então** nasce a marcação em `hr.time_entries` por `POST /api/v1/me/hr/time-entries`, com `at` = instante do servidor (UTC), `type`, `source` = `App` (ou `Pin` no tablet), o usuário que registrou e o aparelho (`device_id`, quando o aparelho é registrado na PRD-03b); o horário nunca vem do aparelho nem é digitado; a tela mostra o comprovante "Entrada registrada às 08:02 de 21/10/2026" e a mensagem de sucesso no topo diz "Entrada registrada" (ADR 0021).
4. **Dado** que esqueci uma marcação, **quando** abro "Outra marcação", **então** posso escolher qualquer um dos quatro tipos, com a hora do servidor; a sequência fora de ordem é aceita e vira inconsistência no espelho (RH-01c), e a tela sugere "Esqueceu uma marcação? Peça o ajuste ao RH." com o atalho para o pedido de ajuste (RH-01b).
5. **[Segurança]** **Dado** um duplo toque, **quando** chegam duas marcações do mesmo funcionário e tipo com menos de 1 minuto entre elas, **então** só a primeira é gravada e a segunda responde 409 "Você já registrou {tipo} às {hora}."; o comando exige `Idempotency-Key` (ADR 0019 §3) e o reenvio com a mesma chave devolve o comprovante original.
6. **[Segurança]** **Dado** as rotas `/api/v1/me/hr/time-entries`, **quando** chamadas, **então** passam pela camada 1 (`RequireModule("hr")`) e pelo filtro de vínculo com funcionário ativo da ADR 0035 (criado na RH-02b), sem ação no grupo; usuário sem funcionário ligado, ou ligado a funcionário desligado ou afastado (`IEmployees`, CAD-04c1), recebe 403 `employee_not_active` e a tela mostra "Seu usuário não está ligado a um funcionário ativo. Fale com o RH."; a marcação é sempre do funcionário do usuário logado: o corpo do comando não aceita `employeeId` (`UnmappedMemberHandling.Disallow`, 400); as rotas estão na lista versionada de autoatendimento do teste de arquitetura.
7. **[Segurança]** **Dado** a sessão aberta por PIN no tablet registrado (PRD-03b), **quando** o operador abre "Meu ponto", **então** consegue ver as marcações de hoje e marcar: só `GET /api/v1/me/hr/time-entries/today` e `POST /api/v1/me/hr/time-entries` têm `[PinSessionAllowed]` (adendo de 21/10/2026 à ADR 0026; "Minhas marcações", a localização e os demais endpoints do RH respondem 403 `pin_session_scope`); depois da marcação, o servidor **encerra a sessão por PIN** (refresh e sessão revogados na mesma resposta) e o tablet volta à lista de nomes, para o próximo funcionário; quem ganha `hr.view_sensitive` perde o PIN (ADR 0026 §2, teste).
8. **Dado** o aparelho sem rede, **quando** o funcionário toca para marcar, **então** vê "Sem conexão. O ponto precisa de rede para usar a hora do servidor. Tente de novo ou avise o RH."; não existe marcação sem rede (coerente com a P-23).

**Geolocalização opcional**
9. **Dado** o parâmetro do HR `hr.time_clock_geolocation` (`Off`, o padrão, ou `Ask`; em `hr.parameters`, editado em Configurações → Parâmetros → RH com `settings.edit`, auditado), **quando** está `Ask` e o funcionário marca pelo celular ou pelo computador, **então** o navegador pede a localização; com a permissão, a marcação grava `lat` e `lng` com precisão reduzida (arredondados em 4 casas) e a precisão em metros; sem a permissão ou com erro, a marcação é gravada sem localização e com a marca "Sem localização" (nunca bloqueia); a localização é tratada como indício, nunca invalida a marcação.
10. **[Segurança]** **Dado** a primeira marcação com a geolocalização ligada, **quando** o navegador vai pedir a localização, **então** o app mostra antes o aviso "Para que serve a localização: registrar onde a marcação foi feita, como indício. Só o RH com permissão vê. Você pode negar." com "Entendi e continuar"; a ciência fica registrada (funcionário, data e hora, versão do texto) e o aviso volta quando o texto muda; o texto fica na ajuda da tela.
11. **[Segurança]** **Dado** `lat`, `lng` e precisão, **quando** gravados, **então** são `[PersonalData]` (mascarados na auditoria, fora do log e de qualquer evento), saem só por `GET /api/v1/hr/employees/{id}/time-entries/sensitive` com `hr.view_sensitive`, registrando `SensitiveRead` e com `Cache-Control: no-store`; o tablet da fábrica registrado não pede localização (o aparelho é fixo).

**Consulta pelo RH**
12. **Dado** `hr.view`, **quando** abro o funcionário → aba "Ponto" (ou RH → Ponto → Marcações, com filtro de período, funcionário e centro na URL), **então** vejo, por dia, as marcações com hora local, tipo, origem (App, PIN no {aparelho}, Manual), a marca "Sem localização" quando cabe e o ícone de localização só para quem tem `hr.view_sensitive`; o JSON para quem não tem a permissão não tem `lat`, `lng` nem precisão.
13. **Dado** o funcionário com login completo, **quando** abre "Meu ponto" → "Minhas marcações", **então** vê as próprias marcações dos últimos 60 dias, inclusive a própria localização, e nunca as de outro funcionário (o id vem da sessão; teste de IDOR).
14. **Dado** uma marcação, **quando** alguém tenta editá-la ou apagá-la pela API, **então** não existe rota para isso: marcação não é editada nem apagada, só desconsiderada por ajuste aprovado (RH-01b); o teste de arquitetura confere que `TimeEntry` não tem método de alteração de `at` nem de `type`.

**Permissões (3 camadas)**
15. **Dado** o módulo `hr` não liberado, **quando** chamo qualquer endpoint desta story, **então** 403 `module_not_released` e o front mostra `ModuleNotReleased`; a consulta das marcações de outros funcionários exige grupo com `hr` e `hr.view` (403 `forbidden`); "Meu ponto" segue o CA 6. Todo endpoint tem `RequireModule` e `perm:` ou está na lista versionada de autoatendimento, conferido por teste de arquitetura.
16. **Dado** qualquer marcação, ciência da geolocalização ou mudança do parâmetro, **quando** abro o histórico, **então** vejo quem, quando, de qual aparelho e o IP, com a localização mascarada (canário: lat/lng não aparecem em `audit.audit_log` nem no log).

## Regras de negócio e técnicas
- **Hora do servidor:** `at` sempre do `TimeProvider` do servidor, em UTC; exibido em America/Sao_Paulo. O dia da marcação é a data local (`BusinessTime`).
- **Sequência sugerida:** calculada pela última marcação válida do dia; o tipo enviado é aceito mesmo fora da sequência (inconsistência tratada no espelho, RH-01c).
- **Imutável:** marcação não é editada nem apagada; ajuste (RH-01b) desconsidera a original com motivo e cria outra.
- **Tabela (extensão do 03):** `hr.time_entries` + `status` (`Valid`/`Voided`), `source` (`App`/`Pin`/`Manual`), `recorded_by_user_id`, `device_id` (id do aparelho do Identity, só o id), `accuracy_m`, `lat`/`lng` como `numeric(9,6)` arredondados em 4 casas, `created_at`. Índice `(employee_id, at)`. `hr.time_clock_geo_consents` (`employee_id`, `accepted_at`, `text_version`).
- **Parâmetro:** `hr.parameters` (RH-02a), chave `hr.time_clock_geolocation` (seed `Off`).
- **Autoatendimento (ADR 0035):** funcionário de `identity.users.employee_id` pelo `IUserEmployeeLink`; situação pelo `IEmployees`; filtro da RH-02b.
- **Fim da sessão por PIN:** depois do `POST` de marcação na sessão `amr = pin`, o Identity revoga a família de refresh daquela sessão (adendo à ADR 0026).
- Guid v7; `IAuditable` em `TimeEntry`; dado pessoal com `[PersonalData]` e allowlist (ADR 0016).

## Permissões, eventos e tabelas
- **Permissões:** `hr.view` (marcações dos outros), `hr.view_sensitive` (localização), `settings.edit` (parâmetro); "Meu ponto" sem ação no grupo (camada 1 + vínculo ativo, ADR 0035). Nenhuma ação nova.
- **Eventos:** nenhum (o espelho é calculado na RH-01c dentro do HR).
- **Tabelas:** `hr.time_entries` (extensão), `hr.time_clock_geo_consents` (nova), `hr.parameters` (+ `hr.time_clock_geolocation`).

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 8 (autoatendimento com o mesmo layout no `/fab` e no celular; bloco "Dados protegidos" padrão para a localização).
- **`/fab/ponto`** (tablet, layout `/fab`, sem dock): faixa de controle interno, nome, relógio do servidor, jornada do dia, marcações de hoje e o botão grande da próxima marcação; "Outra marcação"; comprovante grande; volta à lista de nomes depois de marcar.
- **`/app/meu-ponto`** (backoffice e celular): o mesmo conteúdo, mais "Minhas marcações".
- **RH → Ponto → Marcações** (`/app/rh/ponto/marcacoes`) e a aba "Ponto" no funcionário (`/app/rh/funcionarios/$id?aba=ponto`). Entradas no `modules.ts`: "Meu ponto" para todos os ligados a funcionário; Ponto na área Pessoas.
- **Estados:** carregando; vazio ("Nenhuma marcação hoje." com o botão da próxima marcação); sem vínculo (CA 6); sem conexão (CA 8); erro com "Tentar de novo"; "Dados protegidos" para a localização.
- **Propositivo:** a próxima marcação já vem sugerida; depois de uma marcação fora de ordem, o atalho para pedir o ajuste.
- **400 px e teclado:** "Meu ponto" cabe sem rolar a 400 px; o botão é acionável por teclado; botões de `ErrorState`/`EmptyState` com 44 px; E2E de 400 px.
- **Ajuda:** `Ajuda/fab-ponto.md`, `Ajuda/meu-ponto.md`, `Ajuda/rh-ponto-marcacoes.md` e `Ajuda/rh-funcionario-ponto.md` (aba `ponto`), todas com o aviso "Controle interno de jornada; não substitui o registro legal de ponto (REP-P, Portaria 671)"; `<Dica>` em "Hora do servidor", "Sem localização" e "Controle interno".

## Plano de testes
- **Unidade:** próxima marcação sugerida por sequência (CA 2, 4); arredondamento da localização (CA 9).
- **Integração (Testcontainers):** faixa e texto do comprovante (CA 1); marcação com hora do servidor e corpo sem `employeeId` (CA 3, 6); duplo toque e `Idempotency-Key` (CA 5); afastado, desligado, sem vínculo e lista versionada (CA 6); sessão por PIN: só os dois endpoints, sessão encerrada depois de marcar, PIN apagado ao ganhar `hr.view_sensitive` (CA 7); geolocalização ligada, negada e desligada (CA 9); ciência registrada e nova versão do texto (CA 10); localização fora do JSON sem `hr.view_sensitive`, `SensitiveRead` e `no-store` (CA 11, 12); IDOR das próprias marcações (CA 13); três camadas (CA 15); canário (CA 16).
- **Arquitetura:** `TimeEntry` sem alteração de `at`/`type` (CA 14); DTOs de lista sem propriedade de localização; rotas `/api/v1/me/hr/*` na lista versionada e endpoints com `[PinSessionAllowed]` revisados.
- **E2E (Playwright):** marcar entrada e intervalo no `/fab/ponto` a 400 px e ver o tablet voltar à lista de nomes; RH sem `hr.view_sensitive` não recebe a localização na rede.

## Fora do escopo
- Lançamento manual e ajustes com aprovação: RH-01b. Espelho, banco de horas e fechamento: RH-01c.
- Ponto como registro oficial (REP-P, AFD, AEJ, comprovante legal, Portaria 671): story pós-MVP da Fase 9 (R-RH-7, P-26).
- Biometria, reconhecimento facial, crachá NFC e cerca geográfica (bloqueio fora da área): fora do MVP.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, D15 e tabela 3.3, pontos 1–5):
1. **Autoatendimento:** "Meu ponto" em `/api/v1/me/hr/*`, camada 1 + vínculo com funcionário ativo, sem ação no grupo; ADR 0035 e adendo à ADR 0026 (`[PinSessionAllowed]`, sessão por PIN encerrada depois de marcar, PIN perdido com `hr.view_sensitive`) (CA 6, 7).
2. **Geolocalização:** desligada por padrão; ciência registrada; precisão reduzida; visível só com `hr.view_sensitive`; tratada como indício (CA 9 a 11).
3. **Sem marcação sem rede**, coerente com a P-23 (CA 8).
4. **Intervalo mínimo de 1 min** entre marcações iguais; "Minhas marcações" de 60 dias (CA 5, 13).
5. **Uso legal:** o ponto do app entra **sem ser registro oficial**, como controle interno, com aviso claro na tela e na ajuda (CA 1); deixa de ser pré-condição do Marco 6.1; o registro oficial (REP-P / Portaria 671) fica para a story pós-MVP da Fase 9 e a pendência P-26.

Decisões da revisão aplicadas: §4.2 (RH-01a: lista `[PinSessionAllowed]` e perda do PIN, CA 7); §4.4 padrão 8.

**Pré-condição externa:** a Konebras cria um usuário (e-mail de apelido quando preciso) para cada funcionário que vai marcar ponto e define os tablets registrados; não bloqueia o `Ready`.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva) para `hr.time_entries` e `hr.time_clock_geo_consents`.
- [ ] Permissões só do `ModuleCatalog`; autoatendimento pela ADR 0035; `[PinSessionAllowed]` pelo adendo à ADR 0026.
- [ ] Telas com estados de carregando, vazio, erro, sem vínculo e sem conexão, e a faixa de controle interno.
- [ ] Ajuda de cada rota e da aba, com o aviso de controle interno; teste de cobertura passando.
- [ ] Padrões e próximo passo propositivos aplicados.
- [ ] Auditoria ligada, com `[PersonalData]` e `SensitiveRead`.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `TimeEntry` (Domain) com a sequência sugerida; ciência da geolocalização; parâmetro no `HrParameters`.
- [ ] Endpoints `/api/v1/me/hr/time-entries` (marcar, hoje, listar as minhas) e `/api/v1/hr/employees/{id}/time-entries` (+ `/sensitive`).
- [ ] `[PinSessionAllowed]` nos dois endpoints do tablet e o fim da sessão por PIN depois da marcação (PRD-03b, adendo à ADR 0026).
- [ ] Telas `/fab/ponto`, `/app/meu-ponto`, Marcações e aba Ponto; ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (autoatendimento, sessão por PIN, IDOR, geolocalização), design (tela do tablet a 56 px), @qa.
- Gate: CA 1, 3, 5, 6, 7, 11 e 13 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas do CLAUDE.md) que se aplicam | OK (ajustado: CA 6 e 7) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: depois do Marco 6.2, com a RH-02b) |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** a marcação nunca aceita `employeeId` no corpo nem horário do aparelho; a marcação pelo PIN entrou na lista permitida da PRD-03b sem abrir o resto do RH; localização só em `/sensitive`; a dependência da PRD-03b ficou opcional.
- **Correções da revalidação (21/10/2026):** decisão do PO sobre a D15: ponto como controle interno, com a faixa na tela, no comprovante e na ajuda (CA 1, novo), sem pré-condição jurídica; pontos 1 a 5 decididos: `/api/v1/me/hr/*` pela ADR 0035 (CA 6), só os dois endpoints com `[PinSessionAllowed]`, sessão por PIN encerrada no servidor depois de marcar e PIN perdido com `hr.view_sensitive` (CA 7), sem marcação sem rede (CA 8), geolocalização desligada com ciência registrada e tratada como indício (CA 9, 10), 1 min e 60 dias (CA 5, 13); a PRD-03b deixou de ser opcional (Fase 3 concluída antes); dependência da RH-02b (filtro de autoatendimento) e da RH-02a (parâmetros); CAD-04c → CAD-04c1; ordem 6, Marco 6.1; CAs renumerados (1 a 15 viraram 2 a 16).
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: usuários dos funcionários e tablets (Konebras).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da RH-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D15 com o ponto não oficial, tabela 3.3 pontos 1–5, ADR 0035, adendo à ADR 0026, §4.2, §4.4 padrão 8); nova ordem (Marco 6.1, ordem 6, depois da folha); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
