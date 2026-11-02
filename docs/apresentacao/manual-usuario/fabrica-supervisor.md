# Manual do Supervisor de fábrica (e da Engenharia)

**Grupo:** Supervisão de produção (do sistema). **Ações principais:** `production.view`, `production.create`, `production.edit`, `production.approve` (concluir OS e etapa, aceite técnico), `production.approve_quality`; `inventory.view`, `inventory.create`; `kiosk.view`, `kiosk.create`, `kiosk.delete`; `shopfloor.view`, `shopfloor.create`; `assets.view`, `assets.create`, `assets.edit`. **Precisa de:** 2FA (você aprova na produção). Entra sempre com e-mail e senha, também no tablet.

No fim deste manual há uma seção para quem é da **Engenharia**.

## Rotina

### Todo dia, ao chegar

| Tarefa | Tela | Ajuda |
|---|---|---|
| Começar pelo painel: qualidade a decidir, faltas que vão parar a fábrica, atrasadas e em risco, ocupação | Fábrica → Produção → Painel do supervisor `/app/producao/supervisor` | [producao-supervisor](../../../Ajuda/producao-supervisor.md) |
| Decidir a qualidade (aprovar ou reprovar, com a não conformidade) | Produção → Qualidade `/app/producao/qualidade` ou, no tablet, `/fab/qualidade` | [producao-qualidade](../../../Ajuda/producao-qualidade.md) · [producao-qualidade-etapa](../../../Ajuda/producao-qualidade-etapa.md) |
| Acompanhar o quadro e destravar | Produção → Kanban `/app/producao/kanban` | [producao-kanban](../../../Ajuda/producao-kanban.md) |
| Corrigir apontamento esquecido aberto | Produção → Apontamentos abertos `/app/producao/apontamentos-abertos` | [producao-apontamentos-abertos](../../../Ajuda/producao-apontamentos-abertos.md) |
| Concluir as OS prontas | Produção → OS `/app/producao/os` (abre em **Aguardando você**) | [producao-os-lista](../../../Ajuda/producao-os-lista.md) · [producao-os](../../../Ajuda/producao-os.md) |

### Ao longo da semana

| Tarefa | Tela | Ajuda |
|---|---|---|
| Ver a fila de prioridade, o cronograma e a ocupação por centro | Produção → Planejamento `/app/producao/planejamento` | [producao-planejamento](../../../Ajuda/producao-planejamento.md) · [producao-planejamento-ocupacao](../../../Ajuda/producao-planejamento-ocupacao.md) |
| Tratar as não conformidades abertas | Produção → Não conformidades `/app/producao/nao-conformidades` | [producao-nao-conformidades](../../../Ajuda/producao-nao-conformidades.md) |
| Montar o romaneio do que está pronto | Produção → Expedição `/app/producao/expedicao` | [producao-expedicao](../../../Ajuda/producao-expedicao.md) · [producao-romaneio-novo](../../../Ajuda/producao-romaneio-novo.md) |
| Acompanhar a instalação na obra e registrar o aceite técnico | Produção → Instalações `/app/producao/instalacoes` | [producao-instalacoes](../../../Ajuda/producao-instalacoes.md) · [producao-instalacao](../../../Ajuda/producao-instalacao.md) · [producao-instalacao-checklist](../../../Ajuda/producao-instalacao-checklist.md) |
| Abrir manutenção corretiva e acompanhar as preventivas | Fábrica → Patrimônio → Manutenção `/app/patrimonio/manutencao` | [patrimonio-manutencao](../../../Ajuda/patrimonio-manutencao.md) · [patrimonio-manutencao-os](../../../Ajuda/patrimonio-manutencao-os.md) |

### Quando precisar

| Tarefa | Tela | Ajuda |
|---|---|---|
| Ajustar turnos e feriados | Produção → Centros de trabalho, Calendário | [producao-centros-de-trabalho](../../../Ajuda/producao-centros-de-trabalho.md) · [producao-calendario](../../../Ajuda/producao-calendario.md) |
| Criar, trocar ou revogar uma TV | Telas dedicadas → Painel da TV `/app/painel-tv` | [painel-tv](../../../Ajuda/painel-tv.md) |
| Registrar tablet novo ou revogar tablet perdido | No tablet, `/fab` → **Aparelho**; revogar em Configurações → Segurança | [configuracoes-seguranca](../../../Ajuda/configuracoes-seguranca.md) |
| Lançar o custo da manutenção concluída | OS de manutenção → aba Custo | [patrimonio-manutencao-os-custo](../../../Ajuda/patrimonio-manutencao-os-custo.md) |

