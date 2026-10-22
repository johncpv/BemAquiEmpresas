# POR-01b — Portal do cliente: projetos, etapa, previsão de entrega e documentos

| Campo | Valor |
|---|---|
| ID | POR-01b |
| Título | O cliente vê no portal os próprios projetos com a etapa no indicador de andar (`size="portal"`, com nome e frase), os itens, a data prometida (e "Nova previsão {data}" com o motivo em linguagem simples quando a prevista passa dela), a linha do tempo dos marcos públicos e os documentos compartilhados (NF-e por padrão; contrato, manuais e termo de entrega marcados pelo backoffice em `portal.shared_documents`), baixados só por endpoint autorizado e com registro; e-mail nos marcos com opt-out; nunca custo, margem nem dado interno |
| Tamanho | M (quebra da POR-01) |
| Status | Ready |
| Requisitos | PRD §5.15 ("andamento do(s) projeto(s) (etapa atual, previsão de entrega), documentos (contrato, NF, manuais)") · R-PRJ-4 (parte pública) · R-COM-3 · R-SEC-5 · PRD §6 LGPD |
| Dependências | POR-01a (acesso, isolamento e Kit Portal), COM-04a/COM-05 (projetos, itens, `StageIndicator`), COM-06 (entrega técnica), FND-07 (anexos), FND-08 (e-mail), PLN-01a/PLN-01b (data prevista e motivo, `commercial.project_forecasts`, ADR 0029), FIS-03a (NF-e autorizada, XML e DANFE) |
| Bloqueia | POR-01c, POR-02 |
| Ordem de execução | 2 (Marco 8.2 "Acompanhamento") |

## User story
Como **cliente**, quero **ver em que etapa está o meu elevador, quando ele será entregue e baixar o contrato, a nota e os manuais**, para **não precisar ligar para a Konebras a cada semana**.

## Contexto e referências
- PRD §5.15; `04-fluxos` §1 "Projeto" e o `StageIndicator` (P Planejado · E Em produção · F Fabricado · I Instalando · T Entregue · C Encerrado), componente padrão de etapa (CLAUDE.md); no portal, a etapa aparece com o **nome e uma frase** (o que acontece agora e o que vem depois), sem a letra sozinha (Kit Portal).
- `03-modelo-dados` → `commercial.projects` (`promised_date`, `status`), `project_items`, `commercial.project_forecasts` (data prevista e motivo, ADR 0029); anexos `Contract`, `Project` (ADR 0019 §7); NF-e de saída (`fiscal.outgoing_invoices`, FIS-03a).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D10 (datas do plano fora do documento), D16; tabela 3.5, pontos 6 a 10; §4.3 POR-01a (documentos só por endpoint autorizado) e transversal (anexo autorizado por registro); §4.4 padrão 5.
- **ADR 0040** (identidade e isolamento do portal); ADR 0029 (planejamento finito); ADR 0019 §20 (custo e margem só com `projects.view_cost`; o portal nunca).

## Critérios de aceite
**Projetos**
1. **Dado** um usuário do portal ativo, **quando** abre `/portal`, **então** vê os projetos do seu cliente (`party_id` da sessão; POR-01a CA 13), não cancelados, em cartões com nome, cidade e bairro da obra (nunca o endereço completo), o `StageIndicator` `size="portal"` com o nome da etapa e a frase ("Em produção: estamos fabricando o seu elevador. Depois vem a instalação."), e a **data prometida** ("Entrega prevista para {data}"); projeto `Closed` vai para "Concluídos".
2. **Dado** a data prevista do planejamento (`commercial.project_forecasts`, PLN-01b) **depois** da prometida, **quando** o cliente abre o projeto, **então** vê "Nova previsão {data}" com o motivo em linguagem simples, tirado de uma lista fixa pelo código do motivo do plano ("Aguardando a chegada de material", "Ajuste na programação da fábrica", "Ajuste no projeto", "Aguardando liberação da obra"; sem código conhecido: "Ajuste na programação da fábrica"); quem tem `projects.edit` pode trocar o motivo por outro da mesma lista no Projeto 360; prevista igual ou antes da prometida, só a prometida aparece; "em risco", folga e datas de etapa nunca aparecem.
3. **Dado** o projeto, **quando** abro `/portal/projetos/$id`, **então** vejo os itens (descrição e quantidade), a linha do tempo dos marcos públicos (contrato assinado, produção iniciada, fabricação concluída, instalação iniciada, entrega técnica, encerrado, com as datas), e nunca OS internas, apontamentos, não conformidades, nomes de funcionários, custos, margem ou orçado (teste de arquitetura dos DTOs, POR-01a CA 14).

