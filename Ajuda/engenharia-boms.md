---
titulo: BOMs das OS
rota: /app/engenharia/boms
aba:
padrao: true
modulo: engineering
---

## Para que serve
Ver, num lugar só, a situação da BOM de cada OS de fabricação em aberto e destravar as OS que vão parar por falta de BOM.

## Como usar
- A lista abre em **OS que vão parar sem BOM**: as OS que já passaram da primeira etapa (ou estão nela já concluída) e ainda não têm a BOM liberada. A próxima etapa delas não começa até a BOM ser liberada.
- Troque o filtro **Situação da BOM** para ver as OS sem BOM, com rascunho ou com a BOM liberada. Escolha **Todos** para ver todas as OS em aberto.
- O número da OS leva à aba **BOM** da OS, onde a lista de materiais é montada e liberada.
- **Criar BOM** abre o rascunho vazio da OS. **Liberar BOM sem material** libera a BOM vazia de uma OS que usa só mão de obra, e as etapas seguintes podem começar.

## Perguntas frequentes
**Por que a OS aparece como "Vai parar sem BOM"?** Porque a etapa atual é depois da primeira e a OS não tem BOM liberada. A primeira etapa começa sem BOM; as seguintes, não.

**Liberar sem material reserva alguma coisa?** Não. A BOM vazia é liberada sem reservas. Se depois precisar de material, revise a BOM na aba da OS.
