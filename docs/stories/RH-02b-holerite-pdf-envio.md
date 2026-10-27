# RH-02b — Folha lançada: holerite em PDF, "Meus holerites" e aviso ao funcionário

| Campo | Valor |
|---|---|
| ID | RH-02b |
| Título | Holerite em PDF no layout padrão (empresa, funcionário com CPF mascarado 3+2, competência, proventos, descontos, bases de INSS, FGTS e IRRF, FGTS do mês e líquido) pelo `IPdfRenderer`; download individual e em lote pelo RH; **"Meus holerites"** autenticado como o canal do funcionário (`/api/v1/me/hr/*`, ADR 0035); e-mail só de aviso, sem anexo nem valor, depois da folha fechada; situação do aviso e da primeira leitura por holerite |
| Tamanho | M (quebra da RH-02) |
| Status | Done (pré-condição externa: usuários dos funcionários) |
| Requisitos | R-RH-5 (parte: gerar holerite PDF, enviar por e-mail) · R-COM-1 · R-COM-3 · PRD §6 LGPD · R-SEC-5 · R-PRP-2 |
| Dependências | RH-02a (folha, holerites, linhas), RH-02c (holerite só com a folha fechada), CAD-04b (`IUserEmployeeLink`, vínculo usuário ↔ funcionário), COM-03 (`IPdfRenderer`, `StoreGeneratedAsync`, ADR 0019 §7 e 0022), CAD-06 (perfil e logo da empresa), FND-08 (e-mail) |
| Bloqueia | RH-01a (reusa o autoatendimento `/api/v1/me/hr/*` criado aqui), FLH-02a (o holerite calculado usa o mesmo layout) |
| Ordem de execução | 4 (Marco 6.2 "Folha lançada") |

## User story
Como **RH**, quero **gerar o holerite de cada funcionário em PDF e avisar todos com um clique**, e como **funcionário**, quero **ver o meu holerite no sistema, com o meu login, sem que outra pessoa veja o meu salário**, para **acabar com o holerite impresso e manter o sigilo**.

## Contexto e referências
- PRD R-RH-5; backlog RH-02b, CA "holerite com layout padrão (proventos, descontos, bases INSS/FGTS/IRRF, líquido)".
- ADR 0022 (QuestPDF atrás do `IPdfRenderer`, Archivo embutida, teste na imagem Alpine, limites, sem URL externa); ADR 0019 §7 (`StoreGeneratedAsync`, dono de anexo); FND-08 (fila, 3 tentativas, templates Fluid).
- PRD §6 LGPD; `03-modelo-dados` → `shared.email_logs.to` é `[PersonalData]`; `identity.users.employee_id` (vínculo, CAD-04b).
- P-21 (licença Community do QuestPDF, decidida).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D15 ("Meus holerites" autenticado é o canal; o e-mail só avisa, sem anexo; rotas `/api/v1/me/hr/*` pela camada 1 e pelo vínculo com funcionário ativo, sem ação no grupo), tabela 3.3 pontos 20 a 22, §4.3 (transversal: jobs com `tenantId`), §4.4 padrão 8.
- **ADR 0035** (`docs/adr/0035-autoatendimento-funcionario.md`: autoatendimento do funcionário, filtro de vínculo ativo, lista versionada de rotas de autoatendimento, adendo à ADR 0026); ADR 0009 adendo (D19: teste que reprova endpoint sem `RequirePermission`/`RequireModule` fora da lista versionada).

## Critérios de aceite
**PDF**
1. **Dado** `hr.view_sensitive` e um holerite com linhas, **quando** clico "Baixar holerite", **então** o PDF é gerado pelo `IPdfRenderer` com: cabeçalho da empresa (razão social, CNPJ e logo `Clean`, CAD-06), funcionário (nome, matrícula, CPF mascarado com os 3 primeiros e os 2 últimos dígitos, `123.***.***-45`, cargo, CBO, admissão), competência e tipo da folha, tabela de proventos e descontos (código, descrição, referência, valor), totais de proventos e descontos, líquido em destaque, bases do INSS, do FGTS e do IRRF e o FGTS do mês; **não** contém CPF completo, endereço, dados bancários nem dependentes; teste de extração de texto confere os valores e a ausência desses campos.
2. **Dado** a folha, **quando** clico "Baixar todos os holerites", **então** recebo um ZIP com um PDF por funcionário (nome do arquivo `holerite-{competencia}-{matricula ou id curto}.pdf`, sem CPF nem nome), gerado em segundo plano (`TenantJob` com `tenantId` e só ids) para mais de 20 holerites, com a mensagem "Gerando {n} holerites…" e o aviso no sino quando pronto.
3. **Dado** a folha `Draft`, **quando** baixo o holerite, **então** o PDF leva a marca d'água "RASCUNHO — folha não fechada"; com a folha fechada, o PDF é guardado como arquivo do sistema (`StoreGeneratedAsync`, dono `Payslip`, leitura com `hr.view_sensitive` ou pelo próprio funcionário no CA 5, `OwnerExists`) e o download registra `SensitiveRead`.
4. **Dado** o limite da ADR 0022, **quando** um holerite passa de 300 linhas, **então** 422 antes de gerar; o teste de desempenho gera 50 holerites em menos de 30 s na imagem Alpine.