**Avisos por e-mail**
4. **Dado** um marco público alcançado (eventos de projeto já existentes: contrato assinado, `ProjectFabricated`, instalação iniciada, `ProjectDelivered`, encerrado), **quando** o consumidor do Portal recebe o evento, **então** cada usuário do portal ativo do cliente recebe um e-mail (template `portal_milestone`, fila, FND-08) só com o aviso e o link do projeto no portal ("O projeto {nome} avançou: {etapa}. Veja no portal."), sem valor nem documento anexo; idempotente pelo evento; quem desligou "Avisos por e-mail" em "Preferências de aviso" (POR-01a) não recebe, e todo e-mail tem o link para desligar.

**Documentos**
5. **Dado** um usuário do backoffice com `projects.edit`, **quando** marca um anexo do projeto ou do contrato como "Compartilhar no portal" (padrão: **não** compartilhado), **então** nasce a linha em `portal.shared_documents` e o anexo aparece na aba Documentos do projeto no portal; desmarcar grava `unshared_at` e tira na hora; as duas ações são auditadas.
6. **Dado** a NF-e autorizada do projeto (FIS-03a), **quando** existir, **então** o DANFE em PDF e o XML aparecem no portal **por padrão** (documento fiscal do próprio cliente), com a mesma marca em `portal.shared_documents` criada pelo consumidor de `InvoiceAuthorized`; nota cancelada sai do portal e o DANFE cancelado não é oferecido.
7. **[Segurança]** **Dado** o download de um documento no portal, **quando** pedido por `/api/v1/portal/projects/{id}/documents/{documentId}`, **então** a API confere: o anexo é de um dono do projeto do cliente da sessão, está compartilhado (`portal.shared_documents` sem `unshared_at`), está `Clean` e o arquivo está no prefixo do tenant; senão 404 (mesmo tempo); o download é registrado na auditoria (POR-01a CA 15) e responde com `Content-Disposition: attachment`, `nosniff` e `no-store`; nenhuma URL direta do storage é devolvida.
8. **Dado** o projeto sem documento compartilhado, **quando** abro a aba, **então** vejo "Nenhum documento disponível ainda. Os documentos aparecem aqui quando a {empresa} os compartilhar." e o bloco "Fale com a {empresa}".

**Backoffice**
9. **Dado** o Projeto 360 (COM-05), **quando** abro a aba Documentos, **então** cada anexo mostra o chip "No portal" quando compartilhado, e a aba Resumo mostra "Cliente com acesso ao portal: {n} contatos" com o link para a aba Portal do terceiro e o motivo para o cliente quando houver "Nova previsão" (CA 2).

**Permissões**
10. **Dado** o módulo `portal` ou `projects` não liberado, **quando** o portal chama, **então** 403 `module_not_released` com a tela padrão; marcar para o portal e trocar o motivo exigem `projects.edit` no backoffice (sem a ação, 403 `forbidden` e o botão some). Endpoints do portal com o esquema do portal e o filtro de isolamento (teste de arquitetura).

## Regras de negócio e técnicas
- **Leitura** pelo `IProjects.ForPortalAsync(partyId)` e `IProjects.PortalDetailAsync(partyId, projectId)` (Commercial.Contracts), que já filtram pelo cliente e devolvem a data prometida, a prevista e o código do motivo; e pelo `IAttachmentService` com a conferência do CA 7.
- **Marca de compartilhamento:** `portal.shared_documents` (`attachment_id`, `owner_type`, `owner_id`, `party_id`, `source` Manual/Invoice, `shared_by`, `shared_at`, `unshared_by`, `unshared_at`), dono Portal.
- **Motivo para o cliente:** mapa fixo código do plano → frase; `commercial.project_forecasts` + `customer_reason_override` (código da lista) gravado pelo Commercial.
- **Preferência de e-mail:** `portal.portal_users` + `notify_email` (padrão ligado).

