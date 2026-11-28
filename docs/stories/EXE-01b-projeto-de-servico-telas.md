# EXE-01b — Projeto de serviço (telas): página do projeto, lista, margens, painel do dono, PDFs, rótulos, pauta e ajuda

| Campo | Valor |
|---|---|
| ID | EXE-01b |
| Título | As telas e os documentos passam a falar "serviço": página do projeto com um próximo passo por estado, indicador P · I · E · A · C, diálogos de iniciar, entrega, aceite e desfazer com o bloco "efeito nas parcelas"; lista de projetos com tipo e faixa de aceite pendente; margens e cartões do painel do dono sem "Em produção" para serviço; PDF do resumo e exportações por tipo; rótulos do orçamento, contrato e títulos ("Aceite do cliente", "Conclusão da execução"); pauta "Aceite do cliente pendente" (DSH-04); ajuda por tela e aba |
| Tamanho | M |
| Status | Done (28/11/2026) |
| Requisitos | [Proposta](../produto/execucao-pos-contrato.md) §2.2, §4.2 e §10.1 · COM-04b (padrão do diálogo que mostra a parcela) · COM-05 (Projeto 360) · DSH-01b, DSH-02, DSH-04 · EXP-01/04/05 (PDF e Excel) · CLAUDE.md "Interface" (próximo passo propositivo, estados, verbo igual na confirmação) |
| Dependências | EXE-01a (motor, DTOs, `ProjectStageMap`, `CompletionRules`) |
| Bloqueia | EXE-03 (a aba Etapas se apoia nesta página) |
| Ordem de execução | 2 (Execução depois do contrato, antes da F2 do Conecta) |

## User story
Como **responsável por um projeto de serviço**, quero **abrir o projeto e ver só o que faz sentido para serviço, com o próximo passo claro e o efeito de cada passo nas parcelas**, para **dar andamento sem dúvida e sem liberar cobrança por engano.**

## Contexto e referências
- `ProjectPage.tsx` decide as ações por `canFabricate`/`canDeliver` sem olhar o tipo; `contracts.ts` tem `PROJECT_STAGES`/`PROJECT_LETTERS` fixos de fábrica; `ProjectEventDialog.tsx` serve a fabricação e a entrega; `quotes/milestones.ts`, `ContractItems.tsx`, `AmendmentMilestones.tsx` e `finance/receivables/receivables.ts` rotulam os gatilhos; `owner-cards.tsx` e `ProjectMarginsPage.tsx` mostram a situação de fábrica; `CommercialExports.cs` escreve "E · Em produção"; `ProjectSummaryPdf.cs` desenha o indicador P E F I T C.
- O motor (EXE-01a) já entrega `executionKind`, a etapa pela `ProjectStageMap` e a regra `CompletionRules.RequiresAcceptance`.

## Critérios de aceite

**Próximo passo (um botão primário + uma linha de contexto)**
1. **Dado** um projeto `Service`, **quando** abro, **então** o cabeçalho mostra o chip "Serviço" e um único botão primário com a linha de contexto, pela tabela fixa:

| Situação | Botão primário | Linha de contexto |
|---|---|---|
| Planejado | "Iniciar execução" | "Projeto planejado. Quando começar o trabalho, inicie a execução." |
| Em execução | "Registrar entrega" | "Em execução desde {data}." |
| Entregue, sem aceite, aceite exigido ou não | "Registrar aceite do cliente" | "Entregue em {data}. Falta o aceite do cliente; libera {parcela}." (sem parcela: "Entregue em {data}. Falta o aceite do cliente.") |
| Aceito | — | "Aceito em {data}. O projeto encerra quando a última parcela for recebida." |
| Encerrado | — | "Encerrado em {data}." |

