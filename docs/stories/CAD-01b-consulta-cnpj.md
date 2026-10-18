# CAD-01b — Consulta de CNPJ que preenche o cadastro do terceiro

| Campo | Valor |
|---|---|
| ID | CAD-01b |
| Título | Consulta automática de CNPJ em API pública, com cache, adaptador `ICnpjLookup`, fallback manual e "Atualizar pela Receita" |
| Tamanho | M (quebra da CAD-01) |
| Status | Draft |
| Requisitos | R-TER-1 (consulta automática de dados por CNPJ) · R-PRP-3 (CNPJ preenche cadastro) · R-PRP-7 (sugere, o usuário confirma) · PRD §6 Observabilidade (alerta de integração) |
| Dependências | CAD-01a; Fase 0 (FND-12 alerta de integração). **Pré-condição:** ponto em aberto 1 (provedor) |
| Bloqueia | — |
| Ordem de execução | 5 |

## User story
Como **quem cadastra clientes e fornecedores**, quero **digitar só o CNPJ e ver razão social, nome fantasia e endereço preenchidos**, para **cadastrar mais rápido e sem erro de digitação**.

## Contexto e referências
- PRD R-TER-1 ("consulta automática de dados por CNPJ (API pública)"), R-PRP-3 ("CNPJ preenche cadastro").
- `docs/02-arquitetura.md` §1 (integração "API CNPJ") e §6.3 ("Consulta de CNPJ (API pública, ex.: BrasilAPI) com cache"). O "ex." não é decisão: o provedor está em aberto.
- FND-08 (regra "nunca buscar URL fornecida pelo tenant"), FND-12 (alerta de falha de integração, chave por integração), ADR 0014 (padrão de adaptador só com `HttpClient`, tempo limite, erros traduzidos, sem nova tentativa).
- Backlog CAD-01 (CA): "consulta preenche razão social e endereço".

## Critérios de aceite
1. **Dado** o formulário "Novo terceiro" com tipo PJ, **quando** digito um CNPJ válido e saio do campo (ou clico "Consultar CNPJ"), **então** o sistema consulta o provedor e mostra os dados encontrados (razão social, nome fantasia, endereço completo com código IBGE, telefone, e-mail e situação cadastral) num quadro "Dados da Receita", com o botão "Usar estes dados".
2. **Dado** o quadro "Dados da Receita", **quando** clico "Usar estes dados", **então** os campos do formulário são preenchidos e continuam editáveis; nada é gravado até eu clicar "Salvar terceiro" (R-PRP-7).
3. **Dado** um CNPJ com situação cadastral diferente de "Ativa" (baixada, inapta, suspensa, nula), **quando** a consulta volta, **então** o quadro mostra o chip de alerta "Situação na Receita: {situação}" e o cadastro não é bloqueado.
4. **Dado** um CNPJ já consultado nos últimos 30 dias (prazo no ponto em aberto 3), **quando** consulto de novo, **então** o resultado vem do cache, sem chamar o provedor, com a data da consulta ("Consultado em dd/mm/aaaa").
5. **Dado** o provedor fora do ar, com erro ou acima do tempo limite (sugestão: 5 s), **quando** consulto, **então** a tela diz "Não foi possível consultar o CNPJ agora. Preencha os dados ou tente de novo." e o cadastro manual segue; a falha conta para o alerta de integração `cnpj:<provedor>` (FND-12).
6. **Dado** um CNPJ não encontrado no provedor, **quando** consulto, **então** a tela diz "CNPJ não encontrado na base pública. Confira o número ou preencha os dados."
7. **Dado** um CNPJ inválido, **quando** tento consultar, **então** a API responde 400 "CNPJ inválido." e o provedor não é chamado.
8. **Dado** um terceiro PJ existente com `parties.edit`, **quando** clico "Atualizar pela Receita", **então** vejo a comparação campo a campo (atual × Receita), escolho quais aplicar e confirmo "Aplicar selecionados"; o resto não muda.
9. **Dado** o tipo PF, **quando** informo o CPF, **então** não há consulta (não há fonte pública; LGPD) e só a validação do dígito vale.
10. **[Segurança]** **Dado** o endpoint `GET /api/v1/parties/cnpj/{cnpj}`, **quando** um usuário sem `parties.create` e sem `parties.edit` chama, **então** recebe 403 `forbidden`; com o módulo `parties` não liberado, 403 `module_not_released`.
11. **[Segurança]** **Dado** muitas consultas seguidas, **quando** um usuário passa do limite (sugestão: 30 por minuto por usuário + tenant), **então** recebe 429 com "Muitas consultas seguidas. Aguarde um minuto."
12. **[Segurança]** **Dado** a chamada ao provedor, **quando** inspeciono a requisição (servidor HTTP falso), **então** ela leva só o CNPJ, vai para a URL base fixa da configuração da plataforma (nunca informada pelo tenant), e a resposta é validada por esquema e tratada como texto (sem HTML); campos fora do esperado são descartados.
13. **Dado** o adaptador, **quando** troco de provedor por configuração, **então** nada muda na tela nem no cadastro (interface `ICnpjLookup`).

