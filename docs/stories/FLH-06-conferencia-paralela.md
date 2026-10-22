# FLH-06 — Folha calculada: conferência paralela com a contadora e virada

| Campo | Valor |
|---|---|
| ID | FLH-06 |
| Título | Conferência paralela: para cada competência, a folha lançada (da contadora, oficial) e a calculada (simulação) lado a lado, por funcionário e por rubrica, com a diferença em R$ e em %, classificada e justificada; aceite da competência só com diferença zero no líquido e em cada rubrica, assinado pela contadora responsável; virada pelo Proprietário depois de 3 competências seguidas aceitas; férias, 13º e rescisão viram oficiais cada um depois de um caso conferido; volta para a lançada com motivo |
| Tamanho | M |
| Status | Ready (pré-condição externa) |
| Requisitos | R-RH-6 · `06` Riscos (3 meses de conferência paralela antes de substituir) · R-SEC-5 · R-PRP-2 |
| Dependências | FLH-02a e FLH-02b (calculada mensal); RH-02a (lançada e importação); RH-03 (pacote para a contadora); FLH-03 a FLH-05 (casos de férias, 13º e rescisão que ocorrerem) |
| Bloqueia | Virada da folha calculada (FLH-02a CA 15, FLH-03 CA 8, FLH-04 CA 5, FLH-05 CA 7) |
| Ordem de execução | 7 (Marco 7.3 "Virada"; a comparação começa a ser usada depois da FLH-02b e corre em paralelo às FLH-03 a FLH-05) |

## User story
Como **proprietário**, quero **comparar por três meses a folha calculada pelo sistema com a da contadora e só então trocar**, para **assumir o cálculo com segurança, sem pagar errado ninguém**.

## Contexto e referências
- `06` Riscos: "Folha calculada completa… responsabilidade legal" → mitigação "3 meses de conferência paralela com a contadora antes de substituir".
- Backlog FLH-06 ("Conferência paralela com a folha da contadora por 3 meses antes de virar a chave").
- RH-02a (lançada pela contadora), FLH-02a CA 15 (fechamento da calculada bloqueado até a virada).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D1 (Fase 7 antes do go-live); tabela 3.4, pontos 25 a 28 e linha "Determinismo"; §4.3 FLH-01/02 (`parallel_differences` com `[PersonalData]`); §4.4 padrão 3 e o achado pontual "FLH-06 diferença em R$ e % por rubrica".
- **ADR 0039** (`docs/adr/0039-motor-folha-calculada.md`): dados sintéticos no CI, dataset de referência da contadora só no banco do tenant, nunca no Git.
- **A story fica `Done` com a ferramenta pronta e testada**; a virada em si é uma operação que acontece depois de 3 competências aceitas e não é critério de `Done` desta story nem do marco.

## Critérios de aceite
**Comparação**
1. **Dado** uma competência com a folha lançada (oficial) e a calculada (simulação), **quando** abro RH → Conferência da folha, **então** vejo por funcionário e por rubrica: valor da contadora, valor do sistema, **diferença em R$ e em %** (sobre o valor da contadora; "—" quando ele é zero) e a situação (Igual, Diferente), com o total de diferenças, o líquido de cada lado e a diferença do líquido; a correspondência de rubricas usa o código (RH-02a) e o de-para editável quando os códigos diferem.
2. **Dado** uma diferença, **quando** a classifico ("Erro do sistema", "Erro da contadora", "Regra diferente: ajustar parâmetro", "Arredondamento") e escrevo a justificativa, **então** ela fica registrada; "Erro do sistema" gera o caso de correção anonimizado (rubricas, bases, parâmetros e entradas sem nome, CPF, matrícula nem data de nascimento), exportável para virar caso no dataset sintético do CI; "Regra diferente" leva à tela do parâmetro (Configurações → Parâmetros → RH).

