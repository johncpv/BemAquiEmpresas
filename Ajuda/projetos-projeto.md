---
titulo: Projeto — resumo
rota: /app/projetos/$id
aba: resumo
padrao: true
modulo: projects
---

## Para que serve
Acompanhar o que a fábrica vai entregar do contrato. O projeto nasce na aprovação do orçamento, com os itens e o orçado por natureza de custo.

## Como usar
- O cabeçalho mostra a etapa: **P** Planejado, **E** Em produção, **F** Fabricado, **I** Instalando, **T** Entregue e **C** Encerrado.
- A data prometida é a data de assinatura mais o prazo de entrega do orçamento, em dias corridos.
- O responsável é quem fez o orçamento.
- **Fabricação concluída automática:** quando o projeto tem OS de fabricação, ele passa a **Fabricado** sozinho quando a última OS de fabricação (não cancelada) é concluída. A parcela do marco "fabricação concluída" é liberada na hora, com vencimento contado da data da conclusão. A linha do tempo mostra "Fabricação concluída pelas OS".
- **Registrar fabricação concluída:** só para projeto **sem** OS de fabricação (por exemplo, só serviço). Informe a data (até hoje). O projeto passa a fabricado e a parcela do marco de fabricação ganha vencimento. O diálogo mostra qual parcela é liberada.
- **Registrar entrega** (Proprietário): para venda sem instalação. Informe a data; o projeto passa a entregue e as parcelas de entrega e de aceite técnico são liberadas. Se o contrato tem marco de fabricação, registre a fabricação antes.
- O **Resumo** mostra o contratado, a entrega prometida, o contrato e o orçamento de origem. Quem vê o financeiro vê também o recebido e o que falta receber; quem vê custo, a margem orçada e a projetada.
- Quem pode editar muda o nome, o responsável e o endereço da obra e clica em **Salvar projeto**. A data prometida e o valor só mudam por aditivo.

## Perguntas frequentes
**Quando usar "Registrar entrega"?** Quando não há instalação. Com OS de instalação, a entrega vem do aceite técnico.

**Por que o botão "Registrar fabricação concluída" sumiu?** Porque o projeto tem OS de fabricação. A fabricação é concluída pelas OS: conclua cada OS e o projeto passa a fabricado sozinho. O aviso no topo mostra quantas OS faltam e leva até elas.

**A conclusão de uma OS foi estornada. E o projeto?** Se ele foi fabricado pelas OS, volta para Em andamento. A parcela já liberada continua em aberto e o Financeiro é avisado.

**Como mudo valor ou prazo do projeto?** Pelo botão **Aditivo**, que leva ao contrato. A data prometida e o valor mudam com o aditivo aprovado.

**Posso mudar o nome, o responsável ou a data?** Numa próxima versão, na tela completa do projeto.

**De onde vem o orçado?** Da composição de custo dos itens do orçamento aprovado. A soma das naturezas fecha no custo total do orçamento.