2. **Dado** um usuário sem a permissão do passo, **quando** abre, **então** o botão não aparece e a linha de contexto ganha "Quem registra a entrega: usuários com permissão de aprovar projetos." (ou "Quem registra o aceite: usuários com a permissão Registrar aceite do cliente."; "Quem inicia: usuários com permissão de editar projetos.").
3. **Dado** um projeto `Planejado`, **quando** abro "Mais ações", **então** está lá "Registrar entrega", com o aviso no diálogo "O projeto passa direto por Em execução."; "Desfazer aceite" também fica em "Mais ações" (CA 9).
4. **Dado** um projeto `Fabrication`, **quando** abro, **então** a página é idêntica à de hoje (teste de snapshot).

**Indicador de etapa**
5. **Dado** um projeto `Service`, **quando** vejo o indicador (display de andar), **então** as letras são **P · I · E · A · C** com o rótulo completo da etapa atual ao lado: P acende em Planejado; I em Em execução; E em Entregue sem aceite; A em Aceito; C em Encerrado. Sem aceite exigido nem registrado, "A" aparece como **dispensado** (traço, `aria-label` "Aceite dispensado"). Entregue sem aceite com aceite exigido ganha o chip âmbar "Aguardando aceite do cliente". `<Dica>` com a legenda das letras.
6. **Dado** um projeto `Service`, **quando** abro, **então** não aparecem "Registrar fabricação concluída", a nota "A fabricação deste projeto é concluída pelas OS", a nota de instalação, a aba "Cronograma" nem as listas de OS de fabricação, romaneios e OS de instalação; a aba "Itens e OS" vira "Itens".

**Diálogos com o efeito nas parcelas**
7. **Dado** os diálogos "Iniciar a execução?", "Registrar a entrega?" e "Registrar o aceite do cliente?", **quando** abrem, **então** todos têm o bloco fixo:
   - com parcelas: "Ao confirmar, estas parcelas passam a valer para cobrança:" + uma linha por parcela "Parcela {n}, {marco}: R$ {valor}, vence {data}" + "Total liberado: R$ {total}"; a de compatibilidade aparece como "Conclusão da execução (antes: Fabricação concluída)";
   - sem parcela: "Nenhuma parcela é liberada por este passo.";
   - parcela que continua esperando: "A parcela {marco} continua aguardando o aceite do cliente.";
   - o verbo do botão é o mesmo do aviso de sucesso: "Iniciar execução" → "Execução iniciada"; "Registrar entrega" → "Entrega registrada"; "Registrar aceite do cliente" → "Aceite registrado"; "Desfazer aceite" → "Aceite desfeito".
   As parcelas vêm de uma prévia do servidor (`GET /projects/{id}/milestone-preview?step=`), não calculadas no front.
8. **Dado** o diálogo de entrega e quem tem `projects.register_acceptance`, **quando** abre, **então** a caixa "Também registrar que o cliente aceitou (não há parcela ligada ao aceite)" vem **sempre desmarcada**; só aparece quando o contrato não tem marco de aceite; marcada, "Quem aceitou" fica obrigatório (com "Papel ou cargo" opcional).
9. **Dado** "Desfazer aceite" em "Mais ações", **quando** abro, **então** o título é "Desfazer o aceite de {nome}, registrado em {data}?", mostra o efeito na parcela ("A parcela {marco} volta a aguardar o aceite."), pede o motivo obrigatório com contador (até 200); se a parcela já foi recebida, cobrada ou há nota, o botão fica desabilitado com "A parcela já foi recebida. Para corrigir, use um aditivo." e o link "Fazer aditivo".
10. **Dado** a data do passo, **quando** é anterior a hoje, **então** aparece o campo "Por que a data é anterior?" (obrigatório, até 200); mensagens de erro: "A data não pode ser depois de hoje.", "A data não pode ser antes do início ({data}).", "O aceite não pode ser antes da entrega ({data}).", "O contrato precisa estar ativo para registrar este passo.". Conflito de versão: "O projeto mudou desde que você abriu." com "Recarregar".
11. **Dado** o aceite, **quando** registro, **então** o diálogo pede data, "Quem aceitou" (obrigatório), "Papel ou cargo" e o anexo do termo assinado (upload no padrão dos anexos, com prévia do nome do arquivo).