## Permissões, eventos e tabelas
- **Permissões:** usuário do portal (sessão); `projects.edit` (compartilhar e trocar o motivo).
- **Eventos:** consome os eventos de marco do projeto e `InvoiceAuthorized`/`InvoiceCancelled` (Fiscal.Contracts), idempotentes pela inbox.
- **Tabelas:** `portal.shared_documents` (nova); `portal.portal_users` + `notify_email`; `commercial.project_forecasts` + `customer_reason_override`; template `portal_milestone`.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 5 (Kit Portal: etapa com nome e frase, vocabulário leigo, "Fale com a {empresa}", "Preferências de aviso"); 4 (`StageIndicator` com `size="portal"`).
- **Portal:** `/portal` (lista de projetos em cartões), `/portal/projetos/$id` (abas Andamento, Itens, Documentos); seção "Avisos por e-mail" em `/portal/preferencias`.
- **Estados:** carregando; vazio ("Nenhum projeto em andamento." + "Fale com a {empresa}"); erro com "Tentar de novo" e o bloco "Fale com a {empresa}".
- **Propositivo:** o cartão leva direto ao que mudou por último ("Fabricação concluída em {data}"); a NF-e aparece sozinha.
- **400 px:** cartões; o indicador de etapa legível no celular.
- **Ajuda:** `Ajuda/portal-projetos.md`, `Ajuda/portal-projeto.md` (uma por aba: `andamento`, `itens`, `documentos`), previstas; atualizar `Ajuda/projetos-projeto-documentos.md` ("Compartilhar no portal") e `Ajuda/projetos-projeto.md` ("Motivo para o cliente").

## Plano de testes
- **Integração (Testcontainers):** projetos só do cliente, cidade e bairro, data prometida (CA 1); nova previsão com motivo e troca do motivo (CA 2); DTO sem dado interno (CA 3); e-mail nos marcos, idempotência e opt-out (CA 4); compartilhar e descompartilhar (CA 5); NF-e por padrão e cancelada (CA 6); download com as conferências e 404 uniforme (CA 7); três camadas (CA 10).
- **Arquitetura:** DTOs do portal (CA 3).
- **Front:** teste de vocabulário das telas novas (Kit Portal).
- **E2E (Playwright):** o cliente de teste vê o projeto e baixa o contrato compartilhado; outro cliente recebe 404 no mesmo id.

## Fora do escopo
- Parcelas e boletos: POR-01c. Aceite de orçamento: POR-02. WhatsApp: WPP-01.
- Mensagens entre cliente e empresa pelo portal (chamado): depois do MVP.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.5, pontos 6–10):
1. Data prometida; quando a prevista passa dela, "Nova previsão {data}" com o motivo em linguagem simples (CA 2).
2. NF-e compartilhada por padrão (CA 6).
3. Marca de compartilhar em `portal.shared_documents` (CA 5).
4. E-mail só nos marcos, com opt-out (CA 4).
5. Endereço da obra: só cidade e bairro (CA 1).

Decisões da revisão aplicadas: D10/ADR 0029 (data prevista e motivo vêm de `project_forecasts`, CA 2); D16/ADR 0040; §4.3 (documentos só por endpoint autorizado, anexo autorizado por registro e storage com prefixo de tenant, CA 7); §4.4 padrões 4 e 5.

**Pré-condição externa:** nenhuma.

## Definition of Done
- [ ] Critérios de aceite atendidos; testes de integração, arquitetura, front e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Telas com estados e Kit Portal; ajudas previstas escritas; teste de cobertura passando; auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Contratos `ForPortal*` no Commercial; mapa de motivos; `portal.shared_documents` e consumidores (marcos, NF-e).
- [ ] Endpoints `/api/v1/portal/projects`; template `portal_milestone`; telas, preferências e ajudas.

## Quality gates
- Agentes previstos: @dev, cybersecurity (IDOR e download), design (Kit Portal), @qa.
- Gate: CA 1, 2, 3 e 7 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: PLN-01a/01b e FIS-03a pelos nomes novos; POR-01c em "Bloqueia") |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** anexo não compartilhado por padrão (CA 4); download com quatro conferências (CA 6); a data de entrega virou pré-condição (ponto 1) porque a prevista pode revelar risco interno.
- **Correções da revalidação (21/10/2026):** pontos 6 a 10 decididos (data prometida e "Nova previsão" com motivo da lista fixa, CA 2; NF-e por padrão, CA 6; `portal.shared_documents`, CA 5; e-mail nos marcos com opt-out, CA 4; cidade e bairro, CA 1); download só por endpoint autorizado e no prefixo do tenant (CA 7); etapa com nome e frase e `size="portal"` (padrões 4 e 5); dependências pelos nomes novos; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da POR-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D10, D16, tabela 3.5 pontos 6–10, §4.3 POR-01a e transversal, §4.4 padrões 4 e 5); nova ordem (Marco 8.2, ordem 2, antes do go-live); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
