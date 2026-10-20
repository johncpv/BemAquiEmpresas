# ADR 0022: Geração de PDF

- Status: Aceito (20/10/2026). Decisão D17 do PO (John) na revisão pré-Fase 2, com uma pré-condição de licença que fica com o PO.
- Contexto: a COM-03 é a primeira story que gera PDF (o orçamento). A revisão de arquitetura marcou a COM-03 como NO-GO porque faltavam o contrato `IPdfRenderer`, a fonte, o comportamento na imagem Alpine da API (ADR 0015) e a confirmação da licença. A ADR 0009 já escolheu o QuestPDF, com a licença Community, que só vale abaixo de um limite de receita bruta anual.
- Complementa as ADRs 0009 (stack e licença) e 0015 (imagem Alpine).

## Decisão
- **Biblioteca:** QuestPDF, atrás do contrato **`IPdfRenderer`** nos BuildingBlocks (`Task<Stream> RenderAsync(IDocument document, CancellationToken)`). Os layouts ficam nos módulos (ex.: `QuotePdfDocument` no Commercial). Nenhum módulo referencia o QuestPDF fora do layout; trocar de biblioteca muda só o renderer e os layouts.
- **Fonte:** Archivo (a fonte da identidade visual) embutida como recurso do assembly e registrada na subida (`FontManager.RegisterFont`), com `QuestPDF.Settings.UseEnvironmentFonts = false`. O PDF nunca depende de fonte instalada no sistema. Números com `tabular-nums` (variante da fonte) e formato `pt-BR`.
- **Imagem Alpine:** um teste de integração gera um PDF de referência dentro da imagem da API (a mesma do deploy) e confere: o PDF abre, o texto extraído tem os valores esperados e as fontes embutidas são só a Archivo. O teste roda no `scripts/test-docker.sh` e no CI.
- **Limites:** até 300 itens e observações de até 2.000 caracteres por documento; acima disso, 422 antes de gerar. Tempo de geração conferido em teste (COM-03).
- **Imagens:** só o logo `Clean` do tenant (ADR 0016), lido pelo `IFileStorage`. Nenhuma URL externa é buscada na geração.
- **Licença:** a licença Community do QuestPDF só vale para empresa com receita bruta anual abaixo do limite da licença (hoje, US$ 1 milhão). Esta ADR **não decide** a elegibilidade: **pré-condição da COM-03, "o PO confirma a elegibilidade da Community ou adquire a licença"**, registrada em `06-pendencias-riscos.md` (P-21). A licença escolhida é configurada na subida (`QuestPDF.Settings.License`) e reavaliada a cada novo tenant ou mudança de receita (ADR 0009).
- **Adendo de 20/10/2026 (PO):** usar a licença **Community** (`LicenseType.Community`). A P-21 está decidida; a reavaliação acima continua valendo.

## Consequências
- A COM-03 implementa o `IPdfRenderer`, o registro da fonte, o teste na imagem Alpine e o `QuotePdfDocument`.
- O holerite (RH-02), as etiquetas (PAT-01) e o DANFE próprio, se houver, reaproveitam o `IPdfRenderer`.
- O pacote QuestPDF entra no `Directory.Packages.props` na COM-03, com a varredura de vulnerabilidades da FND-01.
