# RH-01a — Ponto: marcação pelo app (Meu ponto), geolocalização opcional e consulta das marcações

| Campo | Valor |
|---|---|
| ID | RH-01a |
| Título | "Meu ponto": o funcionário registra entrada, intervalo e saída pelo app (tablet da fábrica com PIN, celular ou computador), com a hora do servidor, comprovante na tela, geolocalização opcional por parâmetro do tenant; o RH consulta as marcações por funcionário e por dia, com a localização só com `hr.view_sensitive` |
| Tamanho | M (quebra da RH-01 em 01a marcação, 01b lançamento manual e ajustes, 01c espelho, banco de horas e fechamento) |
| Status | Draft |
| Requisitos | R-RH-4 (parte: marcação pelo PWA com geolocalização opcional) · PRD §6 (LGPD, Usabilidade) · R-SEC-5 · R-PRP-1 · R-PRP-2 |
| Dependências | CAD-04b (funcionário e vínculo com o usuário, `IUserEmployeeLink`), CAD-04c (situação `OnLeave` no `IEmployees`), CAD-04a (jornada do funcionário), PRD-03a (superfície `/fab`), PRD-03b (aparelho registrado e sessão por PIN, opcional), FND-09e; Fase 0 (FND-06c, FND-07, FND-14) |
| Bloqueia | RH-01b, RH-01c, FLH-02b, LGPD-01a (exportação inclui marcações) |
| Ordem de execução | 1 (Marco 6.1 "Ponto") |

## User story
Como **funcionário**, quero **registrar a minha entrada, o intervalo e a saída com um toque, no tablet da fábrica ou no celular**, e como **RH**, quero **ver as marcações de cada funcionário por dia**, para **ter o registro de jornada que vai alimentar o espelho, o banco de horas e a folha**.

## Contexto e referências
- PRD R-RH-4 ("Ponto básico: marcação pelo PWA (com geolocalização opcional) ou lançamento manual pelo RH, espelho mensal, banco de horas simples, ajustes com justificativa e aprovação"); R-RH-7 (ponto legal da Portaria 671 é P, Fase 9); PRD §6 LGPD ("dados pessoais de funcionários com acesso restrito ao RH").
- `03-modelo-dados` → `hr.time_entries` (`employee_id`, `at`, `type` In/Out/BreakStart/BreakEnd, `source` PWA/Manual, `lat`, `lng`, `justification`, `approved_by`); `hr.work_schedules` (CAD-04a, dias e horários da jornada).
- `ModuleCatalog`: `hr` com `view`, `create`, `edit`, `approve` (significado: "ajustes de ponto"), `delete` e `view_sensitive`. Seed: o grupo RH tem `view`, `create`, `edit` e `view_sensitive`; `approve` fica com o Proprietário (ADR 0008).
- `02-arquitetura` §4 (PWA `/fab`, leitura por câmera, offline tolerante só para apontamento); PRD-03b (aparelho registrado, PIN, `amr = pin` limitado ao `shopfloor`, fila offline do apontamento).
- ADR 0016 (rota `/sensitive`, `SensitiveRead`, `[PersonalData]`, `no-store`), ADR 0018 (retenção do funcionário), ADR 0019 §2 e §3 (`xmin`, `Idempotency-Key`), ADR 0021 (mensagens), ADR 0016 adendo M6 (parâmetros do módulo dono).
- CLT art. 74 §2 (registro de jornada obrigatório acima de 20 empregados; a Konebras tem de 10 a 15). O ponto básico **não** é o REP-P da Portaria 671 (Fase 9): o uso como registro oficial é ponto em aberto (5).
- Protótipo: o app do operador usa o layout `/fab` (botões ≥ 56 px, uso com luva).