## Entre as telas

- **A etapa só anda pelo apontamento.** O Kanban não arrasta. Quando a etapa terminou sem apontamento, use **Concluir etapa** no cartão, com o motivo.
- **Qualidade não é conclusão.** Aprovar a qualidade deixa a OS **Pronta para concluir**. A conclusão é outra decisão, sua, em **Concluir OS**. Ao concluir a última OS do projeto, ele passa a **Fabricado** e a parcela da fabricação é liberada no financeiro.
- **Você não aprova a qualidade do que apontou.** Se apontou uma etapa da tentativa, outra pessoa decide. Se não houver ninguém, a decisão leva a marca **Autoinspeção** e o Proprietário é avisado. Para não cair nisso, não aponte em seu nome nas OS que você vai inspecionar; use o apontamento por funcionário.
- **Faltas de material:** o bloco do painel diz quando a falta vai parar a OS (**Bloqueia em n dias**) e em que pé está a compra. Sem solicitação, peça a quem compra.
- **Máquina parada tira capacidade.** A OS de manutenção com **Máquina parada** reduz as horas do centro, e o planejamento recalcula as entregas.
- **Aceite técnico:** só com todas as etapas e o checklist da obra completos. Ele confirma o título do instalador, libera a parcela do cliente e prepara a NFS-e.

## Os 5 erros mais comuns

1. **Deixar apontamento aberto de um dia para o outro.** Horas erradas viram custo e prazo errados. Confira **Apontamentos abertos** todo fim de turno.
2. **Usar "Concluir etapa" como atalho.** Etapa concluída sem apontamento não tem horas, e o custo do projeto fica menor do que foi. Use só quando a etapa terminou de verdade sem registro.
3. **Reprovar a qualidade sem tratar a NC.** A OS volta para a etapa de retorno, e a NC fica aberta no painel. Defina o responsável e encerre quando resolver.
4. **Registrar o aceite técnico antes de conferir o checklist na obra.** O aceite libera cobrança e pagamento do instalador. Marque o checklist na obra, com quem aceitou pelo cliente.
5. **Esquecer de revogar a TV ou o tablet que saiu da fábrica.** Revogue em Painel da TV ou em Configurações → Segurança na hora.

---

## Se você é da Engenharia

**Grupo:** Engenharia (do sistema). **Ações:** `engineering.view`, `engineering.create`, `engineering.edit`, `engineering.delete`, `engineering.approve` (liberar a BOM); `production.view`, `production.create`, `production.edit`; `projects.view`, `projects.edit`. **Precisa de:** 2FA (você libera a BOM).

| Tarefa | Tela | Ajuda |
|---|---|---|
| Cadastrar e manter os roteiros (etapas, letra, centro, horas, checklist) | Fábrica → Engenharia → Roteiros `/app/engenharia/roteiros` | [engenharia-roteiros](../../../Ajuda/engenharia-roteiros.md) · [engenharia-roteiro-novo](../../../Ajuda/engenharia-roteiro-novo.md) · [engenharia-roteiro-produtos](../../../Ajuda/engenharia-roteiro-produtos.md) |
| Todo dia: ver as OS que vão parar sem BOM | Engenharia → BOMs `/app/engenharia/boms` | [engenharia-boms](../../../Ajuda/engenharia-boms.md) |
| Montar e liberar a BOM de cada OS (copiar de outra OS do mesmo produto) | OS → aba **BOM** `/app/producao/os/$id` | [producao-os-bom](../../../Ajuda/producao-os-bom.md) |
| Anexar desenhos para o operador (PDF, PNG, JPG; DWG e DXF para baixar) | OS → aba **Documentos** | [producao-os-documentos](../../../Ajuda/producao-os-documentos.md) |
| Criar a OS de instalação a partir do projeto | Projeto `/app/projetos/$id` | [producao-instalacoes](../../../Ajuda/producao-instalacoes.md) |

Lembre: a BOM é **da OS**, não do produto. A primeira etapa (Projeto) começa sem BOM; da segunda em diante, a fábrica para até a BOM ser liberada. Liberar reserva o material e cria a solicitação de compra do que falta.