**Meus holerites (canal do funcionário)**
5. **Dado** um usuário ligado a um funcionário ativo ou afastado e o módulo `hr` liberado, **quando** abre "Meus holerites" (`/app/meus-holerites`, no backoffice e no celular), **então** vê os próprios holerites das folhas `Closed` e `Paid`, do mais recente ao mais antigo (competência, tipo, líquido), e baixa o PDF guardado (CA 3); a lista vem de `GET /api/v1/me/hr/payslips` e o PDF de `GET /api/v1/me/hr/payslips/{id}/pdf`, com o funcionário sempre da sessão (o id de holerite de outro funcionário responde 404, IDOR), `SensitiveRead` e `Cache-Control: no-store`; holerite de folha em rascunho não aparece; a primeira leitura grava "Visto em {data}".
6. **[Segurança]** **Dado** as rotas `/api/v1/me/hr/*`, **quando** chamadas, **então** passam pela camada 1 (`RequireModule("hr")`, 403 `module_not_released`) e pelo filtro de vínculo com funcionário ativo (ADR 0035; em "Meus holerites", o afastado também entra, porque continua recebendo holerite; sem vínculo ou desligado, 403 `employee_not_active` com "Seu usuário não está ligado a um funcionário ativo. Fale com o RH."), sem ação no grupo; o corpo e a query não aceitam `employeeId`; as rotas entram na lista versionada de autoatendimento do teste de arquitetura (ADR 0035, D19); na sessão por PIN do tablet (`amr = pin`), "Meus holerites" não entra na lista `[PinSessionAllowed]` (403 `pin_session_scope`) e o `/fab` mostra "Veja o seu holerite no celular ou no computador, com o seu login." (salário em aparelho compartilhado).

**Aviso por e-mail**
7. **Dado** a folha `Closed` (RH-02c) e `hr.edit` + `hr.view_sensitive`, **quando** clico "Avisar funcionários", **então** o diálogo mostra quantos funcionários têm usuário ligado e ativo (recebem o aviso), quantos não têm (com a lista e o atalho "Convidar usuário", que abre o convite com o e-mail pessoal sugerido) e quantos estão desligados (com "Baixar holerite" para entregar pessoalmente); confirmar "Avisar {n} funcionários" enfileira um e-mail por funcionário (template `payslip-available`, assunto "Seu holerite de {mês}/{ano} está disponível — {empresa}", corpo "Entre no sistema e abra Meus holerites." com o link para `/app/meus-holerites`), **sem anexo** e sem valor no assunto nem no corpo; a mensagem de sucesso diz "{n} avisos na fila de envio".
8. **[Segurança]** **Dado** o aviso, **quando** o e-mail é montado, **então** o destinatário é só o e-mail do usuário ligado ao funcionário (nunca cópia, nunca outro endereço digitado), o modelo (`email_logs.model`) só tem nome, mês, empresa e o link, o `email_logs` guarda o destinatário mascarado na auditoria, o job leva `tenantId` e ids; o comando exige `Idempotency-Key`, e avisar a folha de novo não duplica o que já saiu: só "Avisar de novo" por holerite, com confirmação.
9. **Dado** um holerite avisado, **quando** abro a folha → Holerites, **então** vejo por funcionário a situação (Na fila, Avisado em {data}, Falhou: {motivo}, Sem usuário, Desligado) pelo `email_logs` e "Visto em {data}" (CA 5), com "Avisar de novo" e "Baixar".
10. **Dado** a folha reaberta depois do aviso (RH-02c), **quando** é fechada de novo com mudança no holerite, **então** o PDF guardado é refeito, o holerite ganha a marca "Alterado depois do aviso" e o `NextSteps` oferece "Avisar os funcionários dos holerites alterados".

