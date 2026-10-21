# PAT-01b — Patrimônio: etiquetas com QR code em lote e leitura pelo app

| Campo | Valor |
|---|---|
| ID | PAT-01b |
| Título | Etiqueta do bem com QR code (só a URL com a etiqueta, sem dado), impressão em lote em PDF no tamanho da impressora de etiquetas da Konebras, reimpressão; leitura pela câmera no app (`/fab`) ou por qualquer leitor do celular, que abre a ficha resumida do bem depois do login, com os atalhos "Abrir OS de manutenção" e "Registrar movimentação" |
| Tamanho | M (quebra da PAT-01) |
| Status | Draft |
| Requisitos | R-PAT-2 (etiqueta com QR code, impressão em lote; leitura pelo PWA abre a ficha) · R-PRP-6 (D, impressão em lote) · R-COM-3 · R-SEC-5 |
| Dependências | PAT-01a (bens, etiqueta), COM-03 (`IPdfRenderer`, ADR 0022), PRD-03a (layout `/fab`); P-13 (impressora de etiquetas) |
| Bloqueia | PAT-02 e PAT-03a (atalhos pela leitura) |
| Ordem de execução | 11 (Marco 6.4 "Patrimônio") |

## User story
Como **supervisor**, quero **imprimir as etiquetas de todos os bens de uma vez e, na fábrica, apontar o celular para a etiqueta e ver a ficha da máquina**, para **identificar cada bem sem procurar em planilha e abrir a manutenção na hora**.

## Contexto e referências
- PRD R-PAT-2; `02-arquitetura` §4 ("leitura de QR code (câmera)" no PWA `/fab`).
- `06` **P-13 (decidida):** "a Konebras providencia; o modelo define o tamanho da etiqueta na PAT-01" → o tamanho é parâmetro, com o modelo da impressora como pré-condição externa.
- ADR 0022 (PDF, Archivo embutida, sem URL externa); ADR 0004 e catálogo (`tenant_domains`: host primário do tenant para a URL).
- PRD-03a ponto em aberto 4 (QR code da OS) usa a mesma leitura (proposta de componente comum).

## Critérios de aceite
**Etiqueta**
1. **Dado** `assets.view`, **quando** clico "Imprimir etiqueta" num bem, **então** recebo um PDF com a etiqueta no tamanho do parâmetro `assets.label_size` (proposta: largura × altura em mm, seed 50 × 25; editável em Configurações → Parâmetros com `settings.edit`), contendo o QR code, a etiqueta em texto grande (`PAT-00001`), o nome do bem (até 2 linhas) e o nome fantasia do tenant; nenhum valor, responsável nem série.
2. **[Segurança]** **Dado** o QR code, **quando** gerado, **então** contém só a URL `https://{host primário do tenant}/b/PAT-00001` (proposta), sem id interno, sem token e sem dado do bem; a URL sem sessão leva ao login e depois à ficha; um teste decodifica o QR e confere o conteúdo.
3. **Dado** a lista de bens com filtros, **quando** seleciono vários (ou "todos do filtro", até 500) e clico "Imprimir etiquetas", **então** recebo um PDF com uma etiqueta por página no tamanho do parâmetro (impressora de etiquetas) ou, com a opção "Folha A4", uma grade de etiquetas por página (proposta: colunas e linhas calculadas pelo tamanho), gerado em segundo plano acima de 50 etiquetas, com o aviso no sino quando pronto.
4. **Dado** uma etiqueta danificada, **quando** clico "Reimprimir etiqueta", **então** o PDF é o mesmo da etiqueta original (a etiqueta nunca muda) e a reimpressão fica no histórico do bem.

**Leitura**
5. **Dado** um usuário com `assets.view` logado no celular ou no tablet, **quando** abre a URL do QR (pelo leitor do celular ou pelo botão "Ler etiqueta" do app, que usa a câmera), **então** vê a ficha resumida do bem: foto, etiqueta, nome, tipo, local, responsável, centro de trabalho, situação, a próxima manutenção prevista (PAT-03a) e a última manutenção feita; alvos de 56 px no `/fab`.
6. **Dado** a ficha aberta pela leitura, **quando** o usuário tem a permissão, **então** vê os atalhos "Abrir OS de manutenção corretiva" (PAT-03a, com o bem já preenchido) e "Registrar movimentação" (PAT-02), e "Ver ficha completa" no backoffice.
7. **[Segurança]** **Dado** uma etiqueta inexistente, excluída ou de outro tenant, **quando** a URL é aberta, **então** a tela mostra "Etiqueta não encontrada." com a mesma resposta e tempo para os três casos (404), sem revelar se o bem existe em outro lugar; sem `assets.view`, 403 com a tela padrão de acesso.
8. **Dado** a câmera negada ou indisponível no aparelho, **quando** toco "Ler etiqueta", **então** vejo "Não foi possível usar a câmera. Digite a etiqueta." com um campo para digitar `PAT-` e o número (propositivo).
9. **Dado** a leitura, **quando** a rota `/b/{tag}` existe no roteador, **então** ela tem ajuda (teste de cobertura) e não é rota anônima: a API `GET /api/v1/assets/assets/by-tag/{tag}` exige login, `RequireModule("assets")` e `perm:assets.view`.

