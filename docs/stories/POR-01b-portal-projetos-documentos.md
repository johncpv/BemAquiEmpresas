# POR-01b — Portal do cliente: projetos, etapa, previsão de entrega e documentos

| Campo | Valor |
|---|---|
| ID | POR-01b |
| Título | O cliente vê no portal os próprios projetos com a etapa no indicador de andar, os itens, a data de entrega no formato decidido pelo PO, a linha do tempo dos marcos públicos e os documentos compartilhados (contrato assinado, NF-e, manuais, termo de entrega), baixados só depois da varredura e com registro; o backoffice escolhe o que compartilhar, nunca custo nem margem |
| Tamanho | M (quebra da POR-01) |
| Status | Draft |
| Requisitos | PRD §5.15 ("andamento do(s) projeto(s) (etapa atual, previsão de entrega), documentos (contrato, NF, manuais)") · R-PRJ-4 (parte pública) · R-COM-3 · R-SEC-5 · PRD §6 LGPD |
| Dependências | POR-01a (acesso e isolamento), COM-04a/COM-05 (projetos, itens, etapas `StageIndicator`), COM-06 (entrega técnica), FND-07 (anexos); Fase 4 PLN-01 (data prevista); Fase 5 FIS-03 (NF-e e DANFE) |
| Bloqueia | POR-02 |
| Ordem de execução | 2 (Marco 8.2 "Acompanhamento") |

## User story
Como **cliente**, quero **ver em que etapa está o meu elevador, quando ele será entregue e baixar o contrato, a nota e os manuais**, para **não precisar ligar para a Konebras a cada semana**.

## Contexto e referências
- PRD §5.15; `04-fluxos` §1 "Projeto" e o `StageIndicator` (P Planejado · E Em produção · F Fabricado · I Instalando · T Entregue · C Encerrado), componente padrão de etapa (CLAUDE.md).
- `03-modelo-dados` → `commercial.projects` (`promised_date`, `forecast_date`, `status`), `project_items`; anexos `Contract`, `Project` (ADR 0019 §7); NF-e de saída (`fiscal.outgoing_invoices`, Fase 5).
- PLN-01 (Fase 4) calcula a data prevista; DSH (risco: prevista > prometida) é informação interna.
- ADR 0019 §20 (custo e margem só com `projects.view_cost`; o portal nunca).

## Critérios de aceite
**Projetos**
1. **Dado** um usuário do portal ativo, **quando** abre `/portal`, **então** vê os projetos do seu cliente (`party_id` da sessão; POR-01a CA 8), não cancelados, com número, nome, endereço da obra (cidade e bairro, proposta), o `StageIndicator` com a etapa atual e o rótulo visível, e a data de entrega pela regra do ponto em aberto 1; projeto `Closed` vai para "Concluídos".
2. **Dado** o projeto, **quando** abro `/portal/projetos/$id`, **então** vejo os itens (descrição e quantidade), a linha do tempo dos marcos públicos (contrato assinado, produção iniciada, fabricação concluída, instalação iniciada, entrega técnica, encerrado, com as datas), e nunca OS internas, apontamentos, não conformidades, nomes de funcionários, custos, margem ou orçado (teste de arquitetura dos DTOs, POR-01a CA 9).
3. **Dado** a etapa mudou (evento de projeto já existente: `ProjectFabricated`, `ProjectDelivered` etc.), **quando** o cliente abre o portal, **então** vê a etapa nova; o aviso por e-mail ao cliente fica para o ponto em aberto 4 (e WhatsApp na WPP-01).

**Documentos**
4. **Dado** um usuário do backoffice com `projects.edit`, **quando** marca um anexo do projeto ou do contrato como "Compartilhar no portal" (padrão: **não** compartilhado), **então** o anexo passa a aparecer na aba Documentos do projeto no portal; desmarcar tira na hora; a marcação é auditada.
5. **Dado** a NF-e autorizada do projeto (FIS-03, Fase 5), **quando** existir, **então** o DANFE em PDF e o XML aparecem no portal automaticamente (proposta: documentos fiscais do próprio cliente são compartilhados por padrão; Pontos em aberto 2).
6. **[Segurança]** **Dado** o download de um documento no portal, **quando** pedido, **então** a API confere: o anexo é de um dono do projeto do cliente da sessão, está marcado para o portal e está `Clean`; senão 404 (mesmo tempo); o download é registrado na auditoria (POR-01a CA 11) e responde com `Content-Disposition: attachment`, `nosniff` e `no-store`.
7. **Dado** o projeto sem documento compartilhado, **quando** abro a aba, **então** vejo "Nenhum documento disponível ainda. Os documentos aparecem aqui quando a {empresa} os compartilhar."

