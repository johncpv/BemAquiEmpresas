# Manual do Proprietário

**Grupo:** Proprietário (do sistema). **Entra em:** Painel do proprietário (`/app/painel`). **Precisa de:** e-mail, senha e o código do aplicativo autenticador (2FA) em todo login.

Você vê tudo o que a versão contratada libera e é quem concede as ações sensíveis aos outros grupos. Cada tela tem o botão **Ajuda** com o passo a passo; aqui fica a sua rotina e o que liga uma tela à outra.

## Rotina

### Todo dia (10 a 15 minutos)

| Tarefa | Tela | Ajuda |
|---|---|---|
| Ver caixa, vencidos, margem e o que vai atrasar | Visão geral → Painel `/app/painel` | [painel](../../../Ajuda/painel.md) |
| Resolver a pauta: aprovações, pagamentos, qualidade, pedidos | Início `/app` (Minha pauta) | [inicio](../../../Ajuda/inicio.md) |
| Aprovar ou reprovar ordens de pagamento | Financeiro → Ordens de pagamento `/app/financeiro/ordens-de-pagamento` (filtro **Aguardando você**) | [financeiro-ordem-de-pagamento](../../../Ajuda/financeiro-ordem-de-pagamento.md) |
| Aprovar pedidos de compra | Fábrica → Compras → Pedidos `/app/compras/pedidos` | [compras-pedido](../../../Ajuda/compras-pedido.md) |
| Aprovar o orçamento que o cliente aceitou (gera o contrato) | Comercial → Orçamentos `/app/orcamentos/$id` | [comercial-orcamento](../../../Ajuda/comercial-orcamento.md) |
| Responder pedido de cancelamento de nota (prazo de 24 h) | Pauta → NF-e `/app/fiscal/saida/$id` | [fiscal-nota-saida](../../../Ajuda/fiscal-nota-saida.md) |

### Toda semana

| Tarefa | Tela | Ajuda |
|---|---|---|
| Conferir o caixa das próximas semanas nos dois cenários | Financeiro → Fluxo de caixa `/app/financeiro/fluxo-de-caixa` | [financeiro-fluxo-de-caixa](../../../Ajuda/financeiro-fluxo-de-caixa.md) |
| Cobrar quem está devendo | Financeiro → Inadimplência `/app/financeiro/inadimplencia` | [financeiro-inadimplencia](../../../Ajuda/financeiro-inadimplencia.md) |
| Ver os projetos que estão comendo a margem | Comercial → Projetos → Orçado × realizado `/app/projetos/margens` | [projetos-margens](../../../Ajuda/projetos-margens.md) |
| Ver os projetos em risco de atraso e a ocupação da fábrica | Fábrica → Produção → Planejamento `/app/producao/planejamento` | [producao-planejamento](../../../Ajuda/producao-planejamento.md) |
| Conciliar o extrato importado pelo Financeiro | Financeiro → Conciliação `/app/financeiro/conciliacao` | [financeiro-conciliacao](../../../Ajuda/financeiro-conciliacao.md) |
| Aprovar aditivos de contrato | Comercial → Contratos `/app/contratos/$id/aditivos/$aditivoId` | [comercial-contrato-aditivo](../../../Ajuda/comercial-contrato-aditivo.md) |

### Todo mês

| Tarefa | Tela | Ajuda |
|---|---|---|
| Ler o resultado do mês | Financeiro → DRE `/app/financeiro/dre` | [financeiro-dre](../../../Ajuda/financeiro-dre.md) |
| Ver o resultado de cada projeto no período | Painel → Resultado por projeto `/app/painel/resultado-por-projeto` | [painel-resultado-por-projeto](../../../Ajuda/painel-resultado-por-projeto.md) |
| Fechar a conciliação do mês | Financeiro → Conciliação → **Fechar conciliação** | [financeiro-conciliacao](../../../Ajuda/financeiro-conciliacao.md) |
| Aprovar as ordens de salários e guias da folha | Ordens de pagamento | [rh-folha-detalhe-pagamentos](../../../Ajuda/rh-folha-detalhe-pagamentos.md) |
| Aceitar a competência da conferência paralela da folha (até a virada) | Pessoas → RH → Conferência da folha `/app/rh/conferencia-folha` | [rh-conferencia-folha](../../../Ajuda/rh-conferencia-folha.md) |
| Revisar grupos e quem está em cada um; desativar quem saiu | Sistema → Usuários e acessos `/app/acessos/grupos`, `/app/acessos/usuarios` | [acessos-grupos](../../../Ajuda/acessos-grupos.md) · [acessos-usuarios](../../../Ajuda/acessos-usuarios.md) |
| Atualizar a meta de faturamento e os parâmetros | Sistema → Configurações → Parâmetros `/app/configuracoes/parametros` | [configuracoes-parametros](../../../Ajuda/configuracoes-parametros.md) |

## Entre as telas

- **Do orçamento ao caixa:** ao aprovar o orçamento, o contrato, as parcelas a receber, o projeto e a OS nascem juntos. As parcelas por evento (fabricação, entrega, aceite) ganham vencimento sozinhas quando o evento acontece. Você não precisa avisar o Financeiro.
- **Do pedido de compra ao pagamento:** o pedido aprovado vira previsão no caixa; o recebimento vira título a pagar; o título só sai do caixa por ordem de pagamento aprovada. Você aprova duas vezes: o pedido e a ordem. São decisões diferentes: comprar e pagar.
- **Aprovar pelo celular:** o aviso de ordem de pagamento chega por e-mail com o link; a aprovação pede o código do autenticador.
- **Delegar:** para dividir aprovações, crie o grupo Diretoria / Gerência e as faixas em Financeiro → Alçadas (`/app/financeiro/alcadas`). Use o simulador para conferir quem aprovaria cada valor. Recomendação completa em [`grupos-e-permissoes.md`](../grupos-e-permissoes.md).
- **Ações que só você concede:** aprovar, pagar, ver custo e margem, ver dados sensíveis e de saúde do RH, editar usuários e configurações.

## Os 5 erros mais comuns

1. **Aprovar sem ler os avisos em âmbar da ordem de pagamento.** "Titular não é o fornecedor" e "dados de pagamento mudaram há pouco" são os sinais de golpe mais comuns. Confira com o fornecedor por telefone antes de aprovar.
2. **Deixar a regra "Proprietário aprova tudo" e virar gargalo.** Se a pauta acumula, crie faixas de alçada para a Diretoria. Mudar a regra não afeta o que já foi enviado.
3. **Dar ao mesmo grupo "Aprovar" e "Pagar" em Caixa e pagamentos.** Quebra a segregação. Quem aprova não paga.
4. **Ficar sem um segundo membro no grupo Proprietário.** Se o único Proprietário perde o celular do autenticador, a empresa para. Guarde os códigos de recuperação fora do computador e tenha um segundo sócio no grupo.
5. **Mudar o valor ou o prazo do projeto "por fora".** Valor e data prometida só mudam por aditivo aprovado. Assim o orçado, as parcelas e a margem continuam certos.
