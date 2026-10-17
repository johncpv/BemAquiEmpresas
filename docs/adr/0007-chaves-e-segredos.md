# ADR 0007: Chaves e segredos

- Status: Aceito em 16/10/2026. Revisado em 17/10/2026 após a revisão de segurança da FND-11.
- Origem: revisão pré-Fase 0, §2.2. Este era o achado mais grave: perder o key ring torna ilegíveis as conexões de todos os tenants.

## Decisão

### Key ring
- O key ring do Data Protection fica persistido no banco de catálogo (`PersistKeysToDbContext`, tabela `security.data_protection_keys`).
- Cada chave do key ring é cifrada com um certificado X.509 (`ProtectKeysWithCertificate`), que precisa de chave privada e de RSA com pelo menos 3072 bits. O Host valida isso na subida.
- O certificado chega à api como um arquivo de secret montado: `/run/secrets/dp-cert.pfx`, modo 0400, dono = uid da app.
- A senha do certificado fica em outro arquivo de secret (`DataProtection:CertificatePasswordFile`), nunca em variável de ambiente.
- Rotação automática a cada 90 dias. As chaves antigas continuam decifrando.
- **Não revogar chaves nem trocar o certificado** até existir o job de recifração (FND-10b). O procedimento está em `docs/operacao/segredos-e-chaves.md`.
- O certificado de desenvolvimento só é gerado com opt-in (`GenerateDevelopmentCertificate`), em `Development` e fora de container.

### Cópia offline
- O `.pfx` e a senha ficam no cofre de senhas do PO, fora da VPS e fora do backup dos bancos.
- O backup do catálogo inclui o key ring, cifrado. Sem o certificado, esse backup não decifra nada.

### Fail fast
O Host recusa subir nestes casos:
- key ring inacessível;
- key ring sem chave ativa quando `AllowKeyRingCreation=false`;
- chave que não decifra com o certificado;
- certificado ausente ou inválido.

Com a flag de criação ligada, toda subida registra um aviso.

### Propósito por tenant e por tipo
- Formato: `CreateProtector("bemaqui", "platform" | "tenant:{id sem hífens}", "<tipo>", "v1")`.
- Os nomes dos tipos são fixos e não dependem do nome do enum: `connection`, `smtp`, `totp`, `ai-key`. Na Fase 5 entram `inter-credentials`, `inter-certificate` e `fiscal-certificate`.
- Um teste golden trava essas cadeias.
- Um ciphertext copiado de um tenant para outro não decifra.

### Segredos write-only
- Os segredos nunca voltam numa resposta. A tela mostra só "configurado em dd/mm/aaaa".
- O tipo `Secret` reforça isso:
  - `ToString()` devolve `***`;
  - o conversor JSON lança exceção se tentarem serializar o valor e recusa receber `***`;
  - a comparação é feita em tempo constante.
- Os segredos só são decifrados no momento do uso.
- O Serilog mascara todo `Secret`. A redação de `Authorization`, cookies e tokens entra junto com o request logging (FND-12).

### Teste de restauração mensal
Restaurar o catálogo e um tenant numa máquina limpa, com o certificado offline, e decifrar um segredo.

## Consequências
- A FND-11 implementa o key ring, os protetores e a máscara.
- A FND-10b implementa a montagem dos secrets no compose, a trilha separada do backup do key ring, a lista de certificados antigos e o job de recifração.
- A criação inicial do key ring passa a ser um passo explícito do provisionamento na FND-03a. A flag de subida fica só para o primeiro ambiente.