**Lista, margens, painel e documentos**
12. **Dado** a lista de projetos (`ProjectsPage`), **quando** o tenant tem o recurso, **então** há a coluna e o filtro "Tipo" (Fabricação/Serviço); a coluna de etapa usa a `ProjectStageMap`; projeto entregue sem aceite exigido tem a faixa lateral âmbar no cartão (no celular) e na linha.
13. **Dado** `ProjectMarginsPage` e os cartões do Painel do proprietário (`owner-cards.tsx`: projetos em andamento, carteira, produção, entregas), **quando** há projetos `Service`, **então** a situação aparece pelo rótulo do tipo ("Em execução", não "Em produção"); o cartão de produção conta só `Fabrication`; os de carteira e entregas contam os dois tipos, com o filtro "Tipo" quando o tenant tem o recurso. Konebras sem mudança (teste de regressão das três telas).
14. **Dado** o PDF do resumo do projeto (`ProjectSummaryPdf`) e as exportações (`CommercialExports`), **quando** o projeto é `Service`, **então** indicador e coluna "Etapa" usam P · I · E · A · C ("I · Em execução", "A · Aceito"); **nenhum** dado de quem aceitou sai no PDF do contrato nem nas exportações; no PDF do resumo sai só "Aceito em {data}".
15. **Dado** orçamento, contrato, aditivo, PDF do orçamento e do contrato e o chip "Aguarda: …" do título a receber, **quando** o projeto (ou o orçamento) é `Service`, **então** "Aceite técnico" vira "Aceite do cliente", o gatilho "Fabricação concluída" não é oferecido e o marco antigo aparece como "Conclusão da execução (antes: Fabricação concluída)". O 422 da aprovação por marco inválido (EXE-01a CA 27) abre a aba Condições com o marco em destaque.

**Pauta (DSH-04)**
16. **Dado** o contrato público novo `IProjects.AwaitingAcceptanceAsync(today)` (projetos `Service` entregues sem aceite, com o valor da parcela de aceite quando houver), **quando** a "Minha pauta" é montada, **então** o **responsável do projeto** (`manager_user_id`; sem responsável, quem tem `projects.register_acceptance`) vê:
   - com parcela ligada ao aceite: desde o dia da entrega, "Aceite do cliente pendente: {projeto}, libera R$ {valor}" com o botão "Registrar aceite do cliente";
   - sem parcela: a partir de 7 dias corridos depois da entrega, "Aceite do cliente pendente: {projeto}".
   Some quando o aceite é registrado. Teste com e sem parcela e no 6º/7º dia.

**Estados e celular**
17. **Dado** qualquer tela desta story, **quando** carrega, falha ou está vazia, **então** mostra skeleton; erro com "Tentar de novo"; projeto sem itens "Este projeto não tem itens. Os itens vêm do orçamento aprovado." com o link "Aditivo"; lista vazia de projetos de serviço "Nenhum projeto de serviço ainda. Eles nascem quando um orçamento de serviço é aprovado." com "Ver orçamentos".
18. **Dado** 400 px, **quando** abro o projeto, **então** o botão do próximo passo fica fixo no rodapé, as ações secundárias em "Mais ações" e, nos diálogos, as parcelas empilhadas (uma por linha, valor em `tabular-nums`) com o total no fim.

**Ajuda**
19. **Dado** a ajuda contextual, **quando** abro a do projeto de serviço, **então** existe por tela e aba no padrão (`rota`, `aba`, `modulo: projects`, `recurso: projects.service_execution`): `Ajuda/projetos-projeto-servico.md` (página e aba Resumo, com a tabela "o que libera cada passo": Iniciar → nada; Entrega → parcelas de entrega e, em contrato antigo, de "conclusão da execução"; Aceite → parcelas de aceite; Desfazer → a parcela de aceite volta a aguardar, se não foi cobrada), `Ajuda/projetos-projeto-servico-itens.md` (aba Itens) e as seções novas em `comercial-orcamento-condicoes.md` e `comercial-contrato.md`. `<Dica>` do aceite: "O cliente confirma que recebeu o serviço. Se o contrato liga uma parcela ao aceite, ela passa a valer para cobrança."; `<Dica>` em "Aguardando aceite do cliente" e na legenda do indicador. O teste de cobertura da ajuda passa.