**Backoffice**
8. **Dado** o Projeto 360 (COM-05), **quando** abro a aba Documentos, **então** cada anexo mostra o chip "No portal" quando compartilhado, e a aba Resumo mostra "Cliente com acesso ao portal: {n} contatos" com o link para a aba Portal do terceiro.

**Permissões**
9. **Dado** o módulo `portal` ou `projects` não liberado, **quando** o portal chama, **então** 403 `module_not_released` com a tela padrão; marcar para o portal exige `projects.edit` no backoffice. Endpoints do portal com o esquema do portal e o filtro de isolamento (teste de arquitetura).

## Regras de negócio e técnicas
- **Leitura** pelo `IProjects.ForPortalAsync(partyId)` e `IProjects.PortalDetailAsync(partyId, projectId)` (Commercial.Contracts, proposta), que já filtram pelo cliente; e pelo `IAttachmentService` com a marca do portal.
- **Marca de compartilhamento:** `shared.attachments.portal_shared` (proposta, `bool`, padrão `false`) ou tabela `portal.shared_documents` (Pontos em aberto 3).
- **Data de entrega:** regra do ponto em aberto 1; o portal nunca mostra "em risco".

## Permissões, eventos e tabelas
- **Permissões:** usuário do portal (sessão); `projects.edit` (compartilhar).
- **Eventos:** nenhum novo (lê o estado atual).
- **Tabelas:** marca de compartilhamento (proposta).

## Telas e estados
- **Portal:** `/portal` (lista de projetos em cartões com o `StageIndicator`), `/portal/projetos/$id` (abas Andamento, Itens, Documentos).
- **Estados:** carregando; vazio ("Nenhum projeto em andamento."); erro com "Tentar de novo".
- **400 px:** cartões; o indicador de etapa legível no celular.
- **Ajuda:** `Ajuda/portal-projetos.md`, `Ajuda/portal-projeto.md` (uma por aba); atualizar `Ajuda/projetos-projeto-documentos.md` ("Compartilhar no portal").

## Plano de testes
- **Integração (Testcontainers):** projetos só do cliente e DTO sem dado interno (CA 1, 2); compartilhar e descompartilhar (CA 4); NF-e quando existir (CA 5); download com as quatro conferências e 404 uniforme (CA 6); três camadas (CA 9).
- **Arquitetura:** DTOs do portal (CA 2).
- **E2E (Playwright):** o cliente de teste vê o projeto e baixa o contrato compartilhado; outro cliente recebe 404 no mesmo id.

## Fora do escopo
- Parcelas e boletos: POR-01c. Aprovação de orçamento: POR-02.
- Mensagens entre cliente e empresa pelo portal (chamado): depois do MVP.

## Pontos em aberto
1. **Data de entrega no portal** (pré-condição, PO): mostrar a data prometida (proposta), a prevista do planejamento (PLN-01) ou a prevista só quando for igual ou anterior à prometida; o "em risco" nunca aparece.
2. NF-e compartilhada por padrão (proposta) ou só quando marcada.
3. Onde guardar a marca "Compartilhar no portal" (arquiteto).
4. Avisar o cliente por e-mail quando a etapa muda (PO).
5. Endereço da obra completo ou só cidade e bairro (LGPD, proposta: cidade e bairro).

## Definition of Done
- [ ] Critérios de aceite atendidos; testes de integração, arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Telas com estados; ajuda; teste de cobertura passando; auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Contratos `ForPortal*` no Commercial; marca de compartilhamento; endpoints `/api/v1/portal/projects`; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (IDOR e download), design, @qa.
- Gate: CA 1, 2 e 6 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** anexo não compartilhado por padrão (CA 4); download com quatro conferências (CA 6); a data de entrega virou pré-condição (ponto 1) porque a prevista pode revelar risco interno.
- **Pré-condições para `Ready`:** ponto em aberto 1; POR-01a concluída.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da POR-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
