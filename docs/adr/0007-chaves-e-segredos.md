# ADR 0007 — Chaves e segredos

- Status: Aceito (16/10/2026)
- Origem: revisão pré-Fase 0, §2.2. Este era o achado mais grave: perder o key ring torna ilegíveis as conexões de todos os tenants.

## Decisão
- **Key ring do Data Protection persistido no banco de catálogo** (`PersistKeysToDbContext`, tabela `data_protection_keys`) e **cifrado com um certificado X.509** (`ProtectKeysWithCertificate`).
  - O certificado chega à api como arquivo de secret montado (`/run/secrets/dp-cert.pfx`, modo 0400, dono = uid da app), com senha em outro secret. Nunca entra no repositório, no banco ou em log.
  - Rotação automática do Data Protection a cada 90 dias. As chaves antigas continuam válidas para decifrar.
- **Cópia offline:** o `.pfx` e a senha ficam no cofre de senhas do PO, fora da VPS e fora do backup dos bancos.
  - O backup do catálogo inclui o key ring, que está cifrado. Sem o certificado, esse backup não decifra nada.
- **Propósito por tenant e por tipo:** `CreateProtector("bemaqui", "tenant:{id}", "<tipo>")`. Os tipos são `connection`, `fiscal-cert`, `bank-credentials`, `smtp`, `ai-key` e `totp`.
  - Um ciphertext copiado de um tenant para outro não decifra.
- **Segredos são write-only na API:** nunca voltam numa resposta. A tela mostra só "configurado em dd/mm/aaaa".
  - Só são decifrados no momento do uso.
  - O Serilog tem redaction para campos marcados como segredo e para cabeçalhos `Authorization`, cookies e tokens.
- **Teste de restauração mensal:** restaurar o catálogo e um tenant numa máquina limpa, com o certificado offline, e decifrar um segredo.

## Consequências
- A FND-11 implementa o key ring, os protetores e a redaction.
- A FND-10 monta os secrets no compose e documenta o procedimento de restauração.