## Regras de negócio e técnicas
- Toda decisão de "pode ou não" vem do servidor (permissões em `/me/permissions`, situação e `acceptanceRequired` no DTO, prévia das parcelas); o front só desenha.
- Rótulos por tipo num lugar só no front (`projectStages(kind)` em `contracts.ts`, espelho da `ProjectStageMap`) e no back (`ProjectStageMap`); teste garante que os dois batem.
- Nenhuma chave nova no `BusinessVocabulary` (o rótulo segue o tipo do projeto, não o perfil).

## Permissões, eventos e tabelas
- Nenhuma permissão nova (usa as da EXE-01a: `projects.edit`, `projects.approve`, `projects.register_acceptance`).
- **Contrato novo:** `IProjects.AwaitingAcceptanceAsync` (Commercial.Contracts), lido pela fonte de pauta do Commercial (`CommercialAgendaSources`).
- **Rota nova:** `GET /api/v1/projects/{id}/milestone-preview?step=start|delivery|acceptance|revert` (`projects.view`; valores só com `finance.view`, senão só a descrição do marco).
- Nenhuma tabela nova.

## Plano de testes
- **Front (Vitest):** tabela de próximo passo (todas as situações, com e sem permissão); indicador (cada letra, "A" dispensado, chip âmbar); diálogos (bloco de parcelas com 0, 1 e N, compatibilidade, caixa desmarcada, motivo da data, desfazer bloqueado); lista e cartões com tipo; rótulos de marco por tipo.
- **Integração (Testcontainers):** `AwaitingAcceptanceAsync` (com/sem parcela, 6º e 7º dia, destinatário); prévia das parcelas por passo; PDF e exportação sem dado do aceite.
- **Regressão Konebras:** snapshot de `ProjectPage`, `ProjectsPage`, `ProjectMarginsPage`, cartões do painel, PDF do resumo e exportação de projetos sobre a cópia do `konebras`.
- **Ajuda:** teste de cobertura (rota e aba novas).

## Tarefas
- [x] Front: `ProjectPage.tsx` (tabela de próximo passo, "Mais ações"), `ProjectEventDialog.tsx` (iniciar, entrega, aceite, desfazer; bloco de parcelas), `ProjectItemsTab.tsx` (só itens no `Service`), `contracts/contracts.ts` (`projectStages(kind)`), `ProjectsPage.tsx`, `margins/ProjectMarginsPage.tsx`, `dashboard/owner-cards.tsx`, `quotes/milestones.ts`, `contracts/ContractItems.tsx`, `contracts/amendments/AmendmentMilestones.tsx`, `finance/receivables/receivables.ts`.
- [x] Back: `milestone-preview`, `IProjects.AwaitingAcceptanceAsync` + fonte de pauta, `ProjectSummaryPdf.cs` e `CommercialExports.cs` pela `ProjectStageMap`, `ContractPdfModel.cs` e PDF do orçamento com os rótulos por tipo; OpenAPI e Orval.
- [x] Ajuda: arquivos do CA 19.

## Dev Notes
- Front: `src/Web/src/pages/projects/*`, `src/Web/src/pages/contracts/contracts.ts`, `src/Web/src/pages/dashboard/owner-cards.tsx`, `src/Web/src/pages/projects/margins/ProjectMarginsPage.tsx`, `src/Web/src/components/ui/stage-indicator`, `src/Web/src/help/Dica`.
- Back: `src/Modules/Commercial/BemAqui.Commercial.Application/Documents/ProjectSummaryPdf.cs`, `Documents/ContractPdfModel.cs`, `Exports/CommercialExports.cs`, `CommercialAgendaSources.cs`, `ProjectService.Views.cs`.
- Padrão do diálogo com parcela: COM-04b CA 9 e 10 (`ProjectEventDialog.tsx`).

