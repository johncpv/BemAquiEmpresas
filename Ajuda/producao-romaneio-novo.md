---
titulo: Novo romaneio
rota: /app/producao/expedicao/novo
aba:
padrao: true
modulo: production
---

## Para que serve
Montar o romaneio do que vai para a obra: as séries e quantidades do projeto, o endereço de entrega e o transporte.

## Como usar
- O formulário vem preenchido: todas as séries e quantidades do projeto que ainda não estão em outro romaneio. Vindo da OS (próximo passo **Novo romaneio** depois de concluir), vêm marcadas as séries daquela OS.
- Desmarque a série que não vai agora ou diminua a quantidade. A série que já está em outro romaneio aparece desabilitada, com o número dele.
- **Endereço de entrega**: vem o da obra do projeto. Edite se a entrega for em outro lugar.
- **Transportadora** (opcional): a última usada com o cliente vem escolhida, com a tag **Sugerido**. Placa, motorista, volumes, peso bruto e data prevista também são opcionais.
- **Incluir produto do estoque geral**: busca produto acabado sem projeto e acrescenta a linha com a tag **Estoque geral**. Esse romaneio só sai depois que outra pessoa com aprovação no Estoque confirmar.
- **Salvar romaneio** cria o rascunho com o número ROM-AAAA-0001. A mensagem é **Romaneio salvo**. Os próximos passos levam a **Confirmar expedição** e **Baixar PDF**.

## Perguntas frequentes
**Posso incluir produto de outro projeto?** Não. Cada romaneio é de um projeto. O produto acabado sem projeto entra só pela opção **Incluir produto do estoque geral**.

**Salvar já tira do estoque?** Não. O rascunho só separa as séries. O estoque baixa na **Confirmar expedição**.