## Critérios de aceite
**Meu ponto**
1. **Dado** um usuário ligado a um funcionário ativo (CAD-04b) e o módulo `hr` liberado no tenant, **quando** abre "Meu ponto" (`/fab/ponto` no tablet ou `/app/meu-ponto` no backoffice e no celular), **então** vê o nome ("Olá, {primeiro nome}"), a data e a hora do servidor em America/Sao_Paulo, a jornada do dia (ex.: "08:00–17:48, intervalo 1 h"), as marcações de hoje e um botão grande (≥ 56 px) com a **próxima marcação sugerida** pela sequência Entrada → Saída para o intervalo → Volta do intervalo → Saída.
2. **Dado** o botão sugerido, **quando** toco "Registrar entrada" (ou o verbo da marcação sugerida), **então** nasce a marcação em `hr.time_entries` com `at` = instante do servidor (UTC), `type`, `source` = `App`, o usuário que registrou e o aparelho (`device_id`, quando o aparelho é registrado na PRD-03b); o horário nunca vem do aparelho nem é digitado; a tela mostra o comprovante "Entrada registrada às 08:02 de 21/10/2026" e a mensagem de sucesso no topo diz "Entrada registrada" (ADR 0021).
3. **Dado** que esqueci uma marcação, **quando** abro "Outra marcação", **então** posso escolher qualquer um dos quatro tipos, com a hora do servidor; a sequência fora de ordem é aceita e vira inconsistência no espelho (RH-01c), e a tela sugere "Esqueceu uma marcação? Peça o ajuste ao RH." com o atalho para o pedido de ajuste (RH-01b).
4. **[Segurança]** **Dado** um duplo toque, **quando** chegam duas marcações do mesmo funcionário e tipo com menos de 1 minuto entre elas (proposta), **então** só a primeira é gravada e a segunda responde 409 "Você já registrou {tipo} às {hora}."; o comando exige `Idempotency-Key` (ADR 0019 §3) e o reenvio com a mesma chave devolve o comprovante original.
5. **[Segurança]** **Dado** um usuário sem funcionário ligado, ou ligado a funcionário desligado ou afastado (`IEmployees`, CAD-04c), **quando** tenta marcar, **então** a API responde 403 `employee_not_active` e a tela mostra "Seu usuário não está ligado a um funcionário ativo. Fale com o RH."; a marcação é sempre do funcionário do usuário logado: o corpo do comando não aceita `employeeId` (`UnmappedMemberHandling.Disallow`, 400).
6. **Dado** a sessão aberta por PIN no tablet registrado (PRD-03b), **quando** o operador abre "Meu ponto", **então** consegue marcar: o endpoint de marcação própria entra na lista permitida da sessão `amr = pin` (proposta; os demais endpoints do RH continuam fora), e depois da marcação o tablet volta à lista de nomes em 5 s (proposta), para o próximo funcionário.
7. **Dado** o aparelho sem rede, **quando** o funcionário toca para marcar, **então** vê "Sem conexão. O ponto precisa de rede para usar a hora do servidor. Tente de novo ou avise o RH." (proposta: a marcação não entra na fila offline da PRD-03b, para não depender do relógio do aparelho; Pontos em aberto 3).

**Geolocalização opcional**
8. **Dado** o parâmetro do HR `hr.time_clock_geolocation` (proposta: `Off` (padrão) ou `Ask`; em `hr.parameters`, editado em Configurações → Parâmetros com `settings.edit`, auditado), **quando** está `Ask` e o funcionário marca pelo celular ou pelo computador, **então** o navegador pede a localização; com a permissão, a marcação grava `lat`, `lng` (arredondados em 4 casas, proposta) e a precisão em metros; sem a permissão ou com erro, a marcação é gravada sem localização e com a marca "Sem localização" (nunca bloqueia, proposta; Pontos em aberto 2).
9. **[Segurança]** **Dado** a primeira marcação com a geolocalização ligada, **quando** o navegador vai pedir a localização, **então** o app mostra antes o aviso "Para que serve a localização: registrar onde a marcação foi feita. Só o RH com permissão vê. Você pode negar." com "Continuar"; o texto fica na ajuda da tela (o registro da ciência é ponto em aberto 2).
10. **[Segurança]** **Dado** `lat`, `lng` e precisão, **quando** gravados, **então** são `[PersonalData]` (mascarados na auditoria, fora do log e de qualquer evento), saem só por `GET /api/v1/hr/employees/{id}/time-entries/sensitive` com `hr.view_sensitive`, registrando `SensitiveRead` e com `Cache-Control: no-store`; o tablet da fábrica registrado não pede localização (o aparelho é fixo).