**Permissões e auditoria**
11. **Dado** o módulo `hr` não liberado, **quando** chamo, **então** 403 `module_not_released`; baixar pelo RH exige `hr.view_sensitive`, avisar `hr.edit` + `hr.view_sensitive`; sem a ação, 403 `forbidden` e o botão some. `RequireModule` e `perm:`; IDOR em `/payroll-runs/{id}/payslips/{payslipId}/pdf`; "Meus holerites" pela regra do CA 6.
12. **[Segurança]** **Dado** geração, download, leitura pelo funcionário e aviso, **quando** consulto a auditoria, **então** vejo quem, quando e qual holerite, sem valor; o canário confirma que salário e líquido não aparecem em log, em `email_logs.model` nem em argumento de job.

## Regras de negócio e técnicas
- **Layout** `PayslipPdfDocument` no HR (ADR 0022), números em `pt-BR` com `tabular-nums`; um holerite por página; CPF mascarado 3+2.
- **Arquivo:** gerado na hora para rascunho (sem guardar); guardado como arquivo do sistema com a folha fechada; refeito se a folha for reaberta e fechada de novo.
- **Autoatendimento (ADR 0035):** filtro `RequireActiveEmployeeLink` do HR (funcionário da sessão pelo `IUserEmployeeLink`, situação pelo `IEmployees`), criado nesta story e reusado pela RH-01a ("Meu ponto") e pela RH-01c ("Meu espelho"); as rotas `/api/v1/me/hr/*` ficam fora do `perm:` e dentro da lista versionada do teste de arquitetura.
- **E-mail:** fila `email` (FND-08), template do sistema em Fluid, sem anexo e sem dado do holerite.
- **Lote:** `TenantJob` para ZIP e aviso, argumentos só com ids e `tenantId`.

## Permissões, eventos e tabelas
- **Permissões:** `hr.view_sensitive` (baixar pelo RH), `hr.edit` + `hr.view_sensitive` (avisar); "Meus holerites" sem ação no grupo (camada 1 + vínculo ativo, ADR 0035).
- **Eventos:** nenhum entre módulos.
- **Tabelas:** dono de anexo `Payslip`; template de e-mail `payslip-available`; `hr.payslips` + `pdf_attachment_id`, `notified_at`, `first_viewed_at`, `changed_after_notice`.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 8 ("Meus holerites" com o mesmo layout de autoatendimento do "Meu ponto"; bloco "Dados protegidos" padrão).
- Na folha (RH-02a): ações "Baixar holerite", "Baixar todos", "Avisar funcionários", "Avisar de novo"; coluna de aviso e de leitura na aba Holerites.
- **"Meus holerites"** (`/app/meus-holerites`): lista em cartões com competência, tipo, líquido e "Baixar PDF"; entrada no menu do usuário e na área Pessoas para quem está ligado a funcionário ativo (`modules.ts`).
- **Estados:** gerando (com o tempo limite de cerca de 30 s e depois o estado de demora); vazio em "Meus holerites" ("Nenhum holerite disponível ainda. Ele aparece aqui quando a folha do mês for fechada."); sem vínculo (CA 6); sem usuário e desligado no diálogo de aviso; falha de envio com o motivo; rascunho com a marca d'água; erro.
- **Propositivo:** o diálogo de aviso leva aos funcionários sem usuário com o convite pré-preenchido; depois do fechamento, o `NextSteps` da folha oferece "Avisar funcionários".
- **400 px:** "Meus holerites" desenhado para o celular; a situação do aviso aparece no cartão do holerite; botões de `ErrorState`/`EmptyState` com 44 px.
- **Ajuda:** atualizar `Ajuda/rh-folha-detalhe.md` (aba `holerites`: "Holerite em PDF" e "Aviso aos funcionários"); `Ajuda/meus-holerites.md` (nova); `<Dica>` em "Meus holerites" ("O holerite fica só aqui, com o seu login; o e-mail só avisa.").