**Aceite e virada**
3. **Dado** a competência, **quando** a diferença é **zero no líquido e em cada rubrica** de todos os funcionários, **então** o Proprietário pode clicar "Aceitar competência {mês}", identificar a contadora responsável (nome e CRC) e anexar o "Termo de conferência da competência {mês}" (PDF gerado pelo sistema com os totais, assinado pela contadora e anexado de volta); a competência fica `Accepted` com quem, quando, a contadora e o termo; com qualquer diferença, "Aceitar" fica bloqueado e a competência fica `Rejected` até nova comparação.
4. **Dado** uma diferença classificada como "Erro da contadora" ou "Arredondamento", **quando** quero aceitar, **então** não basta a justificativa: a lançada corrigida é importada de novo (RH-02a) ou o parâmetro é ajustado e a calculada recalculada, e a comparação refeita precisa dar zero.
5. **Dado** 3 competências **seguidas** aceitas (`hr.parallel_months`, padrão 3, sem exigir competência com férias, 13º ou rescisão), **quando** o Proprietário clica "Virar para a folha calculada" e confirma no diálogo com o impacto em lista (competências aceitas, contadora, o que passa a ser oficial), **então** a partir da competência seguinte a calculada **mensal** pode ser fechada como oficial (FLH-02a CA 15 liberado) e a lançada continua disponível; a decisão fica registrada (data, quem, competências, contadora) e notificada aos membros do Proprietário.
6. **Dado** um caso de férias, 13º ou rescisão comparado com a folha lançada do mesmo tipo e aceito com diferença zero (mesma regra do CA 3), **quando** a virada mensal já foi feita, **então** aquele tipo passa a poder fechar como oficial (FLH-03 CA 8, FLH-04 CA 5, FLH-05 CA 7); a tela mostra por tipo "Oficial desde {data}" ou "Aguardando um caso conferido".
7. **Dado** a virada feita, **quando** uma competência posterior tem divergência grave apontada pela contadora, **então** o Proprietário pode "Voltar para a folha lançada" com motivo (por tipo ou para todos), e a contagem do CA 5 recomeça.
8. **Dado** a exportação para a contadora (RH-03), **quando** a conferência está em andamento, **então** o pacote inclui `folha-calculada.csv` (mesmo layout da lançada) para a contadora comparar do lado dela.

**Segurança e auditoria**
9. **[Segurança]** **Dado** o módulo `hr` não liberado, **quando** chamo, **então** 403 `module_not_released`; ver, classificar e justificar com `hr.view_sensitive`; aceitar, virar e voltar só membro do Proprietário; sem a permissão, 403 `forbidden` e o botão some. `hr.parallel_differences` é `[PersonalData]` (valores por funcionário), entra no `IPersonalDataProvider` (LGPD-01a) e fica fora do log; os casos de correção não levam nome, CPF, matrícula nem data de nascimento (teste com canário). `RequireModule` e `perm:`.
10. **Dado** justificativas, aceites, termos, virada e volta, **quando** abro o histórico, **então** vejo quem, quando e o quê (valores só com `hr.view_sensitive`).

## Regras de negócio e técnicas
- **Tabelas:** `hr.parallel_runs` (`period`, `run_type` Monthly/Vacation/Thirteenth/Termination, `entered_run_id`, `calculated_run_id`, `status` Open/Accepted/Rejected, `accepted_by`, `accepted_at`, `accountant_name`, `accountant_crc`, `signed_term_attachment_id`), `hr.parallel_differences` (`parallel_run_id`, `employee_id`, `payroll_item_code`, `entered`, `calculated`, `difference`, `difference_pct`, `classification`, `justification`; `[PersonalData]`), `hr.payroll_item_mappings`, `hr.payroll_mode_changes` (`run_type`, `mode`, `from_period`, `changed_by`, `reason`); parâmetros `hr.payroll_mode` (`Entered`/`Calculated`, por tipo) e `hr.parallel_months`.
- Comparação por funcionário e rubrica; tolerância **zero** (ponto 25); `difference_pct` com 4 casas.
- O termo assinado é anexo `Payroll` do HR (`IAttachmentService`, varredura antes de liberar).

## Permissões, eventos e tabelas
- **Permissões:** `hr.view_sensitive`; Proprietário para aceitar, virar e voltar.
- **Eventos:** `PayrollModeChanged` (`{ runType, mode, fromPeriod }`), dentro do HR.
- **Tabelas:** acima.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 3 (tabela analítica com coluna fixa do funcionário, navegação por célula e drill-down da diferença em painel lateral com o total igual ao da célula); 7 (virada com o impacto em lista); 8 (bloco "Dados protegidos").
- **RH → Conferência da folha** (`/app/rh/conferencia-folha`): competências com o chip (Em conferência, Aceita, Reprovada), contador "{n} de 3 competências seguidas aceitas", situação por tipo (mensal, férias, 13º, rescisão); detalhe com a tabela lado a lado (R$ e %), filtro "Só diferenças" e o drill-down na memória de cálculo da linha (FLH-02a CA 12).
- **Verbos:** "Aceitar competência" / "Competência aceita"; "Virar para a folha calculada" / "Folha calculada é a oficial"; "Voltar para a folha lançada" / "Folha lançada é a oficial".
- **Estados:** vazio ("Calcule a folha da competência para comparar com a da contadora." + "Calcular folha"); carregando; erro; sem a lançada ("Importe a folha da contadora de {mês} para comparar." + link).
- **Propositivo:** ordena pelas rubricas com mais diferença; "Ajustar parâmetro" leva à tela do parâmetro; o termo de conferência vem pronto para imprimir e assinar.
- **400 px:** a tabela vira cartões por funcionário com as rubricas diferentes primeiro.
- **Ajuda:** `Ajuda/rh-conferencia-folha.md`; `<Dica>` em "Virar para a folha calculada", "Diferença %" e "Termo de conferência".

