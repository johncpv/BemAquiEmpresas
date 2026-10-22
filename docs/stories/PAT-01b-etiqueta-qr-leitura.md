# PAT-01b — Patrimônio: etiquetas com QR code em lote e leitura pelo app

| Campo | Valor |
|---|---|
| ID | PAT-01b |
| Título | Etiqueta do bem com QR code gerado no servidor (biblioteca MIT; só a URL curta `/b/{etiqueta}`, sem dado), impressão em lote em PDF no tamanho configurável da impressora de etiquetas, reimpressão; leitura pela câmera no app (`BarcodeDetector`, "Ler etiqueta" no `/fab`) ou por qualquer leitor do celular, que abre a ficha resumida do bem depois do login, com os atalhos "Abrir OS de manutenção" e "Registrar movimentação" |
| Tamanho | M (quebra da PAT-01) |
| Status | Ready (pré-condição externa) |
| Requisitos | R-PAT-2 (etiqueta com QR code, impressão em lote; leitura pelo PWA abre a ficha) · R-PRP-6 (D, impressão em lote) · R-COM-3 · R-SEC-5 |
| Dependências | PAT-01a (bens, etiqueta, foto sem EXIF), COM-03 (`IPdfRenderer`, ADR 0022), PRD-03a (layout `/fab`) |
| Bloqueia | PAT-02 e PAT-03a (atalhos pela leitura) |
| Ordem de execução | 12 (Marco 6.4 "Patrimônio") |

## User story
Como **supervisor**, quero **imprimir as etiquetas de todos os bens de uma vez e, na fábrica, apontar o celular para a etiqueta e ver a ficha da máquina**, para **identificar cada bem sem procurar em planilha e abrir a manutenção na hora**.

## Contexto e referências
- PRD R-PAT-2; `02-arquitetura` §4 ("leitura de QR code (câmera)" no PWA `/fab`).
- `06` **P-13 (decidida):** "a Konebras providencia; o modelo define o tamanho da etiqueta na PAT-01" → o tamanho é parâmetro configurável, com o modelo da impressora como pré-condição externa.
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): tabela 3.3 pontos 45 a 47, §4.3 (PAT-01b: `returnUrl` só relativo; `assets.view` conferido antes de buscar a etiqueta; foto do bem sem EXIF/GPS, entregue na PAT-01a CA 8; jobs com `tenantId`).
- **ADR 0038** (`docs/adr/0038-patrimonio-manutencao-capacidade.md`); ADR 0022 (PDF, Archivo embutida, sem URL externa); ADR 0004 e catálogo (`tenant_domains`: host primário do tenant para a URL); ADR 0026 (lista `[PinSessionAllowed]`).
- PRD-03a (QR code da OS) usa a mesma leitura (componente comum).

## Critérios de aceite
**Etiqueta**
1. **Dado** `assets.view`, **quando** clico "Imprimir etiqueta" num bem, **então** recebo um PDF com a etiqueta no tamanho do parâmetro `assets.label_size` (largura × altura em mm, seed 50 × 25, editável em Configurações → Parâmetros → Patrimônio com `settings.edit`, auditado), contendo o QR code, a etiqueta em texto grande (`PAT-00001`), o nome do bem (até 2 linhas) e o nome fantasia do tenant; nenhum valor, responsável nem série.
2. **[Segurança]** **Dado** o QR code, **quando** gerado no servidor (biblioteca de licença MIT, conferida na varredura de licença da FND-01, sem chamada externa), **então** contém só a URL `https://{host primário do tenant}/b/PAT-00001`, sem id interno, sem token e sem dado do bem; a URL sem sessão leva ao login e depois à ficha; um teste decodifica o QR e confere o conteúdo.
3. **Dado** a lista de bens com filtros, **quando** seleciono vários (ou "todos do filtro", até 500) e clico "Imprimir etiquetas", **então** recebo um PDF com uma etiqueta por página no tamanho do parâmetro (impressora de etiquetas) ou, com a opção "Folha A4", uma grade de etiquetas por página (colunas e linhas calculadas pelo tamanho), gerado em segundo plano acima de 50 etiquetas (`TenantJob` com `tenantId` e só ids), com o aviso no sino quando pronto.
4. **Dado** uma etiqueta danificada, **quando** clico "Reimprimir etiqueta", **então** o PDF é o mesmo da etiqueta original (a etiqueta nunca muda) e a reimpressão fica no histórico do bem.