**Consulta pelo RH**
11. **Dado** `hr.view`, **quando** abro o funcionário → aba "Ponto" (ou RH → Ponto → Marcações, com filtro de período, funcionário e centro na URL), **então** vejo, por dia, as marcações com hora local, tipo, origem (App, PIN no {aparelho}, Manual), a marca "Sem localização" quando cabe e o ícone de localização só para quem tem `hr.view_sensitive`; o JSON para quem não tem a permissão não tem `lat`, `lng` nem precisão.
12. **Dado** o funcionário, **quando** abre "Meu ponto" → "Minhas marcações", **então** vê as próprias marcações dos últimos 60 dias (proposta), inclusive a própria localização, e nunca as de outro funcionário (o id vem da sessão; teste de IDOR).
13. **Dado** uma marcação, **quando** alguém tenta editá-la ou apagá-la pela API, **então** não existe rota para isso: marcação não é editada nem apagada, só desconsiderada por ajuste aprovado (RH-01b); o teste de arquitetura confere que `TimeEntry` não tem método de alteração de `at` nem de `type`.

**Permissões (3 camadas)**
14. **Dado** o módulo `hr` não liberado, **quando** chamo qualquer endpoint desta story, **então** 403 `module_not_released` e o front mostra `ModuleNotReleased`; a consulta das marcações de outros funcionários exige grupo com `hr` e `hr.view` (403 `forbidden`); "Meu ponto" segue a regra de autoatendimento decidida no ponto em aberto 1. Todo endpoint tem `RequireModule` e `perm:` (ou a marca de autoatendimento aprovada), conferido por teste de arquitetura.
15. **Dado** qualquer marcação ou mudança do parâmetro de geolocalização, **quando** abro o histórico, **então** vejo quem, quando, de qual aparelho e o IP, com a localização mascarada (canário: lat/lng não aparecem em `audit.audit_log` nem no log).

## Regras de negócio e técnicas
- **Hora do servidor:** `at` sempre do `TimeProvider` do servidor, em UTC; exibido em America/Sao_Paulo. O dia da marcação é a data local (`BusinessTime`).
- **Sequência sugerida:** calculada pela última marcação válida do dia; o tipo enviado é aceito mesmo fora da sequência (inconsistência tratada no espelho, RH-01c).
- **Imutável:** marcação não é editada nem apagada; ajuste (RH-01b) desconsidera a original com motivo e cria outra.
- **Tabela (proposta de extensão do 03):** `hr.time_entries` + `status` (`Valid`/`Voided`; `Pending` e `Rejected` entram na RH-01b), `source` (`App`/`Pin`/`Manual`), `recorded_by_user_id`, `device_id` (id do aparelho do Identity, só o id), `accuracy_m`, `lat`/`lng` como `numeric(9,6)` arredondados, `created_at`. Índice `(employee_id, at)`.
- **Parâmetro:** `hr.parameters` (dono HR, ADR 0016 M6), chave `hr.time_clock_geolocation` (seed `Off`).
- **Autoatendimento:** o funcionário vem de `identity.users.employee_id` por `Identity.Contracts` (o mesmo ponto em aberto 5 da PRD-03a); a situação vem do `IEmployees`.
- Guid v7; `IAuditable` em `TimeEntry`; dado pessoal com `[PersonalData]` e allowlist (ADR 0016).

## Permissões, eventos e tabelas
- **Permissões:** `hr.view` (marcações dos outros), `hr.view_sensitive` (localização), `settings.edit` (parâmetro); "Meu ponto" pela regra do ponto em aberto 1. Nenhuma ação nova sem decisão.
- **Eventos:** nenhum (o espelho é calculado na RH-01c dentro do HR).
- **Tabelas:** `hr.time_entries` (extensão proposta), `hr.parameters` (nova, se ainda não existir).

## Telas e estados
- **`/fab/ponto`** (tablet, layout `/fab`, sem dock): nome, relógio do servidor, jornada do dia, marcações de hoje e o botão grande da próxima marcação; "Outra marcação"; comprovante grande.
- **`/app/meu-ponto`** (backoffice e celular): o mesmo conteúdo, mais "Minhas marcações".
- **RH → Ponto → Marcações** (`/app/rh/ponto/marcacoes`) e a aba "Ponto" no funcionário (`/app/rh/funcionarios/$id?aba=ponto`).
- **Estados:** carregando; vazio ("Nenhuma marcação hoje." com o botão da próxima marcação); sem vínculo (CA 5); sem conexão (CA 7); erro com "Tentar de novo"; "Dados protegidos" para a localização.
- **Propositivo:** a próxima marcação já vem sugerida; depois de uma marcação fora de ordem, o atalho para pedir o ajuste.
- **400 px e teclado:** "Meu ponto" cabe sem rolar a 400 px; o botão é acionável por teclado; E2E de 400 px.
- **Ajuda:** `Ajuda/fab-ponto.md`, `Ajuda/meu-ponto.md`, `Ajuda/rh-ponto-marcacoes.md` e `Ajuda/rh-funcionario-ponto.md` (aba `ponto`); `<Dica>` em "Hora do servidor" e "Sem localização".