## Regras de negócio e técnicas
- `ICnpjLookup.LookupAsync(cnpj)` em `Parties.Application`, devolvendo `Result<CnpjData>`; adaptadores em `Parties.Infrastructure`, só com `HttpClient` (sem SDK), com tempo limite e sem nova tentativa (mesmo padrão da ADR 0014).
- Cache: dado público, guardado no banco do tenant (**proposta**: `parties.cnpj_lookups` com `document`, `payload` jsonb, `looked_up_at`) para não depender de memória entre instâncias. O cache não é dado pessoal (PJ).
- O mapeamento da resposta (CEP, UF, código IBGE, logradouro) para os campos do terceiro fica no adaptador; o domínio recebe sempre `CnpjData`.
- Logs: o CNPJ pode aparecer (dado público de PJ); a resposta completa do provedor não é logada.
- Rate limit pela infraestrutura existente da FND-04 (política nova, proposta `RateLimit:CnpjPerMinute`).
- CNPJ alfanumérico: se o provedor ainda não o aceitar, a consulta devolve "não encontrado" e o cadastro segue manual.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `parties.create` ou `parties.edit` para consultar.
- **Eventos:** nenhum.
- **Tabelas:** `parties.cnpj_lookups` (**proposta**, não está no 03).

## Telas e estados
- Dentro do formulário de terceiro (CAD-01a): botão "Consultar CNPJ" ao lado do documento; quadro "Dados da Receita" com os dados, o chip de situação e "Usar estes dados"; na edição, "Atualizar pela Receita" com a comparação.
- **Estados:** consultando (indicador no campo, sem travar o formulário); encontrado; não encontrado; falha com o texto do CA 5; limite excedido.
- **Propositivo:** a consulta dispara sozinha ao sair de um CNPJ válido; o quadro já marca todos os campos vazios para aplicar.
- **Ajuda:** seção "Consulta de CNPJ" e pergunta frequente "Por que a consulta não achou meu CNPJ?" em `Ajuda/cadastros-terceiro.md` (aba `dados`); `<Dica>` no botão "Consultar CNPJ".

## Plano de testes
- **Unidade:** mapeamento da resposta de cada adaptador para `CnpjData` (respostas gravadas); comparação campo a campo (CA 8).
- **Integração (Testcontainers + servidor HTTP falso):** consulta e preenchimento (CA 1, 2); situação não ativa (CA 3); cache (CA 4); fora do ar e tempo limite contando para o alerta (CA 5); não encontrado (CA 6); CNPJ inválido sem chamada (CA 7); 403 e 429 (CA 10, 11); requisição só com o CNPJ e para a URL fixa (CA 12); troca de adaptador (CA 13).
- **E2E (Playwright):** digitar CNPJ, usar os dados e salvar, com o servidor falso.

## Fora do escopo
- Consulta de CEP (ver ponto em aberto 4).
- Consulta de CPF.
- Validação da inscrição estadual pelo Sintegra.

## Pontos em aberto
1. **Provedor da consulta** (pré-condição): os docs citam BrasilAPI só como exemplo. Sugestão: **BrasilAPI** (`/api/cnpj/v1/{cnpj}`, gratuita, sem chave) como principal e **ReceitaWS** (plano gratuito com limite por minuto) ou **CNPJá** como reserva. Confirmar termos de uso, limite e disponibilidade antes de implementar; registrar em ADR.
2. **Reserva automática:** quando o principal falha, tentar o segundo provedor (aumenta o tempo de resposta) ou só avisar.
3. **Validade do cache** (sugestão: 30 dias).
4. **Consulta de CEP** para preencher endereço de PF e endereços extras (R-PRP-3): incluir aqui (sugestão: BrasilAPI CEP ou ViaCEP) ou em story própria.
5. **Situação cadastral não ativa:** só avisar (proposta) ou exigir confirmação extra para cliente com CNPJ baixado.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (com servidor HTTP falso) e E2E passando.
- [ ] Migration criada (`cnpj_lookups`, se aceita).
- [ ] Permissões usadas só do `ModuleCatalog`.
- [ ] Estados da consulta na tela (consultando, encontrado, não encontrado, falha, limite).
- [ ] Ajuda atualizada (aba `dados` do terceiro).
- [ ] Propositivo avaliado (consulta automática, aplicar só o que falta).
- [ ] Alerta de integração ligado (FND-12).
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] ADR do provedor registrada; `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `ICnpjLookup`, `CnpjData` e o adaptador do provedor escolhido.
- [ ] Cache no banco e política de rate limit.
- [ ] Endpoint `GET /api/v1/parties/cnpj/{cnpj}`.
- [ ] Quadro "Dados da Receita" e comparação "Atualizar pela Receita".

## Quality gates
- Agentes previstos: @dev, cybersecurity (integração externa e resposta não confiável), @qa.
- Gate: CA 12 (requisição mínima e URL fixa) obrigatório.

## Validação do PO
Checklist do @po (Pax), 18/10/2026.

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

- **Correções feitas na validação:** —
- **Pré-condições para `Ready`:** Ponto em aberto 1 (provedor da consulta, com ADR).
- **Prontidão:** 9/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da CAD-01) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
