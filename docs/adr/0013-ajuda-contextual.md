# ADR 0013: Ajuda contextual (padrão NSHome)

- Status: Aceito (18/10/2026).
- Contexto: a FND-14 deixou em aberto quatro pontos: onde fica a pasta `Ajuda/`, o que significa `padrao`, se as rotas públicas e da plataforma entram na cobertura e onde guardar as ajudas já vistas. O NSHome (`D:\GIT\NSHome`) é a referência do PRD §5.15a.

## Decisão
- **Pasta:** `Ajuda/<chave>.md`, na raiz do repositório, como diz o CLAUDE.md.
  - Os arquivos são embutidos no assembly do Host como `Ajuda.<chave>.md`, como no NSHome.
  - O catálogo valida cabeçalho e seções na subida (ajuda inválida derruba o Host) e num teste de arquitetura (o CI falha).
- **Cabeçalho:** `titulo`, `rota` (o padrão da rota do TanStack Router, como `/app/$module`), `aba`, `padrao` e `modulo`.
  - `padrao: true` (ou `sim`, como no NSHome) marca a ajuda padrão da rota quando a tela não informa a aba.
  - Há no máximo uma ajuda padrão por rota e uma ajuda por rota + aba.
- **Seções obrigatórias:** "Para que serve", "Como usar" e "Perguntas frequentes".
- **Acesso:**
  - ajuda sem `modulo` é geral;
  - ajuda de módulo segue a permissão efetiva da FND-06c e devolve 403 com o código da camada que barrou;
  - `modulo: plataforma` não aparece nos hosts de tenant.
- **Cobertura:** o teste do front cruza todas as rotas do roteador com as ajudas, incluindo as rotas públicas e as da plataforma. As rotas que só redirecionam ficam de fora.
- **Já vista:** fica em `identity.user_preferences`, com chave `help:<chave>`, sem tabela nova. Por isso vale em qualquer dispositivo.
- **Renderização:** um renderizador próprio de Markdown, com parágrafos, listas, negrito e código, produz elementos React. Não há HTML cru: tag e script aparecem como texto.
- **Painel na superfície Plataforma:** os arquivos já estão escritos e cobertos pelo teste; o botão de ajuda da plataforma fica para quando ela ganhar mais telas.
