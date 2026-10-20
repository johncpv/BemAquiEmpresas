# ADR 0021: Mensagens do sistema e próximos passos

- Status: Aceito (20/10/2026). Decisão D11 do PO (John) na revisão pré-Fase 2. Resolve a pendência P-18.
- Contexto: hoje o sucesso aparece num toast (sonner) no canto inferior, escondido atrás do dock e sem destaque ("Terceiro salvo"). O PO pediu em 19/10/2026 as mensagens num lugar visível (P-18). A WCAG impõe dois limites: um sucesso que some sozinho não pode bloquear a tela, e um erro precisa de foco e de uma ação. O `toastDone` e o `ConfirmDialog` também carregam o próximo passo dentro do toast, que some antes de o usuário ler.
- Altera o padrão da FND-09c (toast) e da FND-09d (`ConfirmDialog`). Complementa as ADRs 0009 e 0013.

## Decisão

### Sucesso
- Aparece numa **região `role="status"`** (`aria-live="polite"`) **no topo da área de conteúdo**, logo abaixo da barra superior, nunca atrás do dock nem no rodapé.
- **Não bloqueia:** não é modal, não rouba o foco e não cobre o formulário.
- Fica visível por **no mínimo 5 s**, e enquanto o ponteiro ou o foco estiverem sobre ela; tem botão "Fechar".
- O texto é o particípio do verbo da ação ("Título a receber salvo", "Aprovada", "Pagamento registrado").
- Mensagens seguidas se empilham (até 3); a mais nova fica em cima.

### Erro
- **Erro de validação (422):** sempre no campo, inclusive em linhas de editor e dentro de diálogos, com o contador por aba e o foco no primeiro campo com erro (FND-09d). O `applyServerErrors` aceita caminhos aninhados (`lines[2].unitCost`).
- **Erro de regra ou de servidor durante uma ação:** sempre **dentro do diálogo ou do formulário** que fez a ação, num bloco `role="alert"` com o foco movido para ele, dizendo o que houve e como resolver, e com a ação de recuperação quando existe (`recovery` do `ConfirmDialog`: "Estornar baixa", "Inativar").
- **Conflito de concorrência (409 `concurrency_conflict`, ADR 0019):** dentro do diálogo ou formulário, com o botão "Recarregar", que busca a versão atual sem perder o que é do usuário quando possível.
- **Erro fora de diálogo** (ação direta numa lista, falha de rede): na mesma região do topo, como `role="alert"`, sem sumir sozinho, com "Tentar de novo".
- **Falha ao carregar uma tela ou aba:** o `ErrorState` da FND-09c, como hoje.

### Aviso
- Aviso que não bloqueia (âmbar) fica junto do campo ou no corpo do diálogo, nunca na região de mensagens.

### Próximo passo
- O próximo passo **nunca vai na mensagem de sucesso**. Vai sempre no `NextSteps` (faixa fixa no detalhe, até 3 ações, FND-09d) ou no corpo do diálogo de conclusão.
- O `toastDone` e o `ConfirmDialog` perdem o parâmetro `nextStep`. Quem precisa de próximo passo usa o `NextSteps`.

### Processamento assíncrono
- Quando o efeito acontece depois (ex.: parcelas do contrato pelo outbox), a mensagem de sucesso diz o que já foi feito e o que está em andamento ("Aprovado: contrato e projeto criados. Gerando as parcelas…"). A tela de destino mostra o estado "em andamento" e, depois de cerca de 30 s, o estado de demora com o que fazer.

## Consequências
- A **FND-09e** implementa: a região de mensagens no shell (no lugar do `Toaster` no rodapé), o `notifySuccess`/`notifyError` (substituem o `toastDone` e o `toastError`), o `ConfirmDialog` sem `nextStep`, com tamanho grande e erro por campo, e o `applyServerErrors` com caminhos aninhados. As telas das Fases 0 e 1 passam a usar a região nova na mesma story (troca mecânica, sem mudar texto).
- Os CAs das stories da Fase 2 dizem "a mensagem de sucesso diz '…'" no lugar de "o toast diz '…'".
- A pendência P-18 está marcada como decidida em `06-pendencias-riscos.md`.
- As ajudas que citam o toast passam a citar a mensagem no topo da tela.