## Plano de testes
- **Unidade:** próxima marcação sugerida por sequência (CA 1, 3); arredondamento da localização (CA 8).
- **Integração (Testcontainers):** marcação com hora do servidor e corpo sem `employeeId` (CA 2, 5); duplo toque e `Idempotency-Key` (CA 4); afastado, desligado e sem vínculo (CA 5); sessão por PIN na lista permitida (CA 6); geolocalização ligada, negada e desligada (CA 8); localização fora do JSON sem `hr.view_sensitive`, `SensitiveRead` e `no-store` (CA 10, 11); IDOR das próprias marcações (CA 12); três camadas (CA 14); canário (CA 15).
- **Arquitetura:** `TimeEntry` sem alteração de `at`/`type` (CA 13); DTOs de lista sem propriedade de localização.
- **E2E (Playwright):** marcar entrada e intervalo no `/fab/ponto` a 400 px; RH sem `hr.view_sensitive` não recebe a localização na rede.

## Fora do escopo
- Lançamento manual e ajustes com aprovação: RH-01b. Espelho, banco de horas e fechamento: RH-01c.
- Ponto legal da Portaria 671 (REP-P, AFD, AEJ, comprovante legal): Fase 9 (R-RH-7).
- Biometria, reconhecimento facial, crachá NFC e cerca geográfica (bloqueio fora da área): fora do MVP.

## Pontos em aberto
1. **Permissão do autoatendimento** (pré-condição, PO + arquiteto, ADR): "Meu ponto" sem ação no grupo (proposta: camada 1, módulo `hr` liberado, mais usuário ligado a funcionário ativo, como as rotas `/me`), ou uma ação específica. Uma ação no `hr` daria `hr.view` a todos (marcar qualquer ação marca `view`, ADR 0003), o que expõe a lista de funcionários.
2. **Geolocalização** (PO + jurídico): padrão desligado; arredondamento em 4 casas; marcação sem localização aceita; registro da ciência do aviso; retenção da localização (proposta: a mesma do ponto, ADR 0018).
3. **Ponto sem rede** (PO): proposta de não aceitar marcação offline; a alternativa é a fila da PRD-03b com o relógio do aparelho e a marca "Horário do aparelho".
4. Intervalo mínimo entre marcações iguais (1 minuto, proposta) e retenção de "Minhas marcações" na tela (60 dias, proposta).
5. **Uso legal do ponto básico** (contadora/jurídico): o ponto básico substitui o controle atual como registro oficial de jornada, ou é só controle interno até o ponto legal (Fase 9)?

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva) para `hr.time_entries` e `hr.parameters`.
- [ ] Permissões só do `ModuleCatalog` (ou ADR do autoatendimento, ponto 1).
- [ ] Telas com estados de carregando, vazio, erro, sem vínculo e sem conexão.
- [ ] Ajuda de cada rota e da aba; teste de cobertura passando.
- [ ] Padrões e próximo passo propositivos aplicados.
- [ ] Auditoria ligada, com `[PersonalData]` e `SensitiveRead`.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `TimeEntry` (Domain) com a sequência sugerida; `HrParameters` e seed.
- [ ] Endpoints `/api/v1/hr/me/time-entries` (marcar, listar as minhas) e `/api/v1/hr/employees/{id}/time-entries` (+ `/sensitive`).
- [ ] Inclusão do endpoint de marcação na lista permitida da sessão por PIN (PRD-03b).
- [ ] Telas `/fab/ponto`, `/app/meu-ponto`, Marcações e aba Ponto; ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (autoatendimento, IDOR, geolocalização), design (tela do tablet a 56 px), @qa.
- Gate: CA 2, 4, 5, 10 e 12 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas do CLAUDE.md) que se aplicam | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** a marcação nunca aceita `employeeId` no corpo nem horário do aparelho (CA 2, 5); a marcação pelo PIN entra como proposta na lista permitida da PRD-03b, sem abrir o resto do RH (CA 6); localização só em `/sensitive` (CA 10); a dependência da PRD-03b ficou opcional (sem ela, o tablet usa o login completo).
- **Pré-condições para `Ready`:** ponto em aberto 1 (autoatendimento, com ADR); PRD-03a e CAD-04c concluídas.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da RH-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