## Plano de testes
- **Unidade:** comparação, diferença em R$ e % e tolerância zero (CA 1, 3); contagem de competências seguidas e por tipo (CA 5, 6).
- **Integração (Testcontainers):** classificação, caso anonimizado e reprovação (CA 2, 4); aceite com contadora e termo só pelo Proprietário (CA 3, 9); virada mensal e por tipo (CA 5, 6); volta (CA 7); pacote da RH-03 (CA 8); `[PersonalData]` e canário (CA 9); histórico (CA 10).
- **E2E (Playwright):** comparar uma competência do tenant `demo` com dados sintéticos e aceitar.

## Fora do escopo
- Integração automática com o sistema da contadora.
- eSocial, DCTFWeb e FGTS Digital: fora do produto (PRD §3.3).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.4, pontos 25–28 e linha "Determinismo"):
1. Aceite da competência só com diferença zero no líquido e em cada rubrica (CA 3, 4).
2. Assina a contadora responsável (nome, CRC e termo assinado anexado), registrado (CA 3).
3. 3 meses seguidos sem exigir evento; férias, 13º e rescisão só viram oficiais depois de um caso conferido de cada tipo (CA 5, 6).
4. eSocial, DCTFWeb e FGTS Digital fora do produto.
5. Diferença em R$ e em % por rubrica (achado pontual §4.4, CA 1).

Decisões da revisão aplicadas: D1 (Fase 7 antes do go-live); §4.3 FLH-01/02 (`parallel_differences` com `[PersonalData]`, CA 9); ADR 0039 (o dataset da contadora fica no banco do tenant, nunca no Git; casos de correção anonimizados viram dados sintéticos, CA 2).

**Pré-condição externa:** o **dataset de referência da contadora** (a folha lançada oficial das competências em conferência e um caso de cada tipo: férias, 13º e rescisão) e a assinatura da contadora responsável são pré-condição da **virada**, não do `Ready` nem do `Done` desta story.

## Definition of Done
- [ ] Critérios de aceite atendidos; testes de unidade, integração e E2E passando.
- [ ] Migration criada (não destrutiva); ajuda escrita; teste de cobertura passando; auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; `docs/04-fluxos-estados.md` (modo da folha por tipo) atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Comparador com R$ e %; de-para de rubricas; caso de correção anonimizado.
- [ ] Aceite com termo e contadora; virada e volta por tipo; `folha-calculada.csv` no pacote da RH-03.
- [ ] Tela e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, revisão da contadora (externa, no termo e no layout).
- Gate: CA 3, 5, 6 e 9 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: RH-03 explícita; "Bloqueia" com os CAs de cada tipo) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** a virada é ato do Proprietário com 3 competências seguidas aceitas, como no risco do `06` (CA 4); caminho de volta (CA 5).
- **Correções da revalidação (21/10/2026):** pontos 25 a 28 decididos (tolerância zero no líquido e por rubrica, CA 3, 4; contadora responsável com termo assinado, CA 3; 3 meses sem evento e virada por tipo depois de um caso conferido, CA 5, 6); diferença em R$ e % (CA 1); `parallel_differences` com `[PersonalData]` e caso de correção anonimizado (CA 2, 9); `folha-calculada.csv` definido (CA 8); tabela analítica (padrão 3); a virada como operação, fora do `Done`; ADR 0039 citada; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: dataset de referência e assinatura da contadora, para a virada (não bloqueia).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D1, tabela 3.4 pontos 25–28 e "Determinismo", §4.3 FLH-01/02, §4.4 padrões 3, 7 e 8 e achado da diferença em R$ e %); nova ordem (Marco 7.3, ordem 7, antes do go-live); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