## Plano de testes
- **Unidade:** máscara do CPF 3+2 e nome de arquivo (CA 1, 2).
- **Integração (Testcontainers):** PDF pelo texto extraído com e sem campos proibidos (CA 1); ZIP em segundo plano com `tenantId` (CA 2); marca d'água e arquivo do sistema com `SensitiveRead` (CA 3); limite (CA 4); "Meus holerites" só fechados, funcionário da sessão, IDOR, `no-store` e "Visto em" (CA 5); camada 1, vínculo, desligado, `employeeId` recusado, sessão por PIN 403 e lista versionada (CA 6); aviso só com folha fechada, destinatário, sem anexo, modelo mínimo e `Idempotency-Key` (CA 7, 8); situação do aviso (CA 9); alterado depois do aviso (CA 10); três camadas (CA 11); canário (CA 12).
- **Arquitetura:** rotas `/api/v1/me/hr/*` só com o filtro de vínculo e na lista versionada.
- **Imagem Alpine:** geração de 50 holerites e fontes só Archivo (ADR 0022).
- **E2E (Playwright):** fechar a folha de teste, avisar, entrar como o funcionário no celular a 400 px e baixar o holerite em "Meus holerites".

## Fora do escopo
- Holerite anexado ao e-mail ou PDF com senha: não entra (D15).
- Assinatura eletrônica do recebimento do holerite.
- Acesso do ex-funcionário aos holerites antigos: o RH entrega o PDF (CA 7).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, D15, tabela 3.3, pontos 20–22):
1. **Canal do holerite:** "Meus holerites" autenticado; o e-mail só avisa, sem anexo (CA 5, 7, 8).
2. **"Meus holerites" desde já:** é o canal, em `/api/v1/me/hr/*` pela camada 1 e pelo vínculo ativo, sem ação no grupo (ADR 0035) (CA 5, 6).
3. **CPF mascarado 3+2** no PDF (CA 1).

Decisões da revisão aplicadas: §4.3 (jobs com `tenantId`, CA 2, 8); §4.4 padrão 8; D19 (lista versionada de rotas fora do `perm:`, CA 6).

