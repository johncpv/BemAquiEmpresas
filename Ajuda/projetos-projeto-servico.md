---
titulo: Projeto de serviço — resumo
rota: /app/projetos/$id
aba: servico
padrao: false
modulo: projects
recurso: projects.service_execution
---

## Para que serve
Dar andamento ao projeto de serviço (consultoria, implantação, manutenção): iniciar, registrar a entrega e o aceite do cliente, vendo antes o que cada passo libera para cobrança. O projeto de serviço nasce quando um orçamento só com itens de serviço é aprovado.

## Como usar
- O cabeçalho mostra o chip **Serviço** e a etapa no display de andar: **P** Planejado, **I** Em execução, **E** Entregue, **A** Aceito, **C** Encerrado. Quando o contrato não pede o aceite do cliente e ele não foi registrado, o **A** aparece como um traço (aceite dispensado).
- Abaixo do cabeçalho, uma linha diz em que pé o projeto está e qual é o próximo passo. O botão principal é sempre o próximo passo: **Iniciar execução**, **Registrar entrega** ou **Registrar aceite do cliente**. No celular, ele fica fixo no rodapé.
- Sem a permissão do passo, o botão não aparece e a linha diz quem pode fazer.
- Cada passo abre um diálogo com a data (padrão hoje) e o bloco **efeito nas parcelas**: as parcelas que passam a valer para cobrança, com valor e vencimento (para quem vê o financeiro), e o total. Data anterior a hoje pede o motivo.
- O chip âmbar **Aguardando aceite do cliente** aparece quando o serviço foi entregue e o contrato liga uma parcela ao aceite.
- **Registrar aceite do cliente** pede quem aceitou (obrigatório), o papel ou cargo e, se tiver, o termo assinado.
- Em **Mais ações**: **Registrar entrega** direto do planejado (o projeto passa por Em execução) e **Desfazer aceite**, com o motivo.

## O que libera cada passo
| Passo | O que libera |
|---|---|
| Iniciar execução | Nada. Só marca o início do trabalho. |
| Registrar entrega | As parcelas ligadas à entrega e, em contrato antigo, a de "conclusão da execução (antes: fabricação concluída)". |
| Registrar aceite do cliente | As parcelas ligadas ao aceite do cliente. |
| Desfazer aceite | A parcela de aceite volta a aguardar, se ainda não foi recebida nem cobrada. |

## Imprimir e exportar
- O PDF do resumo mostra a etapa P · I · E · A · C e só a data do aceite ("Aceito em …"). Quem aceitou não sai no PDF nem nas exportações.

## Perguntas frequentes
**Entreguei, mas não tenho parcela ligada ao aceite. Preciso registrar o aceite?** Não é obrigatório. Na entrega, quem pode registrar o aceite vê a caixa **Também registrar que o cliente aceitou**, que vem sempre desmarcada.

**Por que não consigo desfazer o aceite?** A parcela de aceite já foi recebida, cobrada ou há nota emitida depois do aceite. Para corrigir, faça um aditivo.

**Quando o projeto encerra?** Sozinho, quando a última parcela for recebida (com o aceite registrado, se o contrato pede).

**O que aparece na Minha pauta?** O aceite do cliente pendente, para o responsável do projeto: com parcela ligada ao aceite, desde o dia da entrega; sem parcela, a partir de 7 dias depois da entrega.
