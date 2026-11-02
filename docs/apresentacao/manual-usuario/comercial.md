# Manual do Comercial

**Grupo:** Comercial (do sistema). **Ações principais:** `quotes.view`, `quotes.create`, `quotes.edit`, `quotes.view_cost`; `projects.view`, `projects.view_cost`; cadastros (`parties.view`, `parties.create`, `parties.edit`).

Você cadastra o cliente, monta o orçamento com custo real, manda o PDF e acompanha o projeto. Quem aprova o orçamento e gera o contrato é o Proprietário (ou a Diretoria, se a empresa delegar).

## Rotina

### Todo dia

| Tarefa | Tela | Ajuda |
|---|---|---|
| Ver os orçamentos que vencem em 7 dias | Início `/app` (Minha pauta) | [inicio](../../../Ajuda/inicio.md) |
| Cadastrar cliente novo | Comercial → Cadastros → Terceiros `/app/cadastros/terceiros/novo` | [cadastros-terceiro-novo](../../../Ajuda/cadastros-terceiro-novo.md) |
| Montar o orçamento: itens e composição de custo | Comercial → Orçamentos `/app/orcamentos` → orçamento → aba **Itens** | [comercial-orcamentos](../../../Ajuda/comercial-orcamentos.md) · [comercial-orcamento-itens](../../../Ajuda/comercial-orcamento-itens.md) |
| Definir os marcos de pagamento e a validade | Aba **Condições** | [comercial-orcamento-condicoes](../../../Ajuda/comercial-orcamento-condicoes.md) |
| Simular o prazo antes de prometer | Orçamento → **Simular prazo** | [comercial-orcamento](../../../Ajuda/comercial-orcamento.md) |
| Gerar o PDF e enviar por e-mail | Aba **Documentos** | [comercial-orcamento-documentos](../../../Ajuda/comercial-orcamento-documentos.md) |

### Toda semana

| Tarefa | Tela | Ajuda |
|---|---|---|
| Revisar o funil e marcar os perdidos com motivo | Orçamentos → Funil `/app/orcamentos/funil` | [comercial-funil](../../../Ajuda/comercial-funil.md) |
| Prorrogar ou revisar orçamento enviado | Orçamento → **Prorrogar validade** ou **Nova revisão** | [comercial-orcamento-revisoes](../../../Ajuda/comercial-orcamento-revisoes.md) |
| Acompanhar os projetos dos seus clientes: etapa, entrega prevista e risco | Comercial → Projetos `/app/projetos` | [projetos](../../../Ajuda/projetos.md) · [projetos-projeto](../../../Ajuda/projetos-projeto.md) |

### Quando o cliente muda o pedido

| Tarefa | Tela | Ajuda |
|---|---|---|
| Registrar acréscimo, redução ou mudança de prazo | Contrato → **Aditivo** `/app/contratos/$id` | [comercial-contrato-aditivos](../../../Ajuda/comercial-contrato-aditivos.md) · [comercial-contrato-aditivo](../../../Ajuda/comercial-contrato-aditivo.md) |

## Entre as telas

- **Um item por coisa vendida.** O equipamento (tipo **Equipamento**) vira OS de fabricação sozinho na aprovação. A instalação entra como item **Serviço**: ela não vira OS de fabricação e é faturada por NFS-e no aceite técnico. Vender os dois juntos num item só atrapalha a nota.
- **Mão de obra por centro.** Na composição, informe as horas por centro de trabalho e o cargo. É com essas horas que a simulação de prazo calcula a data e que a fábrica é planejada.
- **Do "sim" ao contrato:** quando o cliente aceitar, avise quem aprova (o orçamento fica na pauta dele). Depois da aprovação, anexe o contrato assinado na aba Documentos do contrato.
- **O PDF nunca mostra custo nem margem**, para ninguém. Pode mandar sem medo.
- **Prazo prometido:** a data prometida é a assinatura mais o prazo do orçamento, em dias corridos. O sistema compara a previsão da fábrica com ela todos os dias; o chip **Em risco** do projeto é o sinal para falar com o cliente antes do atraso.

## Os 5 erros mais comuns

1. **Prometer prazo sem simular.** Use **Simular prazo** antes de enviar; ele usa a fila real da fábrica. A simulação não considera o prazo de compra do material: confira também **Ver capacidade de produzir**.
2. **Marcos que não somam 100%.** O orçamento salva, mas não aprova. Use **Ajustar o último** antes de mandar.
3. **Editar o orçamento enviado em vez de criar revisão.** Enviado fica só para consulta. Para mudar, **Nova revisão**; para mudar só a data, **Prorrogar validade**.
4. **Digitar o preço à mão e esquecer.** A marca **Digitado à mão** fica no item; se o custo mudar, o preço não acompanha. Use **Voltar ao sugerido** quando não houver motivo comercial.
5. **Cliente sem endereço de obra com código IBGE.** A NFS-e da instalação usa o município da obra. Cadastre o endereço de obra completo no cliente.
