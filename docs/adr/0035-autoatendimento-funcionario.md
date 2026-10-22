# ADR 0035: Autoatendimento do funcionário

- Status: Aceito (21/10/2026). O PO (John) aprovou a decisão D15 da revisão pré-Fases 4 a 8 (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`), com uma mudança: **o ponto do app é controle interno, não registro oficial**, e deixa de ser pré-condição do Marco 6.1. Aprovou também os pontos 1 a 4, 14 e 20 a 22 da tabela 3.3 e o padrão 8 da seção 4.4.
- Contexto: "Meu ponto" (RH-01a), "Meu espelho" (RH-01c) e "Meus holerites" (RH-02b) são do funcionário sobre os próprios dados. Pela regra das 3 camadas, cada um exigiria uma ação no grupo (`hr.view`), o que daria ao funcionário a consulta dos outros funcionários, ou um grupo "Funcionários" com ação que não diz nada. A revisão também apontou o uso legal do ponto do app (CLT art. 74 §2; Portaria 671: REP-P registrado ou REP-A por acordo coletivo): a recomendação era tornar o parecer jurídico pré-condição do Marco 6.1; o PO preferiu lançar o ponto como controle interno, com aviso claro, e deixar o registro oficial para depois do MVP.
- Complementa as ADRs 0003, 0008, 0016 (`/sensitive`, `SensitiveRead`), 0018 (retenção), 0026 (PIN no tablet, adendo de 21/10/2026) e 0009 (teste de rotas, adendo de 21/10/2026).

## Decisão

### 1. Rotas de autoatendimento
- O autoatendimento do funcionário fica em **`/api/v1/me/hr/*`** (ex.: `GET /me/hr/time-clock`, `POST /me/hr/time-entries`, `GET /me/hr/time-entries`, `GET /me/hr/timesheets/{period}`, `POST /me/hr/timesheets/{period}/acknowledge`, `GET /me/hr/payslips`, `GET /me/hr/payslips/{id}/pdf`).
- **Liberadas pela camada 1** (módulo `hr` liberado no tenant, `RequireModule("hr")`) **e pelo vínculo do usuário com um funcionário ativo** (`IUserEmployeeLink` + `IEmployees`), **sem ação no grupo**. Vínculo ausente, funcionário desligado ou afastado: 403 `employee_not_active`, com "Seu usuário não está ligado a um funcionário ativo. Fale com o RH."
- O endpoint declara a marca **`RequireSelfService("hr")`** (no lugar de `perm:`), e o teste de arquitetura das rotas (ADR 0009, adendo, D19) aceita só endpoints de `/api/v1/me/*` com essa marca.
- **O funcionário vem sempre da sessão**: nenhum comando aceita `employeeId` (`UnmappedMemberHandling.Disallow`, 400); teste de IDOR em todas as rotas.
- Consultar os dados de **outros** funcionários continua exigindo `hr.view` (e `hr.view_sensitive` para o sensível).

### 2. Ponto do app: controle interno, não registro oficial
- O ponto do app é **controle interno de jornada**. Ele **não substitui** o registro de ponto exigido pela legislação (CLT art. 74 §2, Portaria 671) e **não é REP-P nem REP-A**.
- **Aviso fixo** na tela "Meu ponto", no "Meu espelho", no espelho em PDF e na ajuda (`Ajuda/`): "Controle interno de jornada. Este registro não substitui o registro de ponto exigido pela legislação." Teste de texto confere o aviso nas três telas e no PDF.
- O uso legal **deixa de ser pré-condição do Marco 6.1**. Fica a pendência **P-26** no `06-pendencias-riscos.md` e a story futura pós-MVP **"Ponto como registro oficial (REP-P / Portaria 671)"** na Fase 9 do backlog.
- Regras do ponto: hora sempre do servidor; marcação imutável (só desconsiderada por ajuste aprovado, RH-01b); intervalo mínimo de 1 min entre marcações do mesmo tipo (409); "Minhas marcações" dos últimos 60 dias; **sem marcação sem rede** (coerente com a P-23).

### 3. Geolocalização
- **Desligada por padrão** (`hr.time_clock_geolocation = Off`); ligada só pelo parâmetro, com `settings.edit`, auditado.
- Ligada, o funcionário vê antes o aviso de finalidade e a **ciência fica registrada** (quem, quando, texto da versão); sem permissão do navegador, a marcação entra com "Sem localização" e nunca é bloqueada.
- **Precisão reduzida** (4 casas), `[PersonalData]`, visível só com `hr.view_sensitive` pela rota `/sensitive` com `SensitiveRead`; o próprio funcionário vê a dele. É **indício**, nunca regra automática de recusa.
- O tablet registrado da fábrica não pede localização.

### 4. Holerite
- **"Meus holerites" autenticado é o canal** do holerite: o PDF só sai pela rota autenticada do próprio funcionário, com `Cache-Control: no-store`.
- O **e-mail só avisa** ("Seu holerite de {mês} está disponível no BemAqui."), **sem anexo, sem valor e sem link com token**; o link leva ao login.
- CPF mascarado 3+2 no holerite.
- Holerite e espelho **exigem login completo**: não entram na sessão por PIN (§5).

### 5. Tablet da fábrica (PIN)
- Só a **marcação** de "Meu ponto" funciona na sessão por PIN (adendo de 21/10/2026 à ADR 0026): os endpoints `GET /me/hr/time-clock` e `POST /me/hr/time-entries` entram na lista `[PinSessionAllowed]`.
- **Marcar no tablet encerra a sessão por PIN no servidor** (a sessão e o refresh dela são revogados na mesma transação da marcação), e o tablet volta à lista de nomes. O próximo funcionário entra com o próprio PIN.
- Quem ganha `hr.view_sensitive` perde o PIN (regra da ADR 0026 §2, sem mudança).

### 6. Layout
- "Meu ponto", "Meu espelho" e "Meus holerites" têm o **mesmo layout** no `/fab` e no celular (`/app/meu-ponto`, `/app/meu-espelho`, `/app/meus-holerites`), com botões de 56 px e o bloco "Dados protegidos" padrão (padrão 8 das Fases 4 a 8).
- "Meu espelho" tem "Li e confirmo", com quem e quando registrados.

## Consequências
- **RH-01a** implementa as seções 1, 2, 3 e 5 e o adendo à ADR 0026; **RH-01c** o "Meu espelho" e o aviso no PDF; **RH-02b** o "Meus holerites" e o aviso por e-mail.
- **FND-06d** (menu): o item "Meu ponto" aparece para o usuário com vínculo ativo, mesmo sem grupo com `hr`, quando o módulo está liberado.
- `06-pendencias-riscos.md` ganha a P-26; `05-backlog` ganha a story pós-MVP na Fase 9.

## Alternativas consideradas
- **Ação `hr.self_service` no grupo:** rejeitada; todo funcionário precisaria de um grupo só para ver os próprios dados, e a ação seria marcada em massa sem decisão real.
- **Parecer jurídico como pré-condição do Marco 6.1** (recomendação da revisão): rejeitada pelo PO; o ponto entra como controle interno, com aviso, e o registro oficial vira story própria.
- **Holerite anexado ao e-mail:** rejeitada; o e-mail não é canal seguro para salário e CPF.