**Leitura**
5. **Dado** um usuário com `assets.view` logado no celular ou no tablet, **quando** abre a URL do QR (pelo leitor do celular ou pelo botão "Ler etiqueta" do `/fab`, que usa a câmera pela API `BarcodeDetector` do navegador), **então** vê a ficha resumida do bem: foto (a regravada sem EXIF/GPS, PAT-01a CA 8), etiqueta, nome, tipo, local, responsável, centro de trabalho, situação, a próxima manutenção prevista (PAT-03a) e a última manutenção feita; alvos de 56 px no `/fab`.
6. **Dado** a ficha aberta pela leitura, **quando** o usuário tem a permissão, **então** vê os atalhos "Abrir OS de manutenção corretiva" (PAT-03a, com o bem já preenchido) e "Registrar movimentação" (PAT-02), e "Ver ficha completa" no backoffice.
7. **[Segurança]** **Dado** a API `GET /api/v1/assets/assets/by-tag/{tag}`, **quando** chamada, **então** confere `assets.view` **antes** de buscar a etiqueta (sem a permissão, 403 com a tela padrão de acesso, sem revelar se a etiqueta existe); com a permissão, etiqueta inexistente, excluída ou de outro tenant responde "Etiqueta não encontrada." com a mesma resposta e o mesmo tempo para os três casos (404).
8. **[Segurança]** **Dado** a rota `/b/{tag}` sem sessão, **quando** o login termina, **então** o retorno usa só `returnUrl` relativo do próprio app (`/b/...`, `/fab/...` ou `/app/...`); URL absoluta, com outro host, `//` ou esquema é recusada e o usuário cai no Início (teste).
9. **Dado** a câmera negada, indisponível ou o navegador sem `BarcodeDetector`, **quando** toco "Ler etiqueta", **então** vejo "Não foi possível usar a câmera. Digite a etiqueta." com um campo para digitar `PAT-` e o número (propositivo), sem biblioteca de leitura nova.
10. **Dado** a rota `/b/$tag`, **quando** existe no roteador, **então** ela tem ajuda (teste de cobertura) e não é rota anônima: a API exige login, `RequireModule("assets")` e `perm:assets.view`; na sessão por PIN do tablet, "Ler etiqueta" não aparece (o endpoint fica fora do `[PinSessionAllowed]`, 403 `pin_session_scope`).

**Permissões e auditoria**
11. **Dado** o módulo `assets` não liberado, **quando** chamo, **então** 403 `module_not_released`; imprimir exige `assets.view` e a reimpressão `assets.edit`; o tamanho da etiqueta, `settings.edit`; `RequireModule` e `perm:`.
12. **Dado** impressão e reimpressão, **quando** consulto o histórico do bem, **então** vejo quem e quando; a leitura não é registrada.

## Regras de negócio e técnicas
- **Geração do QR** no servidor, dentro do layout do PDF, com biblioteca de licença MIT (`Directory.Packages.props`, varredura de vulnerabilidade e de licença da FND-01).
- **Leitura no app:** `BarcodeDetector` do navegador; sem ele, digitação da etiqueta (nenhuma biblioteca de leitura no front). A URL do QR funciona também com o leitor nativo do celular, sem instalar nada.
- **Rota curta** `/b/$tag` no front que resolve a etiqueta e redireciona para a ficha resumida (`/fab/bens/$id` no `/fab`, `/app/patrimonio/bens/$id` no backoffice); `returnUrl` só relativo.
- **Lote:** `TenantJob` com ids e `tenantId`; PDF guardado como arquivo do sistema por 7 dias.
- **Parâmetro:** `assets.label_size` em `assets.parameters` (dono Assets, ADR 0016 M6), seção Patrimônio de Configurações → Parâmetros.

## Permissões, eventos e tabelas
- **Permissões:** `assets.view` (imprimir e ler), `assets.edit` (reimprimir), `settings.edit` (tamanho da etiqueta).
- **Eventos:** nenhum.
- **Tabelas:** `assets.parameters` (`assets.label_size`); dono de anexo `AssetLabelBatch` para o PDF do lote.

