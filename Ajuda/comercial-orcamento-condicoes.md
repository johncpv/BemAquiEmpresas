---
titulo: Orçamento — condições
rota: /app/orcamentos/$id
aba: condicoes
padrao: false
modulo: quotes
---

## Para que serve
Definir como o cliente vai pagar, em marcos, e até quando o preço vale.

## Como usar
- Adicione um marco por parcela: descrição, percentual e gatilho. O valor aparece ao lado, calculado sobre o preço total.
- No gatilho **Data**, informe o dia. No gatilho **Evento**, escolha o evento (assinatura, fabricação concluída, entrega ou aceite técnico) e o prazo em dias depois dele.
- A soma aparece no fim. Use **Dividir igualmente** ou **Ajustar o último** para fechar 100%.
- **Preencher pela condição de pagamento** troca os marcos pelas parcelas da condição escolhida (a do cliente vem sugerida).
- Ajuste a **validade** e clique em **Salvar condições**.

## Orçamento de serviço
- Orçamento só com itens de serviço vira projeto de serviço na aprovação (quando a empresa tem o perfil Serviços).
- Nele, o gatilho **Fabricação concluída** não é oferecido e o **aceite técnico** aparece como **Aceite do cliente**. O PDF do orçamento usa os mesmos nomes.
- Se um marco ainda estiver com fabricação concluída, a aprovação recusa e **Abrir as condições** traz você direto para cá, com o marco em destaque. Troque o gatilho para entrega, aceite do cliente ou data.

## Perguntas frequentes
**Posso salvar sem fechar 100%?** Pode, como rascunho. Para aprovar o orçamento, a soma precisa ser 100%.

**Se o preço mudar, os marcos mudam?** Os valores sim, os percentuais não.

**O orçamento venceu. E agora?** Ajuste a validade antes de enviar.
