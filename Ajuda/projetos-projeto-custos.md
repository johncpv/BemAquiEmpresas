---
titulo: Projeto — custos
rota: /app/projetos/$id
aba: custos
padrao: false
modulo: projects
---

## Para que serve
Ver quanto o orçamento aprovado previu gastar em cada natureza de custo. É a base do orçado contra o realizado.

## Como usar
- Cada natureza (material, mão de obra, instalação, frete, impostos e outros) mostra **Orçado**, **Realizado**, **Projetado** e **Desvio**.
- **Realizado:** os títulos a pagar lançados com o projeto e a natureza. Material e horas entram com a produção (Fase 3).
- **Projetado:** o realizado mais o que ainda falta do orçado. Se o realizado passou do orçado, é o próprio realizado.
- **Desvio:** projetado menos orçado. Positivo quer dizer que o custo vai passar do orçado.
- A margem projetada do Resumo usa estes valores.

## Perguntas frequentes
**Por que não vejo esta aba?** O orçado é sigilo comercial. Só quem tem a permissão **Ver custo e orçado do projeto** vê.