## Fora do escopo
- Motor, backfill, permissões e eventos (EXE-01a). Etapas (EXE-03). Termo de aceite em PDF (EXE-06). Botão "Encerrar projeto" (o encerramento segue automático pela quitação).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | **Revisões de 27/11/2026**: parte de telas da antiga EXE-01, com a revisão de UX (tabela de próximo passo, indicador P · I · E · A · C com "A" dispensado, bloco de efeito nas parcelas, caixa de aceite sempre desmarcada, desfazer em "Mais ações", mensagens, pauta com e sem parcela, faixa âmbar, ajuda por tela e aba, celular) e os leitores de situação de tela (lista, margens, painel, PDF, exportações). | Claude |
| 28/11/2026 | 1.0 | **Implementada.** Back: `GET /projects/{id}/milestone-preview?step=start\|delivery\|acceptance\|revert[&date=]` (grupo do recurso `projects.service_execution`, `projects.view`; valor e vencimento só com `finance.view`; traz `stillWaiting`, `lockedReason` do desfazer pela mesma checagem do EXE-01a e `hasAcceptanceMilestone`); `IProjects.AwaitingAcceptanceAsync` + categoria de pauta `projects.acceptance_pending` no `ProjectsAgendaSource`; `MilestoneEventLabels` (Domain) usado no PDF do orçamento, do contrato, do aditivo, do resumo e na prévia da aprovação; `ProjectSummaryPdf` com letras e etapa pela `ProjectStageMap` e "Aceito em {data}" (sem quem aceitou, sem bloco de OS no serviço); exportação de projetos com "I · Em execução"/"A · Aceito" no serviço; filtro `kind` (`fabricacao`/`servico`) na lista e na exportação; `executionKind` aditivo em `QuoteDetail`, `ContractDetail`, `ActiveProjectItem`, `ProjectMarginHeader` e `projectExecutionKind` em `ReceivableDetail`; cartão Produção conta só fabricação. Front: `ServiceProjectView` (chip Serviço, indicador P · I · E · A · C com "A" em traço, chip âmbar, um botão primário + linha de contexto, "Mais ações", botão fixo no rodapé a 400 px, `?acao=aceite` vindo da pauta), `ServiceStepDialog` (efeito nas parcelas pela prévia, motivo da data, "também aceitou" sempre desmarcada, termo assinado, desfazer travado com "Fazer aditivo", contador 200), aba "Itens" sem OS, `projectStages(kind)`/`stageView` em `contracts.ts`, `ProjectStageIndicator` na lista, margens e painel do dono, coluna/filtro "Tipo" e faixa âmbar na lista, rótulos de marco por tipo (orçamento, contrato, aditivo, título a receber), 422 da aprovação com "Abrir as condições" e o marco em destaque; ajuda `projetos-projeto-servico.md` (aba `servico`) e `projetos-projeto-servico-itens.md` (aba `servico-itens`, via `UrlTabs.helpTabs`, porque rota+aba é chave única) e seções novas no orçamento e no contrato. **Decisões (a mais segura, sem PO):** (1) a faixa âmbar da lista usa a mesma condição do chip (entregue sem aceite **com** aceite exigido); (2) mensagem do desfazer travado é a do servidor ("A parcela já foi recebida ou cobrada…"), mais completa que a do CA 9; (3) os cartões Carteira e Entregas contam os dois tipos sem filtro próprio: o filtro "Tipo" fica na lista de projetos para onde os cartões levam (cartões são cacheados por tenant); (4) a exportação não ganhou coluna "Tipo" (só o filtro), para a planilha da Konebras não mudar; (5) "Encerrado em {data}" usa a data de conclusão do contrato; (6) o PDF do resumo do serviço não traz o bloco de OS de fabricação. E2E fica para o fechamento da feature (regra do CLAUDE.md). | Claude |
