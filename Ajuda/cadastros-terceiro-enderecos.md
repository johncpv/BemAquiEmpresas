---
titulo: Terceiro — Endereços
rota: /app/cadastros/terceiros/$id
aba: enderecos
padrao: false
modulo: parties
---

## Para que serve
Guarda os endereços de cobrança, de entrega e de obra do terceiro. Cada tipo tem um endereço padrão, usado automaticamente nos documentos.

## Como usar
- Clique em **Adicionar endereço**, escolha o tipo e preencha. O primeiro endereço de cada tipo vira o padrão.
- Use **Tornar padrão** para trocar o padrão de um tipo.
- **Editar** e **Remover** ficam em cada endereço. Se você remover o padrão, outro do mesmo tipo assume.

## Consulta de CEP
- Ao completar o CEP, logradouro, bairro, cidade, UF e código IBGE são preenchidos nos campos vazios. Confira o número e o complemento.
- CEP não encontrado ou consulta fora do ar: preencha o endereço à mão.

## Perguntas frequentes
**Para que serve o endereço de obra?** É onde o elevador será instalado. Ele aparece no projeto e na ordem de serviço de instalação.

**Preciso do código IBGE?** Não precisa digitar. Ele é usado na nota fiscal e o sistema preenche sozinho pela cidade e pela UF: ao completar o CEP, ao salvar o endereço e, para os endereços antigos, numa conferência automática de madrugada. Se a cidade estiver escrita errado, o código fica vazio; corrija a cidade e salve de novo.