**Permissões e auditoria**
10. **Dado** o módulo `assets` não liberado, **quando** chamo, **então** 403 `module_not_released`; imprimir exige `assets.view` (proposta) e a reimpressão `assets.edit`; `RequireModule` e `perm:`.
11. **Dado** impressão, reimpressão e leitura, **quando** consulto o histórico do bem, **então** vejo impressão e reimpressão com quem e quando (a leitura não é registrada, proposta).

## Regras de negócio e técnicas
- **Geração do QR** no servidor, dentro do layout do PDF (biblioteca de QR com licença permissiva, conferida no `Directory.Packages.props` pela varredura da FND-01; Pontos em aberto 3).
- **Leitura no app:** API do navegador (`BarcodeDetector`) quando existir; senão, uma biblioteca de leitura com licença permissiva (Pontos em aberto 3). A URL do QR funciona também com o leitor nativo do celular, sem instalar nada.
- **Rota curta** `/b/$tag` no front que resolve a etiqueta e redireciona para a ficha resumida (`/fab/bens/$id` no `/fab`, `/app/patrimonio/bens/$id` no backoffice).
- **Lote:** `TenantJob` com ids; PDF guardado como arquivo do sistema por 7 dias (proposta).

## Permissões, eventos e tabelas
- **Permissões:** `assets.view` (imprimir e ler), `assets.edit` (reimprimir), `settings.edit` (tamanho da etiqueta).
- **Eventos:** nenhum.
- **Tabelas:** `assets.parameters` (`assets.label_size`) ou a chave no módulo dono (ADR 0016 M6); dono de anexo `AssetLabelBatch` para o PDF do lote.

## Telas e estados
- Na ficha e na lista (PAT-01a): "Imprimir etiqueta", "Imprimir etiquetas" (seleção), "Reimprimir etiqueta".
- **`/b/$tag`** e **`/fab/bens/$id`** (ficha resumida no layout do app), **"Ler etiqueta"** no `/fab`.
- **Estados:** gerando o lote; câmera negada (CA 8); etiqueta não encontrada (CA 7); sem permissão.
- **400 px:** a ficha resumida é desenhada para o celular.
- **Ajuda:** `Ajuda/patrimonio-etiquetas.md`, `Ajuda/fab-bem.md` (rotas `/b/$tag` e `/fab/bens/$id`); `<Dica>` em "Tamanho da etiqueta".

## Plano de testes
- **Unidade:** conteúdo do QR (CA 2); grade A4 pelo tamanho (CA 3).
- **Integração (Testcontainers):** PDF da etiqueta sem dado proibido, pelo texto extraído e pela decodificação do QR (CA 1, 2); lote em segundo plano (CA 3); reimpressão igual (CA 4); `by-tag` com login, 404 uniforme e 403 (CA 7, 9); três camadas (CA 10); histórico (CA 11).
- **E2E (Playwright):** abrir `/b/PAT-00001` sem sessão, entrar e ver a ficha; digitar a etiqueta sem câmera.
- **Manual:** imprimir na impressora da Konebras (P-13) e ler com um celular Android de entrada.

## Fora do escopo
- Inventário físico por leitura em sequência: fora do MVP.
- Etiqueta RFID ou NFC.

## Pontos em aberto
1. **Modelo da impressora e tamanho da etiqueta** (pré-condição externa, Konebras, P-13): define o seed de `assets.label_size` e se a impressora aceita PDF.
2. Formato da URL curta (`/b/{etiqueta}`, proposta) e se a leitura fica registrada.
3. **Bibliotecas de QR** (arquiteto): geração no servidor e leitura no navegador, com licença permissiva e sem chamada externa.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), E2E e o teste manual com a impressora passando.
- [ ] Pacotes novos aprovados na varredura de vulnerabilidades e de licença.
- [ ] Telas com estados; ajuda das rotas novas; teste de cobertura passando.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `AssetLabelPdfDocument` (unitária e grade); job de lote.
- [ ] Endpoint `by-tag`; rota `/b/$tag`; componente de leitura por câmera reaproveitável (PRD-03a ponto 4).

## Quality gates
- Agentes previstos: @dev, cybersecurity (conteúdo do QR, 404 uniforme), design (ficha no celular), @qa.
- Gate: CA 2, 5, 7 e 9 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** o QR leva só a URL com a etiqueta e a leitura exige login (CA 2, 9), sem criar rota anônima; 404 uniforme (CA 7); tamanho da etiqueta como parâmetro, com a P-13 como pré-condição externa.
- **Pré-condições para `Ready`:** ponto em aberto 1 (externo) e 3; PAT-01a concluída.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da PAT-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
