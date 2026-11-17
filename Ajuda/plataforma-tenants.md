---
titulo: Tenants
rota: /plataforma/tenants
aba:
padrao: true
modulo: plataforma
---

## Para que serve
Ver as empresas que usam o BemAqui Empresas e provisionar uma nova.

## Como usar
- Busque por nome, CNPJ ou endereço. Clique no nome para abrir o detalhe.
- **Provisionar tenant** é feito em passos:
  1. **Empresa:** a chave, a razão social, o CNPJ, o endereço (o DNS já precisa apontar para o servidor) e o e-mail do proprietário.
  2. **Perfil de negócio:** Fábrica, Comércio, Prestação de serviços ou E-commerce. Cada card diz o que libera. Marque pelo menos um; marque mais de um se a empresa tiver mais de um negócio. Fábrica já vem marcado.
  3. **Segmento:** aparece só para Comércio e Prestação de serviços. No Comércio, "Balcão" já vem escolhido.
  4. **Plano:** se o plano não cobre algum módulo dos perfis escolhidos, a tela avisa. Esses módulos aparecem com cadeado até o plano mudar.
- O provisionamento roda em segundo plano, leva menos de um minuto, e o detalhe mostra cada etapa, inclusive o perfil aplicado.

## Perguntas frequentes
**A chave pode mudar depois?** Não. Ela identifica o tenant e o banco dele.

**E se eu escolher o perfil errado?** Troque na aba **Perfis** do detalhe: adicione o certo e remova o errado. Nenhum dado é apagado.