**Pré-condição externa:** a Konebras cria um usuário (e-mail de apelido no domínio da empresa, quando o funcionário não tiver e-mail próprio) para cada funcionário que vai usar "Meus holerites"; sem usuário, o funcionário aparece como "Sem usuário" no aviso e o RH entrega o PDF; não bloqueia o `Ready`.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers), arquitetura, imagem Alpine e E2E passando.
- [x] Migration criada (não destrutiva).
- [x] Permissões só do `ModuleCatalog`; dono `Payslip` com `OwnerExists`; template registrado; filtro de autoatendimento e lista versionada (ADR 0035).
- [x] Estados de geração, aviso e "Meus holerites"; ajuda escrita e atualizada; teste de cobertura passando.
- [x] Auditoria ligada, com `SensitiveRead`.
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `PayslipPdfDocument`; jobs de ZIP e de aviso.
- [x] Filtro `RequireActiveEmployeeLink` e a lista versionada de autoatendimento (ADR 0035).
- [x] Endpoints `/payroll-runs/{id}/payslips/{payslipId}/pdf`, `/payroll-runs/{id}/pdfs`, `/payroll-runs/{id}/notify`, `/payslips/{id}/notify`, `/api/v1/me/hr/payslips` (+ `/{id}/pdf`); tela "Meus holerites" e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (autoatendimento, ADR 0035), cybersecurity (sigilo do PDF, autoatendimento, e-mail), @qa.
- Gate: CA 1, 5, 6, 8 e 12 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: autoatendimento, CA 6) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: depois da RH-02c, antes da RH-01a) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** envio só com a folha fechada, por isso a ordem passou para depois da RH-02c; destinatário sem cópia; assunto e corpo sem valor; PDF sem dado que não é do holerite.
- **Correções da revalidação (21/10/2026):** D15 e pontos 20 a 22 decididos: "Meus holerites" autenticado é o canal (CA 5, novo), rotas `/api/v1/me/hr/*` com camada 1 e vínculo ativo, fora da sessão por PIN (CA 6, novo), e-mail só de aviso sem anexo para o usuário ligado (CA 7, 8), "Visto em" (CA 9), CPF 3+2 (CA 1); jobs com `tenantId`; o filtro de autoatendimento nasce aqui e a RH-01a reusa (bloqueio novo); "Enviar holerites" virou "Avisar funcionários"; ordem 4 do Marco 6.2; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: usuários dos funcionários (Konebras).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da RH-02) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D15, D19, tabela 3.3 pontos 20–22, ADR 0035, §4.3, §4.4 padrão 8); nova ordem (Marco 6.2, ordem 4, antes da RH-01a); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 27/10/2026 | 1.0 | Implementada (@dev). Backend: `PayslipPdfDocument` (QuestPDF pelo `IPdfRenderer`, Archivo, pt-BR tabular; empresa com logo `Clean`, funcionário com CPF 3+2, cargo, CBO e admissão, proventos/descontos, totais, líquido em destaque, bases INSS/FGTS/IRRF e FGTS do mês; marca d'água "RASCUNHO — folha não fechada" no rascunho; o modelo nem tem CPF completo, endereço, banco ou dependentes); `PayslipDocumentService` (rascunho gerado na hora sem guardar; folha `Closed`/`Paid` guarda no primeiro download pelo `StoreGeneratedAsync`, dono `Payslip`, e reaproveita; limite de 300 linhas com 422 `payslip_pdf_too_many_lines`; `SensitiveRead` em todo download; "Baixar todos" com ZIP na hora até 20 e o `PayslipBatchJob` acima, só `tenantId` e ids, ZIP no dono `PayslipBatch` e aviso no sino com `?lote=`); `PayslipNoticeService` (só com a folha fechada, 409; um `payslip-available` por holerite para o e-mail do usuário ligado e ativo, sem cópia nem anexo, modelo só com nome, mês, empresa e link; `Idempotency-Key`; avisar a folha de novo só pega quem não foi avisado, 422 `payslip_notice_none`; "Avisar de novo" por holerite; situação pelo `email_logs`; os sem usuário com o e-mail pessoal sugerido e os desligados); filtro `RequireActiveEmployeeLink` do HR (funcionário pela sessão via `IUserEmployeeLink`, situação pelo `IEmployees`, afastado entra em "Meus holerites", 403 `employee_not_active`, `employeeId` na query 400) com a marca `RequireSelfService("hr")` (BuildingBlocks) e as rotas `/api/v1/me/hr/link`, `/payslips` e `/payslips/{id}/pdf` (camada 1 no grupo, `no-store`, fora do `[PinSessionAllowed]`); reabrir a folha anula o PDF guardado e fechar de novo marca "Alterado depois do aviso" (`updated_at > notified_at`), devolvido em `PayrollCloseResult.ChangedAfterNotice`. Template `payslip-available`. Migration `PayslipDocumentsRh02b` (só colunas novas em `hr.payslips`: `pdf_attachment_id`, `notified_at`, `notice_email_log_id`, `first_viewed_at`, `changed_after_notice`). Testes: `PayslipDocumentRulesTests` (máscara, nome do arquivo, situação, texto do PDF, marca d'água, só Archivo, 50 holerites < 30 s na Alpine), `PayslipDocumentTests` (7 cenários de integração: PDF, limite, ZIP e job, "Meus holerites" com IDOR/no-store/"Visto em"/vínculo/desligado/afastado/módulo, aviso e canário, alterado depois do aviso com o fechamento real), lista versionada de autoatendimento no `EndpointAuthorizationTests` e DTOs sem valor no `PersonalDataTests`. Front: aba Holerites com "Baixar", "Baixar todos os holerites", "Avisar funcionários" (diálogo com sem usuário → "Convidar usuário" pré-preenchido pela sessão do navegador, nunca pela URL, e desligados → "Baixar holerite"), situação do aviso, "Visto em", "Alterado depois do aviso" e "Avisar de novo"; `NextSteps` do fechamento com "Avisar funcionários"/"Avisar os funcionários dos holerites alterados"; tela `/app/meus-holerites` (cartões, botão de 56 px, vazio, sem vínculo, erro, `<Dica>`); entrada no menu do usuário e na área Pessoas; aviso no `/fab`; ajudas `meus-holerites` (nova), `rh-folha-detalhe` e `fab`. Desvios e decisões (recomendação aceita, PO 24/10): (1) "Avisar de novo" fica em `POST /payroll-runs/{id}/payslips/{payslipId}/notify` (no lugar de `/payslips/{id}/notify`), para o IDOR pela folha da rota como no resto do RH; (2) o aviso em lote não ganhou `TenantJob` próprio: o comando só grava `email_logs` + `EmailQueued` na transação, e cada envio já é o `SendEmailJob` com `tenantId` e o id do log; (3) a auditoria do aviso fica no holerite (`notified_at`, `notice_email_log_id`), sem o destinatário (nem mascarado), e o `email_logs.to` continua `[PersonalData]`; (4) o PDF da folha fechada é guardado no primeiro download (não no fechamento), para não alongar a transação do fechamento; (5) "Meus holerites" na área Pessoas só aparece quando a área existe para o usuário (com o RH habilitado em algum grupo); no menu do usuário aparece sempre que houver vínculo; (6) nenhuma regra desta story depende da contadora. | @dev |
