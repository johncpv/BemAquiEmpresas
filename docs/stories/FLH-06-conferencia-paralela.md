# FLH-06 — Folha calculada: conferência paralela com a contadora e virada

| Campo | Valor |
|---|---|
| ID | FLH-06 |
| Título | Conferência paralela de 3 meses: para cada competência, a folha lançada (da contadora, oficial) e a calculada (simulação) lado a lado, por funcionário e por rubrica, com as diferenças classificadas e justificadas; critério de aceite da competência; virada da chave pelo Proprietário com registro, depois de 3 competências seguidas aceitas |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-RH-6 · `06` Riscos (3 meses de conferência paralela antes de substituir) · R-SEC-5 · R-PRP-2 |
| Dependências | FLH-02a e FLH-02b (calculada mensal); RH-02a (lançada); FLH-03 a FLH-05 para os cenários de férias, 13º e rescisão que ocorrerem no período |
| Bloqueia | Virada da folha calculada (FLH-02a CA 14) |
| Ordem de execução | 7 (Marco 7.3 "Virada"; começa depois da FLH-02b e corre em paralelo às FLH-03 a FLH-05) |

## User story
Como **proprietário**, quero **comparar por três meses a folha calculada pelo sistema com a da contadora e só então trocar**, para **assumir o cálculo com segurança, sem pagar errado ninguém**.

## Contexto e referências
- `06` Riscos: "Folha calculada completa… responsabilidade legal" → mitigação "3 meses de conferência paralela com a contadora antes de substituir".
- Backlog FLH-06 ("Conferência paralela com a folha da contadora por 3 meses antes de virar a chave").
- RH-02a (lançada pela contadora), FLH-02a CA 14 (fechamento da calculada bloqueado até a virada).

## Critérios de aceite
1. **Dado** uma competência com a folha lançada (oficial) e a calculada (simulação), **quando** abro RH → Conferência da folha, **então** vejo por funcionário e por rubrica: valor da contadora, valor do sistema, diferença e a situação (Igual, Diferente), com o total de diferenças e o líquido de cada lado; a correspondência de rubricas usa o código (RH-02a) e o de-para editável quando os códigos diferem.
2. **Dado** uma diferença, **quando** a classifico ("Erro do sistema", "Erro da contadora", "Regra diferente: ajustar parâmetro", "Arredondamento") e escrevo a justificativa, **então** ela fica registrada; "Erro do sistema" abre automaticamente a tarefa de correção (proposta: anotação para o @dev com o caso anonimizado) e reprova a competência.
3. **Dado** a competência, **quando** todas as diferenças estão justificadas e nenhuma é "Erro do sistema" (ou a diferença total fica dentro da tolerância do parâmetro, proposta R$ 0,00; Pontos em aberto 1), **então** o Proprietário clica "Aceitar competência {mês}" e ela fica `Accepted` com quem e quando.
4. **Dado** 3 competências seguidas aceitas (parâmetro `hr.parallel_months`, padrão 3), **quando** o Proprietário clica "Virar para a folha calculada" e confirma com a contadora identificada, **então** a partir da competência seguinte a calculada pode ser fechada como oficial (FLH-02a CA 14 liberado) e a lançada continua disponível; a decisão fica registrada (data, quem, competências aceitas) e notificada aos membros do Proprietário.
5. **Dado** a virada feita, **quando** uma competência posterior tem divergência grave apontada pela contadora, **então** o Proprietário pode "Voltar para a folha lançada" com motivo, e a regra do CA 4 recomeça.
6. **Dado** a exportação para a contadora (RH-03), **quando** a conferência está em andamento, **então** o pacote pode incluir `folha-calculada.csv` para a contadora comparar do lado dela (proposta).
7. **[Segurança]** **Dado** o módulo `hr` não liberado, **quando** chamo, **então** 403 `module_not_released`; ver e justificar com `hr.view_sensitive`, aceitar e virar só membro do Proprietário; os casos anonimizados das diferenças não levam nome, CPF nem matrícula. `RequireModule` e `perm:`.
8. **Dado** justificativas, aceites e virada, **quando** abro o histórico, **então** vejo quem, quando e o quê.

## Regras de negócio e técnicas
- **Tabelas (propostas):** `hr.parallel_runs` (`period`, `entered_run_id`, `calculated_run_id`, `status` Open/Accepted/Rejected, `accepted_by`, `accepted_at`), `hr.parallel_differences` (`parallel_run_id`, `employee_id`, `payroll_item_code`, `entered`, `calculated`, `classification`, `justification`), `hr.payroll_item_mappings`; parâmetro `hr.payroll_mode` (`Entered`/`Calculated`) e `hr.parallel_months`.
- Comparação por funcionário e rubrica; arredondamento não é diferença quando dentro da tolerância.

## Permissões, eventos e tabelas
- **Permissões:** `hr.view_sensitive`; Proprietário para aceitar e virar.
- **Eventos:** `PayrollModeChanged` (`{ mode, fromPeriod }`).
- **Tabelas:** acima.

## Telas e estados
- **RH → Conferência da folha** (`/app/rh/conferencia-folha`): competências com o chip (Em conferência, Aceita, Reprovada), contador "{n} de 3 competências aceitas"; detalhe com a tabela lado a lado e filtro "Só diferenças".
- **Estados:** vazio ("Calcule a folha da competência para comparar com a da contadora."); erro.
- **Propositivo:** destaca as rubricas com mais diferença; "Ajustar parâmetro" leva à tela do parâmetro.
- **Ajuda:** `Ajuda/rh-conferencia-folha.md`; `<Dica>` em "Virar para a folha calculada".

## Plano de testes
- **Unidade:** comparação e tolerância (CA 1, 3); contagem de competências seguidas (CA 4).
- **Integração (Testcontainers):** classificação e reprovação (CA 2); aceite e virada só pelo Proprietário (CA 3, 4, 7); volta (CA 5); histórico (CA 8).
- **E2E (Playwright):** comparar uma competência de teste e aceitar.

## Fora do escopo
- Integração automática com o sistema da contadora.

## Pontos em aberto
1. **Critério de aceite da competência** (PO + contadora): tolerância zero (proposta) ou por rubrica.
2. Quem da contadora assina a virada e como fica registrado (PO).
3. Se o período de 3 meses precisa incluir um mês com férias, 13º ou rescisão (proposta: não obrigatório, mas registrado).

## Definition of Done
- [ ] Critérios de aceite atendidos; testes de unidade, integração e E2E passando.
- [ ] Migration criada (não destrutiva); ajuda escrita; teste de cobertura passando; auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Comparador; de-para de rubricas; aceite e virada; tela e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, revisão da contadora.
- Gate: CA 3, 4 e 7 obrigatórios.

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

- **Correções feitas na validação:** a virada é ato do Proprietário com 3 competências seguidas aceitas, como no risco do `06` (CA 4); caminho de volta (CA 5).
- **Pré-condições para `Ready`:** ponto em aberto 1; FLH-02b concluída.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
