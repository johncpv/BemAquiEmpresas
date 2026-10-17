# FND-06d — Menu por permissão e tela de módulo não liberado

| Campo | Valor |
|---|---|
| ID | FND-06d |
| Título | Dock e paleta de comandos montados por `/me/permissions`; tela padrão `ModuleNotReleased` |
| Tamanho | P |
| Status | Draft |
| Requisitos | R-SEC-11 · R-SEC-12 |
| Dependências | FND-06c (`/me/permissions`), FND-09b (dock, popover/sheet e Ctrl K) |
| Bloqueia | FND-14 (ajuda filtrada pelo mesmo menu) |
| Ordem de execução | 15 |

## User story
Como **usuário**, quero **ver no menu só os módulos que posso usar e, quando um módulo não faz parte da versão da minha empresa, uma tela clara dizendo isso**, para **não me perder em telas sem acesso nem achar que o sistema quebrou**.

## Contexto e referências
- `CLAUDE.md`: controle de acesso camadas 1 e 2 (módulo não liberado → botão com cadeado e mensagem exata **"Funcionalidade não liberada nessa versão do sistema."**; sem permissão → botão oculto); Interface → "Módulo não liberado: tela padrão `ModuleNotReleased` com o nome do módulo, a mensagem e uma linha explicando o que o módulo fará".
- ADR 0005 D8: o dock mostra as 7 áreas; cada área abre um menu (popover no desktop, sheet no celular) com os módulos; Ctrl K abre a paleta de comandos.
- `docs/04-fluxos-estados.md` §3.7 (regras de menu).
- Revisão §2.4: rótulos de "não liberado" e de contadores para leitor de tela.
- Protótipo: itens `.locked` com cadeado e o texto "Quando o módulo for liberado, o administrador habilita o acesso no grupo de usuários."
- Backlog original (CA): acessar a URL de um módulo bloqueado mostra a tela padrão, nunca uma tela quebrada ou em branco.

## Critérios de aceite
1. **Dado** um módulo não liberado no tenant, **quando** abro o menu da área dele, **então** o módulo aparece com cadeado e rótulo acessível "{módulo}, não liberado".
2. **Dado** esse módulo, **quando** clico nele ou acesso a URL direto, **então** vejo a tela `ModuleNotReleased` com o nome do módulo, a mensagem exata "Funcionalidade não liberada nessa versão do sistema." e a linha do catálogo explicando o que o módulo fará; nunca uma tela em branco ou quebrada.
3. **Dado** um módulo liberado sem nenhum grupo do usuário que o habilite, **quando** abro o menu, **então** o módulo não aparece; **quando** acesso a URL direto, **então** vejo a tela padrão de acesso negado (ver Pontos em aberto), nunca uma tela quebrada.
4. **Dado** uma área do dock sem nenhum módulo visível para o usuário (nem liberado com permissão, nem com cadeado), **quando** o dock é montado, **então** a área não aparece.
5. **Dado** a paleta de comandos (Ctrl K), **quando** busco, **então** só aparecem os módulos permitidos; módulos não liberados aparecem com cadeado e levam à tela padrão; módulos sem permissão não aparecem.
6. **Dado** uma permissão revogada enquanto o usuário navega, **quando** o próximo request devolve versão nova (FND-06c CA 15), **então** o dock e a paleta são remontados e, se a tela atual ficou sem acesso, ela mostra a tela de acesso negado.
7. **Dado** um contador no menu (ex.: OPs aguardando aprovação), **quando** é lido por leitor de tela, **então** o rótulo é "{módulo}, {N} pendentes".

## Regras de negócio e técnicas
- O menu é derivado de `/me/permissions` + metadados do `ModuleCatalog` (área, ordem, ícone, descrição); nenhuma lista de menu fixa no front.
- Rotas de módulo protegidas por um guard do TanStack Router que lê o mesmo estado de permissões.
- A autorização real continua no backend; o front só decide o que mostrar.
- Texto da mensagem idêntico ao do CLAUDE.md (teste compara a string).

## Permissões, eventos e tabelas
- Sem tabela nova. Usa `/me/permissions` e o catálogo.
- Permissões: nenhuma nova.

## Telas e estados
- **`ModuleNotReleased`:** ícone de cadeado, título com o nome do módulo, a mensagem exata, a linha do catálogo e "Quando o módulo for liberado, o administrador habilita o acesso no grupo de usuários." (protótipo). Ação: "Voltar ao início".
- **Acesso negado:** título "Sem acesso a esta tela", texto "Seu usuário não tem permissão para {módulo}. Peça ao administrador para incluir você num grupo com acesso." Ação: "Voltar ao início".
- **Estados do menu:** carregando (skeleton das áreas enquanto `/me/permissions` carrega); erro ("Não foi possível carregar o menu" + "Tentar de novo").
- **Ajuda:** `Ajuda/modulo-nao-liberado.md` e `Ajuda/acesso-negado.md`.

## Plano de testes
- **Unidade (Vitest):** montagem do menu a partir de `/me/permissions` (CA 1, 3, 4); paleta filtrada (CA 5); rótulos acessíveis (CA 1, 7).
- **E2E (Playwright):** usuário com módulo não liberado acessa a URL direto e vê a mensagem exata (CA 2); usuário sem permissão vê a tela de acesso negado (CA 3); revogação durante a navegação (CA 6); comparação com o protótipo nos dois temas e a 400px.

## Fora do escopo
- Os próprios módulos de negócio.
- Contadores reais (chegam com os módulos; aqui só o rótulo acessível e o slot).

## Pontos em aberto
- **Tela para "liberado mas sem permissão" acessado por URL:** o CLAUDE.md define só o 403 `forbidden` e que o botão fica oculto; o texto da tela de acesso negado acima é proposta. Confirmar com o PO.
- **Área "Telas dedicadas":** TV e Operador abrem superfícies próprias (`/tv`, `/fab`) sem dock; confirmar se o item do dock abre em nova aba ou na mesma.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade e E2E passando.
- [ ] Migration: não se aplica.
- [ ] Permissão registrada no seed: não se aplica.
- [ ] Telas com estados de carregando e erro.
- [ ] Ajuda das telas escrita.
- [ ] Padrões e próximo passo propositivos avaliados (ação "Voltar ao início").
- [ ] Auditoria: não se aplica.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Selector de menu a partir de `/me/permissions` e do catálogo.
- [ ] Guard de rota por módulo.
- [ ] Componentes `ModuleNotReleased` e `AccessDenied`.
- [ ] Integração com o dock, o popover/sheet e a paleta da FND-09b.
- [ ] Testes E2E.

## Quality gates
- Agentes previstos: @dev, @ux-design-expert, @qa.
- Gate: teste E2E da mensagem exata obrigatório.

## Validação do PO
Checklist do @po (Pax), 16/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança da revisão §2.3 que se aplicam | N/A |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** —
- **Pré-condições para `Ready`:** Texto da tela de acesso negado.
- **Prontidão:** 9/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 16/10/2026 | 0.1 | Story detalhada com ADR 0005 D8 e os ajustes de acessibilidade da revisão | River (SM) |
| 16/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |

## Dev Agent Record
_A preencher pelo @dev._

## QA Results
_A preencher pelo @qa._