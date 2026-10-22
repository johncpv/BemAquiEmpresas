# ADR 0033: Gantt próprio em SVG

- Status: Aceito (21/10/2026). O PO (John) aprovou a decisão D18 da revisão pré-Fases 4 a 8 (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`), o ponto 42 da tabela 3.1 e o padrão 1 da seção 4.4 (família de gráficos sobre `ChartFrame`).
- Contexto: a PLN-03 precisa de um cronograma por projeto e por centro de trabalho, com até 1.500 barras (300 OS com 5 etapas), planejado × realizado × projetado, acessível por teclado e com tabela equivalente, nos temas claro e escuro e a 400 px. A stack do front não tem biblioteca de Gantt. As opções conhecidas são pesadas, têm licença comercial nas versões com o que precisamos (dependências, linha do hoje, virtualização) ou trazem estilo próprio que não segue os tokens (toda cor vem de token; nenhum componente usa cor literal).
- Complementa a ADR 0009 (bibliotecas do front) e o padrão 1 das Fases 4 a 8 (`ChartFrame`).

## Decisão

### 1. Componente próprio, sem biblioteca
- **`Gantt`** nos componentes de UI (`src/Web/src/components/`), desenhado em **SVG**, sem biblioteca nova. Recharts continua para os gráficos de série (`Chart`, `ColumnChart`), e os dois ficam sobre o mesmo **`ChartFrame`** (título, resumo textual acima, legenda, estados de carregando, vazio e erro, "Ver como tabela").
- Datas calculadas no fuso `America/Sao_Paulo` com utilitário do próprio projeto (sem biblioteca de datas nova).

### 2. Regras do componente
- **Escala** dia, semana e mês, com a linha "Hoje" e os dias não úteis do calendário sombreados; referências em x (hoje, data prometida) desenhadas como linha com rótulo.
- **Série nunca só por cor:** planejado, realizado e projetado diferenciados por **traço** (contínuo, hachurado, tracejado) e por **rótulo direto**; atrasado com faixa lateral e chip "Atrasada", em risco com âmbar (cor semântica, separada do acento). Cores só dos tokens; troca do `--chart-3`, hoje âmbar, para não confundir série com estado.
- **Virtualização** por linha (só as linhas visíveis e um buffer são desenhadas); meta de desempenho da PLN-03: 1.500 barras com rolagem fluida num notebook comum.
- **Acessibilidade:** cada barra é focável, com navegação por setas (linha e tempo), `aria-label` com etapa, centro, datas e situação; **tabela equivalente** com as mesmas linhas e totais; resumo textual acima ("12 OS, 3 atrasadas, próxima entrega 05/11").
- **Só leitura no MVP:** o Gantt não arrasta nem reagenda (as datas são do motor, ADR 0029); clique abre o painel lateral da OS ou do projeto. OS de instalação aparece como barra só de leitura.
- Coluna de nomes fixa à esquerda; a 400 px, a coluna vira cabeçalho da linha e a escala padrão é a semana.

### 3. Entrega
- O `Gantt` nasce **na PLN-03**, a última story do Marco 4.3 (o `ChartFrame` já existe desde o Marco 4.1, FIN-07), com página de exemplo e testes de componente (Vitest) para escala, virtualização, teclado e tabela equivalente.

## Consequências
- **PLN-03** implementa o componente e as telas; **PLN-04** usa o `ChartFrame` com `ColumnChart` para a ocupação.
- Nenhuma dependência nova no `package.json` para o cronograma.

## Alternativas consideradas
- **Biblioteca de Gantt pronta:** rejeitada; licença comercial ou peso, estilo fora dos tokens e acessibilidade fraca para teclado e leitor de tela.
- **Grade CSS no lugar de SVG:** rejeitada; linhas de referência, hachuras e virtualização de 1.500 barras ficam mais simples e leves em SVG.