## Telas e estados
- Na ficha e na lista (PAT-01a): "Imprimir etiqueta", "Imprimir etiquetas" (seleção), "Reimprimir etiqueta".
- **`/b/$tag`** e **`/fab/bens/$id`** (ficha resumida no layout do app), **"Ler etiqueta"** no `/fab`.
- **Configurações → Parâmetros → Patrimônio:** tamanho da etiqueta, com a prévia de uma etiqueta.
- **Estados:** gerando o lote; câmera negada ou sem `BarcodeDetector` (CA 9); etiqueta não encontrada (CA 7); sem permissão; erro.
- **400 px:** a ficha resumida é desenhada para o celular; botões de `ErrorState`/`EmptyState` com 44 px.
- **Ajuda:** `Ajuda/patrimonio-etiquetas.md`, `Ajuda/fab-bem.md` (rotas `/b/$tag` e `/fab/bens/$id`) e a seção Patrimônio de `Ajuda/configuracoes-parametros.md`; `<Dica>` em "Tamanho da etiqueta".

## Plano de testes
- **Unidade:** conteúdo do QR (CA 2); grade A4 pelo tamanho (CA 3); validação do `returnUrl` (CA 8).
- **Integração (Testcontainers):** PDF da etiqueta sem dado proibido, pelo texto extraído e pela decodificação do QR (CA 1, 2); lote em segundo plano com `tenantId` (CA 3); reimpressão igual (CA 4); `by-tag` com `assets.view` antes da busca, 404 uniforme e 403 (CA 7); PIN fora (CA 10); três camadas (CA 11); histórico (CA 12).
- **E2E (Playwright):** abrir `/b/PAT-00001` sem sessão, entrar e ver a ficha; `returnUrl` absoluto recusado; digitar a etiqueta sem câmera.
- **Manual:** imprimir na impressora da Konebras (P-13) e ler com um celular Android de entrada.

## Fora do escopo
- Inventário físico por leitura em sequência: fora do MVP.
- Etiqueta RFID ou NFC.
- Biblioteca de leitura de QR no navegador: não entra (CA 9).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.3, pontos 45–47):
1. **Impressora e etiqueta (P-13):** pré-condição externa; o tamanho fica configurável (`assets.label_size`, seed 50 × 25) (CA 1).
2. **URL curta `/b/{etiqueta}`,** sem dado nem id interno, leitura com login e não registrada (CA 2, 10, 12).
3. **QR no servidor com biblioteca MIT;** leitura pelo `BarcodeDetector`; "Ler etiqueta" no `/fab` (CA 2, 5, 9).

Decisões da revisão aplicadas: §4.3 (`returnUrl` só relativo, CA 8; `assets.view` antes da busca, CA 7; foto sem EXIF/GPS pela PAT-01a, CA 5; job com `tenantId`, CA 3).

**Pré-condição externa:** a Konebras informa o modelo da impressora de etiquetas e o tamanho da etiqueta (P-13); até lá vale o seed 50 × 25 mm, editável; o teste manual de impressão espera a impressora; não bloqueia o `Ready`.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando; teste manual com a impressora quando ela chegar.
- [ ] Pacote novo (QR, MIT) aprovado na varredura de vulnerabilidades e de licença.
- [ ] Telas com estados; ajuda das rotas novas; teste de cobertura passando.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `AssetLabelPdfDocument` (unitária e grade); job de lote.
- [ ] Endpoint `by-tag`; rota `/b/$tag` com `returnUrl` relativo; componente de leitura por câmera com `BarcodeDetector`, reaproveitável (PRD-03a).
- [ ] Parâmetro `assets.label_size` e a seção Patrimônio em Configurações → Parâmetros.

## Quality gates
- Agentes previstos: @dev, cybersecurity (conteúdo do QR, 404 uniforme, `returnUrl`), design (ficha no celular), @qa.
- Gate: CA 2, 5, 7, 8 e 10 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: CA 7, 8, 10) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** o QR leva só a URL com a etiqueta e a leitura exige login, sem criar rota anônima; 404 uniforme; tamanho da etiqueta como parâmetro, com a P-13 como pré-condição externa.
- **Correções da revalidação (21/10/2026):** pontos 45 a 47 decididos: tamanho configurável em Configurações → Parâmetros → Patrimônio (CA 1), `/b/{etiqueta}` com leitura não registrada (CA 12), QR no servidor com biblioteca MIT (CA 2), `BarcodeDetector` sem biblioteca de leitura nova (CA 5, 9); §4.3: `assets.view` conferido antes da busca (CA 7), `returnUrl` só relativo (CA 8, novo), foto sem EXIF/GPS da PAT-01a (CA 5), job com `tenantId` (CA 3); fora da sessão por PIN (CA 10); ordem 12 do Marco 6.4; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: impressora e etiqueta (P-13).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da PAT-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (tabela 3.3 pontos 45–47, ADR 0038, §4.3); nova ordem (Marco 6.4, ordem 12); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
